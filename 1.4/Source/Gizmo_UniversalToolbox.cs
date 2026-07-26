using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace MyWeapons;

[StaticConstructorOnStartup]
public class Gizmo_UniversalToolbox : Gizmo
{
    private const float PanelWidth = 240f;
    private const float PanelHeight = 75f;
    private const float Margin = 5f;
    private const float RowHeight = 16f;
    private const float RowGap = 1f;
    private const float LabelWidth = 64f;
    private const float ValueWidth = 38f;
    private const float Gap = 4f;
    private const float BarHeight = 10f;

    private static readonly Color BorderColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);

    private const string EditControlName = "MW_UTB_Edit";

    private readonly CompUniversalToolbox comp;
    private readonly bool[] dragging = new bool[4];
    private int editingRow = -1;
    private string editBuffer;
    private bool wantEditFocus;

    private static readonly Texture2D BarFillTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.4f, 0.65f, 0.9f));
    private static readonly Texture2D BarFillHighlightTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.5f, 0.75f, 1f));
    private static readonly Texture2D BarBgTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.25f, 0.25f, 0.25f));
    private static readonly Texture2D DragTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.4f, 0.9f, 0.98f));
    private static readonly Texture2D PanelBgTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.1f, 0.1f, 0.1f, 0.85f));

    public Gizmo_UniversalToolbox(CompUniversalToolbox comp)
    {
        this.comp = comp;
        Order = -99f;
    }

    public override float GetWidth(float maxWidth)
    {
        return PanelWidth;
    }

    private static readonly (string labelKey, Func<CompUniversalToolbox, float> get,
        Action<CompUniversalToolbox, float> set, float max, bool isInt)[] Rows =
    {
        ("MW_UniversalToolbox_RowQuality", c => c.qualityOffset, (c, v) => c.qualityOffset = Mathf.RoundToInt(v), 5f, true),
        ("MW_UniversalToolbox_RowWorkSpeed", c => c.workSpeedOffset, (c, v) => c.workSpeedOffset = v, 50f, false),
        ("MW_UniversalToolbox_RowResearch", c => c.researchSpeedOffset, (c, v) => c.researchSpeedOffset = v, 50f, false),
    };

    public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
    {
        try
        {
            Rect outer = new Rect(topLeft.x, topLeft.y, PanelWidth, PanelHeight);
            GUI.color = Color.white;
            GUI.DrawTexture(outer, PanelBgTex);
            Color oldColor = GUI.color;
            GUI.color = BorderColor;
            Widgets.DrawBox(outer);
            GUI.color = oldColor;
            // 1.4 没有 EntityStudyRate，只有三行：在 75px 格子内垂直居中
            float y = (PanelHeight - Rows.Length * RowHeight - (Rows.Length - 1) * RowGap) / 2f;
            for (int i = 0; i < Rows.Length; i++)
            {
                DrawRow(new Rect(outer.x + Margin, outer.y + y, PanelWidth - 2f * Margin, RowHeight), i);
                y += RowHeight + RowGap;
            }
        }
        catch (Exception e)
        {
            Verse.Log.ErrorOnce($"[MyWeapons] UniversalToolbox gizmo error: {e}", 918273645);
        }
        return new GizmoResult(GizmoState.Clear);
    }

    private void DrawRow(Rect row, int i)
    {
        var (labelKey, get, set, max, isInt) = Rows[i];
        Rect labelRect = new Rect(row.x, row.y, LabelWidth, row.height);
        Rect barRect = new Rect(row.x + LabelWidth + Gap,
            row.y + (row.height - BarHeight) / 2f,
            row.width - LabelWidth - ValueWidth - 2f * Gap, BarHeight);
        Rect valueRect = new Rect(row.xMax - ValueWidth, row.y, ValueWidth, row.height);

        GameFont oldFont = Text.Font;
        TextAnchor oldAnchor = Text.Anchor;
        Text.Font = GameFont.Tiny;
        Text.Anchor = TextAnchor.MiddleLeft;
        GUI.color = new Color(0.9f, 0.9f, 0.9f);
        Widgets.Label(labelRect, labelKey.Translate());

        float value = get(comp);
        if (editingRow == i)
        {
            GUI.color = Color.white;
            GUI.SetNextControlName(EditControlName);
            editBuffer = GUI.TextField(valueRect, editBuffer, Verse.Text.CurTextFieldStyle);
            if (wantEditFocus)
            {
                GUI.FocusControl(EditControlName);
                wantEditFocus = false;
            }
            bool commit = false;
            if (Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                commit = true;
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseDown && Event.current.button == 0 &&
                     !valueRect.Contains(Event.current.mousePosition))
            {
                commit = true;
            }
            if (commit)
            {
                CommitEdit(i);
                GUIUtility.keyboardControl = 0;
            }
        }
        else
        {
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = Color.white;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Widgets.Label(valueRect, isInt ? $"+{(int)value}" : $"+{value.ToString("0.#", inv)}");
            if (Widgets.ButtonInvisible(valueRect))
            {
                editingRow = i;
                editBuffer = isInt ? ((int)value).ToString() : value.ToString("0.0", inv);
                wantEditFocus = true;
            }
        }

        DoDragBar(barRect, i, value, max, isInt, set);

        GUI.color = Color.white;
        Text.Font = oldFont;
        Text.Anchor = oldAnchor;
    }

    // 自定义拖拽条：按下后全局跟踪鼠标（不要求悬停在条上），松开即停。
    // 修复 Widgets.DraggableBar 拖出条区域即中断、两端边缘不灵敏的问题。
    private void DoDragBar(Rect barRect, int i, float value, float max, bool isInt,
        Action<CompUniversalToolbox, float> set)
    {
        Event e = Event.current;
        bool mouseOver = Mouse.IsOver(barRect);
        Widgets.FillableBar(barRect, Mathf.Min(value / max, 1f),
            mouseOver || dragging[i] ? BarFillHighlightTex : BarFillTex, BarBgTex, doBorder: true);

        float stepPct = isInt ? 1f / 5f : 5f / max;
        float mousePct = Mathf.Clamp01((e.mousePosition.x - barRect.x) / barRect.width);
        float snapped = Mathf.Clamp01(Mathf.Round(mousePct / stepPct) * stepPct);

        bool apply = false;
        if (e.type == EventType.MouseDown && e.button == 0 && mouseOver)
        {
            dragging[i] = true;
            apply = true;
            e.Use();
        }
        else if (dragging[i] && e.type == EventType.MouseUp && e.button == 0)
        {
            dragging[i] = false;
            e.Use();
        }
        else if (dragging[i])
        {
            apply = true;
            if (e.type == EventType.MouseDrag)
            {
                e.Use();
            }
        }

        if (apply)
        {
            float scaled = snapped * max;
            float newValue = isInt ? Mathf.Round(scaled) : Mathf.Round(scaled / 5f) * 5f;
            if (!Mathf.Approximately(newValue, value))
            {
                set(comp, newValue);
                comp.Notify_SettingsChanged();
                if (editingRow == i)
                {
                    editingRow = -1;
                    GUIUtility.keyboardControl = 0;
                }
            }
        }

        GUI.color = Color.white;
        DrawBarTarget(barRect, dragging[i] ? snapped : value / max);
    }

    // 与 Verse.Widgets.DrawDraggableBarTarget 相同的几何
    private static void DrawBarTarget(Rect rect, float percent)
    {
        float num = Mathf.Round((rect.width - 8f) * percent);
        GUI.DrawTexture(new Rect(rect.x + 3f + num, rect.y, 2f, rect.height), DragTex);
        GUI.DrawTexture(new Rect(rect.x + 2f + num, rect.y - 3f, 4f, 5f), DragTex);
        GUI.DrawTexture(new Rect(rect.x + 2f + num, rect.yMax - 2f, 4f, 5f), DragTex);
    }

    private void CommitEdit(int i)
    {
        var (_, get, set, max, isInt) = Rows[i];
        editingRow = -1;
        if (!editBuffer.NullOrEmpty() && float.TryParse(editBuffer,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float committed))
        {
            committed = Mathf.Clamp(committed, 0f, max);
            committed = isInt ? Mathf.Round(committed) : committed;
            if (!Mathf.Approximately(committed, get(comp)))
            {
                set(comp, committed);
                comp.Notify_SettingsChanged();
            }
        }
    }
}

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

    private static readonly (string label, Func<CompUniversalToolbox, float> get,
        Action<CompUniversalToolbox, float> set, float max, int increments, bool isInt)[] Rows =
    {
        ("品质偏移", c => c.qualityOffset, (c, v) => c.qualityOffset = Mathf.RoundToInt(v), 5f, 5, true),
        ("工作速度", c => c.workSpeedOffset, (c, v) => c.workSpeedOffset = v, 50f, 500, false),
        ("研究速度", c => c.researchSpeedOffset, (c, v) => c.researchSpeedOffset = v, 50f, 500, false),
        ("调查速率", c => c.entityStudyRateOffset, (c, v) => c.entityStudyRateOffset = v, 50f, 500, false),
    };

    public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
    {
        try
        {
            Rect outer = new Rect(topLeft.x, topLeft.y, PanelWidth, PanelHeight);
            GUI.DrawTexture(outer, PanelBgTex);
            Color oldColor = GUI.color;
            GUI.color = BorderColor;
            Widgets.DrawBox(outer);
            GUI.color = oldColor;
            float y = Margin;
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
        var (label, get, set, max, increments, isInt) = Rows[i];
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
        Widgets.Label(labelRect, label);

        float value = get(comp);
        float target = value / max;
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
            if (!editBuffer.NullOrEmpty() && float.TryParse(editBuffer,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float committed))
            {
                committed = Mathf.Clamp(committed, 0f, max);
                committed = isInt ? Mathf.Round(committed) : committed;
                if (!Mathf.Approximately(committed, value))
                {
                    set(comp, committed);
                    comp.Notify_SettingsChanged();
                }
            }
            if (Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                editingRow = -1;
                GUIUtility.keyboardControl = 0;
                Event.current.Use();
            }
        }
        else
        {
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = Color.white;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Widgets.Label(valueRect, isInt ? $"+{(int)value}" : $"+{value.ToString("0.0", inv)}");
            if (Widgets.ButtonInvisible(valueRect))
            {
                editingRow = i;
                editBuffer = isInt ? ((int)value).ToString() : value.ToString("0.0", inv);
                wantEditFocus = true;
            }
        }

        bool drag = dragging[i];
        float targetBefore = target;
        Widgets.DraggableBar(barRect, BarFillTex, BarFillHighlightTex, BarBgTex, DragTex,
            ref drag, value / max, ref target, null, increments);
        dragging[i] = drag;
        if (!Mathf.Approximately(targetBefore, target))
        {
            float scaled = target * max;
            set(comp, isInt ? Mathf.Round(scaled) : Mathf.Round(scaled * 10f) / 10f);
            comp.Notify_SettingsChanged();
        }

        GUI.color = Color.white;
        Text.Font = oldFont;
        Text.Anchor = oldAnchor;
    }
}

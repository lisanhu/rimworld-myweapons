# 万能工具箱（MW_UniversalToolbox）设计文档

日期：2026-07-26
状态：已批准（设计方案 A）
目标版本：仅 RimWorld 1.6（`1.6/Source`）

## 背景

MyWeapons 现有三个工具箱（`MW_Toolbox` / `MW_ToolboxTwo` / `MW_ToolboxThree`，定义于
`Defs/SuperApparels/SuperApparels.xml`），增益写死在 `equippedStatOffsets`：

- `MW_Toolbox`：WorkSpeedGlobal +49，MW_PawnCreatedQualityOffset +3
- `MW_ToolboxTwo`：WorkSpeedGlobal +1，品质 +3，ResearchSpeed +20，EntityStudyRate +20
- `MW_ToolboxThree`：品质 +3

需求：合并为一个**万能工具箱**，穿着者选中时出现类似 MoeLotl 源气面板 / 心灵熵面板的
整合 Gizmo，可动态调节四项参数（机制借鉴，外观不照搬）。

## 参数规格

| 参数 | 属性 | 范围 | 步进 | 默认值 |
|---|---|---|---|---|
| 品质偏移 | MW_PawnCreatedQualityOffset | 0–5 整数 | 1 | 3 |
| 全局工作速度偏移 | WorkSpeedGlobal | 0–50 | 0.1 | 0 |
| 研究速度偏移 | ResearchSpeed | 0–50 | 0.1 | 0 |
| 实体调查速率偏移 | EntityStudyRate | 0–50 | 0.1 | 0 |

## 源码核实的事实（实现依据）

1. 装备 gizmo 通道：`Pawn.GetGizmos()`（仅 `IsColonistPlayerControlled` 等）→
   `Apparel.GetWornGizmos()` → comp 的 `CompGetWornGizmosExtra()`。
   不需自己做派系判断。
2. Gizmo 格子固定 75px 高（`GizmoGridDrawer` 按 75 步进换行），宽度由
   `Gizmo.GetWidth(maxWidth)` 决定。面板设计为 240×75，四行紧凑布局。
3. 拖拽条使用原版公开 API `Widgets.DraggableBar(...)`（vanilla `Gizmo_Slider` 同款），
   不复制 MoeLotl 的私有 `DraggableBar`。
4. 数值直接输入使用 `Widgets.TextFieldNumeric`（点击数值文本切换为编辑状态）。
5. Pawn 装备属性在 `StatWorker.GetValueUnfinalized` 中经
   `StatWorker.StatOffsetFromGear(Thing gear, StatDef stat)`（static）累加；
   同一方法被属性解释面板调用。Harmony postfix 一处补丁，数值与解释同时生效。
6. 属性默认不缓存（`GetValue` 每次重算）；capacity 有缓存但四个属性均不喂 capacity。
   UI 改值后调用穿着者 `pawn.health.capacities.Notify_CapacityLevelsDirty()`，
   与原版 `Notify_ApparelAdded/Removed` 行为对齐。
7. 品质偏移复用现有 `QualityUtilityGenerateQualityCreatedByPawnPatch`
   （读 `pawn.GetStatValue(MW_PawnCreatedQualityOffset)`），动态值走同一 stat 注入，
   现有 patch 无需修改。

## 组件设计

### CompProperties_UniversalToolbox : CompProperties

- `compClass = typeof(CompUniversalToolbox)`
- 默认值字段：`defaultQualityOffset = 3`、`defaultWorkSpeedOffset = 0`、
  `defaultResearchSpeedOffset = 0`、`defaultEntityStudyRateOffset = 0`
- 范围常量：品质 0–5（int），其余 0–50（float，UI 步进 0.1）

### CompUniversalToolbox : ThingComp

- 实例字段：`qualityOffset` (int)、`workSpeedOffset`、`researchSpeedOffset`、
  `entityStudyRateOffset` (float)
- 构造/初始化时取 Props 默认值
- `PostExposeData()`：`Scribe_Values.Look` 持久化四个字段（存档安全）
- `CompGetWornGizmosExtra()`：缓存一个 `Gizmo_UniversalToolbox` 实例并 yield
- 提供 `Notify_SettingsChanged()`：找穿着者
  (`parent.ParentHolder as Pawn_ApparelTracker`)，调用
  `capacities.Notify_CapacityLevelsDirty()`

### Gizmo_UniversalToolbox : Gizmo

- `GetWidth()` → 240；绘制区域 240×75（严格压在格子内）
- 四行，每行约 17px：属性标签（Tiny 字体）+ `Widgets.DraggableBar`
  （品质行 increments=5，整数吸附；其余 0.1 步进）+ 数值文本
- 点击数值文本 → 该值切换为 `Widgets.TextFieldNumeric` 直接编辑，
  Enter/失焦提交并 clamp 到范围
- 每次改值写回 comp 字段并调用 `Notify_SettingsChanged()`
- `GizmoOnGUI` 返回 `new GizmoResult(GizmoState.Clear)`；try/catch 保护，
  参考 MoeLotl 的错误隔离做法

### StatOffsetFromGearPatch（Harmony postfix）

```csharp
[HarmonyPatch(typeof(StatWorker), nameof(StatWorker.StatOffsetFromGear))]
static void Postfix(Thing gear, StatDef stat, ref float __result)
```

- `gear.TryGetComp<CompUniversalToolbox>()` 命中时按 stat 匹配加对应字段值
- 与现有 Harmony 初始化方式一致（见现有 `MyWeapons.cs`）

## XML 改动（Defs/SuperApparels/SuperApparels.xml）

- 新增 `MW_UniversalToolbox` ThingDef：以 `MW_Toolbox` 为蓝本，
  移除 `equippedStatOffsets`，`comps` 加 `CompProperties_UniversalToolbox`，
  保留 `recipeMaker`（CraftingSpot / PraySpot）、texture 复用 `MyWeapons/toolbox`
- 旧三个 def（`MW_Toolbox` / `MW_ToolboxTwo` / `MW_ToolboxThree`）：
  **保留 def，删除整个 `<recipeMaker>` 节点**——无常规制作入口，
  旧存档中的引用不丢失

## UI Mockup 阶段：`rimworld-imgui-sim` Python 包

写正式 C# 之前，先做一个**独立的、可复用的 Python 包**，按反编译出的
IMGUI 源码**逐像素级还原** RimWorld 的 IMGUI 渲染结果（不是"看起来像"的模拟），
再用它来迭代万能工具箱面板设计。包位置：`/home/lisanhu/mine/workspace/rimworld/rimworld-imgui-sim/`
（独立目录，不放进 MyWeapons 仓库，附 README 使用文档，供其他项目复用）。

### 还原依据（已从源码核实）

- 字体：`Verse.Text` 从 Resources 加载 `Fonts/Calibri_tiny`、`Fonts/Arial_small`、
  `Fonts/Arial_medium`（text.cs:157-159）。用 UnityPy 从
  `RimWorldLinux_Data/resources.assets` 提取真实 Font 资产（含字号）；
  若 Font 是 dynamic 引用系统字体，则按资产中的字体名/字号在系统找匹配字体，
  并在 README 注明还原边界。
- 控件几何：从 `Verse.Widgets` 反编译源码逐行移植——
  `DraggableBar`（widgets.cs:3426-3500，含阈值带、拖拽目标标记绘制）、
  `FillableBar`、`Label`（TextAnchor 九宫格对齐 + GUIStyle padding）、
  `TextFieldNumeric`（编辑态背景、光标）、`ButtonImage`（mouseover 色）。
- 渲染语义：Pillow 后端，还原 `GUI.DrawTexture`（GUI.color 做逐像素乘色）、
  `GUI.color` 状态栈、`Text.Font`（Tiny/Small/Medium 三档）、
  `Text.Anchor` 状态机、`GenUI.ContractedBy/ExpandedBy` 等 Rect 运算。
- 贴图：`SolidColorMaterials` 纯色贴图直接等价生成；vanilla UI 贴图
  （如需）UnityPy 提取；mod 贴图（Axolotl gizmo 贴图、toolbox 图标）
  本身就是 PNG 直接可用。

### 包 API（草案）

```python
from rimworld_imgui import IMGUIContext, Rect, GameFont, TextAnchor

ctx = IMGUIContext(scale=1.0)
ctx.draw_texture(rect, tex, color=...)        # GUI.DrawTexture + GUI.color
ctx.fillable_bar(rect, pct, fill, bg)         # Widgets.FillableBar
ctx.draggable_bar(rect, value, ...)           # Widgets.DraggableBar（含交互模拟）
ctx.label(rect, "text")                       # Widgets.Label
ctx.text_field_numeric(rect, buffer)          # Widgets.TextFieldNumeric 编辑态
img = ctx.render()                            # 输出 PIL Image / PNG
# 交互模拟：注入鼠标位置/按键状态，渲染 hover/drag/editing 帧
```

### 还原边界（诚实声明）

Unity dynamic font 的光栅化（hinting/kerning）与 Pillow 可能有 ±1px 差异；
不影响布局与观感迭代。交付时 README 写清。

### 面板迭代流程

用该包写 `toolbox_panel_mock.py`：声明与最终 C# 一致的布局代码
（Rect 常量、行高、间距、字号），渲染 240×75 的静态帧与交互帧
（悬停、拖拽中、文本编辑态）为 PNG，反复审视调整直到好看，
再把同一套几何参数翻译成 C# IMGUI 代码。

## 不做的事（YAGNI）

- 不改动 1.4 / 1.5 源码树
- 不删除旧三个工具箱 def、不改它们的贴图与描述
- 不增加新的 StatDef、不给 vanilla StatDef 打 XML 补丁
- 面板不做折叠/展开、不做多语言以外的扩展配置

## 验证

- `dotnet build` 1.6 源码树，产物覆盖 `1.6/Assemblies/MyWeapons.dll`
- 游戏内：穿着万能工具箱 → 选中殖民者出现面板；调节四项 →
  对应 stat（含品质产出、工作速度、研究/调查速率 breakdown）即时变化；
  存档读档后数值保持；旧存档中已有工具箱不报错

# Universal Toolbox Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 MyWeapons 的三个工具箱合并为一个带动态 Command 面板的万能工具箱（RimWorld 1.6），先交付可复用的 rimworld-imgui-sim Python 高保真 IMGUI 还原包并用它迭代面板设计。

**Architecture:** ThingComp 存四项可调参数 → Harmony postfix `StatWorker.StatOffsetFromGear` 注入动态偏移 → `CompGetWornGizmosExtra` 暴露 240×75 自定义 Gizmo（`Widgets.DraggableBar` + `TextFieldNumeric`）。UI 先用独立 Python 包按反编译源码像素级还原 IMGUI 渲染来迭代。

**Tech Stack:** C# (net481, Krafs.Rimworld.Ref 1.6.4488-beta, Lib.Harmony 2.3.1.1)、XML defs、Python 3 + Pillow + UnityPy + pytest。

## Global Constraints

- 只改 `1.6/` 源码树；不动 `1.4/`、`1.5/`。
- 旧三个 def（`MW_Toolbox` / `MW_ToolboxTwo` / `MW_ToolboxThree`）必须保留，只删除它们的 `<recipeMaker>` 节点。
- 参数范围：品质偏移 int 0–5（默认 3）；WorkSpeedGlobal / ResearchSpeed / EntityStudyRate float 0–50，步进 0.1（默认 0）。
- Gizmo 绘制区域严格 240×75（GizmoGridDrawer 每格固定 75px 高）。
- 不新增 StatDef、不给 vanilla StatDef 打 XML 补丁。
- 不复制 MoeLotl 的私有 `DraggableBar`；用 `Widgets.DraggableBar`。
- Python 包是独立项目：`/home/lisanhu/mine/workspace/rimworld/rimworld-imgui-sim/`，自带 git 仓库、README，不放进 MyWeapons 仓库。
- 反编译参考材料路径：游戏 `/Data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/`；MoeLotl 反编译已在 `/home/lisanhu/mine/tmp/moelotl-decompiled/`。
- 规格文档：`docs/superpowers/specs/2026-07-26-universal-toolbox-design.md`（MyWeapons 仓库内）。

---

## Phase A — rimworld-imgui-sim Python 包

### Task 1: 包脚手架 + 参考源码与字体资产提取

**Files:**
- Create: `/home/lisanhu/mine/workspace/rimworld/rimworld-imgui-sim/pyproject.toml`
- Create: `/home/lisanhu/mine/workspace/rimworld/rimworld-imgui-sim/tools/extract_assets.py`
- Create: `/home/lisanhu/mine/workspace/rimworld/rimworld-imgui-sim/rimworld_imgui/__init__.py`
- Create: `/home/lisanhu/mine/workspace/rimworld/rimworld-imgui-sim/reference/`（反编译 .cs，git 跟踪，供移植对照）

**Interfaces:**
- Produces: `rimworld_imgui/assets/fonts.json`（`{"tiny": {"file": ..., "size": int}, "small": ..., "medium": ...}`），后续 Task 3 的 `fonts.py` 读它。

- [ ] **Step 1: 初始化仓库与 venv**

```bash
cd /home/lisanhu/mine/workspace/rimworld/rimworld-imgui-sim
git init
python3 -m venv .venv
.venv/bin/pip install pillow pytest UnityPy
printf '.venv/\n__pycache__/\n*.pyc\n' > .gitignore
```

- [ ] **Step 2: 写 pyproject.toml**

```toml
[project]
name = "rimworld-imgui-sim"
version = "0.1.0"
description = "Pixel-faithful reimplementation of RimWorld (Verse) IMGUI rendering for offline UI mockups"
requires-python = ">=3.10"
dependencies = ["pillow"]

[project.optional-dependencies]
assets = ["UnityPy"]
test = ["pytest"]

[build-system]
requires = ["setuptools"]
build-backend = "setuptools.build_meta"

[tool.setuptools.packages.find]
include = ["rimworld_imgui*"]
```

然后 `.venv/bin/pip install -e .`

- [ ] **Step 3: 提取反编译参考源码（一次性，结果提交进 reference/）**

```bash
cd /home/lisanhu/mine/workspace/rimworld/rimworld-imgui-sim
ASM=/Data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed/Assembly-CSharp.dll
for t in Verse.Widgets Verse.Text Verse.Gizmo_Slider RimWorld.PsychicEntropyGizmo RimWorld.StatWorker; do
  ilspycmd "$ASM" -t "$t" > "reference/${t}.cs"
done
cp /home/lisanhu/mine/tmp/moelotl-decompiled/Axolotl/Gizmo_LotlQiOverView.cs reference/Axolotl.Gizmo_LotlQiOverView.cs
```

- [ ] **Step 4: 写 tools/extract_assets.py 并运行**

从 `resources.assets` 提取 `Fonts/Calibri_tiny`、`Fonts/Arial_small`、`Fonts/Arial_medium`
（对应 `Verse.Text` text.cs:157-159 的三个 Resources.Load）。Font 资产若含字体数据
（`m_FontData`）则导出 ttf；若是 dynamic 引用 OS 字体，记录字体名与 `m_FontSize`，
Linux 下用度量兼容字体替代（Arial→Liberation Sans，Calibri→Carlito）。输出
`rimworld_imgui/assets/fonts.json` 与可用字体文件。

```python
"""Extract RimWorld UI fonts from resources.assets into rimworld_imgui/assets/."""
import json
from pathlib import Path

import UnityPy

GAME_DATA = Path("/Data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data")
ASSETS = GAME_DATA / "resources.assets"
OUT = Path(__file__).resolve().parent.parent / "rimworld_imgui" / "assets"
WANTED = {"Calibri_tiny": "tiny", "Arial_small": "small", "Arial_medium": "medium"}

# Metric-compatible fallbacks on Linux when the Font asset has no embedded data.
FALLBACK = {
    "Arial": "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
    "Calibri": "/usr/share/fonts/truetype/crosextra/Carlito-Regular.ttf",
}

def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    env = UnityPy.load(str(ASSETS))
    found = {}
    for obj in env.objects:
        if obj.type.name != "Font":
            continue
        font = obj.read()
        name = font.m_Name
        if name not in WANTED:
            continue
        slot = WANTED[name]
        entry = {"unity_name": name, "size": int(font.m_FontSize)}
        data = bytes(font.m_FontData) if getattr(font, "m_FontData", None) else b""
        if data[:4] in (b"\x00\x01\x00\x00", b"OTTO", b"true"):
            path = OUT / f"{slot}.ttf"
            path.write_bytes(data)
            entry["file"] = path.name
        else:
            names = getattr(font, "m_FontNames", None) or [name.split("_")[0]]
            entry["os_name"] = names[0]
            fb = FALLBACK.get(names[0])
            if fb and Path(fb).exists():
                entry["file"] = fb  # absolute system path
        found[slot] = entry
    missing = set(WANTED.values()) - found.keys()
    if missing:
        raise SystemExit(f"missing fonts: {missing}")
    (OUT / "fonts.json").write_text(json.dumps(found, indent=2))
    print(json.dumps(found, indent=2))

if __name__ == "__main__":
    main()
```

运行：`.venv/bin/python tools/extract_assets.py`
预期：打印三个 slot 的 json，`fonts.json` 落盘。若 `m_FontData` 为空且系统缺
fallback 字体，记录实际 os_name 并在 Task 3 用 PIL 默认字体兜底（README 注明）。

- [ ] **Step 5: 提交**

```bash
git add -A && git commit -m "chore: scaffold, reference sources, font extraction"
```

---

### Task 2: 几何与颜色核心（geometry.py / color.py / 贴图绘制）

**Files:**
- Create: `rimworld_imgui/geometry.py`
- Create: `rimworld_imgui/color.py`
- Create: `rimworld_imgui/context.py`（本任务先只含 `IMGUIContext` + `draw_texture` + `solid_tex`）
- Test: `tests/test_geometry.py`、`tests/test_color.py`

**Interfaces:**
- Produces:
  - `Rect(x, y, width, height)`，属性 `x_max`/`y_max`，方法 `contracted_by(d)`（对应 `GenUI.ContractedBy`）
  - `Color(r, g, b, a=1.0)`（0–1 float），`to_rgba()`
  - `IMGUIContext(width, height, scale=1.0)`：`gui_color: Color` 状态字段；
    `draw_texture(rect, tex, color=None)`（GUI.color 逐像素乘色，等价 `GUI.DrawTexture`，ScaleMode.StretchToFill）；
    `solid_tex(color)` → 1×1 PIL Image（等价 `SolidColorMaterials.NewSolidColorTexture`）；
    `render()` → PIL Image；`save(path)`

- [ ] **Step 1: 写失败测试**

```python
# tests/test_geometry.py
from rimworld_imgui.geometry import Rect

def test_contracted_by():
    r = Rect(10, 10, 100, 50).contracted_by(3)
    assert (r.x, r.y, r.width, r.height) == (13, 13, 94, 44)

def test_edges():
    r = Rect(1, 2, 3, 4)
    assert r.x_max == 4 and r.y_max == 6
```

```python
# tests/test_color.py
from PIL import Image
from rimworld_imgui.color import Color
from rimworld_imgui.context import IMGUIContext
from rimworld_imgui.geometry import Rect

def test_gui_color_multiplies_texture():
    ctx = IMGUIContext(10, 10)
    ctx.gui_color = Color(1, 0.5, 0, 1)
    ctx.draw_texture(Rect(0, 0, 10, 10), ctx.solid_tex(Color(1, 1, 1, 1)))
    px = ctx.render().getpixel((5, 5))
    assert px[0] == 255 and abs(px[1] - 127) <= 1 and px[2] == 0
```

- [ ] **Step 2: 跑测试确认失败**

`.venv/bin/python -m pytest tests/ -v` → 预期 ImportError/失败。

- [ ] **Step 3: 实现**

```python
# rimworld_imgui/geometry.py
from __future__ import annotations
from dataclasses import dataclass

@dataclass(frozen=True)
class Rect:
    x: float
    y: float
    width: float
    height: float

    @property
    def x_max(self) -> float:
        return self.x + self.width

    @property
    def y_max(self) -> float:
        return self.y + self.height

    def contracted_by(self, d: float) -> "Rect":
        return Rect(self.x + d, self.y + d, self.width - 2 * d, self.height - 2 * d)

    def expanded_by(self, d: float) -> "Rect":
        return self.contracted_by(-d)
```

```python
# rimworld_imgui/color.py
from __future__ import annotations
from dataclasses import dataclass

@dataclass(frozen=True)
class Color:
    r: float
    g: float
    b: float
    a: float = 1.0

    def to_rgba(self) -> tuple[int, int, int, int]:
        c = lambda v: max(0, min(255, round(v * 255)))
        return (c(self.r), c(self.g), c(self.b), c(self.a))
```

```python
# rimworld_imgui/context.py
from __future__ import annotations
from pathlib import Path
from PIL import Image
from .color import Color
from .geometry import Rect

class IMGUIContext:
    """Immediate-mode GUI canvas replicating Unity IMGUI + Verse.Widgets semantics."""

    def __init__(self, width: int, height: int, scale: float = 1.0):
        self.scale = scale
        self.canvas = Image.new("RGBA", (round(width * scale), round(height * scale)), (0, 0, 0, 0))
        self.gui_color = Color(1, 1, 1, 1)

    def _px(self, rect: Rect) -> tuple[int, int, int, int]:
        s = self.scale
        return (round(rect.x * s), round(rect.y * s),
                max(1, round(rect.width * s)), max(1, round(rect.height * s)))

    def solid_tex(self, color: Color) -> Image.Image:
        return Image.new("RGBA", (1, 1), color.to_rgba())

    def draw_texture(self, rect: Rect, tex: Image.Image, color: Color | None = None) -> None:
        """GUI.DrawTexture with ScaleToFit=StretchToFill and GUI.color tint."""
        x, y, w, h = self._px(rect)
        tint = color if color is not None else self.gui_color
        img = tex.convert("RGBA").resize((w, h), Image.BILINEAR)
        if tint.to_rgba() != (255, 255, 255, 255):
            tr, tg, tb, ta = tint.r, tint.g, tint.b, tint.a
            ch = img.split()
            img = Image.merge("RGBA", tuple(
                c.point(lambda v, k=k: round(v * k)) for c, k in zip(ch, (tr, tg, tb, ta))))
        self.canvas.alpha_composite(img, (x, y))

    def render(self) -> Image.Image:
        return self.canvas

    def save(self, path: str | Path) -> None:
        self.canvas.save(str(path))
```

- [ ] **Step 4: 跑测试确认通过**

`.venv/bin/python -m pytest tests/ -v` → 全 PASS。

- [ ] **Step 5: 提交**

`git add -A && git commit -m "feat: geometry, color, texture drawing core"`

---

### Task 3: 文本渲染（fonts.py / text.py / Label）

**Files:**
- Create: `rimworld_imgui/fonts.py`
- Create: `rimworld_imgui/text.py`
- Modify: `rimworld_imgui/context.py`（加 `font`/`anchor` 状态 + `label()`）
- Test: `tests/test_text.py`

**Interfaces:**
- Consumes: Task 1 的 `assets/fonts.json`；Task 2 的 `IMGUIContext`/`Rect`/`Color`。
- Produces:
  - `GameFont`（IntEnum：TINY=0, SMALL=1, MEDIUM=2）
  - `TextAnchor`（Enum：UPPER_LEFT, UPPER_CENTER, UPPER_RIGHT, MIDDLE_LEFT, MIDDLE_CENTER, MIDDLE_RIGHT, LOWER_LEFT, LOWER_CENTER, LOWER_RIGHT，对应 Unity TextAnchor 九宫格）
  - `load_font(game_font) -> PIL.ImageFont.FreeTypeFont`
  - `ctx.font: GameFont`、`ctx.anchor: TextAnchor` 状态字段
  - `ctx.label(rect, text, color=None)` —— 等价 `Widgets.Label`：按 `Text.Anchor` 在 rect 内对齐，单行，黑色时用 `GUI.color` 控制

- [ ] **Step 1: 写失败测试**

```python
# tests/test_text.py
from rimworld_imgui.context import IMGUIContext
from rimworld_imgui.geometry import Rect
from rimworld_imgui.text import GameFont, TextAnchor
from rimworld_imgui.color import Color

def _ink_bbox(img):
    return img.getbbox()

def test_label_draws_something():
    ctx = IMGUIContext(240, 75)
    ctx.gui_color = Color(0, 0, 0)
    ctx.label(Rect(0, 0, 240, 20), "Test 123")
    assert _ink_bbox(ctx.render()) is not None

def test_anchor_middle_center_centers_text():
    ctx = IMGUIContext(200, 40)
    ctx.gui_color = Color(0, 0, 0)
    ctx.anchor = TextAnchor.MIDDLE_CENTER
    ctx.label(Rect(0, 0, 200, 40), "ab")
    bbox = _ink_bbox(ctx.render())
    cx = (bbox[0] + bbox[2]) / 2
    assert abs(cx - 100) <= 3  # 水平居中（±3px 光栅化误差）
```

- [ ] **Step 2: 跑测试确认失败** → `.venv/bin/python -m pytest tests/test_text.py -v` FAIL。

- [ ] **Step 3: 实现**

```python
# rimworld_imgui/fonts.py
from __future__ import annotations
import json
from functools import lru_cache
from pathlib import Path
from PIL import ImageFont
from .text import GameFont

ASSETS = Path(__file__).resolve().parent / "assets"
# Fallback sizes if fonts.json was generated without sizes (see README fidelity notes).
DEFAULT_SIZES = {GameFont.TINY: 11, GameFont.SMALL: 14, GameFont.MEDIUM: 20}

@lru_cache(maxsize=None)
def load_font(game_font: GameFont, scale: float = 1.0) -> ImageFont.FreeTypeFont:
    meta = json.loads((ASSETS / "fonts.json").read_text())
    key = {GameFont.TINY: "tiny", GameFont.SMALL: "small", GameFont.MEDIUM: "medium"}[game_font]
    entry = meta[key]
    size = entry.get("size") or DEFAULT_SIZES[game_font]
    file = entry.get("file")
    if file and Path(file if Path(file).is_absolute() else ASSETS / file).exists():
        p = file if Path(file).is_absolute() else str(ASSETS / file)
        return ImageFont.truetype(p, round(size * scale))
    return ImageFont.load_default(round(size * scale))
```

```python
# rimworld_imgui/text.py
from __future__ import annotations
from enum import Enum, IntEnum

class GameFont(IntEnum):
    TINY = 0
    SMALL = 1
    MEDIUM = 2

class TextAnchor(Enum):
    UPPER_LEFT = 0
    UPPER_CENTER = 1
    UPPER_RIGHT = 2
    MIDDLE_LEFT = 3
    MIDDLE_CENTER = 4
    MIDDLE_RIGHT = 5
    LOWER_LEFT = 6
    LOWER_CENTER = 7
    LOWER_RIGHT = 8
```

`context.py` 增加：

```python
from PIL import ImageDraw
from .text import GameFont, TextAnchor
from .fonts import load_font

# __init__ 中:
self.font = GameFont.SMALL
self.anchor = TextAnchor.UPPER_LEFT

def label(self, rect: Rect, text: str, color: Color | None = None) -> None:
    """Widgets.Label: single line, aligned by Text.Anchor inside rect, GUI.color tinted."""
    tint = (color or self.gui_color).to_rgba()
    font = load_font(self.font, self.scale)
    s = self.scale
    d = ImageDraw.Draw(self.canvas)
    w = d.textlength(text, font=font)
    asc, desc = font.getmetrics()
    h = asc + desc
    name = self.anchor.name  # e.g. "MIDDLE_CENTER"
    vpart, hpart = name.split("_", 1)
    if hpart == "LEFT":
        x = rect.x * s
    elif hpart == "CENTER":
        x = rect.x * s + (rect.width * s - w) / 2
    else:  # RIGHT
        x = rect.x_max * s - w
    if vpart == "UPPER":
        y = rect.y * s
    elif vpart == "MIDDLE":
        y = rect.y * s + (rect.height * s - h) / 2
    else:  # LOWER
        y = rect.y_max * s - h
    d.text((x, y), text, font=font, fill=tint)
```

（注意：`LOWER_*` 的名字拆分后 hpart 仍是 `LEFT/CENTER/RIGHT`，`split("_", 1)` 已正确处理。）

- [ ] **Step 4: 跑测试确认通过** → PASS。

- [ ] **Step 5: 提交**

`git add -A && git commit -m "feat: font loading, anchors, label rendering"`

---

### Task 4: Widgets 移植（FillableBar / DraggableBar / TextFieldNumeric 编辑态）

**Files:**
- Create: `rimworld_imgui/widgets.py`
- Modify: `rimworld_imgui/context.py`（挂接 `fillable_bar` / `draggable_bar` / `text_field_numeric`）
- Test: `tests/test_widgets.py`

**Interfaces:**
- Consumes: Task 2/3 全部。
- Produces（均为 ctx 方法，渲染单帧静态结果；交互态由调用方显式传入）：
  - `ctx.fillable_bar(rect, fill_percent, fill_tex, bg_tex=None, do_border=True)`
    —— 逐行移植 reference/Verse.Widgets.cs:2561-2576
  - `ctx.draggable_bar(rect, bar_value, target_value, bar_tex, bar_highlight_tex, empty_tex, drag_tex, dragging=False, mouse_over=False, bands=None)`
    —— 逐行移植 reference/Verse.Widgets.cs:3426-3508 的**绘制部分**
    （`FillableBar(min(value,1), highlight if mouse_over else bar_tex, empty, doBorder=True)` +
    `DrawDraggableBarThreshold` + `DrawDraggableBarTarget`；事件/音效逻辑不移植）
  - `ctx.text_field_numeric(rect, buffer, focused=True)`
    —— 编辑态视觉：深色底 + 白色边框 + Tiny 文本 + 末尾光标（还原边界写入 README）

- [ ] **Step 1: 写失败测试**

```python
# tests/test_widgets.py
from rimworld_imgui.context import IMGUIContext
from rimworld_imgui.geometry import Rect
from rimworld_imgui.color import Color

def test_fillable_bar_half():
    ctx = IMGUIContext(106, 20)
    fill = ctx.solid_tex(Color(1, 0, 0))
    bg = ctx.solid_tex(Color(0, 0, 1))
    ctx.fillable_bar(Rect(0, 0, 106, 20), 0.5, fill, bg, do_border=True)
    img = ctx.render()
    # do_border: 外圈 3px 黑边；内部宽 100，左半红右半蓝
    assert img.getpixel((10, 10))[:3] == (255, 0, 0)
    assert img.getpixel((90, 10))[:3] == (0, 0, 255)
    assert img.getpixel((0, 0))[:3] == (0, 0, 0)

def test_draggable_bar_target_marker():
    ctx = IMGUIContext(120, 20)
    t = lambda c: ctx.solid_tex(c)
    ctx.draggable_bar(Rect(0, 3, 100, 12), 0.5, 0.5,
                      t(Color(0.6, 0.1, 0.9)), t(Color(0.7, 0.2, 1.0)),
                      t(Color(0.8, 0.8, 0.8)), t(Color(0.4, 0.9, 1.0)))
    img = ctx.render()
    # DrawDraggableBarTarget: x = rect.x + 3 + round((width-8)*pct)，宽 2px 贯穿 bar 高度
    x = 3 + round(92 * 0.5)
    assert img.getpixel((x, 8))[:3] == Color(0.4, 0.9, 1.0).to_rgba()[:3]
```

- [ ] **Step 2: 跑测试确认失败** → FAIL。

- [ ] **Step 3: 实现 `rimworld_imgui/widgets.py`**

严格对照 `reference/Verse.Widgets.cs` 移植（行号见 Interfaces）：

```python
# rimworld_imgui/widgets.py
from __future__ import annotations
from PIL import ImageDraw
from .color import Color
from .geometry import Rect
from .text import GameFont, TextAnchor
from .fonts import load_font

BLACK = Color(0, 0, 0)
GREY = Color(0.5, 0.5, 0.5)
WHITE = Color(1, 1, 1)

def fillable_bar(ctx, rect: Rect, fill_percent: float, fill_tex, bg_tex=None, do_border: bool = True) -> Rect:
    # Verse.Widgets.cs:2561-2576
    if do_border:
        # 注意：solid_tex 已烘焙颜色，不要再传 color=（否则双重乘色 c²，与 GUI.color=white 的原文不符）
        ctx.draw_texture(rect, ctx.solid_tex(BLACK))
        rect = rect.contracted_by(3)
    if bg_tex is not None:
        ctx.draw_texture(rect, bg_tex)
    result = rect
    filled = Rect(rect.x, rect.y, rect.width * fill_percent, rect.height)
    ctx.draw_texture(filled, fill_tex)
    return result

def _draw_draggable_bar_threshold(ctx, rect: Rect, percent: float, cur_value: float) -> None:
    # Verse.Widgets.cs:3466-3483
    pos = Rect(rect.x + 3 + (rect.width - 8) * percent, rect.y + rect.height - 9, 2, 6)
    ctx.draw_texture(pos, ctx.solid_tex(GREY if cur_value < percent else BLACK))

def _draw_draggable_bar_target(ctx, rect: Rect, percent: float, target_tex) -> None:
    # Verse.Widgets.cs:3485-3508（UIScaling 1.0 时 floor/ceil 即原值）
    num = round((rect.width - 8) * percent)
    ctx.draw_texture(Rect(rect.x + 3 + num, rect.y, 2, rect.height), target_tex)
    ctx.draw_texture(Rect(rect.x + 2 + num, rect.y - 3, 4, 5), target_tex)
    ctx.draw_texture(Rect(rect.x + 2 + num, rect.y_max - 2, 4, 5), target_tex)

def draggable_bar(ctx, rect, bar_value, target_value, bar_tex, bar_highlight_tex,
                  empty_tex, drag_tex, dragging=False, mouse_over=False, bands=None) -> None:
    # Verse.Widgets.cs:3426-3464（仅绘制部分；交互由调用方以状态参数表达）
    fillable_bar(ctx, rect, min(bar_value, 1.0),
                 bar_highlight_tex if mouse_over else bar_tex, empty_tex, do_border=True)
    for b in (bands or []):
        _draw_draggable_bar_threshold(ctx, rect, b, bar_value)
    _draw_draggable_bar_target(ctx, rect, target_value, drag_tex)
    ctx.gui_color = WHITE

def text_field_numeric(ctx, rect: Rect, buffer: str, focused: bool = True) -> None:
    """Editing-state visual of Widgets.TextFieldNumeric (approximation, see README)."""
    ctx.draw_texture(rect, ctx.solid_tex(Color(0.13, 0.13, 0.13)))
    if focused:
        d = ImageDraw.Draw(ctx.canvas)
        x, y, w, h = ctx._px(rect)
        d.rectangle([x, y, x + w - 1, y + h - 1], outline=(255, 255, 255, 255))
    inner = rect.contracted_by(2)
    old_anchor, old_font = ctx.anchor, ctx.font
    ctx.anchor, ctx.font = TextAnchor.MIDDLE_LEFT, GameFont.TINY
    ctx.label(inner, buffer)
    if focused:
        font = load_font(GameFont.TINY, ctx.scale)
        tw = ImageDraw.Draw(ctx.canvas).textlength(buffer, font=font)
        s = ctx.scale
        cx = inner.x * s + tw + 1
        d = ImageDraw.Draw(ctx.canvas)
        d.line([(cx, inner.y * s), (cx, inner.y_max * s)], fill=(255, 255, 255, 255))
    ctx.anchor, ctx.font = old_anchor, old_font
```

`context.py` 中挂接：

```python
from . import widgets as _w
def fillable_bar(self, *a, **k): return _w.fillable_bar(self, *a, **k)
def draggable_bar(self, *a, **k): return _w.draggable_bar(self, *a, **k)
def text_field_numeric(self, *a, **k): return _w.text_field_numeric(self, *a, **k)
```

- [ ] **Step 4: 跑测试确认通过** → PASS（含 Task 2/3 回归）。

- [ ] **Step 5: 提交**

`git add -A && git commit -m "feat: FillableBar/DraggableBar/TextFieldNumeric ports"`

---

### Task 5: README 使用文档 + 包导出

**Files:**
- Create: `/home/lisanhu/mine/workspace/rimworld/rimworld-imgui-sim/README.md`
- Modify: `rimworld_imgui/__init__.py`（公开导出）

**Interfaces:**
- Produces: `from rimworld_imgui import IMGUIContext, Rect, Color, GameFont, TextAnchor`

- [ ] **Step 1: `__init__.py`**

```python
from .color import Color
from .context import IMGUIContext
from .geometry import Rect
from .text import GameFont, TextAnchor

__all__ = ["IMGUIContext", "Rect", "Color", "GameFont", "TextAnchor"]
```

- [ ] **Step 2: 写 README.md**

内容要求（全部写实，不虚构）：
- 项目目的：离线像素级还原 RimWorld IMGUI（Verse.Widgets）渲染，用于 mod UI 设计迭代
- 安装：`python3 -m venv .venv && .venv/bin/pip install -e .[test]`；
  字体提取 `.venv/bin/pip install -e .[assets] && .venv/bin/python tools/extract_assets.py`
- 快速上手示例：渲染一个 FillableBar + Label 到 PNG 的最小脚本
- API 参考：`IMGUIContext`（draw_texture / fillable_bar / draggable_bar /
  text_field_numeric / label / solid_tex / render / save）、`Rect`、`Color`、
  `GameFont`、`TextAnchor`；`gui_color` / `font` / `anchor` 状态语义
- 还原边界（诚实声明）：
  1. Unity dynamic font 光栅化与 Pillow 有 ±1px 差异
  2. `text_field_numeric` 的底/边框是 Unity 默认 skin 的近似，非像素级
  3. 只实现 2D 静态帧渲染；交互（hover/drag/edit）由调用方以状态参数显式指定
  4. 移植对照的源码行号记录在 docstring（reference/ 目录有反编译原文）
- 测试：`.venv/bin/python -m pytest tests/ -v`

- [ ] **Step 3: 验证示例可跑**

README 里的最小示例原样跑通并产出 PNG（用 ReadMediaFile 看一眼）。

- [ ] **Step 4: 提交**

`git add -A && git commit -m "docs: README with usage and fidelity boundaries"`

---

## Phase B — 面板 Mockup 迭代

### Task 6: toolbox_panel_mock.py + 视觉迭代

**Files:**
- Create: `/home/lisanhu/mine/workspace/rimworld/rimworld-imgui-sim/examples/toolbox_panel_mock.py`
- Create: `/home/lisanhu/mine/workspace/rimworld/rimworld-imgui-sim/examples/out/`（PNG 输出，gitignore）

**Interfaces:**
- Consumes: Phase A 全部。
- Produces: `PANEL = dict(width=240, height=75, ...)` 布局常量表 + 四张 PNG
  （default / drag-quality / edit-work / hover-research）。这些常量之后**原样翻译**进
  Task 8 的 `Gizmo_UniversalToolbox.cs`。

- [ ] **Step 1: 写 mockup 脚本**

布局草案（240×75，均可调，迭代以视觉为准）。注意最后一行是清理过的
`y += p["row_height"] + p["row_gap"]`（无草稿残留）：

```python
"""Mockup of Gizmo_UniversalToolbox (240x75). Renders interaction frames to out/."""
from pathlib import Path
from rimworld_imgui import IMGUIContext, Rect, Color, GameFont, TextAnchor

PANEL = dict(
    width=240, height=75,
    margin=5,            # 外框内边距
    row_height=16,       # 每行高
    row_gap=1,           # 行间距
    label_w=64,          # 行首标签宽
    value_w=38,          # 行尾数值宽（可点击编辑）
    gap=4,               # 标签/条/数值之间间距
    bar_h=10,            # 拖拽条高（行内垂直居中）
)
ROWS = [  # (标签, 值字符串, 0-1 填充比例, 是否整数行)
    ("品质偏移", "+3", 3 / 5, True),
    ("工作速度", "+0.0", 0.0, False),
    ("研究速度", "+0.0", 0.0, False),
    ("调查速率", "+0.0", 0.0, False),
]
BAR_FILL = Color(0.4, 0.65, 0.9)     # 条填充色（迭代调）
BAR_BG = Color(0.25, 0.25, 0.25)
DRAG = Color(0.4, 0.9, 0.98)
PANEL_BG = Color(0.1, 0.1, 0.1, 0.85)

def draw_frame(state: dict) -> IMGUIContext:
    """state: {row, dragging, mouse_over_row, editing_row, edit_buffer}"""
    p = PANEL
    ctx = IMGUIContext(p["width"], p["height"])
    ctx.draw_texture(Rect(0, 0, p["width"], p["height"]), ctx.solid_tex(PANEL_BG), color=PANEL_BG)
    y = p["margin"]
    for i, (label, value, fill, _is_int) in enumerate(ROWS):
        row = Rect(p["margin"], y, p["width"] - 2 * p["margin"], p["row_height"])
        bar = Rect(row.x + p["label_w"] + p["gap"],
                   row.y + (row.height - p["bar_h"]) / 2,
                   row.width - p["label_w"] - p["value_w"] - 2 * p["gap"],
                   p["bar_h"])
        val_rect = Rect(row.x_max - p["value_w"], row.y, p["value_w"], row.height)
        ctx.font, ctx.anchor = GameFont.TINY, TextAnchor.MIDDLE_LEFT
        ctx.gui_color = Color(0.9, 0.9, 0.9)
        ctx.label(Rect(row.x, row.y, p["label_w"], row.height), label)
        if state.get("editing_row") == i:
            ctx.text_field_numeric(val_rect, state.get("edit_buffer", "12.3"))
        else:
            ctx.anchor = TextAnchor.MIDDLE_RIGHT
            ctx.gui_color = Color(1, 1, 1)
            ctx.label(val_rect, value)
        shown = state.get("drag_fill", fill) if state.get("row") == i and state.get("dragging") else fill
        ctx.draggable_bar(bar, shown, shown,
                          ctx.solid_tex(BAR_FILL), ctx.solid_tex(Color(0.5, 0.75, 1.0)),
                          ctx.solid_tex(BAR_BG), ctx.solid_tex(DRAG),
                          dragging=state.get("row") == i and state.get("dragging", False),
                          mouse_over=state.get("mouse_over_row") == i)
        y += p["row_height"] + p["row_gap"]
    return ctx

if __name__ == "__main__":
    out = Path(__file__).parent / "out"
    out.mkdir(exist_ok=True)
    draw_frame({}).save(out / "default.png")
    draw_frame({"row": 0, "dragging": True, "drag_fill": 4 / 5}).save(out / "drag-quality.png")
    draw_frame({"editing_row": 1, "edit_buffer": "12.3"}).save(out / "edit-work.png")
    draw_frame({"mouse_over_row": 2}).save(out / "hover-research.png")
```

- [ ] **Step 2: 渲染并用 ReadMediaFile 逐张审视四张 PNG**

- [ ] **Step 3: 迭代调整 PANEL 常量与颜色直到好看**（行高/间距/字号/配色/编辑态宽度；
  检查项：四行不溢出 75px、中文标签 Tiny 字号可读、拖拽标记对齐、编辑态不挤压条）

- [ ] **Step 4: 提交**

`git add -A && git commit -m "feat: universal toolbox panel mockup"`

---

## Phase C — MyWeapons Mod 实现

### Task 7: CompProperties_UniversalToolbox + CompUniversalToolbox

**Files:**
- Create: `/home/lisanhu/mine/workspace/rimworld/MyWeapons/1.6/Source/CompProperties_UniversalToolbox.cs`
- Create: `/home/lisanhu/mine/workspace/rimworld/MyWeapons/1.6/Source/CompUniversalToolbox.cs`

**Interfaces:**
- Produces:
  - `CompUniversalToolbox.OffsetFor(StatDef stat) -> float`（Task 9 补丁调用）
  - `CompUniversalToolbox.Notify_SettingsChanged()`（Task 8 gizmo 调用）
  - `CompUniversalToolbox.Wearer -> Pawn`
  - `CompUniversalToolbox` 公共字段 `qualityOffset`/`workSpeedOffset`/`researchSpeedOffset`/`entityStudyRateOffset`

- [ ] **Step 1: 写 CompProperties**

```csharp
using Verse;

namespace MyWeapons;

public class CompProperties_UniversalToolbox : CompProperties
{
    public int defaultQualityOffset = 3;
    public float defaultWorkSpeedOffset;
    public float defaultResearchSpeedOffset;
    public float defaultEntityStudyRateOffset;

    public CompProperties_UniversalToolbox()
    {
        compClass = typeof(CompUniversalToolbox);
    }
}
```

- [ ] **Step 2: 写 Comp**

```csharp
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MyWeapons;

public class CompUniversalToolbox : ThingComp
{
    public int qualityOffset;
    public float workSpeedOffset;
    public float researchSpeedOffset;
    public float entityStudyRateOffset;

    private Gizmo_UniversalToolbox gizmo;

    public CompProperties_UniversalToolbox Props => (CompProperties_UniversalToolbox)props;

    public override void Initialize(CompProperties props)
    {
        base.Initialize(props);
        qualityOffset = Props.defaultQualityOffset;
        workSpeedOffset = Props.defaultWorkSpeedOffset;
        researchSpeedOffset = Props.defaultResearchSpeedOffset;
        entityStudyRateOffset = Props.defaultEntityStudyRateOffset;
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref qualityOffset, "qualityOffset", 3);
        Scribe_Values.Look(ref workSpeedOffset, "workSpeedOffset", 0f);
        Scribe_Values.Look(ref researchSpeedOffset, "researchSpeedOffset", 0f);
        Scribe_Values.Look(ref entityStudyRateOffset, "entityStudyRateOffset", 0f);
    }

    public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
    {
        gizmo ??= new Gizmo_UniversalToolbox(this);
        yield return gizmo;
    }

    public Pawn Wearer => (parent.ParentHolder as Pawn_ApparelTracker)?.pawn;

    public void Notify_SettingsChanged()
    {
        Wearer?.health?.capacities?.Notify_CapacityLevelsDirty();
    }

    public float OffsetFor(StatDef stat)
    {
        if (stat == QualityOffsetDefOf.MW_PawnCreatedQualityOffset)
        {
            return qualityOffset;
        }
        if (stat == StatDefOf.WorkSpeedGlobal)
        {
            return workSpeedOffset;
        }
        if (stat == StatDefOf.ResearchSpeed)
        {
            return researchSpeedOffset;
        }
        if (stat == StatDefOf.EntityStudyRate)
        {
            return entityStudyRateOffset;
        }
        return 0f;
    }
}
```

- [ ] **Step 3: 编译确认（此时 Gizmo_UniversalToolbox 尚未存在，先注释掉 gizmo 字段与 CompGetWornGizmosExtra 编译通过，Task 8 再加回——或直接跳到 Task 8 写完一起编译）**

`cd /home/lisanhu/mine/workspace/rimworld/MyWeapons/1.6/Source && dotnet build`
预期：Build succeeded（若跳过 gizmo 引用）。

- [ ] **Step 4: 提交**

`git add -A && git commit -m "feat: universal toolbox comp with dynamic stat values"`

---

### Task 8: Gizmo_UniversalToolbox

**Files:**
- Create: `/home/lisanhu/mine/workspace/rimworld/MyWeapons/1.6/Source/Gizmo_UniversalToolbox.cs`

**Interfaces:**
- Consumes: Task 7 的 comp 字段与方法；Task 6 定稿的 PANEL 布局常量（行高 16、
  边距 5、label 64、value 38、bar 高 10 等——以 Task 6 最终值为准）。
- Produces: `Gizmo_UniversalToolbox(CompUniversalToolbox)`，`GetWidth() => 240f`。

- [ ] **Step 1: 写 Gizmo（布局常量与 Task 6 最终 PNG 保持一致）**

```csharp
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
    private const float LabelWidth = 64f;
    private const float ValueWidth = 38f;
    private const float Gap = 4f;
    private const float BarHeight = 10f;

    private readonly CompUniversalToolbox comp;
    private readonly bool[] dragging = new bool[4];
    private int editingRow = -1;
    private string editBuffer;

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
            float y = Margin;
            for (int i = 0; i < Rows.Length; i++)
            {
                DrawRow(new Rect(outer.x + Margin, outer.y + y, PanelWidth - 2f * Margin, RowHeight), i);
                y += RowHeight + 1f;
            }
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[MyWeapons] UniversalToolbox gizmo error: {e}", 918273645);
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
            float parsed = isInt ? Mathf.RoundToInt(value) : value;
            Widgets.TextFieldNumeric(valueRect, ref parsed, ref editBuffer, 0f, max);
            if (!editBuffer.NullOrEmpty() && float.TryParse(editBuffer, out float committed))
            {
                set(comp, isInt ? Mathf.Round(committed) : committed);
                comp.Notify_SettingsChanged();
            }
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
            {
                editingRow = -1;
                Event.current.Use();
            }
        }
        else
        {
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = Color.white;
            Widgets.Label(valueRect, isInt ? $"+{(int)value}" : $"+{value:0.0}");
            if (Widgets.ButtonInvisible(valueRect))
            {
                editingRow = i;
                editBuffer = isInt ? ((int)value).ToString() : value.ToString("0.0");
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
```

注意点（实现时核对）：
- 品质行 DraggableBar 的 `target` 是 0–1 比例，`Mathf.Round(scaled)` 吸附整数。
- `editingRow` 时该行的数值区变为输入框；点击其它行/Enter 退出编辑。
- 外部语言文件：若需要英文 label，走 `Languages` 目录 keyed 翻译；初版中文硬编码
  与 mod 现有风格一致（mod 内其它字符串也是硬编码）。

- [ ] **Step 2: 编译通过**

`cd /home/lisanhu/mine/workspace/rimworld/MyWeapons/1.6/Source && dotnet build`
预期：Build succeeded，`1.6/Assemblies/MyWeapons.dll` 更新。

- [ ] **Step 3: 提交**

`git add -A && git commit -m "feat: universal toolbox gizmo panel"`

---

### Task 9: Harmony postfix StatOffsetFromGear

**Files:**
- Create: `/home/lisanhu/mine/workspace/rimworld/MyWeapons/1.6/Source/StatOffsetFromGearPatch.cs`

**Interfaces:**
- Consumes: Task 7 `CompUniversalToolbox.OffsetFor`。

- [ ] **Step 1: 写补丁（Harmony 由现有 `MyWeapons.cs:23` 的 `harmony.PatchAll` 自动注册）**

```csharp
using HarmonyLib;
using RimWorld;
using Verse;

namespace MyWeapons;

[HarmonyPatch(typeof(StatWorker), nameof(StatWorker.StatOffsetFromGear))]
public static class StatOffsetFromGearPatch
{
    public static void Postfix(Thing gear, StatDef stat, ref float __result)
    {
        CompUniversalToolbox comp = gear.TryGetComp<CompUniversalToolbox>();
        if (comp != null)
        {
            __result += comp.OffsetFor(stat);
        }
    }
}
```

- [ ] **Step 2: 编译通过** → `dotnet build` succeeded。

- [ ] **Step 3: 提交**

`git add -A && git commit -m "feat: inject toolbox offsets via StatOffsetFromGear postfix"`

---

### Task 10: XML——万能工具箱 def + 旧三 def 去配方

**Files:**
- Modify: `/home/lisanhu/mine/workspace/rimworld/MyWeapons/Defs/SuperApparels/SuperApparels.xml`

- [ ] **Step 1: 删除三个旧 def 中的 `<recipeMaker>...</recipeMaker>` 节点**

涉及行（当前文件）：`MW_Toolbox`（133-139 行）、`MW_ToolboxTwo`（195-201 行）、
`MW_ToolboxThree`（260-266 行）。def 其余部分一律不动。

- [ ] **Step 2: 在文件末尾（`</Defs>` 前）新增 MW_UniversalToolbox**

```xml
    <ThingDef ParentName="ApparelBase">
        <defName>MW_UniversalToolbox</defName>
        <tradeability>None</tradeability>
        <label>universal toolbox</label>
        <description>Universal toolbox. Adjustable quality offset, global work speed, research speed and entity study rate via its command panel when worn.</description>
        <thingClass>Apparel</thingClass>
        <useHitPoints>false</useHitPoints>
        <generateCommonality>0</generateCommonality>
        <generateAllowChance>0</generateAllowChance>
        <graphicData>
            <texPath>MyWeapons/toolbox</texPath>
            <graphicClass>Graphic_Single</graphicClass>
        </graphicData>
        <recipeMaker>
            <workSkill>Crafting</workSkill>
            <recipeUsers>
                <li>CraftingSpot</li>
                <li>PraySpot</li>
            </recipeUsers>
        </recipeMaker>
        <tickerType>Normal</tickerType>
        <techLevel>Spacer</techLevel>
        <statBases>
            <Mass>0.000001</Mass>
            <WorkToMake>1</WorkToMake>
            <Flammability>0</Flammability>
            <MarketValue>0</MarketValue>
            <DeteriorationRate>0</DeteriorationRate>
            <EquipDelay>0</EquipDelay>
        </statBases>
        <comps>
            <li Class="MyWeapons.CompProperties_UniversalToolbox"/>
        </comps>
        <thingSetMakerTags>
            <li>RewardStandardQualitySuper</li>
        </thingSetMakerTags>
        <stuffCategories>
            <li>Metallic</li>
        </stuffCategories>
        <costStuffCount>1</costStuffCount>
        <thingCategories Inherit="False">
            <li>MW_Apparels</li>
        </thingCategories>
        <apparel>
            <countsAsClothingForNudity>false</countsAsClothingForNudity>
            <careIfWornByCorpse>false</careIfWornByCorpse>
            <careIfDamaged>false</careIfDamaged>
            <wearPerDay>0</wearPerDay>
            <bodyPartGroups>
                <li>Shoulders</li>
            </bodyPartGroups>
            <layers>
                <li>MW_CarryWith</li>
            </layers>
            <tags>
                <li>BeltDefense</li>
            </tags>
            <developmentalStageFilter>Child, Adult</developmentalStageFilter>
        </apparel>
    </ThingDef>
```

- [ ] **Step 3: XML 合法性检查**

`python3 -c "import xml.dom.minidom; xml.dom.minidom.parse('/home/lisanhu/mine/workspace/rimworld/MyWeapons/Defs/SuperApparels/SuperApparels.xml'); print('ok')"`
预期输出 `ok`。再确认旧 def 无残留配方：
`grep -c "recipeMaker" Defs/SuperApparels/SuperApparels.xml` → 预期 `1`。

- [ ] **Step 4: 提交**

`git add -A && git commit -m "feat: add universal toolbox def, retire legacy toolbox recipes"`

---

### Task 11: 构建产物与验证

**Files:**
- Modify: `/home/lisanhu/mine/workspace/rimworld/MyWeapons/1.6/Assemblies/MyWeapons.dll`（构建产物）

- [ ] **Step 1: 干净构建**

```bash
cd /home/lisanhu/mine/workspace/rimworld/MyWeapons/1.6/Source
dotnet build -v m 2>&1 | tail -5
ls -la ../Assemblies/MyWeapons.dll
```
预期：0 Warning 0 Error（或仅有既存 warning），dll 时间戳更新。

- [ ] **Step 2: 静态自检清单**

- `ilspycmd ../Assemblies/MyWeapons.dll -l c | grep -E "CompUniversalToolbox|Gizmo_UniversalToolbox|StatOffsetFromGearPatch"` → 三个类都在
- XML：`grep -c recipeMaker` 为 1；`grep MW_UniversalToolbox` 命中

- [ ] **Step 3: 游戏内验证（人工，向用户报告步骤）**

启动 RimWorld（mod 目录若通过符号链接/拷贝部署，确认 1.6/Assemblies 与 Defs 生效）：
1. Dev mode spawn `MW_UniversalToolbox`，殖民者穿上
2. 选中该殖民者 → gizmo 区出现 240×75 面板，与 Task 6 PNG 一致
3. 拖品质条到 +5 → 制作一件物品确认传奇；拖工作速度到 +10 →
   小人属性页 WorkSpeedGlobal breakdown 显示万能工具箱 +10
4. 点击数值输入 12.3 回车 → 值生效且存档/读档后保持
5. 旧存档中已有的三个旧工具箱穿戴正常、无红字，制作台不再出现旧配方

- [ ] **Step 4: 提交**

`git add -A && git commit -m "build: MyWeapons 1.6 with universal toolbox"`

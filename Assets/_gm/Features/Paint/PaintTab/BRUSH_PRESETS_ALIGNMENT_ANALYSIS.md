# Brush Presets alignment – line-by-line structure

## Goal
The dropdown (chevron), folder icon, and ABR name (e.g. "Built-in") must sit **directly under** the "Load ABR/PNG…" button, with 0.75 (3px) spacing – i.e. same left edge.

---

## 1. Hierarchy that controls horizontal position

### When created by code (CreateSection returns ScrollContent)
```
BrushPresetsSection (= ScrollContent)
├── VerticalLayoutGroup (padding 3,3,3,3)  ← ONLY source of left offset for both rows
├── BrushPresets_Buttons (child 0)
│   └── HorizontalLayoutGroup (padding 3,0,3,3)  ← "Load ABR/PNG" starts at 3px
└── BrushPresets_Content (child 1) = AlphaPicker.transform = _gridRoot
    ├── VerticalLayoutGroup (padding 0,3,3,3)   ← left 0 so sections align
    └── Section_Built-in, Section_Splatter, ...
        ├── VerticalLayoutGroup (padding 0,3,3,3)  ← left 0
        └── Header
            └── HorizontalLayoutGroup (padding 0,0,3,0)  ← left 0
                ├── Chevron  ← should be at 3px (from ScrollContent padding)
                ├── FolderIcon
                └── Title ("Built-in")
```

So the **only** left offset for both the button row and the Built-in row is the **ScrollContent’s VLG padding left = 3**. Everything else uses left = 0.

### When section comes from prefab/scene (BrushPresetsSection = section root)
```
BrushPresetsSection (= 3_BrushPresets, section ROOT)
├── VerticalLayoutGroup (outerVlg, padding 0,0,0,2)
├── Header ("Brush Presets" title)
├── Content (viewport with ScrollRect)
│   └── ScrollContent  ← REAL parent for button row + picker
│       ├── (button row and picker should be HERE)
│       └── ...
```

If we add the button row and picker to **BrushPresetsSection** when it’s the section root, they become siblings of **Header** and **Content**, not children of **ScrollContent**. So they sit **outside** the scroll and are laid out by the section root’s VLG. The scroll view’s content might be empty or different, and the Built-in row can end up in the wrong place or “in the middle” because we’re not touching the real ScrollContent’s layout.

---

## 2. Code path that sets position

### PaintTab_KritaLayout_UI.CreateSection (lines 79–163)
- Returns **scrollInnerRect** (ScrollContent).
- ScrollContent has: `anchorMin=(0,1)`, `anchorMax=(1,1)`, `pivot=(0,1)`.
- **scrollInnerVlg.padding = (3,3,3,3)** → left 3 for all children.

### PaintTab_CollectPaintUI.CreateBrushPresetsRuntime(parent) (lines 276–327)
- **parent** must be the **ScrollContent** (the rect that has the VLG with 3px padding).
- Adds **BrushPresets_Buttons** and **BrushPresets_Content** as children of `parent`.
- Content VLG: **padding (0, 3, 3, 3)** so sections have no extra left.
- AlphaPicker is on Content; **_gridRoot** defaults to `transform` = Content.

### PaintTab_CollectPaintUI.CollectNow – Brush Presets block (lines 89–170)
- **pickerParent = alphaPicker.transform.parent** → must be ScrollContent.
- We set **parentRect** (ScrollContent) to anchor (0,1)–(0,1), pivot (0,1), and width from viewport.
- We set **parentVlg.padding = (3,3,3,3)** and **pickerVlg.padding = (0,3,3,3)**.
- We call **RebuildGrid()** which creates sections with section/header padding left = 0.

If **BrushPresetsSection** is the section root (prefab), then:
- **pickerParent** is still the **scroll content** only if the picker was already reparented into it. If the picker was created with `parent = BrushPresetsSection` (section root), then **pickerParent = section root**, and we’re applying padding to the **section root’s** VLG, not the ScrollContent’s. The ScrollContent (inside Content) never gets our padding, so its children can stay centered or use prefab padding.

---

## 3. Root cause of “dropdown not moving”

- **We always use `_layout.BrushPresetsSection` as the parent** when creating and when applying layout.
- When **BrushPresetsSection** is the **section root** (e.g. from prefab):
  - Button row and picker are added as **children of the section root**, not of **ScrollContent**.
  - So they’re laid out by the section root’s VLG (Header, Content, btnRow, Content).
  - The **ScrollContent** (inside Content) is never used and may have different/prefab padding or alignment, so anything that *is* inside it doesn’t align with our code.
- Even when we **reparent** the picker with `SetParent(_layout.BrushPresetsSection)`, we’re still reparenting to the section root, so the picker stays **outside** the scroll. The Built-in row is then laid out by the section root, and can appear “in the middle” if the section root’s layout or width is different.

---

## 4. Fix

- **Resolve the actual ScrollContent** from `BrushPresetsSection`:
  - If there is a child named `"Content"` with a **ScrollRect**, use **ScrollRect.content** as the scroll content.
  - Otherwise treat **BrushPresetsSection** as the scroll content (runtime-created case).
- Use this **resolved scroll content** for:
  - **CreateBrushPresetsRuntime(parent)** – pass the resolved scroll content.
  - **CollectNow** – reparent picker to resolved scroll content, set padding and alignment on it, and force rebuild on it.

Then the dropdown (chevron), folder icon, and ABR name will always be laid out by the same VLG as the "Load ABR/PNG…" button, with a single 3px left padding.

# Paint Tab – Scaffolding Audit (Nothing Wiped)

This audit confirms the Paint tab always shows **all five sections** and what was fixed so content is not missing.

---

## Root cause of "only layers / everything missing"

**CollectNow** only called `SetCreateSectionsIfMissing(true)` when **BrushPresetsSection** was null. So:

- If the layout prefab had **BrushPresetsSection** assigned but **ToolchestRow**, **ToolOptionsSection**, or **ColorPaletteSection** were unassigned (or cleared), those sections were **never created**.
- Result: Only sections with non-null refs got populated (e.g. only Layers if only LayersSection was set). Toolchest, Brush Presets, Tool Options, and Color/Palette could all be missing.

**No code was removed.** The logic that populates each section is still there; the section **transforms** were missing when their layout refs were null.

---

## Fix applied

**PaintTab_CollectPaintUI.CollectNow()** now triggers **CreateSectionsIfMissing** when **any** of the five sections is null:

```csharp
if (_layout.ToolchestRow == null || _layout.LayersSection == null || _layout.BrushPresetsSection == null
    || _layout.ToolOptionsSection == null || _layout.ColorPaletteSection == null)
    _layout.SetCreateSectionsIfMissing(true);
```

So:

1. **Toolchest row** – created if null.
2. **Layers section** – created if null.
3. **Brush Presets section** – created if null.
4. **Tool Options section** – created if null.
5. **Color / Palette section** – created if null.

After this, all five section refs on the layout are non-null and CollectNow runs every block that populates them.

---

## What CollectNow does (no early exit between sections)

| Order | Block | Condition | Action |
|-------|--------|-----------|--------|
| 0 | (pre) | _layout == null | return (only exit) |
| 1 | (pre) | Any section null | SetCreateSectionsIfMissing(true) |
| 2 | Toolchest | ToolchestRow != null, WorkflowRibbon_UI / SD_WorkflowOptionsRibbon_UI instances | Reparent ribbons into ToolchestRow |
| 3 | Layers | LayersSection != null | Get scroll content, ensure stack, find/create panel, SetLayerStack, Add Layer button, reparent |
| 4 | Brush Presets | GetBrushPresetsScrollContent(BrushPresetsSection) != null | Ensure scroll, find/create AlphaPicker, reparent, layout, RebuildGrid |
| 5 | Tool Options | ToolOptionsSection != null && childCount <= 1 | CreateToolOptionsRuntime |
| 6 | Color/Palette | ColorPaletteSection != null | EnsurePaletteLoadButton, find/create PaletteSwatches_UI, reparent |
| 7 | (post) | — | _collected = true; ForceRebuildLayoutImmediate(root + ToolchestRow) |

There is **no return** between sections. All blocks run when their section ref is non-null.

---

## Checklist (nothing removed)

- [x] Toolchest row populated when ToolchestRow != null and ribbon instances exist.
- [x] Layers section: stack, panel, Add Layer button, list root – all wired.
- [x] Brush Presets: scroll content, AlphaPicker (find or create), layout and RebuildGrid.
- [x] Tool Options: CreateToolOptionsRuntime when section empty (childCount <= 1).
- [x] Color/Palette: palette load button, PaletteSwatches_UI (find or create), reparent.
- [x] CreateSectionsIfMissing creates all five sections when **any** is null (fix applied).

---

## If the tab still looks empty

1. **Layout ref** – PaintTab_CollectPaintUI must have a valid _layout (same GameObject as PaintTab_KritaLayout_UI or assigned). OnEnable uses GetComponent if _layout is null.
2. **Paint panel active** – The Paint panel GameObject (and its parent) must be active when the user opens the Paint tab so OnEnable runs and CollectNow executes.
3. **Ribbons / picker / palette** – Toolchest content needs WorkflowRibbon_UI and SD_WorkflowOptionsRibbon_UI instances in the scene. Brush Presets and Color sections create their UI if not found; ensure no script disables or destroys those managers.

This file is the single source of truth for “nothing was wiped; scaffolding is enforced.”

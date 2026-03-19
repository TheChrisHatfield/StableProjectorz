# Brush Presets – Line-by-line flow and logic review

**Status**: Fixes below were applied. Code now forces section creation when null and uses a single scroll (Content + Buttons directly on ScrollContent). Keep this doc in sync when changing brush presets flow.

## 1. Entry point: CollectNow() [PaintTab_CollectPaintUI]

- **L97–98** `if (_layout.BrushPresetsSection == null) _layout.SetCreateSectionsIfMissing(true)` – **APPLIED**: Sections are created when null so the brush presets block is never skipped.
- **L148** `GetBrushPresetsScrollContent(_layout.BrushPresetsSection)` – scrollContent is the section’s ScrollRect.content (or section root if no nested scroll).
- **L154–156** Picker created via `CreateBrushPresetsRuntime(scrollContent)` when not found; Buttons and Content are direct children of scrollContent (no nested scroll).
- **L228** `alphaPicker.RebuildGrid()` – runs after picker is reparented and layout applied.

---

## 2. CreateSection [PaintTab_KritaLayout_UI]

- **L71** `_brushPresetsSection = CreateSection(root, "3_BrushPresets", ..., 140, 1f)` – only when `_brushPresetsSection == null` and `_createSectionsIfMissing` was true in Awake.
- **L79–86** Section GameObject: RectTransform, LayoutElement (minHeight 140, flex 1).
- **L104–111** VerticalFlex: outerVlg, then AddSectionHeader, then **Content** (contentGo) with Image, Mask.
- **L117–134** **ScrollContent** (scrollInnerGo) = child of Content. Content has **ScrollRect**: viewport = **contentRect** (Content itself), **content** = **scrollInnerRect** (ScrollContent).
- **L163** `return scrollInnerRect` – so **BrushPresetsSection** is **ScrollContent**, not the section root.

**Hierarchy after CreateSection**:
```
Section (3_BrushPresets)
  Header
  Content (viewport + ScrollRect; viewport = self, content = ScrollContent)
    ScrollContent  ← BrushPresetsSection
```

---

## 3. CreateBrushPresetsRuntime(parent) where parent = ScrollContent

- **APPLIED**: Buttons and Content are added **directly to parent** (ScrollContent). No BrushPresets_Scroll or Viewport.

**Current hierarchy after CreateBrushPresetsRuntime**:
```
ScrollContent (section’s ScrollRect.content)
  BrushPresets_Buttons (first sibling)
  BrushPresets_Content (picker component here; _gridRoot = this; sections as children)
```

Single scroll (section’s scroll); Content uses ContentSizeFitter.PreferredSize so height grows with sections.

---

## 4. RebuildGrid() [BrushRibbon_UI_AlphaPicker]

- Resolve _gridRoot and _thumbnailTemplate; safe for pre-Start call.
- Clear thumb list, EnsureVerticalLayoutOnGridRoot, destroy all children except _thumbnailTemplate.
- If no manager or no entries → AddPlaceholderText and return.
- Create one collapsible section per group under _gridRoot; then ReapplyFlushLayoutToAllSections, layout rebuild, Canvas.ForceUpdateCanvases.

**Correlation**: _gridRoot = picker.transform = **BrushPresets_Content**. Sections are children of Content. Content has VLG + ContentSizeFitter so height comes from sections.

---

## 5. Logic / correlation fixes — APPLIED

1. **Force section creation when BrushPresetsSection is null** – Done in CollectNow (L99–100): `if (_layout.BrushPresetsSection == null) _layout.SetCreateSectionsIfMissing(true)`.
2. **Single scroll (no nested scroll)** – Done in CreateBrushPresetsRuntime: Content and Buttons are direct children of ScrollContent; no BrushPresets_Scroll/Viewport.

When editing this flow, re-check: GetBrushPresetsScrollContent, EnsureBrushPresetsScrollContentCanGrow, and picker reparent/layout in CollectNow.

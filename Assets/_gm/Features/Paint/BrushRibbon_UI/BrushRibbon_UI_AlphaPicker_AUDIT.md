# Line-by-line audit: BrushRibbon_UI_AlphaPicker.cs

## 1–14: Usings and class

| Lines | Notes |
|-------|------|
| 1–4 | Usings OK. SimpleFileBrowser used only in OnLoadBrushFileClicked. |
| 8–11 | Class summary is accurate. |
| 12–13 | MonoBehaviour, no issues. |

## 14–30: Serialized fields

| Lines | Notes |
|-------|------|
| 14–15 | _brushAlphasMGR, _hardness: optional; resolved in Start. |
| 17–18 | _gridRoot, _thumbnailTemplate: critical; null-checked in RebuildGrid / Start. |
| 19–23 | Buttons: all null-checked before use. |
| 24 | _thumbnailSize = 32: used for grid and thumb size; consistent. |
| 25–28 | Optional labels; null-checked in UpdateSelectedBrushLabels. |
| 30 | _thumbInstances: instance list, cleared in RebuildGrid; indices match AllEntries. |

## 32–54: Start / OnDestroy

| Lines | Notes |
|-------|------|
| 34–35 | Fallback: instance then FindObjectOfType(true). FindObjectOfType can be slow; acceptable at Start. |
| 37 | _hardness same pattern. |
| 39 | _gridRoot fallback to transform is correct. |
| 40–42 | Template created if null and hidden; correct. |
| 43–50 | Button listeners: no duplicate subscription; single assignment. |
| 51–52 | Static events: subscribed in Start. |
| 53 | RebuildGrid() on Start: correct. |
| 56–60 | OnDestroy: unsubscribes both events; no leak. |

## 62–68: RefreshSelectedBrushLabelsFromBrushSystem

| Lines | Notes |
|-------|------|
| 63–66 | Null check then UpdateSelectedBrushLabels(CurrentIndex). Correct. |

## 69–88: CreateDefaultThumbnailTemplate

| Lines | Notes |
|-------|------|
| 71 | Parent = transform; fine for runtime template. |
| 73 | AddComponent<RectTransform>: on new GameObject this replaces default Transform with RectTransform in UI context. |
| 74 | sizeDelta uses _thumbnailSize; correct. |
| 75–77 | Image + Button; RawImage child with insets. |
| 78–84 | RawImage rect: anchored stretch, 2px inset. |
| 86 | Returned inactive; correct. |

## 90–129: OpenLoadBrushDialog / OnLoadBrushFileClicked

| Lines | Notes |
|-------|------|
| 96–99 | mgr resolution and null return; OK. |
| 100–104 | FileBrowser filters and default; OK. |
| 106–121 | Callback: path check, LoadFromExternalPath, RebuildGrid, status text, SelectBrushAtIndex(3) when ok and count > 3. |
| 107 | _brushAlphasMGR = mgr: keeps reference after load; correct. |

## 131–181: Round / Refresh / Delete handlers

| Lines | Notes |
|-------|------|
| 143–163 | OnDeleteBrushClicked: null check, IsCustomAlpha guard, RemoveCustomBrushAt, RebuildGrid, status. Correct. |
| 171–180 | SelectBrushAtIndex: null check, CurrentIndex set, hardness UI, suggested params, ApplyBrushOptionsToRibbon, labels, highlight. Correct. |

## 203–228: ApplyBrushOptionsToRibbon / UpdateSelectedBrushLabels

| Lines | Notes |
|-------|------|
| 207–218 | Prefer BrushRibbon_UI_Size.instance; fallbacks to SD_WorkflowOptionsRibbon_UI then BrushRibbon_UI. |
| 215, 221, 228 | suggestedRoundness01 > 0 ? value : 1f; avoids 0. OK. |
| 232–250 | UpdateSelectedBrushLabels: bounds check, entry.name, attrs from BrushRibbon_UI_Size. Correct. |
| 244 | New List<string> per call; minor alloc, acceptable. |

## 252–290: RebuildGrid

| Lines | Notes |
|-------|------|
| 254–255 | Early exit if _gridRoot or _thumbnailTemplate null; mgr resolution. |
| 257 | _thumbInstances.Clear(): no Destroy of old thumbs; they are under section children destroyed next. Correct. |
| 258–260 | EnsureVerticalLayoutOnGridRoot(); then destroy all children of _gridRoot. Order correct. |
| 262–266 | No mgr or no entries: AddPlaceholderText and return. |
| 268–271 | groups/entries null check. |
| 273–277 | Skip groups with null or empty indices; create section per group. |
| 279–280 | HighlightSelected and UpdateSelectedBrushLabels for current index. |
| 282–288 | ForceRebuildLayoutImmediate(rootRect) and Canvas.ForceUpdateCanvases() so layout and scroll content height update; needed so grids get height. |

## 292–302: EnsureVerticalLayoutOnGridRoot / GetLegacyFont

| Lines | Notes |
|-------|------|
| 294 | Only runs if GridLayoutGroup present; replaces with VerticalLayoutGroup and padding. Correct. |
| 304–305 | HeaderIconSize = 8; used for chevron and folder icon. |
| 307–312 | GetLegacyFont: static cache; null then load. OK. |

## 314–324: GridHeightForCount

| Lines | Notes |
|-------|------|
| 316 | count <= 0 return 0. |
| 318–319 | Width from _gridRoot.rect or 200f fallback when 0. |
| 320–322 | cols from width / (cell + spacing), rows from count, height = rows * cell + (rows-1)*spacing, min _thumbnailSize. Correct. |

## 326–437: CreateCollapsibleSection

| Lines | Notes |
|-------|------|
| 328–330 | sectionGo parented to parent; RectTransform + VerticalLayoutGroup. |
| 331–329 | VLG: childControlHeight false so LayoutElement preferredHeight is used. |
| 332 | sectionRect: AddComponent<RectTransform> on GameObject already has Transform; in UI hierarchy this is typically a RectTransform. Adding RectTransform to a plain GameObject may not replace Transform in all Unity versions; usually sectionGo is under Canvas so may already be RectTransform. Low risk. |
| 336–337 | Header: parent sectionGo, RectTransform, LayoutElement (preferredHeight 14). |
| 338–340 | headerBg: transparent Image, raycastTarget true for full-row click. |
| 341–346 | HorizontalLayoutGroup for chevron + icon + title. |
| 348–361 | Chevron: 8x8 LayoutElement, Text "▼", GetLegacyFont(), raycastTarget false. |
| 363–372 | Folder icon: 8x8, GetFolderIconSprite(), same grey, raycastTarget false. |
| 374–386 | Title: flexibleWidth, preferredHeight 8, groupName text. |
| 388–394 | Button on header, targetGraphic = headerBg. |
| 398–410 | gridGo: child of section; expandedHeight = Max(GridHeightForCount, _thumbnailSize+6). gridRect = gridGo.transform as RectTransform: under UI parent this is often RectTransform; if not, sizeDelta not set (no crash). LayoutElement + CanvasGroup + GridLayoutGroup. |
| 412–428 | expanded = true. Listener: null check gridGo/gridLE/arrowText; toggle expanded; set gridLE.preferredHeight (0 or expandedHeight); gridCg alpha/blocksRaycasts/interactable; arrow text; rebuild section then root layout, ForceUpdateCanvases. Correct. |
| 430–436 | foreach index in indices: instantiate template under gridGo, set preview texture, button -> SelectBrushAtIndex(index), sizeDelta; add to _thumbInstances. Order preserves global index. Correct. |

## 439–464: GetFolderIconSprite

| Lines | Notes |
|-------|------|
| 441 | Static cache. |
| 443–446 | 16x16 Texture2D, clear. |
| 448–455 | Fill folder shape (tab + body) with white; sprite tinted in UI. |
| 456–458 | Apply(), Sprite.Create(), cache and return. Texture is not explicitly destroyed; sprite holds reference. Acceptable for one small texture. |

## 466–480: HighlightSelected

| Lines | Notes |
|-------|------|
| 468–476 | Iterate _thumbInstances; get Image, set color by i == selectedIndex. Null checks on go and bg. Correct. |

## 482–497: AddPlaceholderText

| Lines | Notes |
|-------|------|
| 484–493 | Placeholder under _gridRoot, LayoutElement, Text with GetLegacyFont(); added to _thumbInstances (so list non-empty; HighlightSelected won’t match). Correct. |

---

## Summary: issues and recommendations

### Potential issues

1. **Line 332 (sectionRect)**: `sectionGo.AddComponent<RectTransform>()` – GameObject from `new GameObject()` has a Transform. In a Canvas hierarchy the child often gets a RectTransform when parented. Adding RectTransform can be redundant or platform-dependent. **Recommendation**: use `sectionGo.transform as RectTransform` or ensure section is created under a UI parent so it’s a RectTransform; avoid relying on AddComponent<RectTransform> for a new GameObject.

2. **Line 361 (headerRect)**: Same pattern: `headerGo.AddComponent<RectTransform>()`. Same recommendation as above.

3. **Line 364**: `gridGo.transform as RectTransform` – when gridGo is parented under sectionGo (which has a RectTransform), Unity may not auto-convert gridGo’s Transform to RectTransform. So gridRect can be null and sizeDelta not set; layout then relies on LayoutElement only. No crash; optional improvement: ensure grid is created with a RectTransform (e.g. create under a UI parent that forces it).

4. **GetFolderIconSprite texture**: Texture2D created and embedded in Sprite; never destroyed. One-off 16x16 is acceptable; document or add a comment that the texture is intentionally kept for the sprite lifetime.

### Redundancy

- **Line 401 vs 406**: `gridRect.sizeDelta` (line 401) and `gridLE.preferredHeight` (406) both set height. When parent uses LayoutElement, preferredHeight wins. sizeDelta is a safe extra hint when gridRect is non-null.

### What’s solid

- Null checks before use of _brushAlphasMGR, _gridRoot, labels, and in the collapse listener.
- Event subscribe/unsubscribe in Start/OnDestroy.
- _thumbInstances order matches global brush indices for HighlightSelected and selection.
- Collapse uses CanvasGroup (alpha/raycasts) + preferredHeight so layout always sees the grid; no SetActive on grid.
- RebuildGrid forces layout and Canvas update so initial and post-rebuild layout is correct.
- GetGroupsForUI / indices null and empty checks; empty groups skipped.

### Optional improvements

- **Font size for 8px header**: Arrow and title use fontSize 12 with 8px icon; consider 10 for title to match scale.
- **GetComponent<RectTransform>()**: Section/header created as `new GameObject()`; under Canvas, consider creating from a prefab or using a factory that guarantees RectTransform to avoid AddComponent<RectTransform> ambiguity.

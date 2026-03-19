# Brush Presets – execution and connectivity audit

## Entry points (when does brush panel code run?)

1. **PaintTab_CollectPaintUI.OnEnable()** (lines 46–55)  
   - Runs when: Paint panel GameObject is enabled (user switches to Paint tab).  
   - Line 48: `if (_layout == null) _layout = GetComponent<PaintTab_KritaLayout_UI>();`  
   - Line 49: `if (_layout != null)` → then CollectNow() and StartCoroutine(RefreshBrushPresetsLayoutWhenReady()).  
   - **Gap risk:** If CollectPaintUI is on a child that’s disabled, OnEnable might not run when the tab is shown. Assumption: CollectPaintUI is on the same root (or an active child) as the layout.

2. **BrushRibbon_UI_AlphaPicker.OnEnable()** (lines 66–92)  
   - Runs when: AlphaPicker’s GameObject is enabled (same time as panel, since picker is under the panel).  
   - Re-applies _gridRoot, template, EnsureVerticalLayoutOnGridRoot(), RebuildGrid(), parent VLG padding, ForceRebuildLayoutImmediate(parent).  
   - **Connectivity:** picker.transform.parent = scroll content (BrushPresets_Content’s parent is scrollContent). So parent VLG is the scroll content’s VerticalLayoutGroup. ✓

3. **BrushRibbon_UI_AlphaPicker.Start()** (lines 43–64)  
   - Runs once when the picker’s GameObject is first enabled.  
   - Sets _gridRoot = transform if null, creates default template if null, subscribes to events, RebuildGrid().  
   - **Order:** OnEnable can run before Start. OnEnable sets _gridRoot and template if null, so RebuildGrid in OnEnable is safe.

---

## CollectNow() – line-by-line to Brush Presets

- **74:** `if (_layout == null) return;` → Without layout, nothing runs. **Requirement:** _layout must be set (serialized or same GameObject as layout).
- **76–78:** If BrushPresetsSection is null, call SetCreateSectionsIfMissing(true). That creates sections and sets _brushPresetsSection (and _toolchestRow, etc.). So after this, BrushPresetsSection can be non-null.
- **79:** `if (_layout.ToolchestRow == null) return;`  
  **CRITICAL GAP:** If ToolchestRow is still null (e.g. layout not on expected root, or CreateSectionsIfMissing didn’t run), CollectNow **exits here** and the Brush Presets block (124–217) **never runs**. So scrollContent is never computed, picker is never found/created, layout is never applied.  
  **Fix:** Do not gate the whole CollectNow on ToolchestRow. Let each section block run on its own; only the Toolchest blocks need ToolchestRow.

- **125:** `var scrollContent = GetBrushPresetsScrollContent(_layout.BrushPresetsSection);`  
  - If BrushPresetsSection is null → returns null.  
  - If section root (prefab): looks for child named "Content", then ScrollRect.content → scrollContent.  
  - If runtime-created: BrushPresetsSection is already ScrollContent → returns it. ✓  
- **126:** `if (scrollContent != null)` → Brush Presets block runs only when scrollContent is non-null. So we must not return earlier (e.g. at line 79) when BrushPresetsSection exists but ToolchestRow is null.

- **128:** `FindObjectOfType<BrushRibbon_UI_AlphaPicker>(true)` → finds picker even if inactive.  
- **129–130:** If no picker, CreateBrushPresetsRuntime(scrollContent) creates btn row + content + picker, parents to scrollContent. ✓  
- **132–140:** Reparent picker to scrollContent if not already child. ✓  
- **141:** `if (alphaPicker != null)` → all layout and RebuildGrid run. Connectivity: pickerParent = scrollContent, so parent VLG and scrollContent layout are applied. ✓  

---

## RebuildGrid() – connectivity

- **342–346:** _gridRoot == null → set to transform. _thumbnailTemplate == null → CreateDefaultThumbnailTemplate(). If either still null after that, return (line 346). So we need template to exist (created or serialized). ✓  
- **348:** _brushAlphasMGR can be set from instance or FindObjectOfType. ✓  
- **350:** EnsureVerticalLayoutOnGridRoot() applies VLG spacing 2, padding 0 on _gridRoot. ✓  
- **351–356:** Destroy all children of _gridRoot except template. ✓  
- **358–363:** If no entries, AddPlaceholderText and return. So empty manager shows placeholder. ✓  
- **364–366:** groups/entries null check. ✓  
- **368–372:** CreateCollapsibleSection for each group. Sections get spacing 1, grid spacing 2. ✓  
- **382–379:** scrollContent = _gridRoot.parent; ForceRebuildLayoutImmediate(scrollContent). So scroll content height updates. **_gridRoot.parent** must be the scroll content (picker’s transform is BrushPresets_Content; its parent is scrollContent). ✓  

---

## Coroutine RefreshBrushPresetsLayoutWhenReady

- **59:** `yield return null` → runs rest after one frame.  
- **60:** If _layout is null we use null for BrushPresetsSection → scrollContent = null → yield break. So we need _layout to still be set next frame.  
- **63:** FindObjectOfType<BrushRibbon_UI_AlphaPicker>(true). If picker was created in CollectNow, we find it. ✓  
- **64–67:** RebuildGrid(), ForceRebuildLayoutImmediate(scrollContent), ForceUpdateCanvases.  
- **Risk:** If the Paint panel is disabled before the next frame, the coroutine is destroyed and the rest never runs. So this is a best-effort second pass; primary path is CollectNow + AlphaPicker.OnEnable.

---

## Fix applied

- Remove the early return `if (_layout.ToolchestRow == null) return;` so that when ToolchestRow is null we still run the rest of CollectNow (Layers, Brush Presets, Tool Options, Color). Each block already checks its own section for null.

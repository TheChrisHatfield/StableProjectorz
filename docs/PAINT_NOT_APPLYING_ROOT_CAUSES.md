# Paint Not Applying – Likely Causes

Use this checklist to find why brush strokes (or bucket/eraser) don’t stick or don’t show on the mesh.

---

## 1. Brush never starts (no stroke at all)

| Cause | What to check |
|-------|----------------|
| **No 3D model** | `maskResolution().z` is 0 → we never create brush path RTs and never set `_isPainting`. **Fix:** Import/load a 3D model first. |
| **Select mode on** | `ClickSelect_Meshes_MGR._isSelectMode` is true (Ctrl held or Select toggle on). **Fix:** Release Ctrl or turn off the mesh Select tool. |
| **Wrong workflow mode** | `isDoingSomethingElse()` is true because mode isn’t “inpaint” or view isn’t UsualView. **Fix:** Use **Inpaint Color** or **Inpaint No Color** and ensure you’re on the main viewport (UsualView). |
| **Alt or Ctrl held** | `isDoingSomethingElse()` returns true when Alt or Ctrl is pressed (orbit/zoom). **Fix:** Don’t hold Alt/Ctrl while painting. |
| **Ribbons missing** | `WorkflowRibbon_UI.instance`, `MainViewport_UI.instance`, or `SD_WorkflowOptionsRibbon_UI.instance` is null → `isDoingSomethingElse()` true. **Fix:** Ensure scene/UI is fully loaded (e.g. don’t paint before workflow ribbon exists). |
| **Dimension mode not SD** | `DimensionMode_MGR._dimensionMode != dim_sd` → `isAllowedToPaintNow` false. **Fix:** Be in the SD (Stable Diffusion) dimension mode. |
| **Editing mode off** | `MultiView_Ribbon_UI._isEditingMode` false → you can still paint, but the mesh only re-renders when `ReRenderAll_soon()` is called (e.g. on mouse release). **Fix:** Turn on editing mode if you want live preview every frame. |
| **Viewport not hovered** | First click must be with cursor over the main viewport; `isCursorHoveringMe()` is checked only on pointer down. **Fix:** Click inside the 3D viewport. |

---

## 2. Stroke commits but doesn’t show on mesh (commit OK, display fails)

| Cause | What to check |
|-------|----------------|
| **Wrong workflow mode for display** | `Apply_InpaintSketch_ColorLayer()` only blits when `allowed_to_showBrushMask()` is true → **Inpaint Color** or **Inpaint No Color**. If you’re in e.g. Projections Masking or Total Object, paint is committed but never drawn on the mesh. **Fix:** Switch to **Inpaint Color** or **Inpaint No Color** to see brush on the model. |
| **Not UsualView** | `MainViewport_UI.showing != UsualView` → we skip applying the paint layer to the accumulation buffer. **Fix:** Use the normal main view (not a different screen). |
| **No paint source for blit** | In `ApplyColorLayer_To_UV_Textures`, `source` is null (no layer composite and no `_ObjectUV_brushedColorRGBA`). You’ll see: `[Inpaint] Paint not shown on mesh: no paint source...` **Fix:** Ensure a model is loaded and that the color buffer was created (see InitTextures / UDIM match). |
| **Render pass not running** | `Objects_Renderer_MGR` only runs the pass that calls `Apply_InpaintSketch_ColorLayer()` when `_renderAll_ASAP` is true or `MultiView_Ribbon_UI._isEditingMode` is true. If both are false (e.g. `MultiView_Ribbon_UI.instance` null and no recent `ReRenderAll_soon()`), the mesh won’t update. **Fix:** Ensure editing mode or something triggers `ReRenderAll_soon()` after painting (we do call it on stroke end). If `MultiView_Ribbon_UI.instance` was null, the null check we added prevents a crash; `_renderAll_ASAP` alone should still drive one pass after each stroke. |

---

## 3. Commit path fails (stroke never written to paint target)

| Cause | What to check |
|-------|----------------|
| **GetPaintTarget() null** | Active layer has no content and `_ObjectUV_brushedColorRGBA` is null. You’ll see warnings in console from `OnRenderIntoCurrTex_please` or `OnFinal_ApplyIncomingVals_intoMask`. **Fix:** Load a model so that InitTextures creates the color buffer; ensure layer stack resolution is in sync (EnsureResolution called with correct UDIM count). |
| **ApplyBrushStroke_ToUvMask missing** | `_applyBrushStroke_toUvMask` is null and `FindObjectOfType<ApplyBrushStroke_ToUvMask>` finds nothing. You’ll see: `ApplyBrushStroke_ToUvMask not found. Brush strokes will not persist.` **Fix:** Ensure the scene has an `ApplyBrushStroke_ToUvMask` component. |
| **InitTextures didn’t create color buffer** | UDIM count mismatch (e.g. `ModelsHandler_3D.instance._allKnownUdims` null or count ≠ numSlices). You’ll see: `[Inpaint] Could not create paint color buffer: UDIM count mismatch...` **Fix:** Load the model before painting; ensure no race where you click before UDIMs are set. |

---

## 4. Bucket / Clear (eraser) not applying

| Cause | What to check |
|-------|----------------|
| **GetPaintTarget() null** | Same as brush: no layer content and no `_ObjectUV_brushedColorRGBA`. **Fix:** Same as “GetPaintTarget() null” above. |
| **Wrong view or mode (bucket)** | OnBucketFill_button returns if not UsualView or not `isMode_using_img2img()`. **Fix:** Be in main view and in an img2img-related mode (e.g. Inpaint Color / No Color). |
| **Camera / dilation / renderer null** | `OnBucketFill_orDelete_button` returns early if camera, TextureDilation_MGR, or Objects_Renderer_MGR is null. **Fix:** Ensure scene has these systems; check console for NREs. |

---

## 5. Quick verification order

1. **Model:** Load a 3D model so `_allKnownUdims.Count > 0` and InitTextures can create the color buffer and brush path RTs.
2. **Mode:** Use **Inpaint Color** or **Inpaint No Color** (so brush is allowed and `allowed_to_showBrushMask()` is true).
3. **View:** Main viewport, UsualView, cursor over viewport when you click.
4. **No block:** Not in Select mode, not holding Alt/Ctrl, dimension mode = SD, editing mode on if you want per-frame preview.
5. **Scene:** Inpaint_MaskPainter and ApplyBrushStroke_ToUvMask present; WorkflowRibbon_UI, MainViewport_UI, SD_WorkflowOptionsRibbon_UI exist.

If paint still doesn’t apply, check the **Unity Console** for the warnings added in the paint path (e.g. “Paint target is null”, “no paint source”, “Could not create paint color buffer”, “ApplyBrushStroke_ToUvMask not found”); they point to which of the above failed.

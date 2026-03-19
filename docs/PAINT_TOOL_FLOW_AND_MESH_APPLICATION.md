# Paint Tool Connectivity & Application-on-Mesh Flow (Line-by-Line)

This document traces how the tool panel options (Select, Bucket, Eraser/Clear) connect to the paint system and how brush/bucket/eraser data is applied to the mesh.

---

## 1. Tool panel → code connectivity

### 1.1 Where buttons are wired

| UI location | What it triggers | Static event / API |
|-------------|------------------|--------------------|
| **Brush ribbon** (legacy) | Bucket fill button | `BrushRibbon_UI_BucketFill._Act_onClicked` (static `Action`) |
| **Brush ribbon** | Delete/Clear mask button | `BrushRibbon_UI_DeleteButton.onClicked` (static `Action`) |
| **Paint tab (Krita layout)** | "Bucket Fill" | Same: `BrushRibbon_UI_BucketFill._Act_onClicked?.Invoke()` |
| **Paint tab** | "Clear Mask" | Same: `BrushRibbon_UI_DeleteButton.onClicked?.Invoke()` |
| **Paint tab** | "Invert Mask" | `BrushRibbon_UI_InvertMask.onClicked?.Invoke()` |

So both the ribbon and the Paint tab tool row call the same static events. No separate code paths.

### 1.2 Who subscribes to bucket / delete (and when)

- **MaskPainter (base)**  
  - In **Awake**:  
    - `BrushRibbon_UI_BucketFill._Act_onClicked += OnBucketFill_button;`  
    - `BrushRibbon_UI_DeleteButton.onClicked += OnDelete_button;`  
  - In **OnDestroy**:  
    - Same handlers are **unsubscribed** (so only the active painter instance stays subscribed).

- **Inpaint_MaskPainter**, **Projections_MaskPainter**, **Background_Painter**  
  - Each is a MaskPainter subclass.  
  - Each **overrides** `OnBucketFill_button` and `OnDelete_button`.  
  - So when the user clicks Bucket or Clear, **all** painter instances’ handlers are invoked; the one that matches the current mode does the work, the others return early.

- **BrushRibbon_UI_Colors**  
  - In **Awake**: `BrushRibbon_UI_BucketFill._Act_onClicked += OnBucketFill;`  
  - Its `OnBucketFill` only plays the color icon animation; it does **not** perform the fill.  
  - In **OnDestroy**: unsubscribes from `_Act_onClicked` and `_onResult`.

- **Projections_MaskPainter**  
  - In its own **OnDestroy**: also unsubscribes from bucket/delete (redundant with base once base unsubscribes).

### 1.3 Select mode and painting

- **ClickSelect_Meshes_MGR._isSelectMode**  
  - True when: Ctrl/Cmd held (and not ignored by settings) or the Select toggle is on; false when Alt or MMB is pressed.

- **Painting is blocked when Select is active**  
  - **Inpaint_MaskPainter.isAllowedToPaintNow** (and similar in other painters) includes:  
    `isAllowed &= !ClickSelect_Meshes_MGR.instance?._isSelectMode ?? false;`  
  - So when `_isSelectMode` is true, **brush** strokes cannot start.  
  - **Bucket and Delete** are not gated by `_isSelectMode`; they only check viewport and (for Inpaint) img2img mode and non-null `GetPaintTarget()`.

So: **Select** disables **brush** painting only; **Bucket** and **Clear** stay connected and run as long as their own guards pass.

---

## 2. Conditions that enable each feature

### 2.1 Brush (stroke painting)

1. **Update_callbacks_MGR.brushing** runs every frame (in **Update**).
2. **MaskPainter.OnUpdate** (e.g. Inpaint_MaskPainter):
   - `isAllowedToShow_BrushCursorNow()` → cursor shown only when: `MainViewport_UI.showing == UsualView` and `WorkflowRibbon_UI.isMode_using_img2img()`.
   - **Pointer down** only if:  
     `isAllowedToPaintNow(also_check_viewportHovered:false) && MainViewport_UI.instance.isCursorHoveringMe()`.
3. **isAllowedToPaintNow** (Inpaint) requires:
   - `MainViewport_UI.instance?.showing == UsualView`
   - `DimensionMode_MGR.instance?._dimensionMode == DimensionMode.dim_sd`
   - `WorkflowRibbon_UI.instance?.isMode_using_img2img() == true`
   - `MultiView_Ribbon_UI.instance?._isEditingMode == true`
   - `!SD_WorkflowOptionsRibbon_UI.instance?.IsEyeDropperMagnified`
   - `!ClickSelect_Meshes_MGR.instance?._isSelectMode`  ← **Select mode blocks brush**
   - `!GlobalClickBlocker.isLocked()`
   - If `also_check_viewportHovered`: `MainViewport_UI.instance?.isCursorHoveringMe()`
4. **isDoingSomethingElse()** (base): if true, we don’t start or continue painting (e.g. Alt/Ctrl, or WorkflowRibbon/MainViewport/SD_WorkflowOptionsRibbon null).
5. **maskResolution()**: `textureRes.z` must be > 0 (model with UDIMs). Otherwise we never set `_isPainting` or create brush path RTs.
6. After **initTextures_Maybe**: if `_currBrushPath_R8 == null`, we set `_isPainting = false` and return (no painting).
7. **OnRenderIntoCurrTex_please**: needs `GetPaintTarget() != null` (active layer or `_ObjectUV_brushedColorRGBA`).
8. **OnFinal_ApplyIncomingVals_intoMask** (on mouse up): needs non-null target (with fallback to `_ObjectUV_brushedColorRGBA`) and non-null `_applyBrushStroke_toUvMask`.

So brush is enabled by: **UsualView + img2img + SD dimension + editing mode + no Select mode + no eye dropper + model loaded + paint target and apply component exist**.

### 2.2 Bucket fill (Inpaint)

1. User clicks Bucket (ribbon or Paint tab) → `BrushRibbon_UI_BucketFill._Act_onClicked?.Invoke()`.
2. **Inpaint_MaskPainter.OnBucketFill_button**:
   - `MainViewport_UI.instance.showing == UsualView` else return.
   - `WorkflowRibbon_UI.instance.isMode_using_img2img() == true` else return.
   - `target = GetPaintTarget(); if (target == null) return;`
   - Then `OnBucketFill_orDelete_button(col, target.texArray, visibilTex: null)`.
3. **MaskPainter.OnBucketFill_orDelete_button** (base):
   - Now guarded: `if (dest == null) return;` and early return if `UserCameras_MGR.instance?._curr_viewCamera`, `TextureDilation_MGR.instance`, or `Objects_Renderer_MGR.instance` is null.
   - Sets fill color on `_fillUVchunks_mat`, renders into a temp RT with `UserCameras_MGR.instance._curr_viewCamera.RenderImmediate_Arr`, dilates via `TextureDilation_MGR`, blits to `dest`, then `Objects_Renderer_MGR.instance.ReRenderAll_soon()`.

So bucket is enabled by: **UsualView + img2img + non-null GetPaintTarget() + camera/dilation/renderer available**. Select mode does **not** block bucket.

### 2.3 Eraser / Clear mask (Inpaint)

1. User clicks Clear (ribbon or Paint tab) → `BrushRibbon_UI_DeleteButton.onClicked?.Invoke()`.
2. **Inpaint_MaskPainter.OnDelete_button**:
   - `MainViewport_UI.instance.showing == UsualView` else return.
   - `target = GetPaintTarget(); if (target == null) return;`
   - `OnBucketFill_orDelete_button(Color.clear, target.texArray, visibilTex: null)`.

Same application path as bucket; only the fill color is clear. Select mode does **not** block clear.

---

## 3. Application onto the mesh (line-by-line)

What the user sees on the mesh is the **accumulation texture** `_accumulation_uv_RT` in **Objects_Renderer_MGR**, displayed via **VisualizeFinalMat_Helper.ShowFinalMat_on_ALL(_accumulation_uv_RT)**.

### 3.1 When is the accumulation buffer updated?

- **Objects_Renderer_MGR** subscribes to **Update_callbacks_MGR.objectsRender** (runs in **LateUpdate**).
- **OnUpdate** only does a full pass when:
  - `_renderAll_ASAP == true` (e.g. after **ReRenderAll_soon()**), or
  - `MultiView_Ribbon_UI.instance._isEditingMode == true`.
- In that pass it calls **ProcessMeshes()** → **DoStuff()** → **Apply_InpaintSketch_ColorLayer()**.

### 3.2 Apply_InpaintSketch_ColorLayer (critical for paint on mesh)

```text
Objects_Renderer_MGR.Apply_InpaintSketch_ColorLayer():
  if (MainViewport_UI.instance.showing != UsualView) return;
  if (WorkflowRibbon_UI.instance.allowed_to_showBrushMask() == false) return;
  Inpaint_MaskPainter.instance.ApplyColorLayer_To_UV_Textures(_accumulation_uv_RT);
```

- **allowed_to_showBrushMask()** is true only for **Inpaint_Color** and **Inpaint_NoColor**.
- So the brush mask (paint layer) is blitted onto the mesh only when:
  - View is UsualView, and  
  - Mode is Inpaint_Color or Inpaint_NoColor.

If either condition fails, paint is **committed** (brush/bucket/eraser write into the paint target) but **not shown** on the mesh until the next frame when these conditions hold and a render pass runs.

### 3.3 Inpaint_MaskPainter.ApplyColorLayer_To_UV_Textures(ontoHere)

- **ontoHere** is always `_accumulation_uv_RT` (the mesh display texture).
- Chooses **source**:
  - If layer stack exists: composite layers into `_layerStackCompositeTemp`; if any layer has visible content, `source = _layerStackCompositeTemp`, else `source = _ObjectUV_brushedColorRGBA`.
  - If no layer stack: `source = _ObjectUV_brushedColorRGBA`.
- If `source == null` → return (no blit).
- Sets `_SrcTex` and other material props, then **TextureTools_SPZ.Blit(source.texArray, ontoHere.texArray, _blitApplyEntireColorLayer_mat)**.

So the **paint-on-mesh** chain is:

1. Brush: **MaskPainter** → render stroke into R8 path → on release **Apply_into_ColorBrushTex(target)** writes into **GetPaintTarget()** (layer or `_ObjectUV_brushedColorRGBA`).
2. Bucket/Delete: **OnBucketFill_orDelete_button** writes directly into **GetPaintTarget().texArray** (same target).
3. Display: **Objects_Renderer_MGR** (LateUpdate, when render pass runs) → **Apply_InpaintSketch_ColorLayer()** → **ApplyColorLayer_To_UV_Textures(_accumulation_uv_RT)** blits from that paint source into `_accumulation_uv_RT`.
4. Mesh: **ShowFinalMat_on_ALL(_accumulation_uv_RT)** so the mesh shows the accumulation texture.

### 3.4 Summary: what can prevent paint from appearing on the mesh

- **Brush not sticking (commit path):**
  - No model (`maskResolution().z <= 0`) or init failed (`_currBrushPath_R8 == null`).
  - **GetPaintTarget()** null (no layer content and no `_ObjectUV_brushedColorRGBA`).
  - **Select mode** on → brush stroke never starts.
  - **isDoingSomethingElse()** true (e.g. WorkflowRibbon / MainViewport / SD_WorkflowOptionsRibbon null).
  - **ApplyBrushStroke_ToUvMask** missing or not found.

- **Paint committed but not visible (display path):**
  - **MainViewport_UI.showing != UsualView** → **Apply_InpaintSketch_ColorLayer** returns without blitting.
  - **allowed_to_showBrushMask() == false** (mode not Inpaint_Color / Inpaint_NoColor) → same.
  - **ApplyColorLayer_To_UV_Textures** gets `source == null` (no layer composite and no `_ObjectUV_brushedColorRGBA`).
  - Render pass not running (`_renderAll_ASAP` false and `_isEditingMode` false) so **Apply_InpaintSketch_ColorLayer** is not called.

- **Bucket/Clear not applying:**
  - **GetPaintTarget()** null.
  - **MainViewport_UI.showing != UsualView** or (bucket only) not **isMode_using_img2img()**.
  - Camera, **TextureDilation_MGR**, or **Objects_Renderer_MGR** null in **OnBucketFill_orDelete_button** (now guarded with early return).

---

## 4. Files reference

| Concern | File(s) |
|--------|---------|
| Brush / bucket / delete subscription | `MaskPainter.cs` (Awake / OnDestroy) |
| Bucket fill / delete implementation (Inpaint) | `Inpaint_MaskPainter.cs` (OnBucketFill_button, OnDelete_button) |
| Bucket fill / delete implementation (base) | `MaskPainter.cs` (OnBucketFill_orDelete_button) |
| Tool panel buttons (Paint tab) | `PaintTab_CollectPaintUI.cs` (CreateToolOptionsRuntime) |
| Bucket / Delete button components | `BrushRibbon_UI_BucketFill.cs`, `BrushRibbon_UI_DeleteButton.cs` |
| Color icon animation on bucket | `BrushRibbon_UI_Colors.cs` (OnBucketFill, Awake/OnDestroy) |
| Select mode | `ClickSelect_Meshes_MGR.cs` (_isSelectMode) |
| Painting allowed (Inpaint) | `Inpaint_MaskPainter.cs` (isAllowedToPaintNow) |
| Paint target (layer or single buffer) | `Inpaint_MaskPainter.cs` (GetPaintTarget, InitTextures) |
| Commit brush stroke to target | `ApplyBrushStroke_ToUvMask.cs` (Apply_into_ColorBrushTex) |
| Blit paint layer to accumulation | `Inpaint_MaskPainter.cs` (ApplyColorLayer_To_UV_Textures) |
| When blit runs & display on mesh | `Objects_Renderer_MGR.cs` (Apply_InpaintSketch_ColorLayer, ProcessMeshes, OnUpdateComplete) |
| Workflow mode / show brush mask | `WorkflowRibbon_UI.cs` (currentMode, allowed_to_showBrushMask) |
| Update order (brushing vs objectsRender) | `Update_callbacks_MGR.cs` (Update / LateUpdate) |

This gives a full line-by-line picture of tool connectivity and how paint is applied to the mesh.

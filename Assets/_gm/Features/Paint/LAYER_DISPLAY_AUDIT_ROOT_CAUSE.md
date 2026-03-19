# Layer display audit – root cause analysis

**Goal:** Find what in the **existing** code causes “only one layer visible” or “first layer disappears” when adding a second layer, without proposing new fixes. Display path and failure modes are traced below.

---

## 1. Single display path for paint on the mesh

- **Entry:** `Objects_Renderer_MGR.Apply_InpaintSketch_ColorLayer()` (line ~238)  
  - Only runs when: `MainViewport_UI.instance.showing == UsualView`, `WorkflowRibbon_UI.instance.allowed_to_showBrushMask()`, `Inpaint_MaskPainter.instance != null`.
- **Display call:** `Inpaint_MaskPainter.instance.ApplyColorLayer_To_UV_Textures(_accumulation_uv_RT)`.
- **What the viewer sees:** The mesh uses `_accumulation_uv_RT` (set in `OnUpdateComplete()` via `_finalMat_Helper.ShowFinalMat_on_ALL(_accumulation_uv_RT)`). The **only** place paint is written into that texture is this blit in `ApplyColorLayer_To_UV_Textures`. So “what the layer system shows” is entirely determined by the `source` used there.

There is no other code path that draws “the active layer only” or “top layer only” to the mesh; if one layer appears to override another, it is because `source` in this method is (or effectively is) a single layer or base-only.

---

## 2. How `source` is chosen in `ApplyColorLayer_To_UV_Textures` (Inpaint_MaskPainter.cs)

- **Intent (comments):** With 2+ layers, display must use composite of all visible layers, never “active only”.
- **Logic:**
  - `multiLayer = (stack != null && stack.Layers != null && stack.Layers.Count > 1)`.
  - If `multiLayer`, `EnsureSceneBufferForDisplay()` is called (so base exists even if user added layer before first paint).
  - `useComposite = (stack != null && _ObjectUV_brushedColorRGBA != null && stack.Layers != null && stack.Layers.Count > 1)`.
  - If `useComposite`:
    - `EnsureBottomLayerHasSceneForComposite()`, `SyncStackResolutionFromSceneBuffer(stack, _ObjectUV_brushedColorRGBA)`, `EnsureLayerStackCompositeTemp(_ObjectUV_brushedColorRGBA)`.
    - If `_layerStackCompositeTemp != null`: `stack.CompositeToOnTopOfBase(_ObjectUV_brushedColorRGBA, _layerStackCompositeTemp)`, then `source = _layerStackCompositeTemp`.
    - **If after that `source` is still null:** fallback `source = stack.Layers[0].Content` (when layer 0 visible and Content non-null). So **only layer 0** is shown; layer 1 (and any others) are not in the picture.
  - **Only when `stack.Layers.Count <= 1`:** `source` can be set to `stack.ActiveLayer.Content` (active layer only). With 2+ layers this branch is not used.

So “one layer visible” can come from:

- **A)** Composite path runs but `_layerStackCompositeTemp` is null → fallback to **layer 0 only** (lines 114–118).
- **B)** Composite path runs but `CompositeToOnTopOfBase` only blits **base** (see §4) → `source` is base-only (no layer paint).
- **C)** Resolution change triggers full re-init of all layers (§5) → all layer Contents are recreated empty → composite shows base + empty layers = effectively **base only** or “first layer disappeared”.

---

## 3. Shader does not replace the whole buffer

- **Shader:** `EntireColorLayer_BlitApply.shader`  
  - Uses `BlendOp Add` and `Blend One OneMinusSrcAlpha` to **blend** the chosen paint source on top of what is already in `_accumulation_uv_RT` (projections, etc.). It does **not** clear or replace the full accumulation with “one layer”; the “one layer” effect must come from what is in `source`, not from the shader replacing the buffer.

---

## 4. Composite can collapse to “base only” (PaintLayerStack_MGR)

- **Method:** `CompositeToOnTopOfBase(baseLayer, dest)` (PaintLayerStack_MGR.cs).
- **Internal temps:** It uses `GetOrCreateCompositeTemp(ref _compositeTempA/B)`. Those temps are created from `_resolution` and `UDIMs_Helper._allKnownUdims`.
- **If `GetOrCreateCompositeTemp` returns null** (e.g. `_resolution.x <= 0` or `udims == null`):
  - The method logs: *“CompositeToOnTopOfBase: composite temps null (resolution not set?). … Blitting base only.”*
  - It then does `Graphics.Blit(baseLayer.texArray, dest.texArray)` and returns.
- **Effect:** `dest` (= `_layerStackCompositeTemp`) holds **only the base** (scene). No layer is composited. So in `ApplyColorLayer_To_UV_Textures`, `source` is base-only → viewer sees no layer paint; if they think of “one layer” as the base, that matches “only one thing visible.”

So any path where the stack’s composite temps are null (resolution or UDIMs not set when composite runs) **actively** makes the display “base only” and hides all layer content.

---

## 5. Resolution change wipes all layer content (PaintLayerStack_MGR)

- **Method:** `EnsureResolution(Vector3Int resolution)` (PaintLayerStack_MGR.cs).
- **When resolution actually changes** (`_resolution` or `_udimsCount` differs):
  - It sets `_resolution` / `_udimsCount`, then for **every** layer calls `_layers[i].EnsureContent(udims, _resolution, ...)`.
- **In PaintLayer.EnsureContent:** If size/udims differ from current Content, it **Dispose()s** the existing Content (and Data) and allocates **new** Content (and Data) filled with `Color.clear`. It also sets `HasReceivedSceneInject = false`.
- **Effect:** A single call to `EnsureResolution` with a **new** resolution **actively** clears all existing layer paint. After that, composite is base + empty layers → again “first layer disappears” or “only base visible.”

So any caller that invokes `EnsureResolution` with dimensions that differ from the current stack resolution (e.g. `SyncStackResolutionFromSceneBuffer` with a scene buffer that was created at a different size, or `maskResolution()` changing) can cause the observed behavior.

---

## 6. Fallback to layer 0 only (Inpaint_MaskPainter)

- In `ApplyColorLayer_To_UV_Textures`, when `useComposite` is true but after the composite block `source` is still null, the code sets:
  - `source = stack.Layers[0].Content` (if layer 0 visible and Content non-null).
- So **only layer 0** is shown; the new layer (and any other) is **never** drawn. That **actively** makes “one layer visible” (the first).

This happens when `_layerStackCompositeTemp != null` was false so the composite block did not set `source`, e.g.:

- `EnsureLayerStackCompositeTemp` did not create the temp (e.g. `sameSizeAs` null), or
- `CompositeToOnTopOfBase` was not called, or
- Some other path left `source` null before the fallback.

---

## 7. Misleading comment (not a bug, but reinforces “active = display”)

- **PaintLayerStack_MGR.cs** (around line 34):  
  `ActiveLayerRenderUdims` is documented as “RenderUdims to paint into (active layer's Content — **the display buffer**).”
- **Reality:** For display with 2+ layers, the display buffer is the **composite** (or fallbacks in §2), not the active layer’s Content. So the comment is wrong and can suggest that “display = active” is by design; the actual display path is §1–2.

---

## 8. Summary of root-cause candidates

| # | Cause | File / symbol | Effect |
|---|--------|----------------|--------|
| 1 | Composite temps null → “base only” blit | `PaintLayerStack_MGR.CompositeToOnTopOfBase`, `GetOrCreateCompositeTemp` | Viewer sees only base (scene); all layer paint hidden. |
| 2 | Resolution change → all layers re-inited | `PaintLayerStack_MGR.EnsureResolution` → `PaintLayer.EnsureContent` | All layer Contents recreated empty; composite = base + empty layers. |
| 3 | Fallback to layer 0 when composite didn’t set source | `Inpaint_MaskPainter.ApplyColorLayer_To_UV_Textures` (lines 114–118) | Only first layer visible; new/other layers never shown. |

Recommended next step: **Instrument or log** which of these runs in the failing scenario (e.g. log when composite temps are null, when `EnsureResolution` changes resolution, and when the layer-0 fallback is used), then apply a **targeted** fix to that path instead of adding more generic “solutions.”

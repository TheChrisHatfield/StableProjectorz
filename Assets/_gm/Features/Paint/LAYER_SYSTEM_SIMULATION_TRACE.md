# Layer system: execution simulation and masking analysis

This document traces how the code actually interacts when layers are added and when the viewer is updated, without assuming a single root cause. It simulates execution order across **Inpaint_MaskPainter** and **PaintLayerStack_MGR** and identifies every place that can make one layer’s content invisible or “masked.”

---

## Simulation 1: Add Layer **after** first paint (normal path)

**State before:** 1 layer (index 0), user has painted. `_ObjectUV_brushedColorRGBA` exists, `stack._resolution` set (e.g. 512×512). Layer 0 has Content (scene + paint), `HasReceivedSceneInject = true`.

1. **User clicks Add Layer** → `PaintLayerStack_MGR.AddLayer()`:
   - New `PaintLayer` created, `Visible = true`.
   - `_resolution.x > 0` → new layer gets `EnsureContent(udims, _resolution, ...)` → **new layer has Content (empty)**.
   - `_layers.Add(layer)`, `_activeIndex = 1`.
   - `OnLayerAdded?.Invoke(layer)`.

2. **OnLayerAdded_InjectScene(newLayer)** (Inpaint_MaskPainter):
   - `newIndex = 1`.
   - `EnsureContentForLayerIfNeeded(newLayer)` → no-op (Content already set in AddLayer).
   - `newLayer.Content != null` → we do **not** defer.
   - `_ObjectUV_brushedColorRGBA != null`, `newIndex > 0` → we run:
     - `SyncStackResolutionFromSceneBuffer(stack, _ObjectUV_brushedColorRGBA)` → `EnsureResolution(512,512,slices)`. Stack already had that resolution → **resChanged = false** → we do **not** re-init existing layers. Layer 0 keeps its Content.
     - `CompositeBelowInto(base, newLayer.Content, 1)` → blit **base + layer 0** into `newLayer.Content`. New layer now holds scene + layer 0’s paint.
     - `newLayer.HasReceivedSceneInject = true`.
   - `ReRenderAll_soon()`.

3. **Display (same or next frame)** – `ApplyColorLayer_To_UV_Textures(_accumulation_uv_RT)`:
   - `multiLayer = true`, `useComposite = true`.
   - `EnsureBottomLayerHasSceneForComposite()` → no-op (layer 0 already has scene).
   - `SyncStackResolutionFromSceneBuffer`, `EnsureLayerStackCompositeTemp`, `CompositeToOnTopOfBase(base, _layerStackCompositeTemp)`.
   - Composite = **base + layer 0 + layer 1** → `source = _layerStackCompositeTemp` → blit to accumulation.

**Result:** Both layers visible; new layer shows injected data. **No masking in this path.**

---

## Simulation 2: Add Layer **before** first paint (no InitTextures yet)

**State before:** 1 layer from `EnsureAtLeastOneLayer` (Awake). **InitTextures has never run**: no paint yet, so no scene buffer, no `stack.EnsureResolution` from InitTextures. So `stack._resolution = (0,0)`, `_udimsCount = 0`. Layer 0 was created in AddLayer with `_resolution.x > 0` false → **layer 0 has no Content**.

1. **User clicks Add Layer** → `AddLayer()`:
   - New layer created. `_resolution.x > 0` is **false** → new layer does **not** get `EnsureContent` in AddLayer → **new layer has no Content**.
   - `_layers.Add(layer)`, `_activeIndex = 1`.
   - `OnLayerAdded?.Invoke(layer)`.

2. **OnLayerAdded_InjectScene(newLayer)**:
   - `EnsureContentForLayerIfNeeded(newLayer)` → `_resolution.x <= 0` → **return without doing anything**. New layer still has no Content.
   - `newLayer.Content == null` → we **defer**: `StartCoroutine(DeferredEnsureContentForNewLayer(newLayer))`.
   - We **never** call `SyncStackResolutionFromSceneBuffer` or `CompositeBelowInto` in this frame.

3. **Next frame – DeferredEnsureContentForNewLayer**:
   - `stack.EnsureContentForLayerIfNeeded(newLayer)` → again `_resolution.x <= 0` → **return**. New layer still has no Content.
   - So we **never** run `CompositeBelowInto` for the new layer. Injection never happens.

4. **Display (when it runs)** – `ApplyColorLayer_To_UV_Textures`:
   - `multiLayer = true`. We call **EnsureSceneBufferForDisplay()** because we have 2+ layers.
   - `_ObjectUV_brushedColorRGBA == null` → we create it from `maskResolution()` and then call **`stack.EnsureResolution(new Vector3Int(res.x, res.y, res.z))`**.
   - Stack’s previous `_resolution` was **(0,0)** → **resChanged = true**.
   - **EnsureResolution** runs: `for (i = 0; i < _layers.Count; i++) _layers[i].EnsureContent(...)`. So **both layer 0 and layer 1 get new Content** – and in `PaintLayer.EnsureContent` we **Dispose** existing (null here) and create **new empty** buffers. So **both layers now have Content that is empty (Color.clear)**.
   - We do **not** inject into layer 0 here (comment: “Do not inject into layer 0 here (buffer is empty)”). We also never ran `CompositeBelowInto` for the new layer (deferred path couldn’t run without resolution).
   - Back in `ApplyColorLayer_To_UV_Textures`: `useComposite = true`, we call `CompositeToOnTopOfBase(base, _layerStackCompositeTemp)`.
   - Base = `_ObjectUV_brushedColorRGBA` (just created, **Color.clear**). Layer 0 and layer 1 both have **empty** Content.
   - Composite = **base (clear) + layer0 (empty) + layer1 (empty)** → effectively **clear**.
   - Viewer sees **no paint / “everything disappeared”** or only clear; both layers are effectively “masked” (empty, so nothing to show).

**Result:** Both layers end up with empty Content; new layer never receives injected data; display shows only the (clear) base. This matches “one layer visible” or “data masked” if the user interprets the clear base as “only one thing” or “previous layer gone.”

---

## Simulation 3: Resolution change during Add Layer (stack and buffer mismatch)

**State before:** 1 layer, painted at 512×512. Stack `_resolution = (512,512)`, layer 0 has Content with paint. Later, something makes the **scene buffer** use a different size (e.g. 1024×1024) while the stack still has 512 (e.g. buffer recreated elsewhere, or a different code path sets buffer size but not stack).

1. **User clicks Add Layer** → `AddLayer()`. New layer gets Content at 512×512 (empty).

2. **OnLayerAdded_InjectScene**:
   - `SyncStackResolutionFromSceneBuffer(stack, _ObjectUV_brushedColorRGBA)`.
   - Suppose `_ObjectUV_brushedColorRGBA` is 1024×1024. We call `EnsureResolution(1024, 1024, slices)`.
   - **resChanged = true** (512 ≠ 1024).
   - **EnsureResolution** re-inits **every** layer: `_layers[i].EnsureContent(...)` → **Dispose** existing Content, create new at 1024×1024, **Color.clear**. So **layer 0’s paint is wiped**. Layer 1 (new) also gets new empty Content.
   - Then we run **CompositeBelowInto(base, newLayer.Content, 1)**. At this point layer 0’s Content is **the new empty buffer**. So we composite: base (1024) + **layer 0 (empty)** into new layer → new layer gets **only base**.
   - Display later: composite = base + layer0 (empty) + layer1 (base only) → looks like base with possible blend. **Layer 0’s previous paint is gone** (“first layer disappeared”); new layer only has base (“new layer masks / overwrites” in the sense it never got layer 0’s content).

**Result:** Resolution mismatch in **SyncStackResolutionFromSceneBuffer** causes **EnsureResolution** to wipe all layer Contents. That **actively** clears the first layer and prevents the new layer from receiving “layers below” content.

---

## Simulation 4: Display path – composite temps null (stack resolution not set)

**State:** 2 layers, both have Content. But **stack._resolution** is still 0 (e.g. resolution was never set, or was cleared).

1. **ApplyColorLayer_To_UV_Textures**:
   - `useComposite = true`. We call `SyncStackResolutionFromSceneBuffer` → `EnsureResolution(w, h, slices)`. So we **do** set resolution here. So after this, temps can be created.
   - Unless **EnsureResolution** returns early: e.g. `udims == null || udims.Count != slices` → we **return without updating** `_resolution`. Then stack still has 0.
   - Then `EnsureLayerStackCompositeTemp(_ObjectUV_brushedColorRGBA)` → we create **Inpaint_MaskPainter’s** `_layerStackCompositeTemp` (that’s fine).
   - We call **CompositeToOnTopOfBase(base, dest)**. Inside the stack, **GetOrCreateCompositeTemp(ref _compositeTempA)** → `_resolution.x <= 0` → **return null**. So we only blit **base** into `dest`. So `_layerStackCompositeTemp` holds **base only**; no layers are composited.
   - Viewer sees **base only** → all layer paint is “masked.”

**Result:** Any path where the stack’s **GetOrCreateCompositeTemp** sees `_resolution.x <= 0` or `udims == null` causes display to show only the base and **actively** hide all layer content.

---

## Simulation 5: GetPaintTarget() can trigger EnsureResolution (and wipe)

**GetPaintTarget()** (Inpaint_MaskPainter) does:

```csharp
if (stack != null && stack.ActiveLayer != null && stack.ActiveLayerRenderUdims == null)
{
    var res = maskResolution();
    if (res.x > 0 && res.y > 0 && res.z > 0)
        stack.EnsureResolution(res);
}
```

So when the **active** layer has no Content, we call **EnsureResolution(maskResolution())**. If the stack had a **different** resolution before (e.g. from load or from another code path), **resChanged = true** → we re-init **all** layers → **all layer Contents become new empty buffers**. So a single paint attempt on a layer that doesn’t have Content yet can **wipe every layer’s Content** if resolution differs. That would make the first layer “disappear” and can look like masking.

---

## Cross-component summary: where “masking” or “one layer only” comes from

| Location | Mechanism | Effect |
|----------|-----------|--------|
| **OnLayerAdded_InjectScene** | When `newLayer.Content == null` we defer; when `_resolution <= 0`, deferred path never gives Content and never runs **CompositeBelowInto**. | New layer never receives injected data. |
| **EnsureSceneBufferForDisplay** | Creates scene buffer and calls **stack.EnsureResolution(res)**. If stack had `_resolution == 0`, **resChanged = true** → **all** layers get **EnsureContent** → every layer gets **new empty** Content. We do not inject into layer 0 here. | Both layers end up empty; display = base (clear); “everything masked” or “first layer gone.” |
| **SyncStackResolutionFromSceneBuffer** | Calls **EnsureResolution(scene buffer size)**. If that size **differs** from current `_resolution`, **resChanged = true** → all layers re-inited **empty**. | First layer’s paint wiped; new layer only gets base in **CompositeBelowInto** (because layer 0 is now empty). |
| **CompositeToOnTopOfBase** | When **GetOrCreateCompositeTemp** returns null (e.g. `_resolution.x <= 0` or `udims == null`), we only blit **base** to dest. | Display shows base only; all layer content masked. |
| **ApplyColorLayer_To_UV_Textures** | When composite block doesn’t set `source` (e.g. `_layerStackCompositeTemp` null), we fall back to **source = stack.Layers[0].Content**. | Only layer 0 shown; new layer never drawn (layer 0 “on top” in the sense it’s the only one in the image). |
| **GetPaintTarget** | When active layer has no Content, we call **EnsureResolution(maskResolution())**. If resolution changes, all layers wiped. | One paint attempt can clear all layers. |
| **PaintLayerStack_MGR.EnsureResolution** | On **resChanged**, loops over **all** layers and calls **EnsureContent** → **Dispose** + new empty. | Single point that **actively** clears every layer’s Content when resolution changes. |

---

## Conclusion (unbiased from code)

- The **injection** path (OnLayerAdded_InjectScene → CompositeBelowInto) is present and works when:
  - The new layer already has Content (AddLayer gave it because `_resolution.x > 0`), and
  - **SyncStackResolutionFromSceneBuffer** does **not** change resolution (so layer 0 is not re-inited empty).
- **Masking** / “one layer only” / “first layer disappears” can occur **without** touching that injection logic, from:
  1. **Add layer before first paint** → deferred injection never runs → **EnsureSceneBufferForDisplay** later sets resolution and gives **both** layers **empty** Content → display = base (clear).
  2. **Resolution mismatch** (stack vs scene buffer) when adding a layer → **EnsureResolution** wipes all layer Contents → **CompositeBelowInto** then runs with layer 0 empty → new layer gets only base; layer 0’s paint is gone.
  3. **Composite temps null** in the stack → **CompositeToOnTopOfBase** blits only base → display shows no layers.
  4. **Display fallback** to **Layers[0].Content** when composite didn’t set source → only first layer visible; new layer never in the image.

So the behaviour you see can come from the **layer system** (EnsureResolution wiping, deferred injection never running) and/or the **display path** (composite temps null, fallback to layer 0 only). The codebase allows all of these without any single “make active on top” line; they come from **interaction** between resolution, injection timing, and composite/display conditions.

# Layer system audit: masking and display behaviour

**Current behaviour (Photoshop-like):** Layer order is bottom (index 0) to top (last). Each layer is self-contained (Content + Data). Only the **active** layer receives new paint; the viewport shows only the active layer's Content (when visible) with its Opacity on top of the scene. Hidden layers are not shown in the viewport. Export and SD mask use the full composite of all visible layers (CompositeTo) with per-layer opacity.

This document summarizes how layers are implemented and where explicit or implicit “masking” (hiding paint) occurs, so you can decide how to readjust the codebase.

---

## 1. Where display is chosen (the masking switch)

**File:** `Inpaint_MaskPainter.ApplyColorLayer_To_UV_Textures` (display path to mesh)

The logic is a **single-source branch**: we pick one source and blit it to the mesh. There is no separate “mask” texture; “masking” is **which source is selected**.

```csharp
bool paintTargetIsLayer = PaintLayerStack_MGR.instance.ActiveLayerRenderUdims != null;

if (paintTargetIsLayer)
{
    // SOURCE = layer composite (with optional fallback as base)
    CompositeToWithActiveOnTop(_layerStackCompositeTemp, fallbackAsBase);
    source = _layerStackCompositeTemp;
}
else
{
    // SOURCE = fallback buffer only
    source = _ObjectUV_brushedColorRGBA;
}
// Then: Blit(source → ontoHere)
```

- **When `ActiveLayerRenderUdims != null`** (any layer has content and one is active): we show **only** the result of `CompositeToWithActiveOnTop`. Fallback is visible only if passed as `baseUnderneath` (we do pass it when sizes match).
- **When `ActiveLayerRenderUdims == null`**: we show **only** the fallback buffer. Layer stack is never drawn.

So the only way fallback paint is visible when a layer is active is via the **baseUnderneath** path. If that path is not used (e.g. size mismatch) or if other code paths build composite without a base, fallback is effectively masked (not shown).

**Implicit masking:** The binary choice “layer composite OR fallback” means that as soon as the active layer has content, the **only** way to see fallback is by including it as the composite base. Any code path that builds the composite without that base will hide fallback.

---

## 2. Paint target (where strokes go)

**File:** `Inpaint_MaskPainter.GetPaintTarget()`

```csharp
var layerContent = PaintLayerStack_MGR.instance?.ActiveLayerRenderUdims;
if (layerContent != null)
    return layerContent;
return _ObjectUV_brushedColorRGBA;
```

- If the **active layer has Content**, all new strokes go to that layer. Fallback is not written.
- If the active layer has **no** Content, strokes go to fallback.

So “paint that’s not on any layer” is exactly the fallback buffer. The only way that paint can be visible when a layer is active is if the **display** uses fallback as the base of the composite (as above). There is no masking here; the issue is the display branch, not the paint target.

---

## 3. Composite blend (layer-on-layer)

**File:** `PaintLayer_CompositeBlend.shader`

```hlsl
float t = fg.a * _Opacity;
return lerp(bg, fg, t);
```

- Each layer is blended **over** the current composite with a single factor: `fg.a * _Opacity`.
- **PaintLayer.BlendMode** (Normal, Multiply, Screen, Overlay) is **never used** in this shader; only `_Opacity` is set from `l.Opacity`. So all layers effectively use the same “normal” alpha blend. BlendMode is stored and serialized but has no effect.

**Implicit behaviour:**

- A layer filled with **transparent** pixels (alpha 0) does not mask: we see the background.
- A layer with **opaque** pixels (e.g. alpha 1, opacity 1) fully replaces the background at that pixel (100% fg). So a layer can “mask” the stack below it only by being opaque, not by a separate mask texture.

New layer content is created with `Color.clear` (0,0,0,0), so empty layers do not hide anything. Masking-like behaviour only appears when a layer actually has opaque content.

---

## 4. Composite construction (with or without base)

**File:** `PaintLayerStack_MGR.CompositeTo(dest, baseUnderneath = null)`

- **When `baseUnderneath` is null:**  
  The composite is built from **layers only**. First visible layer is copied into the composite, then other visible layers are blended on top. Fallback is never included → **fallback is masked** in that path.

- **When `baseUnderneath` is non-null and same size:**  
  The composite starts from the base (e.g. fallback), then every visible layer is blended on top. Fallback stays visible underneath → **no masking of fallback** in that path.

So any caller that uses `CompositeTo(dest)` or `CompositeToWithActiveOnTop(dest)` **without** passing the fallback as the second argument will hide fallback when showing the layer stack.

**Call sites:**

| Call site | Passes base? | Effect |
|-----------|-------------|--------|
| `ApplyColorLayer_To_UV_Textures` (display) | Yes (`fallbackAsBase` when sizes match) | Fallback can show under layers. |
| `ExtractColorLayer_as_UV_texture2D` | No | Export/extract uses **layers only**; fallback not included. |
| `GetDisposable_ScreenMask` (SD/projections) | No | Mask for SD/projections uses **layers only**; fallback not included. |

So: **on-screen display** can show fallback (when base is passed), but **export and SD mask** currently do not; they implicitly “mask” fallback by not including it.

---

## 5. “Active layer on top” (drawn twice)

**File:** `PaintLayerStack_MGR.CompositeToWithActiveOnTop`

1. `CompositeTo(dest, baseUnderneath)` builds the full composite (base + all visible layers, including the active one).
2. Then the **active layer** is blended again on top of that result.

So the active layer is included in the stack and then drawn once more on top. This is intentional (so strokes on the active layer are always visible) and is **not** a mask; it only reinforces the active layer’s visibility.

---

## 6. Layer creation and content

**File:** `PaintLayer.EnsureContent` → `new RenderUdims(..., Color.clear, 0)`

- New or resized layer content is cleared to **Color.clear** (0,0,0,0). So new layers start fully transparent and do not mask by default.

**File:** `Inpaint_MaskPainter.OnLayerAdded_MigrateFallback`

- When a new layer is added, we copy into it either the **previous active layer’s Content** or the **fallback** (if previous layer isn’t available/same size). So the new layer starts with existing paint; we do not create an empty layer that would “cover” previous paint without migration.

---

## 7. Clearing paint

**File:** `Inpaint_MaskPainter.ResetPaintMask()`

```csharp
PaintLayerStack_MGR.instance.ActiveLayer.Content?.ClearTheTextures(Color.clear);
_ObjectUV_brushedColorRGBA?.ClearTheTextures(Color.clear);
```

- Resetting clears **both** the active layer and the fallback. No masking; just intended full clear.

---

## 8. Summary: what masks, and what doesn’t

| Cause | Explicit mask? | What happens |
|-------|----------------|--------------|
| Display branch: “layer composite vs fallback” | No (logic choice) | When active layer has content we show composite; fallback only appears if used as base. Without base, fallback is hidden. |
| Composite built without base | Implicit | `CompositeTo(dest)` / `CompositeToWithActiveOnTop(dest)` with no second arg → layers only → fallback not drawn. |
| Blend formula `lerp(bg, fg, t)` | No | Normal alpha blend. Opaque layer pixels replace background; transparent ones don’t. |
| BlendMode on PaintLayer | Unused | Stored but not applied; no extra masking from blend mode. |
| New layer content | No | Cleared to transparent; migration copies previous/fallback so new layer doesn’t “blank” the canvas. |

So the only things that effectively “mask” (hide) paint are:

1. **Using the layer composite as the single display source** while **not** including the fallback as the composite base (e.g. size mismatch or missing argument).
2. **Other code paths** that build composite without a base (`ExtractColorLayer_as_UV_texture2D`, `GetDisposable_ScreenMask`), so fallback is excluded from export and from the mask sent to SD.

There is no separate mask texture or explicit “layer mask” in the current design; masking is “we only show one of two things (composite vs fallback)” and “composite can be built without fallback.”

---

## 9. Recommendations for readjusting the layer system

1. **Unify “what is the composite” everywhere**  
   Decide whether the canonical composite is always “fallback (if any) + layers.” If yes, pass fallback as base in **all** composite call sites (display, extract, screen mask for SD), so export and SD see the same thing as the viewport.

2. **Option: single display model**  
   Instead of a strict branch “either composite or fallback,” always build one composite that includes fallback as base when present, and use that as the only display source when layers exist. That way “activating a layer” never switches to a different buffer that omits fallback.

3. **Option: use BlendMode**  
   If you want Multiply/Screen/Overlay, add a shader branch or a second pass in `PaintLayer_CompositeBlend` that uses `l.BlendMode` so layers can darken/lighten instead of only “over” blend. Right now BlendMode does nothing.

4. **Document or rename “fallback”**  
   Make it explicit in code/comments that “paint not on any layer” lives in `_ObjectUV_brushedColorRGBA` and that any composite used for display (or export/SD) should include it as base when you want that paint to be visible and included.

5. **Fix call sites that omit base**  
   In `ExtractColorLayer_as_UV_texture2D` and `GetDisposable_ScreenMask`, consider calling `CompositeTo(..., baseUnderneath: _ObjectUV_brushedColorRGBA)` (with the same size checks as in the display path) so that export and SD mask match what the user sees on screen.

This audit reflects the current codebase; after readjusting how layers are created or composited, re-check these call sites and the display branch to ensure no path implicitly masks the fallback.

# Layers as ancillary input (scene stays static)

## How paint is applied today

1. **Brush → screen-space stroke**  
   The brush draws in **screen space** (current camera view). The stroke is stored in `currBrushStroke_R8` (and prev). So painting is **view‑dependent projection** from the viewport.

2. **Stroke → UV-space paint target**  
   `Apply_into_ColorBrushTex` uses a **compute shader** that:
   - Takes the screen-space brush stroke and **projects** it onto the UV layout using `_UV_Chunks_R8` (which UV island each texel belongs to).
   - Writes into **destin** = either the **active layer’s Data** (then Bake → Content) or the **fallback** buffer (`_ObjectUV_brushedColorRGBA`).  
   So the result is stored in **UV space** (one texture per UDIM), but the **input** is a projected brush from the current view.

3. **Scene / accumulation**  
   `_accumulation_uv_RT` (in `Objects_Renderer_MGR`) is what gets applied to the mesh (`ShowFinalMat_on_ALL`). Each frame it is built as:
   - `ApplyStartingColor` → base
   - `cycle_through_generations` → **projections** (UV textures from file, brush, or projection cameras) blit into accumulation
   - **Apply_InpaintSketch_ColorLayer** → calls `ApplyColorLayer_To_UV_Textures(_accumulation_uv_RT)`
   - `ApplyAmbientOcclusion` → AO blended in

4. **Current layer→scene step (override)**  
   `ApplyColorLayer_To_UV_Textures(ontoHere)`:
   - Builds **source** = full **composite of all visible layers** (with active on top) or fallback.
   - **Blits** source into `ontoHere` (accumulation) using `EntireColorLayer_BlitApply.shader`.  
   The shader uses **Blend One OneMinusSrcAlpha** (additive-style blend), so the **destination is not cleared**: we blend the source **on top** of what’s already in the accumulation. So the “scene” (base + projections) is still there; we **add** the composite on top.  
   The design issue: we’re adding the **entire layer stack composite**, so the scene is “base + projections + full stack.” That makes the stack behave like an **override** of the visible result, and all visibility/composite logic is tied to that one blit.

## Desired model: layers as ancillary input

- **Scene stays static**  
  Regard the “scene” as: base + projections (and later AO). The accumulation should **not** be driven by “whatever the full layer stack composite is.” So we **don’t** composite all layers and then blend that into the accumulation.

- **Layers = ancillary input**  
  Only the **active** layer (or fallback) is used as the **injected** input:
  - **Source** = **only** the active layer’s **Content** (or fallback when no layer / no content).
  - We **blend** that single buffer on top of the accumulation (same shader as now).  
  So the scene (base + projections) stays intact; we only **inject** the active layer’s paint on top.

- **Per-layer isolation**  
  Each layer still stores its own paint (Content/Data). When the user selects a layer, that layer’s stored data is what gets injected. When they switch back, we inject that layer’s data again. No need to “override” the scene; we only switch **which** layer’s buffer is the current injection.

- **Restore on switch**  
  “Restore” is automatic: we don’t write the active layer into the scene; we only **sample** it for display. So when you go back to a layer, we simply use that layer’s Content as the injection again.

## Code change (minimal)

In **ApplyColorLayer_To_UV_Textures**:

- **Before:**  
  source = full composite (CompositeToWithActiveOnTop of all visible layers, or fallback).

- **After:**  
  source = **only** the active layer’s **Content** if a layer is active and has content; otherwise fallback.  
  No CompositeTo, no CompositeToWithActiveOnTop.  
  Same blit and shader (blend on top of accumulation).

Result:

- Scene (base + projections) is unchanged by the layer stack.
- Only the **active** layer’s paint is injected on top.
- Switching layers = switching which layer’s buffer we inject; scene stays the same; each layer’s data stays isolated and is “restored” when selected by using it again as the injection source.

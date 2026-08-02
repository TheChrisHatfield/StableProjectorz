# Klein structure channel — lock-in note

**Micro:** `docs/delta/20_micro/klein-structure-channel.md`

## Neo API (probe)

- alwayson: `imagestitch integrated`
- args: `[true, [style, depth], max_side]` — **style/reference first, depth second** (RefControl left/right)
- LoRA: `flux2_klein_4b_refcontrol_depth` + trigger `refcontrol` (required)
- **sd_vae:** `flux2_klein_4b_vae.safetensors` (required; `None` breaks structure encode)
- Geometry: `GetDisposable_DepthTexture` / content frustum mesh depth
- **Why:** depth-only / gray ContentCam ImageStitch → Klein copies the depth plate

## SPZ wiring

- Attach: `SD_KleinStructureChannel.TryAttachMeshDepthStructure` on Klein Gen Art **txt2img only**
- Style: CustomFile → ContentCam → synthetic_albedo_seed (reject near-gray)
- Guard: `RejectKleinDepthLikeResult` before bake
- Trace: `KleinStructureTrace` prefs `spz.klein.structure_trace.v1` (default off); agent `structure_trace: true`

## Validation

- EditMode: `KleinStructureChannelContractTests`, updated `ForgeNeoSwapPayloadPhaseCTests` / `AgentBridgeAddonContractTests`
- Batchmode blocked while Unity Editor holds the project open — run filters in Editor Test Runner after domain reload.
- Live Gen Art: after Play Mode reload with Klein-4B + Klein VAE + mesh, `prepare_flux_klein_test` then Gen Art; expect ImageStitch `[style,depth]`, color albedo, no Fun-Union.

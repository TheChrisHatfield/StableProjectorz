# Klein structure channel — lock-in note

**Micro:** `docs/delta/20_micro/klein-structure-channel.md`

## Neo API (probe)

- alwayson: `imagestitch integrated`
- args: `[true, [base64_depth], max_side]`
- Geometry: `GetDisposable_DepthTexture` / content frustum mesh depth

## SPZ wiring

- Attach: `SD_KleinStructureChannel.TryAttachMeshDepthStructure` on Klein Gen Art txt2img/img2img
- Pixel init: CustomFile / ContentCam only (never Depth)
- Guard: `RejectKleinDepthLikeResult` before bake
- Trace: `KleinStructureTrace` prefs `spz.klein.structure_trace.v1` (default off); agent `structure_trace: true`

## Validation

- EditMode: `KleinStructureChannelContractTests`, updated `ForgeNeoSwapPayloadPhaseCTests` / `AgentBridgeAddonContractTests`
- Batchmode blocked while Unity Editor holds the project open — run filters in Editor Test Runner after domain reload.
- Live Gen Art vs XL GT: after Play Mode reload with Klein-4B + mesh, `prepare_flux_klein_test` then Gen Art; expect ImageStitch in payload, color albedo, no Fun-Union.

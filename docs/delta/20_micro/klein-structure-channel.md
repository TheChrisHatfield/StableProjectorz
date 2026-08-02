# Klein structure channel (mesh depth → Neo ImageStitch)

## Navigation

- Plan: Klein depth orchestration (structure ≠ init)
- Ops: `SD_KleinStructureChannel`, `KleinStructureTrace`
- Neo host probe (this machine): alwayson script **`imagestitch integrated`**

## Intent

Flux.2 Klein Gen Art must condition on **live mesh depth** (content-frustum 3D geometry), like XL depth ControlNet, without:

- Fun-Union / SD1.5/XL ControlNet alwayson
- Depth as `init_images` (Depth-as-init produced depth-plate “albedo”)

## Neo API (C0 probe)

| Field | Value |
|-------|--------|
| alwayson script name | `imagestitch integrated` |
| args | `[enable:bool, reference_images:list[base64], max_side:int]` |
| Klein behavior | refs encode as reference latents; txt2img = empty latent + refs |
| Not used | RefControl Depth LoRA (not installed); Fun-Union CN |

## SPZ flow

1. Capture `UserCameras_MGR.camTextures.GetDisposable_DepthTexture()` after depth lock + `content_depthRender`.
2. Attach via `SD_KleinStructureChannel.TryAttachMeshDepthStructure` on Gen Art txt2img/img2img.
3. Pixel stream: txt2img noise, or optional CustomFile/ContentCam img2img — **never Depth**.
4. `RejectKleinDepthLikeResult` blocks bake when Neo result ≈ depth plate.
5. Dev traceback: `KleinStructureTrace` / PlayerPrefs `spz.klein.structure_trace.v1` (default off).

## Fail closed

Missing depth RT → Gen Art denied / payload aborted (`kleinStructureAttachFailed`).

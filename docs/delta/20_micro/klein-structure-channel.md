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
| Ref order | **[0]=RGB style/reference**, **[1]=mesh depth** (RefControl left/right) |
| Style gate | Reject near-gray / depth-like ContentCam; fall back to `synthetic_albedo_seed` |
| LoRA | `flux2_klein_4b_refcontrol_depth` + trigger `refcontrol` (required) |
| Why LoRA | Depth-only ImageStitch makes Klein **copy the depth plate**; RefControl teaches depth-as-structure |
| Not used | Fun-Union CN; Depth as `init_images` |
| **sd_vae** | **`flux2_klein_4b_vae.safetensors` required** — `None` breaks ImageStitch depth encode even if `forge_additional_modules` lists the VAE |

## SPZ flow

1. Capture `UserCameras_MGR.camTextures.GetDisposable_DepthTexture()` after depth lock + `content_depthRender`.
2. Attach via `SD_KleinStructureChannel.TryAttachMeshDepthStructure` on Gen Art **txt2img only**.
3. Style ref (ImageStitch image2): ContentCam first; CustomFile last-resort — **never Depth as init**.
4. Hub forces `do_img2Img = false` for Klein Gen Art (see Neo caveat below).
5. `RejectKleinDepthLikeResult` (+ near-gray remap) blocks bake when Neo result ≈ depth plate.
6. Dev traceback: `KleinStructureTrace` / PlayerPrefs `spz.klein.structure_trace.v1` (default off). `LastRejectReason` always recorded.

## Neo caveat (must stay txt2img)

`backend/diffusion_engine/flux2.py` `get_learned_conditioning` **prepends** img2img `ini_latent` ahead of ImageStitch `ref_latents`. That turns SPZ’s `[depth, style]` into `[init, depth, style]` and breaks RefControl Depth LoRA. Gen Art therefore never uses img2img for Klein.

## Fail closed

Missing depth RT or style ref → Gen Art denied / payload aborted (`kleinStructureAttachFailed` / `style_ref_missing`).

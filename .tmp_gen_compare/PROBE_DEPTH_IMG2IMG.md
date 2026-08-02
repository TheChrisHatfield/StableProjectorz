# Klein probe: mesh Depth as img2img init (no Fun-Union)

Date: 2026-08-01

## Change

When Klein is active, `TryApplyKleinControlNetLayout` now:

1. Clears all ControlNet models to **None** (no Fun-Controlnet-Union alwayson).
2. Arms unit 0 with **What-to-send = Depth**, activated.
3. Feeds that depth bitmap as the **img2img init** (preference: Depth > CustomFile > ContentCam).

SDXL + XL depth ControlNet remains the production Gen Art path until this probe shows mesh lock.

## Live validation (blocked until Play Mode)

Agent bridge `127.0.0.1:8765` was down when this note was written. After Unity domain reload + Play Mode:

```text
# After Play Mode + domain reload (bridge :8765):
py -3.11 -c "import json,socket; s=socket.create_connection(('127.0.0.1',8765),5); s.sendall(b'{\"id\":\"1\",\"tool\":\"prepare_flux_klein_test\",\"params\":{}}\n'); print(s.recv(1<<20).decode())"
py -3.11 -u .tmp_capture_gen_run.py klein_depth_img2img
# then Gen Art from UI or agent generate
```

Expect in capture:

- `klein_init_source` / status: **Depth**
- `klein_depth_img2img_armed`: true
- Neo `params.txt`: img2img with init image; **no** Fun-Union ControlNet unit
- Visual: compare silhouette/pose lock vs `.tmp_gen_compare/20260801_215035_traditional_sd`

## Spec hook

Flux Klein Gen Art / ControlNet rewire (conversation probe after Fun-Union ineffective on Klein-4B).

# SPZ GO — ZBrush bridge (ZBrush 2026)

File-exchange bridge between ZBrush and StableProjectorz. **Not** a Blender-style live mesh stream:
ZBrush has no dependable always-on background timer, so handoffs are explicit palette buttons you tap
(GoZ-style).

Mesh I/O uses the **ZBrush 2026 Python API** (`zbrush.commands`), confirmed against the install's own
type stubs at `Documentation/python-api/stubs/zbrush/commands.pyi`:

- Export: `set_next_filename(path)` → `press("Tool:Export")`
- Import: `set_next_filename(path)` → `press("Tool:Import")`
- Palette: `add_subpalette(...)` + `add_button(...)`; status via `set_notebar_text(...)`

## Install

From StableProjectorz: open the **SPZ GO** panel → ZBrush section → **Settings** → **Install into
ZBrush**. SPZ copies `spz_zbrush_bridge.py` + `spz_http.py` into
`…\ZBrushData<year>\SpzGoBridge\` (user-writable Public Documents — never Program Files) and lights
the ZBrush logo once installed.

> ZBrush 2026 keeps user data under **Public Documents**, e.g.
> `C:\Users\Public\Documents\ZBrushData2026`. The installer targets the newest `ZBrushData*` root, so
> no elevation is needed.

Manual/reproducible install:

```
python install_into_zbrush.py --src <this folder> [--dest <ZBrushData dir>]
```

## Load in ZBrush (one time per session)

1. In ZBrush: **ZPlugin → ZScript/Python** (or the **ZScript** palette) → **Python Scripting → Load**.
2. Pick the installed `spz_zbrush_bridge.py`.
3. Running it registers a **`ZPlugin:SPZ GO`** subpalette with three buttons:
   - **Import from SPZ** — pull the current SPZ model into the active Tool.
   - **Export to SPZ** — push the active Tool/SubTool to SPZ.
   - **Answer SPZ request** — tap after pressing *Import for ZBrush* in SPZ.

Scripted use is also available from the Python console:

```python
import spz_zbrush_bridge as spz
spz.spz_import()             # SPZ → ZBrush: pull the current SPZ model
spz.spz_export()             # ZBrush → SPZ: push the active Tool/SubTool
spz.spz_poll_pull_request()  # answer a pending SPZ Import request once
```

## Protocol (shared with the Blender bridge, tested both sides)

- Exchange root: `<SPZ project data_dir>/StableProjectorzGO_exchange/zbrush/`
- SPZ → ZBrush: SPZ writes `from_spz.fbx` + `from_spz.spz_go_ready` stamp.
- ZBrush → SPZ: ZBrush writes `from_zbrush.obj` and POSTs `/api/v1/meshes/import`.
- SPZ Import request marker: `spz_go_pull_request.json` (consumed by `spz_poll_pull_request`).

## Status

The exchange protocol and SPZ REST endpoints match the shipping Blender bridge and are covered by
contract tests. The ZBrush-side mesh ops now call the **confirmed** ZBrush 2026 Python API (the guessed
`export_tool` / `run_zscript` / raw `[IPress,…]` ZScript strings have been removed).

Remaining live check: an **interactive round-trip in ZBrush** (Import a mesh, sculpt, Export back)
to confirm the OBJ export template and Tool:Import behavior on real geometry. This needs a human click
in ZBrush and is tracked in `docs/specs/spz-go-multi-dcc/tasks.md`.

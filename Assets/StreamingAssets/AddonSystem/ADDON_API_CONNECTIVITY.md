# Addon ↔ API connectivity (line-by-line)

This document traces connectivity and correlation between addons, the Python API (spz), HTTP (FastAPI), socket (Unity), and Unity C# for UI and button callbacks.

---

## Path A: Load addon (Unity → Python → register → socket → Unity UI)

| Step | File | Lines | Correlation |
|------|------|-------|-------------|
| 1 | **Addon_MGR.cs** | 244–258 | `RequestLoadAddon(addonId)` builds `url = http://127.0.0.1:{_httpServerPort}/load_addon`, body `{"addon_id":"<id>"}`, POSTs via `UnityWebRequest`. |
| 2 | **http_server.py** | 347–356 | `POST /load_addon` receives `LoadAddonRequest` (81–82: `addon_id`). Calls `_load_addon_callback(req.addon_id)` (354). |
| 3 | **addon_server.py** | 197–201 | `set_load_addon_callback(lambda addon_id: load_addon_by_id(addon_id, addons_dir))` (201). So HTTP callback → `load_addon_by_id`. |
| 4 | **addon_server.py** | 108–114 | `load_addon_by_id(addon_id, addons_dir)` discovers addons, finds match, calls `load_addon(info)` (113). |
| 5 | **addon_server.py** | 68–89 | `load_addon(addon_info)` loads module (74–81), stores in `_loaded_addon_modules[addon_id]` (84), calls `module.register()` (89). |
| 6 | **MeshTools/__init__.py** | 167–172 | `register()` gets `api = spz.get_api()` (169), then `panel = api.ui.create_panel("MeshTools", "Mesh Tools")` (172). |
| 7 | **spz.py** | 1105–1110, 938–947 | `get_api()` returns global `_api` (SPZAPI). `api.ui` → UIAPI. `create_panel(addon_id, title)` (938) calls `self._client._send_request("spz.ui.create_panel", {"addon_id": addon_id, "title": title})` (940–942). |
| 8 | **spz.py** | 41–73 | `_send_request(method, params)` builds JSON-RPC request (44–49), sends over **socket** (55), reads response (58–67), returns `response.get("result", {})` (72). |
| 9 | **Addon_SocketServer.cs** | 209–220 | Listener receives message → `ProcessMessage` → `ProcessRequest(request)` (220) → returns `response`. |
| 10 | **Addon_SocketServer.cs** | 273–306 | `ProcessRequest`: reads `method` (274), enqueues `ExecuteCommand(method, @params)` on main thread (284–287), waits for `_pendingResponses[id]` (302–306), returns that response. |
| 11 | **Addon_SocketServer.cs** | 321–339 | `ExecuteCommand`: if `method.StartsWith("spz.ui.")` (328) → `ExecuteUICommand(method, @params)` (329). Returns `{ "jsonrpc":"2.0", "result": result }` (335–338). |
| 12 | **Addon_SocketServer.cs** | 1005–1015 | `ExecuteUICommand`: case `"spz.ui.create_panel"` (1006). Reads `addon_id`, `title` from `@params` (1007–1008). Calls `uiMgr.CreatePanel(addonId, title)` (1009). Sets `result["success"]`, `result["panel_id"]` (1010–1012). |
| 13 | **AddonUI_MGR.cs** | 42–152 | `CreatePanel(addonId, title)`: gets ribbon via `CommandRibbon_UI.instance` or `FindObjectOfType<CommandRibbon_UI>(true)` (45–49), `commandRibbon.GetOrCreatePanelForAddon(addonId, title)` (51), creates panel under that parent (99–148), returns `panelObj.GetInstanceID().ToString()` (148). |
| 14 | **spz.py** | 943–946 | Python receives socket response; `result` = `response.get("result", {})` so `result.get("success")`, `result.get("panel_id")`. UIAPI stores `Panel(self._client, panel_id, addon_id)` and returns it (945–946). |
| 15 | **MeshTools/__init__.py** | 173–178 | `panel.add_button("Center Selected", "center_selected_meshes")` etc. Each call → Path B (add_button over socket). |

**Correlation:** Unity HTTP POST `/load_addon` → Python `load_addon_callback` → `load_addon` → `module.register()` → `api.ui.create_panel` (socket) → Unity socket → `ExecuteUICommand` → `AddonUI_MGR.CreatePanel` → ribbon tab + panel. Then `panel.add_button` uses same socket path (Path B).

---

## Path B: Add button (Python → socket → Unity)

| Step | File | Lines | Correlation |
|------|------|-------|-------------|
| 1 | **spz.py** | 962–972 | `Panel.add_button(label, callback)` (962) sends `_client._send_request("spz.ui.add_button", {"addon_id", "panel_id", "label", "callback"})` (964–968). Same socket as Path A. |
| 2 | **Addon_SocketServer.cs** | 273–306, 321–329 | Same as Path A: `ProcessRequest` → `ExecuteCommand` → `ExecuteUICommand` for `"spz.ui.add_button"`. |
| 3 | **Addon_SocketServer.cs** | 1017–1028 | Case `"spz.ui.add_button"`: reads `addon_id`, `panel_id`, `label`, `callback` from `@params` (1018–1021). Calls `uiMgr.AddButton(addonId, panelIdParam, label, callbackName)` (1022). Sets `result["success"]`, `result["button_id"]` (1023–1025). |
| 4 | **AddonUI_MGR.cs** | 154–216 | `AddButton(addonId, panelId, label, callbackName)`: `FindUIElement(panelId)` (155) finds panel by instance ID (AddonUI_MGR 251–263). Creates button, sets `onClick` to `SendCallbackToPython(addonId, callbackName)` when no C# callback (196–207). Registers in `_addonUIElements[addonId]` (208–211). Returns button instance ID (216). |

**Correlation:** `panel_id` from `create_panel` response (Unity instance ID string) is used in `add_button`; Unity finds the same panel via `FindUIElement(panelId)` (instance ID lookup in `_addonUIElements`).

---

## Path C: Button click → invoke addon function (Unity → HTTP → Python)

| Step | File | Lines | Correlation |
|------|------|-------|-------------|
| 1 | **AddonUI_MGR.cs** | 196–207 | Button click: if no `_buttonCallbacks[callbackId]`, calls `SendCallbackToPython(addonId, callbackName)` (206). |
| 2 | **AddonUI_MGR.cs** | 230–251 | `SendCallbackToPython`: gets port from `Addon_MGR.instance.GetHttpServerPort()` (237). Builds `url = http://127.0.0.1:{port}/invoke_callback`, body `{"addon_id":"<id>","callback":"<name>"}` (238–239). POSTs via `UnityWebRequest` (240–245). |
| 3 | **Addon_MGR.cs** | 341–343 | `GetHttpServerPort()` returns `_httpServerPort` (same as Python FastAPI port, default 5557). |
| 4 | **http_server.py** | 359–368 | `POST /invoke_callback` receives `InvokeCallbackRequest` (85–87: `addon_id`, `callback`). Calls `_invoke_callback(req.addon_id, req.callback)` (365). |
| 5 | **addon_server.py** | 202, 118–134 | `set_invoke_callback(invoke_addon_callback)` (202). `invoke_addon_callback(addon_id, callback_name)`: gets module from `_loaded_addon_modules.get(addon_id)` (120), `getattr(module, callback_name, None)` (124), calls `func()` (128). |
| 6 | **MeshTools/__init__.py** | e.g. 29–52 | `callback_name` e.g. `"center_selected_meshes"` → module-level `center_selected_meshes()` runs; uses `spz.get_api()` for scene/models. |

**Correlation:** Unity button uses same `addonId` and `callbackName` that Python passed to `add_button`. HTTP body matches `InvokeCallbackRequest`. Python looks up the same module stored in `load_addon` and invokes the same name used in `panel.add_button(..., callback)`.

---

## API shape consistency

| API surface | Python (spz) | Unity (socket handler) | Unity (AddonUI_MGR) |
|-------------|--------------|------------------------|---------------------|
| create_panel | `spz.ui.create_panel` params: `addon_id`, `title` (940–942) | `@params["addon_id"]`, `@params["title"]` (1007–1008) | `CreatePanel(addonId, title)` (42), returns panel instance ID (148) |
| add_button   | `spz.ui.add_button` params: `addon_id`, `panel_id`, `label`, `callback` (964–968) | same keys (1018–1021) | `AddButton(addonId, panelId, label, callbackName)` (154), returns button ID (216) |
| invoke       | N/A (HTTP) | N/A | `SendCallbackToPython(addonId, callbackName)` (230); body `addon_id`, `callback` (239) |

---

## Summary

- **Load addon:** Unity → HTTP `/load_addon` → Python `load_addon_by_id` → `load_addon` → `register()` → **socket** `spz.ui.create_panel` / `spz.ui.add_button` → Unity `ProcessRequest` → main-thread `ExecuteUICommand` → `AddonUI_MGR.CreatePanel` / `AddButton`. Panel ID and button IDs are Unity GameObject instance IDs returned over the socket.
- **Button click:** Unity button onClick → `SendCallbackToPython` → HTTP POST `/invoke_callback` with `addon_id`, `callback` → Python `invoke_addon_callback` → `_loaded_addon_modules[addon_id]` + `getattr(module, callback_name)()` → addon function runs.
- **Ports:** Socket (spz) = Unity `_serverPort` (5555). HTTP = Unity `_httpServerPort` (5557) = Python FastAPI. Unity uses HTTP for load_addon and invoke_callback; Python uses socket for create_panel/add_button (same `spz.get_api()` connection).

---

## API ↔ StableProjectorz (Unity) connectivity

All Python API calls (spz.py and http_server.py `call_unity`) must use the **exact method names** and **result keys** that Unity implements.

| Layer | Role |
|-------|------|
| **spz.py** | `_send_request(method, params)` over TCP to Unity port 5555. Method = e.g. `"spz.cmd.set_camera_pos"`, params = JSON object. Returns `response["result"]` from Unity. |
| **Addon_SocketServer.cs** | Listens on 5555, `ProcessRequest` → `ExecuteCommand` → `ExecuteFastPathCommand` (for `spz.cmd.*`) or `ExecuteUICommand` (for `spz.ui.*`). |
| **ExecuteFastPathCommand** | Dispatches to `FastPath_API.instance` (e.g. `SetCameraPosition`, `GetMeshPosition`, `TriggerTextureGeneration`, `IsSDConnected`). Requires `FastPath_API.IsReady()` (initialized after `ModelsHandler_3D` and `UserCameras_MGR`). |
| **FastPath_API.cs** | Implements game actions via `UserCameras_MGR`, `ModelsHandler_3D`, `Connection_MGR`, etc. |

**Method name alignment (Python ↔ Unity):**

- Python **spz.py** uses `spz.cmd.trigger_texture_generation` and `spz.cmd.is_sd_connected` (correct).
- **http_server.py** REST endpoints must use the same names: `trigger_texture_generation` (not `trigger_generation`), `is_sd_connected` (not `is_connected`). Status response keys from Unity are `generating` and `connected` (not `is_generating` / `is_connected`).

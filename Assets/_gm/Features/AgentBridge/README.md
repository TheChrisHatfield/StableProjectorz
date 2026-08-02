# Agent Bridge (SPZ MCP add-on)

A local command socket that lets an external tool inspect StableProjectorz and trigger
actions in it. Built for [spz-mcp](https://github.com/redDwarf03/spz-mcp), which exposes
these tools to an LLM agent over MCP, but the protocol is plain line-delimited JSON and
usable from any language.

## Enabling it

Off by default. Enable the **SPZ MCP** add-on (`SpzMcpSPZ`) in Add-on Manager,
open its panel, set Listen / Port / Token, then **Apply Settings**.

The socket stays closed unless:

1. The add-on is enabled, and
2. Listen is on (persisted via PlayerPrefs after Apply).

The console logs `[SPZ_Agent_Bridge] listening on 127.0.0.1:8765` on success.

Settings can also be driven over the existing add-on JSON-RPC socket:

- `spz.cmd.agent_bridge_get_status`
- `spz.cmd.agent_bridge_apply_settings` — `{ "listen": true, "port": 8765, "token": "" }`

## Protocol

One JSON object per line, request and response:

```
->  {"id":"1","tool":"describe","params":{}}
<-  {"id":"1","ok":true,"result":{"protocol_version":1,"tools":[...]}}
<-  {"id":"1","ok":false,"error":"unknown tool 'foo'. Call 'describe' for the catalogue."}
```

Call `describe` first: it returns the protocol version and the full tool catalogue,
including each tool's parameters. **Clients are not meant to hard-code the tool list** —
that is what keeps this repo and the MCP server free to evolve independently.

When a token is set, every request must include it: `{"params":{"token":"..."}}`.

## Tools

| Tool | What it does |
|---|---|
| `describe` | Protocol version + tool catalogue |
| `get_app_state` | Version, WebUI connections, loaded model, UDIM tiles, selection, generation status (+ SD Gen Art fields when ready) |
| `get_viewport_screenshot` | Base64 PNG of a viewport region |
| `list_generations` | Stored generation counts per kind, and the latest GUID |
| `list_events` | Every registered `StaticEvents` id and its parameter types |
| `invoke_event` | Fire a `StaticEvents` id, as the matching UI control would |
| `get_sd_gen_settings` | Checkpoint, VAE, sampler, scheduler, steps, CFG, seed, size, prompts, CN, can_gen |
| `list_sd_options` | Live dropdown catalogues (checkpoints, samplers, schedulers, VAEs, CN models) |
| `set_sd_gen_settings` | Set those fields + prompts; optional `clear_controlnet_models` |
| `set_controlnet_unit` | Set one unit's model / weight by index |
| `generate` | Start Gen Art or Gen BG (`backgrounds: true`) |
| `get_generation_status` | Busy / can_gen / cooldown / Klein structure ready |
| `stop_generation` | Interrupt Gen Art |
| `focus_camera` | Frame selection (F-key) |
| `show_status` | Viewport HUD text |
| `prepare_flux_klein_test` | Klein preset: Euler / 4 steps / CFG 1 / 512²; CN None; mesh-depth ImageStitch structure |
| `list_spz_commands` | Full `spz.cmd` / `spz.ui` catalogue (same as add-on JSON-RPC) |
| `spz_cmd` | Call **any** `spz.cmd.*` / `spz.ui.*` via `Addon_SocketServer.ProcessRequestDirect` — full autonomy |

Protocol version **2** adds `spz_cmd`. Prefer curated Gen Art tools for the common loop; use `spz_cmd` for camera, mesh, workflow, paint, project, export, ribbon tabs, etc.

## How it fits the codebase

* **Add-on gated.** Master power is Add-on Manager (**SPZ MCP**); Listen/port/token live in the lite panel.
* **No scene, no prefab, no Build Settings entry.** The bridge boots from
  `[RuntimeInitializeOnLoadMethod]` onto a `DontDestroyOnLoad` object.
* **Threading.** The listener and each connection run on background threads; every tool
  body runs on the main thread, drained from `Update()`. Never `LateUpdate()`.
* **Async answers.** A tool may answer several frames later (`get_viewport_screenshot`).

## Security

Loopback-bound and opt-in, but it is still an unauthenticated local control channel
unless you set a token. Any process on the machine can connect. Enable it only while
you are actually using it.

# Cartridge: add-on theme surface (phased)

**Status:** Through ribbon icon-only (rpc theme **1.18**, addon rpc **1.15**)  
**Spec:** `docs/specs/addon-theme-foundation/`, `docs/specs/addon-theme-api-p1/` … `p3/`, `docs/specs/addon-theme-api-b4/`  
**Roadmap note:** `continual-learning/theme-api-phased-roadmap.md`
**Delta:** `docs/delta/20_micro/addon-theme-api-ribbon-icon-only.md`

## Laws

- Apply at ownership roots; never scan the global UI skeleton.
- **Builtin boundary:** bound chrome recolors only when a non-builtin theme is active (`ShouldRecolorBoundChrome`). Authored SPZ colors + no Monolith strip icons until Nomad/custom Apply.
- `surfaces[].bound` must stay honest.
- Compose with chrome/skybox / `set_ui_target_active` — no mega experience RPC.
- No true blur/glass shaders; use `panel_alpha` glass-lite only.
- No create/destroy built-in widgets; show/hide named chrome targets only.
- Icon pack v1 = `StudioLineIcon` enum names via `list_line_icons` / `set_line_icon` (no arbitrary upload).
- Typed floats: scales, `corner_radius`, `panel_width`, `panel_alpha`, `ribbon_icon_only` (0–1 gate).
- Persist theme id + tokens; never persist canvas `set_ui_scale`.

## Current rpc

Theme **1.18** · Addon **1.15** (`add_toggle`, `list_line_icons`, `set_line_icon`).
CommandRibbon icon-only when `ribbon_icon_only` ≥ 0.5.

## BoundChrome role matrix (alignment)

Traditional authored SPZ UI is source of truth. `SpzUiThemeOps.ApplyBoundChromeRolesUnder`
maps traditional control kinds → Nomad helpers at ownership roots only.
Spec: `docs/specs/boundchrome-role-matrix/` · Delta: `docs/delta/20_micro/boundchrome-role-matrix.md`.

## Remaining hard non-goals

True blur · custom icon bytes · structural add/reorder of built-ins · theme file export/import.

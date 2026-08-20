# Nomad Theme SPZ

Managed add-on that registers the `nomad-inspired` preset (theme rpc **1.18**) and drives UI
through theme tokens + compose hooks:

- `spz.ui.register_theme` / `apply_theme` / `reset_theme` / scale **patch**
- Tokens: colors + scales + `corner_radius` / `icon_tint` / `panel_width` / `panel_alpha` / **`ribbon_icon_only`**
- Compose: strip `set_line_icon` (SPZ viewport/skybox background is **kept**)
- CommandRibbon: when `ribbon_icon_only` is on, **hides tab labels** and centers larger line icons (Nomad-like); Restore SPZ shows labels again
- RibbonOnlyFullscreen FULL/SRN + OPEN/HIDE RIGHT: under Nomad, **flat grey face + FULL/SRN text** (not icon-only / not beveled peach)
- Left / brush strips: Nomad applies **studio line icons** (wireframe, cursor, camera FOV handle, brush tools) and **open letter-spacing** on BoundChrome labels (`font_scale` still drives size)
- Paint / Smudge / Erase direction cells: **flat square control_bg** + line icons (not beveled 9-slice plates, corner chevrons, or +/− tick overlays)
- BoundChrome Selectable/Graphic: **hard solid squares** (SAVE 2K litmus expanded) — opaque Simple fills, no soft sliced whiskers / corner chevrons; real Toggle checkmarks kept
- `corner_radius` token kept at **0** (soft rounded control sprites retired for Nomad chrome)
- Vertical sliders (FOV, etc.): **pill track + segmented coral fill + bullseye thumb** (not gold disc / red camera chip)
- Dimension mode SD/3D/UV: **flat discs + reverse-out** light type (not glossy spheres)
- Addon Manager fullscreen: header icons share a left gutter; with `ribbon_icon_only`, compact icon-only header actions
- Persistence: host PlayerPrefs remembers last applied theme
- Does **not** call `set_ui_scale` or hide chrome via `set_ui_target_active`

Enabling the add-on **registers** the preset and builds the panel; it does **not**
auto-apply. Use **Apply Nomad Palette**. Disabling restores the builtin palette when Nomad
is active, restores the pre-Apply skybox when captured, then unregisters the preset.

## Panel controls

| Control | Effect |
|---------|--------|
| Apply Nomad Palette | register + `apply_theme` + strip line icons (SPZ skybox/BG kept) |
| Restore SPZ Palette | `reset_theme` (+ restore skybox if an older session painted charcoal) |
| Font scale / Spacing scale | sliders (0.75–1.5) |
| Apply Scales | `apply_theme` patch while Nomad is active (fail closed otherwise) |
| Refresh Theme Status | logs `get_theme` / `list_themes` bound-surface honesty |

## Strip icons (on Apply)

| Tab match | Icon |
|-----------|------|
| Paint | Brush (paintbrush) |
| art list | Image (picture frame) |
| art bg | Layers |
| Control / CTRL / controlnet | Grid |
| Mesh / 3D / Obj | Mesh |
| Nomad | Settings |

Hovering a strip tab shows its name (useful in icon-only mode). Auto-resolve uses the same mapping from tab title + label.

## Palette (defaults)

Colors as Pro-Studio Monolith; `control_bg` `#9A9EAA` stays clearly above `panel_bg` `#1E1F23` so left-column faces remain readable; `corner_radius` 0 (solid-square litmus); `icon_tint` warm; `panel_width` 220; `panel_alpha` 0.92; `font_scale` 0.84; `spacing_scale` 0.94.

Native Unity fallback (`AddonUI_MGR`) mirrors the same path when Python HTTP is down.

Out of scope: true blur, custom icon upload, structural DOM / chrome hide.

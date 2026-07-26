# Nomad Theme SPZ

Managed add-on that registers the `nomad-inspired` preset (theme rpc **1.18**) and drives UI
through theme tokens + compose hooks:

- `spz.ui.register_theme` / `apply_theme` / `reset_theme` / scale **patch**
- Tokens: colors + scales + `corner_radius` / `icon_tint` / `panel_width` / `panel_alpha` / **`ribbon_icon_only`**
- Compose: charcoal skybox + strip `set_line_icon`
- CommandRibbon: when `ribbon_icon_only` is on, **hides tab labels** and centers larger line icons (Nomad-like); Restore SPZ shows labels again
- Persistence: host PlayerPrefs remembers last applied theme
- Does **not** call `set_ui_scale` or hide chrome via `set_ui_target_active`

Enabling the add-on **registers** the preset and builds the panel; it does **not**
auto-apply. Use **Apply Nomad Palette**. Disabling restores the builtin palette when Nomad
is active, restores the pre-Apply skybox when captured, then unregisters the preset.

## Panel controls

| Control | Effect |
|---------|--------|
| Apply Nomad Palette | register + `apply_theme` + charcoal skybox + strip line icons |
| Restore SPZ Palette | `reset_theme` + restore captured skybox |
| Font scale / Spacing scale | sliders (0.75–1.5) |
| Apply Scales | `apply_theme` patch while Nomad is active (fail closed otherwise) |
| Refresh Theme Status | logs `get_theme` / `list_themes` bound-surface honesty |

## Strip icons (on Apply)

| Tab match | Icon |
|-----------|------|
| Paint | Brush |
| Art / BG | Eye |
| Control / CTRL | Grid |
| Mesh / 3D / Obj | Mesh |
| Nomad | Settings |

## Palette (defaults)

Colors as Pro-Studio Monolith; `corner_radius` 5; `icon_tint` muted; `panel_width` 220; `panel_alpha` 0.92; `font_scale` 1.05.

Native Unity fallback (`AddonUI_MGR`) mirrors the same path when Python HTTP is down.

Out of scope: true blur, custom icon upload, structural DOM / chrome hide.

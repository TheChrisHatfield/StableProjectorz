# Nomad Theme SPZ

Enabling this managed add-on registers and applies the `nomad-inspired` preset through
the StableProjectorz theme surface (rpc **1.12**). With the default FastAPI add-on
lifecycle, disabling it restores the builtin palette when the Nomad preset is active,
then unregisters the preset.

P2 binds core chrome ownership roots so the same apply restyles:

- Add-on panels (`AddonPanel_*`)
- Command ribbon strip / panels / thin gold active bar
- Paint tab (Collect / Krita layout / layers)
- Add-on Manager
- Settings chrome (not product wireframe/noise prefs)
- Viewport status line (RGB; sticky alerts stay caller-owned)
- Left + workflow ribbons (known controls)

Palette (Pro-Studio Monolith reference: layered charcoal, metallic gold, soft technical text):

| Token | Hex |
|-------|-----|
| `panel_bg` | `#1E1F23F2` |
| `control_bg` | `#292A2EFF` |
| `field_bg` | `#121317FF` |
| `accent` | `#F2CA50FF` |
| `text_primary` | `#E3E2E7FF` |
| `text_muted` | `#D0C5AFFF` |
| `handle` | `#C8C5CBFF` |
| `success` | `#7BC96FFF` |
| `danger` | `#FFB4ABFF` |
| `border` | `#99907C66` |
| `tab_active` | `#343539FF` |
| `selection` | `#F2CA5033` |

Runtime-created ribbon and Add-on Manager controls also use thin anti-aliased line glyphs.
Out of scope (P3+): persistence, font/spacing tokens, real backdrop blur, skybox-in-theme.

# BoundChrome Role Matrix Specification

## Navigation

- Hooks: `spec.flow`, `context.delta`, `change.validation`
- Delta micro: [`../../delta/20_micro/boundchrome-role-matrix.md`](../../delta/20_micro/boundchrome-role-matrix.md)
- Tasks: [`tasks.md`](./tasks.md)
- Cartridge: [`../../../cartridge/micro/addon-theme-api.md`](../../../cartridge/micro/addon-theme-api.md)

## Goal

Keep **Nomad BoundChrome aligned with traditional (authored SPZ) UI** as features change.
Traditional layout and controls remain the source of truth. The role matrix maps traditional
control kinds to existing Nomad helpers at **ownership roots only**, and fully restores
authored chrome on builtin / Restore SPZ.

## Requirements

1. Traditional / authored SPZ UI is the source of truth for structure and feature changes.
2. When `ShouldRecolorBoundChrome` is true, `ApplyBoundChromeRolesUnder(root)` classifies
   nodes under that root and dispatches to existing BoundChrome helpers.
3. When builtin / leave, the same call (or leave path) uses `RestoreBoundChromeUnder(root)`
   so traditional UI matches pre-Nomad authored state.
4. Optional `SpzUiThemeRoleTag` overrides heuristics when traditional structure is ambiguous.
5. Classifier never walks the global UI skeleton — only the passed ownership root.
6. Domain art (`RawImage` / `Skip`) is not recolored as chrome.
7. `DownloadMoreSlide` uses `ApplyDownloadMoreSlideChrome`.
8. `StripStack` is not auto-applied without an explicit caller (needs glyph); tag alone is documentation unless the caller invokes stacked cell apply.

## Roles (v1)

| Traditional kind | Role | Helper |
|------------------|------|--------|
| Circle dial / numeric overlay | `DialValue` | `ApplyBoundChromeDialValueTmp` / dial `ApplyThemeTokens` |
| Short Button/Toggle caption | `CompactTool` | `ApplyBoundChromeCompactToolLabelTmp` |
| Multi-line list / long dropdown caption | `ReadableBody` | `ApplyBoundChromeReadableBodyTmp` |
| Prompt header / polarity sign | `PromptHeader` / `PromptSign` | prompt helpers |
| Button/Toggle/Dropdown shell | `SelectableFace` | `ApplyBoundChromeSelectable` + hit-face |
| Download-more SlideOut | `DownloadMoreSlide` | `ApplyDownloadMoreSlideChrome` |
| Input field text | `FieldText` | `ApplyBoundChromeTmp` |
| Domain art | `Skip` | no-op |

## Non-goals

- Redesigning traditional SPZ layout to look more Nomad
- Global scene `FindObjects` theme walk
- Replacing token apply / persistence
- True blur / custom icon bytes
- NomadThemeSPZ Python hierarchy walk (RPC honesty later)

## Acceptance

- EditMode: tagged DialValue vs Compact metrics under Nomad; leave restores authored spacing/wrap.
- Pilot roots (SD input, ControlNet unit, Soft workflow options) call the matrix and still Restore SPZ.
- Wiring: `ThemeChanged` → ownership root → matrix → helper; leave → authored.

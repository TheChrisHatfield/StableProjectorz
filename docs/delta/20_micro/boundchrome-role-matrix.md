# BoundChrome Role Matrix

## Navigation

- Hooks: `context.delta`, `spec.flow`, `change.validation`
- Spec: [`../../specs/boundchrome-role-matrix/spec.md`](../../specs/boundchrome-role-matrix/spec.md)
- Tasks: [`../../specs/boundchrome-role-matrix/tasks.md`](../../specs/boundchrome-role-matrix/tasks.md)
- Cartridge: [`../../../cartridge/micro/addon-theme-api.md`](../../../cartridge/micro/addon-theme-api.md)

## Intent

Keep Nomad BoundChrome **aligned with traditional authored SPZ UI** as features change.
Traditional controls stay the source of truth; the matrix is the alignment layer to Nomad
helpers at ownership roots, with full Restore SPZ on leave.

## Architecture delta

- Add `SpzUiThemeRole` + optional `SpzUiThemeRoleTag` beside existing BoundChrome helpers.
- Add `SpzUiThemeOps.ApplyBoundChromeRolesUnder(root, options)` — classify traditional
  structure under one ownership root; never scan the global skeleton.
- Optional ownership-root registry for one-shot ThemeChanged subscription helpers.
- Pilot consumers: SD input panel, ControlNet unit (+ download slide), Soft workflow options ribbon.
- NomadThemeSPZ remains token/palette only in this micro; RPC role honesty is follow-on.

## Boundaries

- No Nomad-first redesign of traditional layout.
- No global UI walk.
- No mutation of `Context_Ref`.
- Reuse existing BoundChrome helpers only (DialValue, Compact, ReadableBody, DownloadMore, etc.).

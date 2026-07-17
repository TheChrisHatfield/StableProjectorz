# Add-on Theme Foundation

## Navigation

- Hooks: `context.delta`, `spec.flow`, `change.impact`
- Spec: [`../../specs/addon-theme-foundation/spec.md`](../../specs/addon-theme-foundation/spec.md)
- Plan: [`../../specs/addon-theme-foundation/plan.md`](../../specs/addon-theme-foundation/plan.md)
- Tasks: [`../../specs/addon-theme-foundation/tasks.md`](../../specs/addon-theme-foundation/tasks.md)

## Intent

Establish an opt-in, token-based runtime theme service that add-ons can control through
the existing JSON-RPC API. The first integration target is add-on-created ribbon UI.
Existing application UI must migrate through explicit theme bindings; the foundation
must not recursively recolor arbitrary scene graphics.

## Architecture delta

- Add `SpzUiThemeOps` beside the existing chrome operations as the main-thread theme
  domain service.
- Add JSON-RPC and Python SDK operations for querying, applying, and resetting theme
  tokens.
- Make `AddonUI_MGR` consume current tokens and reapply them to existing add-on UI.
- Preserve a safe built-in default and reject malformed colors or unknown token names.
- Defer broad Art/Paint/core-chrome migration and the Nomad-inspired preset add-on to
  later tasks.

## Boundaries

- No reflection or hierarchy-wide heuristic recoloring.
- No mutation of `Context_Ref`.
- No persistence in this foundation; a theme add-on may reapply its selected tokens
  during registration.
- No font or sprite replacement until explicit asset-safe bindings exist.

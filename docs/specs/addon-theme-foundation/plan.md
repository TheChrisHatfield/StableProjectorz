# Add-on Theme Foundation Plan

## Navigation

- Spec: [`spec.md`](./spec.md)
- Tasks: [`tasks.md`](./tasks.md)
- Delta micro: [`../../delta/20_micro/addon-theme-foundation.md`](../../delta/20_micro/addon-theme-foundation.md)

## Design

`SpzUiThemeOps` owns an immutable default palette and a validated active palette. It
publishes a change event and serializes API results. `AddonUI_MGR` subscribes while
active, uses tokens while constructing fallback controls, and reapplies tokens to its
registered UI when the event fires.

`Addon_SocketServer` remains the transport/router. It dispatches theme methods before
the `AddonUI_MGR` availability guard because the theme state can be set before panels
exist. `spz.py` and `http_server.py` are thin transport wrappers.

The foundation intentionally themes only UI registered by `AddonUI_MGR`. Core runtime
UI can later adopt the same service through explicit, feature-owned bindings.

## Validation

- Compile-check Python SDK and HTTP mirror.
- Run focused automated transport-wrapper tests with a fake client.
- Inspect C# diagnostics and run the repository's available validation command.
- Trace Python/HTTP → JSON-RPC → theme service → existing/new add-on UI.

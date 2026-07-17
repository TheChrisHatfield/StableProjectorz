# Add-on Theme Foundation Tasks

## T1 — API and architecture audit

- [x] Trace socket dispatch, main-thread execution, capability discovery, Python SDK,
  FastAPI mirror, and dynamic add-on UI construction.
- [x] Record the opt-in token architecture and non-goals in Delta and Spec Kit.

## T2 — Core theme service

- [x] Add validated default/active tokens and apply/reset/query operations.
- [x] Notify consumers only after a complete palette has been accepted.

## T3 — Runtime UI integration

- [x] Apply tokens to existing add-on-owned UI after a theme change.
- [x] Use active tokens for controls created after a theme change.

## T4 — Add-on API integration

- [x] Add JSON-RPC routes and capability metadata.
- [x] Add Python SDK and FastAPI wrappers.

## T5 — Validation and wiring audit

- [x] Add focused transport-wrapper tests.
- [x] Run syntax/compile validation available in the workspace.
- [x] Trace caller → transport → handler → theme service → add-on UI.

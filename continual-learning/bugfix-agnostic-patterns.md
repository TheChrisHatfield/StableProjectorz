# Bug-fix agnostic patterns

**Hook:** `learning.loop`, `change.validation`  
**Source evidence:** add-on theme foundation bug passes (2026-07-16) — measured fixes in
`SpzUiThemeOps` / `AddonUI_MGR`, then generalized. Extended by P2 theme adaptive loops (2026-07-17).  
**Loop log:** [`docs/proposals/learning-loop-mine-apply-bugfix-agnostic-20260716.md`](../docs/proposals/learning-loop-mine-apply-bugfix-agnostic-20260716.md)  
**P2 follow-up:** [`docs/proposals/learning-loop-mine-apply-theme-p2-bugfix-20260717.md`](../docs/proposals/learning-loop-mine-apply-theme-p2-bugfix-20260717.md)

Domain examples (Unity ColorBlock, `AddonPanel_*` names) are **illustrations only**. Prefer the
**→ Pattern:** lines below when reviewing any stack.

---

## Patterns

- Symptom: final value darker/stronger/weaker than the configured token.  
  Cause: the platform multiplies, layers, or inherits the same field twice.  
  Fix: one source of truth + one explicit modifier.  
  **→ Pattern:** Know the compositing model before writing to two layers.

- Symptom: lookup hits the wrong object (`a` matches `ab`).  
  Cause: substring / fuzzy identity checks without a delimiter.  
  Fix: exact equality or prefix-with-boundary.  
  **→ Pattern:** Identity checks must be boundary-exact.

- Symptom: refresh retints scaffolding, hit targets, or duplicates work.  
  Cause: walking every registered leaf instead of the owner root.  
  Fix: update the ownership root; let ownership propagate.  
  **→ Pattern:** Apply changes at the ownership root.

- Symptom: callers mutate validated state without events or checks.  
  Cause: public API returns a live internal reference.  
  Fix: return a snapshot / immutable view.  
  **→ Pattern:** Never hand out live mutable internals.

- Symptom: policy keyed on name/id fails for clones/templates.  
  Cause: generated instances keep the source asset name.  
  Fix: normalize identity at creation time.  
  **→ Pattern:** Generated instances don’t keep your naming contract.

- Symptom: half-applied config after a bad field.  
  Cause: mutate-as-you-parse.  
  Fix: build a candidate, reject atomically, then swap and notify.  
  **→ Pattern:** Validate the whole request, then commit.

- Symptom: “hardening” introduces a regression.  
  Cause: multiple defects mixed into one change.  
  Fix: one defect → one fix → one commit → review.  
  **→ Pattern:** Isolate defect scope so the next bug stays visible.

- Symptom: theme/style pass paints brush thumbnails, color swatches, or transparent hit targets.  
  Cause: treating every Image.color as chrome when some images *are* the content.  
  Fix: skip content-bearing graphics; theme only chrome ownership roots.  
  **→ Pattern:** Never retoken content-bearing graphics.

- Symptom: hierarchy walks retint dials, nested option panels, or product pickers.  
  Cause: GetComponentsInChildren without an ownership allowlist / serialized ref.  
  Fix: apply through known refs (or shallow root children), not recursive leaf scans.  
  **→ Pattern:** Prefer serialized ownership refs over recursive leaf walks.

- Symptom: assist/UI “Accept” only changes color; size/opacity stay as the live brush.  
  Cause: optional context flags defaulted from live UI, overriding model heads.  
  Fix: pass override context only when the caller explicitly wants it.  
  **→ Pattern:** Model-output overrides must be opt-in at the call site.

- Symptom: load succeeds then forward NREs on a head.  
  Cause: validated some weight tensors, skipped sibling biases.  
  Fix: expect every tensor the forward path reads before accepting the network.  
  **→ Pattern:** Validate the whole network graph, then accept.

- Symptom: refused action clears prior success telemetry.  
  Cause: reset flags at function entry before validation.  
  Fix: mutate arm/success state only on the success commit path.  
  **→ Pattern:** Failed attempts must not wipe prior committed state.

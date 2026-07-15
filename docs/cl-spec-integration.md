<!--
meta:hook_id: integration.cl_spec
meta:purpose: Continual Learning and Spec Kit integration contract
meta:status: bootstrap
-->

# Continual Learning + Spec Kit Integration

**Hook:** `integration.cl_spec`, `spec.flow`, `memory.agents_md`, `automation.ci_memory`

## Purpose

Connect Continual Learning memory promotion with Spec Kit behavioral truth so agents do not store product behavior in `AGENTS.md` when it belongs in `spec.md`.

## Division of truth

| Layer | Owner | Content |
|-------|-------|---------|
| Behavioral | Spec Kit | What the system does, APIs, acceptance criteria, requirements |
| Operational | `AGENTS.md` | Commands, paths, read order, repo conventions, tool usage |
| Execution | Code + tests | Implemented behavior |
| Memory promotion | Continual Learning → `agents-propose` | Operational bullets only after validation |

## Rules

1. **Behavioral changes** update `docs/specs/<feature>/spec.md` first, then `plan.md` / `tasks.md` if needed.
2. **Operational learnings** may be proposed to `## Durable Workspace Facts` in `AGENTS.md`.
3. Complete the **Integration wiring audit** (`.cursor/rules/integration-wiring-audit.mdc`) on validated changes **before** memory promotion.
4. Continual Learning candidates tagged `# behavioral` or matching behavioral heuristics are **rejected** by `agents-propose`.
5. Run `spec-drift-check` before merging memory proposals or ending a session (when project tooling provides it).

## Candidate file format

```markdown
# operational
- Run validation with `py -3.11 -m hive_planner ci-check`.

# behavioral
- API must return 404 when feature flag is disabled.
```

Behavioral lines require a spec update; do not merge them into `AGENTS.md`.

## CLI

```powershell
py -3.11 -m hive_planner spec-drift-check
py -3.11 -m hive_planner spec-drift-check --candidates docs/proposals/candidates.md
py -3.11 -m hive_planner agents-propose --candidates docs/proposals/candidates.md
py -3.11 -m hive_planner agents-apply --candidates docs/proposals/candidates.md
py -3.11 -m hive_planner agents-ingest --from path/to/cl-export.txt
```

## Related

- [spec-kit-agent-integration.md](./spec-kit-agent-integration.md)
- [docs/planning-rosetta-stone.md](./docs/planning-rosetta-stone.md)

---
name: continual-learning
description: Mental model triangulation for StableProjectorz—OG upstream clone, Context_Ref v2.4.5 transcript, and continual-learning/ notes as three peers; fork Assets/_gm for implementation. Light-touch, non-dogmatic onboarding and evolution.
---

# Continual learning (StableProjectorz)

## Purpose

Turn **narrative + code** into a **reliable mental model** and **durable patterns** without polluting `Context_Ref/` with fork-specific edits. This skill file lives under **`.cursor/skills/`** (tracked in git after the root `.gitignore` exception); **`continual-learning/`** notes may stay gitignored per repo policy.

## Mental model — three anchors (not dogma)

**Treat these three as one triangulated map.** None of them is law; together they cut down wrong guesses while the fork evolves.

1. **Upstream OG** (baseline code, “what shipped upstream”):  
   `d:\DRIVE_DOWNLOADS\STABLE_PROJECTORZ_OG_GITHUB\StableProjectorz`  
   `README.md` + mirrored `Assets/_gm/` paths when you need **fork vs OG** or “what did this area look like before we touched it?”

2. **Context_Ref transcript** (v2.4.5 spoken architecture, “why and how it was explained”):  
   `d:\DRIVE_DOWNLOADS\Stable_Projectoz_Dev_Build\Context_Ref\StableProjectorz Opensource Reference (v2.4.5, Jan 2025).txt`  
   Flow, scenes, responsibilities, intent. **Expect drift** from current type names—use it as story, then grep the fork.

3. **`continual-learning/`** (this fork’s **living** synthesis—maps, **→ Pattern:** lines, “we learned X”):  
   `d:\DRIVE_DOWNLOADS\Stable_Projectoz_Dev_Build\StableProjectorz\continual-learning\`  
   Same tier as OG and transcript for **orientation**: it is where transcript + OG + real bugs get **compressed** so you do not re-derive everything. Folder may be gitignored; it is still part of the **default mental model** for this workspace.

**Where behavior actually lives (implementation):**  
`StableProjectoz_Dev_Build\StableProjectorz\Assets\_gm\` — all edits, compile, runtime. When a story (OG, transcript, or `continual-learning`) disagrees with the fork, **the fork wins for what runs**; refresh **`continual-learning/`** when you discover a **stable** correction worth keeping.

**Practical habit:** skim **README → transcript → OG slice → `continual-learning/*.md` → `AGENTS.md`**, then work in **`Assets/_gm/`**. Use the three anchors **as much or as little as the task needs**—no requirement to worship any single source.

## Read this first (order)

1. **`README.md`** (fork project root) — Unity version, contribution rules, high-level `_gm` layout.
2. **Transcript:** `d:\DRIVE_DOWNLOADS\Stable_Projectoz_Dev_Build\Context_Ref\StableProjectorz Opensource Reference (v2.4.5, Jan 2025).txt` (or `Context_Ref/...` under the fork repo if linked there).
3. **OG:** `d:\DRIVE_DOWNLOADS\STABLE_PROJECTORZ_OG_GITHUB\StableProjectorz\README.md` and matching `Assets/_gm/` paths as needed.
4. **`continual-learning/*.md`** — `d:\DRIVE_DOWNLOADS\Stable_Projectoz_Dev_Build\StableProjectorz\continual-learning\`
5. **`AGENTS.md`** — connectivity, build paths, IL2CPP caveats, UI tokens.

## Rules

- **Do not edit `Context_Ref/`** to patch the product — it stays a **stable reference export**; product fixes live in the fork (per `AGENTS.md`). For **mental model**, keep it alongside **OG** and **`continual-learning/`**; when any story conflicts with code, **the fork wins for behavior**, and **`continual-learning/`** is the right place to record an updated synthesis (not `Context_Ref/`).
- **Map transcript → fork:** verify symbols and call paths in **`Assets/_gm/`** before assuming a line of transcript matches a current type or file name.
- **Map fork → OG when touching shared systems:** multiview, projection, SD payload, import, etc.—**glance at OG** when it helps; skip when the task is purely local and unrelated.
- **New durable learning** (symptom → cause → fix → **→ Pattern:** line) goes under **`continual-learning/`** — not into `Context_Ref/`.
- **Fork vs OG:** OG may lack fork-only features; a **bug in OG** is not a reason to break the fork — document under **→ Pattern:** when it matters. **Deliberate fork improvements** deserve the same light documentation so future readers are not misled by an outdated default map.

## Mental model checklist (entire stack)

Copy when orienting:

```
- [ ] Bootstrap / additive scenes: where is scene list + Build Settings?
- [ ] Singletons: Awake vs Start; cross-scene = .instance not serialized refs
- [ ] Update order: Update_callbacks_MGR / EarlyUpdate / LateUpdate / coroutine host
- [ ] 3D: ModelsHandler_3D.instance, import helper, UDIMs → projection slices
- [ ] Cameras: UserCameras_MGR, View_UserCamera children (view / depth / content / normals / vtx)
- [ ] Projection: ProjectorCamera, ProjectorCameras_MGR, numPOV vs povInfos
- [ ] Render: Objects_Renderer_MGR, accumulation UV / RenderUdims
- [ ] SD: StableDiffusion_Hub, payload, GenData2D_Archive, GenData2D masks + results
- [ ] UI: WorkflowRibbon_UI, dimension mode, ribbons (see ProjectUiScale in AGENTS)
```

## When user says “continual-learning” or `/continual-learning`

1. Read or refresh **Read this first** (README, transcript, OG, **`continual-learning/`**, `AGENTS.md`) as needed—**not** a mandatory full pass every time.
2. **Implement and debug** in **`Assets/_gm/`**; use **OG + transcript + `continual-learning/`** to orient when helpful.
3. If you found a **stable** mismatch between story and fork worth remembering, add a **`→ Pattern:`** bullet under **`continual-learning/`**; skip trivia.

## Related project hooks

- `.cursor/rules/build-stability.mdc` → `continual-learning/build-stability.md` when editing builds / batchmode.
- `.cursor/hooks/state/continual-learning-index.json` if present — optional index for tooling; keep SKILL instructions primary.

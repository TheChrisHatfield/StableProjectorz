<!-- AUTO-SYNC: from context-library/indexes/chunk-index.json -->

# Source Context

**Feature:** `smart-value-paint`
**Generated:** 2026-07-15T05:33:36.658566+00:00
**Limits:** max 3 documents, max 8 excerpts

## Active documents
- context-library/sources/source4s/MLP_DECIMACON_ORIENT.txt — Mlp_Decimacon_Orient
- context-library/sources/source4s/MLP_DECIMACON_DEV_EXTRA.txt — Mlp_Decimacon_Dev_Extra
- context-library/sources/imported/SMART_VALUE_PAINT_DEV_1.md — can we develop adaptive painting. have predictive stroke pattern in paint items of value scale. could MLP be use to capture patterns generate from the forge system.

## Retrieved excerpts

### Source: context-library/sources/source4s/MLP_DECIMACON_ORIENT.txt (Mlp_Decimacon_Orient)
- # Mlp_Decimacon_Orient  ﻿MLP Decimacon orientation (cartridge beacon) Keywords: MLP Decimacon, staged hybrid, shared latent vault, selective self-attention, router, gate, repo cortex, smart-value-paint, tonal MLP, stroke MLP, LAVD  Definition: MLP Decimacon is a staged hybrid: layers advance sequentially; experts within a stage may run in parallel. Shared latent workspace (latent vault). Self-a...

### Source: context-library/sources/source4s/MLP_DECIMACON_DEV_EXTRA.txt (Mlp_Decimacon_Dev_Extra)
- # Mlp_Decimacon_Dev_Extra    “Hey, can you hear me?”  I hear you loud and clear! What's on your mind?   “How does a transformer work in terms of neural network architecture?”  At its core, a transformer processes input data all at once rather than sequentially. It uses something called self-attention, which means every part of the input can pay attention to every other part. The architecture is...

### Source: context-library/sources/imported/SMART_VALUE_PAINT_DEV_1.md (so explain how it would work under the hood and how end user would use it > Practical split)
- ## Practical split  A clean system split would be:   | Layer | Function | | :-- | :-- | | SDXL | Generates target image, style prior, or local patch guidance. [^13_3][^13_4] | | Decimacon controller | Routes tasks between painting experts. [^13_5] | | Tonal MLP | Predicts value group and transition strength. | | Stroke MLP | Predicts stroke geometry and brush parameters. | | Critic | Scores whe...

### Source: context-library/sources/imported/SMART_VALUE_PAINT_DEV_1.md (let's more focus on the data need for input for the MLP. couldn't we use SDXl to create highly curated dataset > What the MLP should predict (labels))
- ## What the MLP should predict (labels)  For each input state, define small, painterly outputs such as:  - **Value group**: one of N discrete value bands for the next stroke (e.g., 1–5 or 1–7). [^5_6] - **ΔValue**: continuous offset from current local value to desired value (regression). - **Edge hardness**: predicted softness (0–1) or one of a few edge types (hard, medium, soft). [^5_3] - **Bl...

### Source: context-library/sources/imported/SMART_VALUE_PAINT_DEV_1.md (can we develop adaptive painting. have predictive stroke pattern in paint items of value scale. could MLP be use to capture patterns generate from the forge system. > Value-scale design)
- ## Value-scale design  Your “paint items of value scale” idea is strong if you formalize value as discrete bins or a continuous luminance target. The codebase already exposes color, opacity, AO-style dark/midtone/highlight controls, and shadow-reduction utilities, so value-aware painting can be grounded in existing controls rather than invented from scratch. [^1_1]  A practical setup is:  - Inp...

### Source: context-library/sources/imported/SMART_VALUE_PAINT_DEV_1.md (was asking for curiosity what happened to the reaserch for paint transformer > How this relates to your idea)
- ## How this relates to your idea  So in terms of your curiosity:  - Paint Transformer’s core ideas — stroke‑set prediction and synthetic self‑training — *did* land and are part of the research landscape. [^10_1][^10_8] - Later work has moved toward richer planning (where to paint next), better process reconstruction, and more diverse stroke/medium models, rather than just extending Paint Transf...

### Source: context-library/sources/imported/SMART_VALUE_PAINT_DEV_1.md (let's more focus on the data need for input for the MLP. couldn't we use SDXl to create highly curated dataset > How SDXL creates a curated dataset)
- ## How SDXL creates a curated dataset  SDXL is perfect as a **curated target and variation generator**, but you still have to build the mapping between your canvas/stroke state and the SDXL target. [^5_1][^5_7]  A practical strategy:  1. **Generate target paintings**     - Use SDXL to create thousands of images in a consistent style family (e.g., oil-like landscapes, portrait studies, monochrom...

## Why relevant
- Tertiary context retrieved by hybrid keyword/embedding match for the active feature.
- Does not override Delta docs, spec, plan, or tasks.

## Hooks
- `planning.rosetta`
- `compiler.pipeline`
- `context.document_sourcing`

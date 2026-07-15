<!-- AUTO-SYNC note: multipass research summary — meta loop 2026-07-15 -->

# Research Summary

**Feature:** `smart-value-paint`

## Problem statement

Add an **adaptive value-scale paint assist** that proposes tonal bins and stroke parameters so the artist can block-in and refine planes within value bands, using the existing StableProjectorz paint/UV stroke path as the sink — not a separate painter.

## Multipass key findings

| Pass | Finding |
|------|---------|
| A | Decision-head DTO + UV sink already match research; Paint Transformer is later stroke-set literature |
| B | LAVD/MoS teach resource allocation — keep separate from paint reasoning |
| C | MLP Decimacon = staged hybrid + shared latent + selective attention — orientation for future family, not v1 runtime |

## Constraints

| In scope | Out of scope |
|----------|--------------|
| Value-band analysis (5 bands) + proposal DTO | Full generative painter |
| Accept via existing paint stack | Production MLP train farm / SDXL pipeline in v1 |
| Understanding Decimacon/LAVD as tertiary maps | Mandatory Decimacon / MoS runtime |

## Risks

- Hybrid emit can select wrong beacons (e.g. LAVD-only); force `source4s` orientation when Decimacon must lead.
- Draft cartridge banners may block Delta auto-sync — multipass Delta was hand-promoted.

## Validation

```powershell
py -3.11 -m hive_planner spec-drift-check
py -3.11 -m hive_planner ci-check
```

# ABR stamp pipeline: where artifacts can come from

Use this to narrow down whether a brush artifact is from **decode** (ABR → texture) or from **program use** (texture → paint result).

## 1. ABR file → decoded stamp texture

| Step | Code location | What happens |
|------|----------------|--------------|
| Load ABR | `BrushAlphas_MGR.LoadAbr_V6Plus` | Finds `8BIM` + `samp` blocks |
| Parse samp block | `ParseSampBlock` | Strategy 1: scan for brush headers. Strategy 2: length-prefixed chunks |
| Decode one brush | `TryExtractBrushFromSampleWithConsumed` / `TryExtractBrushFromSampleWithConsumedWithPath` | 19-byte or 21-byte header, then packed/strided/RLE pixels → `CreateStampFromGrayscaleBytes` |
| Result | `_allEntries[i].texture` | The stamp `Texture2D` (R8 or RGBA32) |

**If the artifact is already in the decoded texture** → bug is in decode (header offset, stride, or row layout). Use **StableProjectorz → Analyze ABR file...** to export the first decoded stamp as PNG and compare with Photoshop.

## 2. Stamp texture → paint result

| Step | Code location | What happens |
|------|----------------|--------------|
| Who uses the stamp | `BrushAlphas_MGR.GetCurrentBrushStampTexOrFallback()` | Single source for all painters |
| Set on material | `Projections_MaskPainter` / `Inpaint_MaskPainter` | `_brushMaterial.SetTexture("_BrushStamp", stamp)` every paint frame |
| Shader samples it | `BrushEffects.cginc`: `sampleBrushStamp` | `tex2D(i.BrushStamp, uv).r` — UV from `brushStampUV( fragUV, center, size, aspect, angle, roundness )` |
| Paint modes | `PaintInBrushStroke` (segment) / `PaintInBrushStroke_Splotches` (discrete stamps) | Same stamp texture; different placement (segment vs array of positions) |

**If the decoded PNG looks correct but the paint result has artifacts** → bug is in how we use the stamp: UV computation, size/aspect, angle/roundness, or splotch positions. Check `brushStampUV`, `_BrushSizes`, and stamp count/positions in the painter.

## How to use the analyzer

1. **StableProjectorz → Analyze ABR file...** and select your `.abr`.
2. Console shows: samp block size, first 4 bytes (BE), first 64 bytes (hex), **decode path** (e.g. "Reference 19-byte packed (trusted offset)" or "Fallback 19/21-byte packed"), and stamp size.
3. PNG is written to `persistentDataPath/StableProjectorz/abr_analyzer_<filename>_stamp.png` and revealed in Explorer/Finder.
4. Open the PNG: if the artifact is visible there, the problem is in our ABR decode. If the PNG looks correct, the problem is in the program (UV, size, or shader use of the stamp).

# ABR structure analysis & StableProjectorz brush flow

This doc analyzes (1) the actual structure of the ABR test files we have, and (2) how StableProjectorz loads and uses ABR brushes so we can tell decode bugs from app bugs.

---

## 1. ABR file structure (from code + test files)

### Test files (from AppData / project TestAbr)

- **Resource Boy - Stipple Brushes.abr** — v6.2, ~3.5 MB  
- **Splatter Brushes 8.abr** — v6.2, ~21 MB  

Both are **ABR v6.2**: first 4 bytes = `00 06 00 02` (version 6, subversion 2).

### v6 layout (how we parse it)

1. **Global:** Bytes 0–3 = version (BE short, BE short). Bytes 4+ = sequence of **8BIM** blocks.
2. **8BIM block:** Signature `38 42 49 4D` ("8BIM"), then 4-byte key, then 4-byte **type** (e.g. `73 61 6D 70` = "samp"), then 4-byte **size** (BE). Block **data** = next `size` bytes.
3. **samp block:** Contains brush tip image(s). Two layouts we support:
   - **Concatenated:** Block data is a stream of brush images back-to-back (no per-item length). We **scan** for 19- or 21-byte image headers and decode.
   - **Length-prefixed:** Block data = `[uint32 len][chunk][uint32 len][chunk]...` with each chunk padded to 4 bytes. We read `len`, then parse one chunk.

### Resource Boy – inferred structure

- First 8BIM at file offset 4: type **"samp"**, size = large (e.g. 3,321,260).
- **SAMP payload** starts at offset 16. First 4 bytes (BE) = **62,283** → this is the **chunk length** for the first item. So this file is **length-prefixed**.
- **Chunk** = bytes 20 to 20+62283. Our parser uses **Just Solve** layout for this chunk:
  - Byte 0 = Pascal string length **n** (e.g. 36).
  - Bytes 1..n = ID string.
  - Bytes 1+n..8+n = 8 bytes unknown.
  - Bytes 9+n..29+n = **21-byte image header**: depth(2), top, left, bottom, right(4×4), depth(2), comp(1). All BE.
  - Then **pixels**: 8-bit uncompressed = **packed** (width×height bytes, no row stride) per reference.
- So the **decode path** for Resource Boy is: **Strategy 2 (length-prefixed)** → **TryExtractBrushFromLengthPrefixedChunk** → **Just Solve 21-byte** → packed pixels. No scan, no Eric 47-byte skip. (Strategy 1 runs first but finds no valid brush in the first 4 bytes or at trusted offsets; then Strategy 2 parses [len][chunk] and succeeds.)

### Splatter Brushes 8

- Same v6.2. Likely also length-prefixed with multiple chunks (many brushes). Structure per chunk same as above (Just Solve or Eric).

### Summary (what the decoder must match)

- **samp** = one or more brush images; either raw stream (scan for headers) or `[len][chunk]` with chunk = Pascal + 8 + **21-byte** header + pixels (Just Solve) or **47-byte skip** + **19-byte** header + pixels (Eric).
- **8-bit uncompressed** = **packed** only (width×height bytes). No 4-byte row alignment in the reference.
- **RLE** = 2-byte BE row lengths + PackBits, no row padding (unless `UseRle1BitRowAlignment`).

---

## 2. How StableProjectorz handles ABR (full flow)

### 2.1 Load (disk → in-memory stamp)

| Step | Code | What happens |
|------|------|--------------|
| Entry | `LoadCustomAlphasFromFolder` (folder scan) or `LoadFromExternalPath` (dialog) | Resolve path, then `LoadSingleAbrFromPath(abrFilePath)`. |
| Read file | `LoadSingleAbrFromPath` | `File.ReadAllBytes(abrFilePath)`, then `LoadAbrFile(bytes, baseName, abrFilePath, uiGroupIndex)`. |
| Version | `LoadAbrFile` | `ReadInt16BE(data, 0)` → 6. Branch to `LoadAbr_V6Plus`. |
| Find samp | `LoadAbr_V6Plus` | Scan for `8BIM` + type `"samp"`, read block size (BE), block data = `pos` to `pos+blockSize`. |
| Parse samp | `ParseSampBlock(data, pos, blockEnd, ...)` | **Strategy 1:** Treat block as concatenated; loop `TryExtractBrushFromSampleWithConsumed` until no brush. **Strategy 2:** If added==0, loop on `[len][chunk]` with `TryExtractBrushFromLengthPrefixedChunk`. |
| Decode one brush | `TryExtractBrushFromSampleWithConsumed` / `TryExtractBrushFromLengthPrefixedChunk` | 19- or 21-byte header, then `DecodeUncompressed` (packed or strided) or `DecodeRLE`. Output: `byte[]` pixels. |
| To texture | `CreateStampFromGrayscaleBytes(pixels, w, h, flipY: true)` | Optional flip Y; fill `Texture2D` (RGBA32 or R8 per `UseRgba32ForBrushStamp`); `SetPixels32` or `SetPixelData`. |
| Store | `_allEntries.Add(new BrushAlphaEntry { texture = stamp, ... })` | One entry per brush; same `sourceFilePath` and `uiGroupIndex` for all brushes from one ABR. |

So the **only** place ABR bytes become a stamp is: **ParseSampBlock** → decode helpers → **CreateStampFromGrayscaleBytes**. If the artifact is already in that texture, the bug is in **decode** (header offset, stride, or row layout).

### 2.2 Which stamp is “current”

| Code | Behavior |
|------|----------|
| `BrushAlphas_MGR.CurrentBrushStampTex` | Returns `_allEntries[_currentIndex].texture`. No copy; same object. |
| `GetCurrentBrushStampTex()` | Same as above (with index clamp). |
| `GetCurrentBrushStampTexOrFallback()` | Returns `GetCurrentBrushStampTex()` or a shared procedural fallback stamp. **Single source** for all painters that use it. |

So the stamp used at paint time is **exactly** the `Texture2D` stored in `_allEntries` for the selected brush. No re-encode or resize in between.

### 2.3 Paint (stamp → screen/mask)

| Step | Code | What happens |
|------|------|--------------|
| Who uses the stamp | **Projections_MaskPainter**, **Inpaint_MaskPainter** | Each frame in `OnRenderIntoCurrTex_please`: `Texture2D stamp = BrushAlphas_MGR.GetCurrentBrushStampTexOrFallback();` then `_brushMaterial.SetTexture("_BrushStamp", stamp);`. |
| Base class | **MaskPainter** | Sets `_PrevNewBrushScreenCoord` (prev/curr cursor), `_BrushSize_andFirstFrameFlag`, `_StampPosSizeStr` / `_StampCount` (splotches), `_ScreenAspectRatio`, `_BrushAngleRad`, `_BrushRoundness01`. Then calls `OnRenderIntoCurrTex_please`. |
| Shader input | **BrushEffects.cginc** | `PaintInBrushStroke_Input` has `BrushStamp` (sampler), `PrevNewBrushScreenCoord`, `BrushSizes_andFirstFrameFlag`, aspect, angle, roundness. |
| UV | `brushStampUV(fragUV, center, size, aspect, angleRad, roundness01)` | `d = fragUV - center`; rotate by `-angleRad`; `size.x /= aspect`; `size.y = size.x * roundness`; `uv = (d + 0.5*size) / size`; clamp to [0,1]. So stamp is sampled in [0,1]²; **size** is in screen space (one scalar for brush radius, then aspect/roundness). |
| Sample | `sampleBrushStamp(i, uv, strength)` | `tex2D(i.BrushStamp, uv).r` (+ strength curve). So we only read the **.r** channel (same for R8 and RGBA32). |

So after load, the **only** thing that touches the stamp is: **SetTexture("_BrushStamp", stamp)** and the **shader** with **brushStampUV** and **tex2D(..., uv).r**. No resizing or format conversion in the app; texture is used as-is.

### 2.4 Exception: Background painter

**Background_Painter** does **not** use the ABR/custom stamp:

```csharp
Texture2D hardTex = BrushRibbon_UI.instance.brushHardnessUI.readSpecificHardnessTex(2); // always hard
_brushMaterial.SetTexture("_BrushStamp", hardTex);
```

So for **background** painting we always use the ribbon’s “hard” brush texture. For **projections** and **inpaint** we use `GetCurrentBrushStampTexOrFallback()` (ABR/custom or fallback). That’s a different code path, not a mixed source for the same stroke.

---

## 3. Where artifacts can come from (decode vs StableProjectorz)

### 3.1 Decode (ABR → Texture2D)

- **Wrong header:** 19- vs 21-byte, or wrong offset (e.g. using scan at a false-positive offset instead of length-prefixed chunk). Can produce wrong dimensions or wrong pixel start → stripes/crosshairs.
- **Wrong pixel layout:** Packed vs 4-byte row stride. We use packed for 8-bit per reference; if the file or our header parse is wrong, we can read stride as packed (or vice versa) → vertical lines.
- **RLE:** Wrong row lengths or row padding → horizontal/vertical lines.

So if the **exported stamp PNG** (from our decoder) already has the artifact, the bug is **decode**.

### 3.2 StableProjectorz (texture → paint result)

- **Texture format:** `UseRgba32ForBrushStamp` vs R8. Some drivers treat R8 differently (e.g. when bound as shader resource). If switching to RGBA32 removes the artifact, the cause is **format/pipeline**, not decode.
- **Single source:** All projection/inpaint code uses `GetCurrentBrushStampTexOrFallback()`. No mixing with ribbon hardness texture for that path.
- **UV/size:** `brushStampUV` maps a screen-space rectangle (size, aspect, roundness) to [0,1]². Stamp is never resized or cropped in code; non-square stamps are sampled correctly. Wrong **size** or **aspect** would scale or stretch the brush, not add a crosshair.
- **Splotches:** Positions and sizes in `_StampPosSizeStr` / `_StampCount` only affect where stamps are placed; they don’t change the texture content.

So if the **decoded stamp PNG is clean** but the **painted result** has the artifact, the cause is in **StableProjectorz** (e.g. format, or a code path we haven’t traced yet). If the PNG has the artifact, the cause is **decode**.

---

## 4. What to do next

1. **Confirm decode path for your file:** Run **StableProjectorz → Analyze ABR (test: Resource Boy Stipple)** and check the console: it should report **LengthPrefixed Just Solve 21-byte** and export a PNG. Open the PNG: if the artifact is there, fix decode (header/stride/layout). If not, the bug is in app/shaders/format.
2. **Try format:** Set `BrushAlphas_MGR.UseRgba32ForBrushStamp = true` (default), reload ABR. If the artifact disappears when switching to RGBA32, investigate R8 path (driver/Unity).
3. **Trace any other use of _BrushStamp:** Search for `_BrushStamp` and `SetTexture` to ensure no other code overwrites the stamp for the same paint mode.

This doc is the single place that ties ABR structure (as we parse it) to the full StableProjectorz load → storage → stamp → paint flow and separates decode issues from app issues.

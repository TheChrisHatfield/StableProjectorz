# ABR Parsing & Brush Stamp — Code Audit

Line-by-line audit of the ABR parsing and brush-stamp path: intent vs implementation, dead code, scaffolding/connectivity, conflicts, and edge cases.

---

## 1. Execution path (scaffolding / connectivity)

| Step | Code location | Intent | Verified |
|------|----------------|--------|----------|
| ABR load entry | `LoadSingleAbrFromPath` → `LoadAbrFile(bytes, ...)` | Single ABR file loads into manager | ✓ |
| Version branch | `LoadAbrFile`: version 1/2 → `LoadAbr_V1V2`; 6–12 → `LoadAbr_V6Plus` | Correct router | ✓ |
| v1/v2 brush read | `LoadAbr_V1V2`: brushType 2 → `ReadBrushImageData(data, ref pos, brushEnd)` | One brush tip per type-2 record | ✓ |
| v6 brush read | `LoadAbr_V6Plus`: block "samp" → `ParseSampBlock` → `TryExtractBrushFromSampleWithConsumed` in loop | One stamp per brush in sample block | ✓ |
| Storage | `_allEntries.Add(new BrushAlphaEntry { texture = stamp, ... })` | Same `Texture2D` reference stored | ✓ |
| Current stamp | `CurrentBrushStampTex` → `_allEntries[_currentIndex].texture` | No copy; same reference | ✓ |
| Paint use | `Projections_MaskPainter` / `Inpaint_MaskPainter`: `GetCurrentBrushStampTex()` → `SetTexture("_BrushStamp", stamp)` | Stamp used at paint time is the decoded texture | ✓ |

**Conclusion:** Load → decode → store → current index → painters use the same stamp reference. No missing or duplicate conversion step.

---

## 2. Line-by-line audit (ABR parsing)

### 2.1 `LoadAbrFile` (394–416)

- **396:** `data.Length < 4` — need at least version(2) + count(2) for v1/v2; v6 uses first 4 for version. ✓
- **401:** `ReadInt16BE(data, 0)` — ABR is big-endian. ✓
- **406, 408:** Version branching correct. ✓

### 2.2 `LoadAbr_V1V2` (417–492)

- **420:** `count = ReadInt16BE(data, 2)` — v1/v2 brush count at offset 2. ✓
- **432:** `brushEnd > data.Length` — bounds check. ✓
- **434–438:** brushType 1 skipped (no image). ✓
- **440:** `brushSize > 28` — minimum for type 2 header + 19-byte image header. ✓
- **459:** `pos += 1` antialiased flag — may advance into optional data; if no name, pos is still before 19-byte header. ✓
- **461–462:** `ReadBrushImageData(data, ref pos, brushEnd)` — **v1/v2 path only**; uses packed 8-bit and correct 1-bit stride (see 2.5). ✓
- **482:** `pos = brushEnd` — after try/catch we advance to next brush even on failure; avoids re-parsing. ✓

### 2.3 `LoadAbr_V6Plus` (502–536)

- **518–519:** Only "desc" and "samp" handled; others skipped. ✓
- **528:** `pos % 2 != 0` — block padding to even; matches common ABR block alignment. ✓

### 2.4 `ParseSampBlock` (539–601)

- **543–564:** Strategy 1 — whole block as concatenated brushes; `pos += consumed` so next brush starts after last decoded. ✓
- **569–601:** Strategy 2 — length-prefixed chunks; inner loop uses `sampleEnd`; after inner loop `pos = sampleEnd`. ✓
- **564:** `if (added > 0) return added` — Strategy 2 not run if Strategy 1 found brushes. ✓

### 2.5 `TryExtractBrushFromConsumed` (559–698)

- **560–561:** Two passes: `preferOrigin == 1` (prefer top==0, left==0), then `preferOrigin == 0` (any valid header). Reduces false positives. ✓
- **553:** Loop condition `offset + 19 <= end` — need at least 19 bytes for fallback header. ✓
- **561–582:** **v6.1 21-byte header:** depth(2) at offset, rect at +2, depth again at +18, comp at +20. Validates `d == d2`, depth 1 or 8, comp 0 or 1, rect non-negative, w6/h6 in range. On success sets `headerLen = 21`, `dataStart = offset + 21`, jumps to `try_decode`. ✓
- **584–601:** **19-byte header:** rect(16) + depth(2) + comp(1). `preferOrigin == 1` skips if top/left not both 0. Sets `headerLen = 19`, `dataStart = offset + 19`. ✓
- **603–604:** `try_decode` recalculates `w`, `h` from rect — correct for both 21- and 19-byte paths. ✓
- **610:** `pixelBytes = depth==8 ? RowStride8Bit(w)*h : RowStride1Bit(w)*h` — v6 uses row-aligned sizes. ✓
- **612:** `pixelBytes < 4` — rejects trivial payloads; 1×1 8-bit or 1-bit still ≥ 4 with stride. ✓
- **614:** `DecodeUncompressed(..., use8BitStride: true)` — v6 path uses 4-byte row alignment for 8-bit. ✓
- **615:** `consumed = headerLen + pixelBytes` — uncompressed consumed matches header + pixel size. ✓
- **618–619:** RLE path: `SkipRLEBlock(..., depth, w)` so 1-bit uses row alignment. ✓
- **621:** `consumed = SkipRLEBlock(...) - offset` — RLE consumed = (end of RLE block) − brush start. ✓
- **624–627:** `pixels != null` then cap `consumed` by `end - offset` and return. ✓

### 2.6 `ReadBrushImageData` (730–767)

- **732:** `pos + 19 > limit` — need full 19-byte header (v1/v2 has no 21-byte variant). ✓
- **734–739:** Reads rect + depth + comp (19 bytes). ✓
- **752–756:** **v1/v2 uncompressed:** `pixelBytes = depth==8 ? (w*h) : RowStride1Bit(w)*h` — **packed 8-bit** for old format; 1-bit still DWORD-aligned. ✓
- **753:** `DecodeUncompressed(..., use8BitStride: false)` — v1/v2 uses packed rows for 8-bit to avoid over-read. ✓
- **760–762:** RLE: `SkipRLEBlock(..., depth, w)` — 1-bit alignment applied. ✓

### 2.7 `RowStride1Bit` / `RowStride8Bit` (773–786)

- **776–778:** `(w+7)/8` bytes per row, then `(rowBytes+3)&~3` — DWORD alignment. ✓
- **786:** `RowStride8Bit(w) = (w+3)&~3` — 4-byte row alignment for 8-bit. ✓

### 2.8 `DecodeUncompressed` (788–821)

- **789:** `use8BitStride` default true — v6 scan path gets stride; v1/v2 explicitly passes false. ✓
- **793–801:** 8-bit with stride: row-by-row with `RowStride8Bit(w)`. ✓
- **800–801:** 8-bit without stride: `Array.Copy(..., w*h)` — packed. ✓
- **804–818:** 1-bit: always `RowStride1Bit(w)`; bit order MSB first (bitIdx 7-(x%8)). ✓

### 2.9 `DecodeRLE` (823–871)

- **827:** `rowWidth = depth==8 ? w : (w+7)/8` — output bytes per row. ✓
- **828:** `pos + h*2 > limit` — need row-length table. ✓
- **832–834:** Row lengths read as big-endian. ✓
- **836:** `p + rowLens[y] > limit` — per-row bounds. ✓
- **839–856:** PackBits decode; `outOff < outEnd` limits output to `rowWidth` per row. ✓
- **858:** 1-bit: `p = (rowEnd+3)&~3` — next row starts after 4-byte alignment. ✓
- **862–869:** 1-bit expansion from `raw` to full pixels; `rowWidth` and bit index correct. ✓

### 2.10 `SkipRLEBlock` (873–893)

- **877:** `pos + h*2 > limit` — same as DecodeRLE. ✓
- **882–890:** 1-bit: advance by `(p + rowLens[y] + 3) & ~3` per row; 8-bit: advance by `rowLens[y]`. Matches DecodeRLE advancement. ✓
- **892:** `Mathf.Min(p, limit)` — never return past buffer. ✓

### 2.11 `CreateStampFromGrayscaleBytes` (895–916)

- **899:** `flipY` for ABR — document says top-down; flip gives correct orientation. ✓
- **904–909:** Flip copies row `(h-1-y)` to row `y`. ✓
- **912:** `stamp.Apply(true)` — upload to GPU. ✓

---

## 3. Dead / unused code

- **`TryExtractBrushFromSample` (638–642):** Wrapper that returns only `stamp` (drops `consumed`). Not referenced in this codebase. Kept as a thin public-style helper in case external or future code needs “extract one brush, don’t care how many bytes.” Not dead in spirit; currently unused. No change.

---

## 4. Conflicts / impediments

- **v1/v2 vs 8-bit stride:** Previously, 8-bit used `RowStride8Bit` in both v1/v2 and v6. Old ABR v1/v2 likely use packed rows; using stride there could over-read and corrupt next brush. **Fixed:** `ReadBrushImageData` uses `pixelBytes = w*h` and `DecodeUncompressed(..., use8BitStride: false)` for v1/v2; v6 path keeps stride. ✓
- No other code was found that overwrites or re-interprets the stamp after it’s stored in `_allEntries`.

---

## 5. Edge cases

| Case | Handling |
|------|----------|
| Empty or tiny brush (w=1,h=1) | `pixelBytes < 4` in scan path rejects 0–3 byte payloads; 1×1 8-bit packed = 1 byte (rejected in v6); 1×1 1-bit stride = 4 (allowed). v1/v2 allows 1×1. ✓ |
| Very large brush (w/h up to 4096) | Explicit clamp `w > 4096 \|\| h > 4096` in both paths. ✓ |
| Negative top/left | Rejected in both 21-byte and 19-byte validation. ✓ |
| RLE row length 0 | DecodeRLE: `rowEnd = p`; loop exits; `p` advances by 0 then (1-bit) aligned. SkipRLEBlock: same. ✓ |
| RLE row length > remaining buffer | `p + rowLens[y] > limit` → return null / safe skip. ✓ |
| PackBits code -128 | `n != -128` branch; -128 is no-op in PackBits, so we don’t read a repeat byte. ✓ |
| Multiple brushes in one samp chunk | Strategy 1 loop: `pos += consumed`; each iteration starts after previous brush. ✓ |
| 21-byte header at wrong offset (e.g. in middle of Pascal string) | Validation (depth match, rect range, comp 0/1) and subsequent decode (enough bytes, valid dimensions) tend to fail; may consume and advance once, then next iteration continues. Worst case one bad decode; prefer-origin reduces chance. ✓ |
| depth=1 but RLE row lengths not padded in file | If file has no padding, we advance by `(rowEnd+3)&~3` and skip into next row’s data. Would corrupt that row. If crosshairs persist on 1-bit RLE, consider making 1-bit RLE alignment optional or configurable. Not changed in this audit. |

---

## 6. Scan path: packed vs strided (crosshair fix)

**Issue:** When the scan path (`TryExtractBrushFromSampleWithConsumedWithPath`) hits 8-bit uncompressed data, the file may use **4-byte row stride** (v6 style). If we only tried packed when `dataStart + w*h <= end`, we would decode as packed and get wrong pixels (vertical/horizontal lines) because the bytes are actually strided.

**Fix (implemented):** In the fallback `TryCandidate` for 8-bit uncompressed:
- When **both** `packedFits` and `strideFits`, decode **both** packed and strided, run `ScoreDecodedBrush` on each, and keep the result with **lower score** (fewer identical rows / stripe columns). Set `consumed` to the chosen layout (hdrLen + w*h or hdrLen + pixelBytesStride).
- When only one fits, use that one (unchanged).

This matches the **length-prefixed Just Solve** path (TryExtractBrushFromLengthPrefixedChunkWithPath), which already does try-both-and-pick-by-score when both fit. No scaffolding: both paths now use the same logic for stride ambiguity.

**Verification:** `DecodeUncompressed` (8-bit strided): `pixels[y*w+x] = data[dataStart + y*rowStride + x]` with `rowStride = (w+3)&~3`. Correct. `CreateStampFromGrayscaleBytes`: flipY copies row (h-1-y) to row y; RGBA32/R8 both implemented; no placeholder. `ScoreDecodedBrush`: lower = better (identical rows × 2 + near-constant columns). `ReadInt16BE`/`ReadInt32BE`: big-endian. All fully implemented.

---

## 7. Summary

- **Intent vs implementation:** v6.1 21-byte header, prefer-origin scan, 1-bit and 8-bit row strides (v6 only for 8-bit), and v1/v2 packed 8-bit are implemented as intended.
- **Scaffolding:** Load → parse → store in `_allEntries` → `CurrentBrushStampTex` → `GetCurrentBrushStampTex()` → `_BrushStamp` is connected; no missing or duplicate steps.
- **Scan path stride:** Scan fallback now tries both packed and strided when both fit and picks by score (same as length-prefixed path), fixing crosshairs when the ABR uses 4-byte row stride.
- **Unused code:** Only `TryExtractBrushFromSample` is unused; left as optional API.
- **Conflict resolved:** v1/v2 8-bit now uses packed rows in `ReadBrushImageData` so v1/v2 does not over-read.
- **Edge cases:** Bounds, empty/tiny brushes, RLE and PackBits edge cases, and multi-brush advancement are handled; 1-bit RLE alignment remains a possible future toggle if needed.

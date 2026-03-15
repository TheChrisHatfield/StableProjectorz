# ABR unpacking – line-by-line artifact audit

Focused on what could cause **crosshair/line artifacts** in decoded brush stamps. Normal (built-in) brushes don’t use this path, so any artifact is from the ABR decode pipeline.

---

## 1. Artifact causes (what we guard against)

| Cause | Effect | Where we handle it |
|-------|--------|--------------------|
| **Wrong row stride** | File has 4-byte-aligned rows; we read packed → wrong bytes per row → vertical lines at row boundaries. Or file packed, we use stride → read padding as data → vertical stripes. | Try both packed + strided when both fit; pick by `ScoreDecodedBrush` (length-prefixed + scan path). |
| **Width/height swapped** | Wrong aspect, repeated columns/rows. | We use `w = right - left`, `h = bottom - top` everywhere; no swap. |
| **Rect off-by-one** | ABR rect is exclusive right/bottom (right = first column past data). We use `w = r - l`, `h = b - t`. Inclusive would be `r - l + 1`. | Eric/Just Solve docs use width = right-left; we match. |
| **Reading padding as pixel data** | 1-bit rows padded to 4 bytes; we must not read padding. | `RowStride1Bit` gives stride; we only read first `(w+7)/8` bytes per row via `rowByte`; padding never read. |
| **Double flip or wrong flip** | Upside-down or duplicated lines. | Single flip in `CreateStampFromGrayscaleBytes(flipY: true)`; no other flip. |
| **Big-endian wrong** | Garbage dimensions → reject or wrong w/h. | `ReadInt16BE` / `ReadInt32BE` implemented correctly; validation rejects bad depth/rect. |
| **RLE row advancement wrong** | Next row starts in wrong place → stripes. | `p = rowEnd` (or `(rowEnd+3)&~3` for 1-bit when `UseRle1BitRowAlignment`); matches `DecodeRLE`. |
| **Out-of-bounds read** | Reading past buffer can pull in unrelated data → random lines. | Callers check `packedFits`/`strideFits`; `DecodeUncompressed` now validates `dataStart + requiredBytes <= data.Length` and returns null. |

---

## 2. Line-by-line unpacking verification

### 2.1 Header and dimensions

- **Eric path (length-prefixed):** `headerStart = chunkStart + 47`; 19-byte header (top, left, bottom, right, depth, comp). `w = right - left`, `h = bottom - top`. `dataStart = headerStart + 19`. ✓
- **Just Solve path:** `n = data[chunkStart]`, `hdrStart = chunkStart + 1 + n + 8` (matches doc: 1 + Pascal length + 8 = 9+N; header at 13+N from item start when item includes 4-byte length). 21-byte header: depth(2), rect(16), depth(2), comp(1). `pixStart = hdrStart + 21`. ✓
- **Scan path (19-byte):** `top19, left19, bottom19, right19` at `offset..offset+16`, `depth19` at +16, `comp19` at +18. `dataStart = offset + 19`. ✓
- **Scan path (21-byte):** depth at `offset`, rect at `offset+2`, depth at +18, comp at +20. `dataStart = offset + 21`. ✓

No width/height swap; rect used consistently.

### 2.2 DecodeUncompressed

- **8-bit strided:** `rowStride = (w+3)&~3`. Pixel (x,y) = `data[dataStart + y*rowStride + x]`. So we read exactly `w` bytes per row; padding bytes are skipped. ✓
- **8-bit packed:** `Array.Copy(data, dataStart, pixels, 0, w*h)`. Contiguous; no stride. ✓
- **1-bit:** `rowStride = ((w+7)/8 + 3)&~3`. `byteIdx = dataStart + y*rowStride + rowByte` with `rowByte = x/8` (via increment when `x%8==7`). We only read bytes 0..(w-1)/8 per row; padding not read. ✓
- **Bounds:** At start we now require `dataStart + requiredBytes <= data.Length` (and w/h in 1..4096); return null otherwise. Prevents out-of-bounds reads. ✓

### 2.3 RowStride8Bit / RowStride1Bit

- `RowStride8Bit(w) = (w+3)&~3` → 4-byte aligned. ✓
- `RowStride1Bit(w) = ((w+7)/8 + 3)&~3` → DWORD aligned. ✓

### 2.4 CreateStampFromGrayscaleBytes

- **flipY:** `srcRow = (h-1-y)*w`, `dstRow = y*w`. So output row 0 gets grayscale row h-1 (file bottom), output row h-1 gets grayscale row 0 (file top). Unity `SetPixels32` row 0 = bottom → brush top ends up at texture top. ✓
- **RGBA32:** `(g,g,g,255)`; R8: `SetPixelData(src, 0)`. No stride or layout change. ✓
- Single place we flip; no double flip. ✓

### 2.5 DecodeRLE (PackBits)

- Row lengths: `h` × 2-byte BE at start; then for each row decode exactly `rowLens[y]` bytes. ✓
- `p = rowEnd` (or 4-byte aligned for 1-bit when `UseRle1BitRowAlignment`) so next row starts correctly. ✓
- 8-bit: output `raw` is packed (rowWidth = w). 1-bit: expand bits from `raw` with `byteIdx = y*rowWidth + (x/8)`, MSB first. ✓

### 2.6 ScoreDecodedBrush

- Counts identical consecutive rows and columns with range ≤2. **Lower score = fewer artifacts.** We pick the decode with **minimum** score when trying packed vs strided. ✓

### 2.7 ReadInt16BE / ReadInt32BE

- Big-endian; no sign/width bugs for depth/rect/row lengths. ✓

---

## 3. Paths that can still produce artifacts (and mitigations)

1. **File uses a different stride (e.g. 2-byte align):** We only try packed or 4-byte stride. Unusual alignment could still misalign. Mitigation: try-both-and-score picks the less artifacted of packed vs 4-byte; if file used 2-byte, score may still favor one.
2. **RLE 1-bit row padding:** If the file pads 1-bit RLE rows and we don’t skip padding, next row is misaligned. We have `UseRle1BitRowAlignment`; set true if crosshairs appear on 1-bit RLE brushes.
3. **Reference path (trusted offset):** When we take the “Reference 19-byte packed” path we **never** try strided. If that file actually had stride, we’d get artifacts. Now fixed: when both packed and strided fit at trusted offset, we try both and pick by score (same as fallback and length-prefixed). If only one fits we use that one.

---

## 4. Changes made in this audit

- **DecodeUncompressed:** Added defensive checks: `data != null`, `dataStart >= 0`, `w,h in 1..4096`, and `dataStart + requiredBytes <= data.Length`. Returns null if any fail so we never read past the buffer or create garbage from bad inputs.
- **Reference path (trusted offset):** For 8-bit uncompressed at trusted offset, when both packed and strided fit we now try both, score with `ScoreDecodedBrush`, and return the lower-score result with the correct consumed size. Eliminates crosshairs when the file at that offset uses 4-byte row stride.

---

## 5. Summary

- Stride math, rect dimensions, flip, and RLE advancement are correct and consistent.
- Packed vs strided ambiguity is handled by trying both (when both fit) and choosing by score in both length-prefixed and scan paths.
- Defensive bounds in `DecodeUncompressed` avoid out-of-bounds reads.
- Remaining risk: only non–4-byte stride in the file (e.g. 2-byte align) would be unhandled; we only try packed and 4-byte stride.

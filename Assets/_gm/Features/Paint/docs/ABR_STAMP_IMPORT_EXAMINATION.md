# ABR Stamp Import Examination

This doc traces **how the stamp is built from ABR data** — from file bytes to the texture used for painting. If the **initial stamp** is wrong (e.g. wrong header, stride, or bit layout), that bad image is repeated along the stroke and can show as crosshairs or other artifacts.

---

## 1. Entry points

| ABR version | Entry | Where brush tip is read |
|-------------|--------|---------------------------|
| **v1 / v2** | `LoadAbr_V1V2` → `ReadBrushImageData(data, ref pos, brushEnd)` | Single linear read: rect(16) + depth(2) + comp(1) at `pos`; pixel data starts at `pos + 19`. |
| **v6+**     | `LoadAbr_V6Plus` → `ParseSampBlock` → `TryExtractBrushFromSampleWithConsumed(data, pos, end)` | Scan from `start` for a valid brush **header**; pixel data starts at `dataStart` (see below). |

So: **v1/v2** use a fixed 19-byte header. **v6+** use a **scanner** that tries two header layouts and a “prefer origin” pass. The rest of this focuses on **what we assume** and **where the stamp can go wrong**.

---

## 2. Where we determine “the stamp” (v6+)

**File:** `BrushAlphas_MGR.cs` — `TryExtractBrushFromSampleWithConsumed`.

- We **scan** from `offset = start` looking for a valid brush header.
- For each offset we try, in order:
  - **21-byte header (v6.1):** `depth(2)` + `rect(16)` + `depth(2)` + `comp(1)`. Pixel data at `offset + 21`. We require `d == d2`, `depth` 1 or 8, `comp` 0 or 1, and optionally `top==0, left==0` (prefer-origin).
  - **19-byte header (v1-style):** `rect(16)` + `depth(2)` + `comp(1)`. Pixel data at `offset + 19`.
- Dimensions: `w = right - left`, `h = bottom - top`. No scaling; we decode exactly that rectangle.
- If decode succeeds we call **`CreateStampFromGrayscaleBytes(pixels, w, h, flipY: true)`** — that texture **is** the stamp we use. So any error in header choice, stride, or decode becomes the “initial stamp” and is then repeated (e.g. in splotches).

So the **initial stamp is wrong** if:
- We pick the **wrong header** (e.g. 19 bytes when the file uses 21, or vice versa), so `dataStart` is off and we read “garbage” or a shifted buffer → repeated pattern/crosshairs.
- We use the **wrong row layout** (stride or padding) for the format, so rows or bits are misaligned → vertical/horizontal lines.
- **RLE**: row-length array + PackBits decode or row padding doesn’t match the file → wrong pixels.

---

## 3. Pixel data layout (what we assume)

### 3.1 Uncompressed (`comp == 0`)

- **v6 path** (in `TryExtractBrushFromSampleWithConsumed`):
  - We compute `pixelBytes = depth==8 ? RowStride8Bit(w)*h : RowStride1Bit(w)*h`.
  - We call **`DecodeUncompressed(..., use8BitStride: true)`**.
- **v1/v2 path** (`ReadBrushImageData`):
  - We call **`DecodeUncompressed(..., use8BitStride: false)`** (packed 8-bit rows).

**DecodeUncompressed:**

- **8-bit, use8BitStride true:** Row stride = **`RowStride8Bit(w) = (w+3)&~3`** (4-byte aligned). We read `data[dataStart + y*rowStride + x]` per pixel. So we assume **4-byte-aligned rows**.
- **8-bit, use8BitStride false:** We do **`Array.Copy(data, dataStart, pixels, 0, w*h)`** — packed, no row padding.
- **1-bit:** We always use **`RowStride1Bit(w) = ((w+7)/8 + 3) & ~3`** (DWORD-aligned). We read bits with `byteIdx = dataStart + y*rowStride + rowByte`, `bitIdx = 7-(x%8)`, and emit 0 or 255. So we assume **MSB-first within byte** and **DWORD-aligned rows**.

If the ABR file uses **packed 8-bit rows** (no alignment) but we use `use8BitStride: true`, we’ll read with a stride that’s too large and **shift every row** → vertical lines / crosshair-like artifacts. If the file uses a **different bit order** (e.g. LSB first) or **different row alignment**, we get misaligned rows → horizontal/vertical lines.

### 3.2 RLE (`comp == 1`)

- We call **`DecodeRLE(data, dataStart, end, w, h, depth)`**.
- Layout we assume:
  - **h × 2-byte** row lengths (big-endian) starting at `dataStart`.
  - Then, for each row, **PackBits**-encoded data; we advance by `rowLens[y]` bytes (and optionally 4-byte-align for 1-bit when **`UseRle1BitRowAlignment`** is true).
- **DecodeRLE** for 1-bit: after decoding each row we set  
  `p = (depth==1 && UseRle1BitRowAlignment) ? ((rowEnd+3)&~3) : rowEnd`.  
  So if the file **does** pad 1-bit RLE rows to 4 bytes and we leave the flag **false**, we read the next row from the wrong offset → row misalignment → horizontal bands or crosshairs. The opposite (flag true when file has no padding) also shifts rows.

**SkipRLEBlock** is used to advance past the RLE block for “consumed” count. It must use the **same** row-length and padding rules as **DecodeRLE**; it does (when `UseRle1BitRowAlignment`): `p = (p + rowLens[y] + 3) & ~3` per row.

So the **initial stamp** from RLE can be wrong if:
- Row **length array** is not at `dataStart` (e.g. wrong header length), or
- **Row padding** (4-byte for 1-bit) doesn’t match the file.

---

## 4. Final step: grayscale → texture

**CreateStampFromGrayscaleBytes(grayscale, w, h, flipY: true):**

- We **flip Y** when `flipY` is true (ABR path): row `y` in the texture comes from row `(h-1-y)` in `grayscale`. So we assume ABR tip is stored **top-to-bottom** and we want **bottom-to-top** in the stamp (or the reverse — the important part is that we’re consistent).
- We fill a texture (R8 or RGBA32 per **UseRgba32ForBrushStamp**) with the grayscale bytes. No scaling; **w×h** from decode = **w×h** of the stamp.

So the only way the “initial stamp” is wrong **here** is if **pixels** are already wrong (from decode) or if **flipY** is wrong (shape upside down). Format (R8 vs RGBA32) doesn’t change the pattern, only pipeline behavior.

---

## 5. Summary: where the “initial stamp” can be wrong

| Stage | What we use | Risk for crosshairs / wrong stamp |
|-------|-------------|-----------------------------------|
| **Header / dataStart** | v6.1 21-byte vs v1 19-byte; prefer origin | Wrong offset → reading from wrong place → garbage or shifted data repeated as pattern. |
| **8-bit uncompressed** | v6: `RowStride8Bit(w)` (4-byte rows); v1/v2: packed | If file is packed but we use stride (or the reverse) → row shift → vertical lines. |
| **1-bit uncompressed** | `RowStride1Bit(w)` (DWORD), MSB-first | Wrong stride or bit order → vertical/horizontal lines. |
| **RLE 1-bit row padding** | `UseRle1BitRowAlignment` (default false) | If file pads (or doesn’t), we must match; else row misalignment → bands/crosshairs. |
| **RLE row lengths** | h × 2-byte BE at dataStart | If header is wrong, we read lengths from pixel data → total chaos. |
| **CreateStampFromGrayscaleBytes** | flipY, no resize | Only wrong if decode is wrong or flip is inverted. |

So the **translation from ABR to stamp** that can produce a **wrong initial stamp** (and thus repeated crosshairs) is:

1. **Choosing the wrong header** (19 vs 21 bytes, or wrong offset in the samp block).
2. **Using the wrong row layout** for uncompressed (8-bit stride vs packed; 1-bit stride/bit order).
3. **Using the wrong RLE row padding** for 1-bit (`UseRle1BitRowAlignment`).

Next step is to **inspect a concrete ABR** (or the exported `abr_stamp_debug.png`) and fix the path that matches that file (header size, stride, and RLE padding). No code changes were made in this pass — this is examination only.

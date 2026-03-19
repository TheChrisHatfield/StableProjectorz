# ABR (Adobe Brush) Decode Reference

This doc is the **single source of truth** for how we decode ABR brush tips. **As of the ground-up rebuild**, we use **only** the Eric Lamarque GIMP plugin layout: no scanning, no score-based stride choice, no 21-byte header or concatenated-blob heuristics. This avoids crosshairs and fill artifacts from wrong stride/alignment.

---

## Source: Eric Lamarque abr.c (GIMP plugin)

Reference: [abr.c](https://gist.github.com/justint/d839e8100609c28d3617) — “load v1 and v2 ABR brushes; load v6.1 and v6.2 ABR brushes.”

### v1/v2 — exact steps from abr.c

1. **Sampled brush (type 2)**  
   After brush_type and brush_size: `fseek(abr, 6, SEEK_CUR)` — skip 6 bytes.  
   If version 2: read name (long = char count, then char_count*2 bytes UCS-2 BE).  
   Then `fseek(abr, 9, SEEK_CUR)` — skip 1 (antialiasing) + 8 (4× short bounds).

2. **Image header (19 bytes)**  
   - `top    = abr_read_long(abr);`  (4 BE)  
   - `left   = abr_read_long(abr);`  (4 BE)  
   - `bottom = abr_read_long(abr);` (4 BE)  
   - `right  = abr_read_long(abr);` (4 BE)  
   - `depth  = abr_read_short(abr);` (2 BE)  
   - `comp   = abr_read_char(abr);` (1)  
   So **rect(16) + depth(2) + comp(1) = 19 bytes**. No leading depth; image header is 19 bytes only.

3. **Dimensions and size**  
   `width = right - left`, `height = bottom - top`.  
   `size = width * (depth >> 3) * height`  
   So 8‑bit: **size = w * h** (packed). 1‑bit: formula gives 0 in C; reference effectively 8‑bit for uncompressed.

4. **Uncompressed (comp == 0)**  
   `fread(buffer, size, 1, abr)` — **packed rows, no stride, no alignment**.

5. **RLE (comp == 1)**  
   `rle_decode(abr, buffer, height)`:
   - Read **h × 2-byte BE** row lengths into `cscanline_len[]`.
   - For each row: read exactly `cscanline_len[i]` bytes of PackBits (signed byte n: n &lt; 0 ⇒ repeat next byte (-n+1) times; n ≥ 0 ⇒ copy next (n+1) bytes; -128 = nop).
   - **No padding** between rows — next row starts immediately after the last byte of the current row.

### v6 — exact steps from abr.c

1. **Chunk**  
   `brush_size = abr_read_long(abr)`.  
   `brush_end = brush_size` rounded up to multiple of 4.  
   Chunk = next `brush_end` bytes (length already consumed).

2. **Skip to image header**  
   `fseek(abr, 37, SEEK_CUR)` — skip 37 (“key”).  
   If subversion == 1: `fseek(abr, 10, SEEK_CUR)` — skip 10.  
   So **47 bytes** skipped for v6.1 before the image header.

3. **Image header (19 bytes, same as v1/v2)**  
   Same as above: top, left, bottom, right (4× long BE), depth (short BE), comp (byte). **No** leading depth(2); header is 19 bytes.

4. **Pixels**  
   `size = width * (depth >> 3) * height` → 8‑bit: **w*h packed**.  
   Uncompressed: `fread(buffer, size, 1, abr)`.  
   RLE: same as v1/v2 — 2-byte BE length per row, PackBits, **no row padding**.

### Summary (what we must match)

- **Image header**: always **19 bytes** (rect + depth + comp) in the reference. No 21-byte variant in abr.c.
- **8‑bit uncompressed**: **packed only** — `w*h` bytes, row stride = w. No 4‑byte row alignment.
- **RLE**: **h × 2-byte BE** row lengths, then PackBits per row; **no** 4‑byte alignment after each row.
- **1‑bit**: Not used in reference for v6 (size would be 0); for v1/v2 we keep DWORD row stride for 1‑bit uncompressed per common BMP-style layout.

---

## v6+ samp block – two layouts

### A. Length-prefixed items (Just Solve / Eric Lamarque)

Used when the samp block is `[uint32 len][chunk…][uint32 len][chunk…]` with each chunk **padded to 4 bytes**.

**v6.1 item (Just Solve):**

| Offset | Type     | Content |
|--------|----------|--------|
| 0      | uint32   | Length of remainder (excl. padding) |
| 4      | byte     | Pascal string length N |
| 5      | N bytes  | Pascal string (ID) |
| 5+N    | 8 bytes  | Unknown |
| 13+N   | uint16   | Depth |
| 15+N   | 4×int32  | Rect (top, left, bottom, right) |
| 31+N   | uint16   | Depth (again) |
| 33+N   | byte     | Compression (0=raw, 1=RLE) |
| 34+N   | bytes    | Image data |
| …      | 0–3 bytes | Padding to multiple of 4 |

So the **image header** (depth + rect + depth + comp) is **21 bytes** and starts at **13+N**; **pixel data** starts at **34+N**.

**Eric Lamarque v6**: After the length and chunk start, **skip 37** (“key”), then **subversion 1: skip 10** (total 47 bytes), then **19-byte** header (rect + depth + comp), then pixels. So his layout is: 37+10+19 = 66 bytes before pixels for v6.1, and he uses **packed** pixels (`size = width * (depth>>3) * height`; 1‑bit in that formula is wrong in C so his ref may be 8‑bit only for v6).

**Takeaway**: For length-prefixed v6.1 we can parse **without scanning**: read length, skip 1 + Pascal_byte, skip 8, then read 21-byte image header, then decode pixels (packed for 8-bit; 1-bit row stride still DWORD in many specs).

### B. Concatenated brush data (no item length)

Some ABR v6 files have a single blob of brush data with **no per-item length**. Then we must **scan** for the 19- or 21-byte image header. Our current scan + score approach handles this; the reference doesn’t define this layout.

---

## Pixel layout (shared)

- **Uncompressed (comp=0)**  
  - **8-bit**: Packed rows, `w*h` bytes (Eric Lamarque; Just Solve doesn’t specify stride).  
  - **1-bit**: Often **DWORD-aligned** rows: `rowBytes = ((w+7)/8 + 3) & ~3` (BMP-style). Some ABR may use packed 1-bit; if so, row length `(w+7)/8`.
- **RLE (comp=1)**  
  - **h × 2-byte BE** row lengths, then PackBits per row.  
  - **1-bit RLE**: Some files pad each compressed row to 4 bytes; we have `UseRle1BitRowAlignment` to toggle.

---

## Inversion (opacity / transparency)

**Photoshop ABR convention:** In the brush tip, **black (0) = 100% opaque**, **white (255) = fully transparent**; grays = varying opacity. Our shader uses the stamp’s .r as opacity (high = more paint). So we **invert** decoded ABR grayscale when building the stamp: use `(byte)(255 - g)` so file black → 255 (opaque) and file white → 0 (transparent). This is controlled by `BrushAlphas_MGR.InvertAbrGrayscale` (default **false**; many ABR files use white=opaque—if you see a black brush or full fill, keep false; for Photoshop black=opaque set **true**). Eric Lamarque’s GIMP plugin inverts for the same reason (file black = opaque).

---

## Implementation alignment

1. **v1/v2**: 19-byte header; **packed** 8-bit (and DWORD 1-bit uncompressed for legacy); RLE with 2-byte row lengths, **no row padding** (Eric Lamarque).
2. **v6**: **Eric Lamarque only**: find 8BIM `samp` block; for each item read `brush_size` (4B), chunk padded to 4 bytes; inside chunk skip 37, then subversion 1 → skip 10 else skip 264, then 19-byte header, then **packed 8-bit** or RLE (no padding). **8-bit only** in v6 to avoid crosshairs. No scanning, no 21-byte header, no strided decode.
3. **RLE**: PackBits with **no row padding** (matches abr.c).
4. **Stamp**: RGBA32, flipY, invert grayscale (black in file = opaque). Debug export off by default.

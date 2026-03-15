# ABR → brush effect: backward propagation / cross-correlation

Trace backward from the **observed artifact** (horizontal/vertical lines in the brush) through every transformation to find where "loss" or mismatch could be introduced. Lines suggest **periodic or alignment** errors (stride, row length, offset).

---

## 1. Backward chain (effect → cause)

| Stage | What happens | What could introduce lines? |
|-------|---------------------------|-----------------------------|
| **Shader sample** | `tex2D(BrushStamp, uv).r` | Only if **texture content** has lines or **uv** is quantized. uv is continuous from `(d + 0.5*size)/size` → no period. So **no loss here** unless texture is already wrong. |
| **Texture upload** | `SetPixels32(colors)` or `SetPixelData(src, 0)` | We assume **row-major packed**: index `y*w+x` = row y, col x. Unity expects same for SetPixels32/SetPixelData. If our **input** (grayscale[]) had a hidden stride and we indexed as packed, we'd already have wrong data before upload. So loss would be **upstream** (decoder). R8: some drivers use internal row stride; we pass w*h packed → **possible GPU/driver** issue (use RGBA32 to avoid). |
| **CreateStampFromGrayscaleBytes** | flipY reorder, then fill colors from src | Assumes **grayscale.Length == w*h** and **packed** (row length = w). Decoder always outputs w*h packed, so **no hidden stride** here. flipY only reorders rows; no new period. **No loss** if decoder output is correct. |
| **DecodeUncompressed** | Read from file: packed `Array.Copy` or strided `data[dataStart + y*rowStride + x]` | **Critical.** If **file** has 4-byte row stride and we decode as **packed**: we read bytes 0..w-1 (row 0), then w..2w-1 (but in file row 1 starts at byte stride). So we read **wrong bytes** as row 1 → **vertical lines** (columns shifted or repeated). If **file** is packed and we decode as **strided**: we read from offsets stride, 2*stride, … so we **skip** bytes and read past row end → **vertical lines / smear**. So **stride mismatch** in this step **directly** produces the artifact. |
| **Header / dataStart** | 19- or 21-byte header, then dataStart = headerEnd | If we use **wrong header size** or **wrong offset**, dataStart is off. A small offset (e.g. 4 bytes) makes every row read from the wrong place → **vertical banding**. So **wrong dataStart** → lines. |
| **RLE decode** | Row lengths (2B each), then PackBits per row | If the **file** pads each RLE row to 4 bytes and we don’t skip padding, we read the next row from the wrong byte → **horizontal** or **vertical** corruption. `UseRle1BitRowAlignment` toggles 1-bit padding; 8-bit RLE we assume no padding. **Padding mismatch** → lines. |
| **Which path** | Scan vs length-prefixed; packed vs strided | **Scan path**: we try packed first, then strided when packed doesn’t fit; we can also **score** and choose. **Length-prefixed path**: we **only** use packed for 8-bit (Just Solve and Eric). So if a **length-prefixed** ABR has 4-byte row stride, we **never** try strided → **hidden stride mismatch** → vertical lines. |

---

## 2. Cross-correlation: where the “loss” fits

The artifact (lines) is a **structured** error, not random noise. That usually means:

- **Vertical lines** → row stride or column alignment wrong (we read the same wrong column, or we advance by the wrong amount per row).
- **Horizontal lines** → row boundary wrong (we treat the wrong byte as start of row, or we have row padding).

So the **loss** is in a place that introduces a **period**:

1. **DecodeUncompressed with wrong stride** – we use a fixed row stride; if it doesn’t match the file, every row is shifted → **vertical period**.
2. **Wrong dataStart** – we start reading pixels N bytes late; every row is shifted by the same amount → **vertical banding**.
3. **RLE row padding** – we advance by rowLen bytes; if the file pads to 4 bytes we don’t skip → next row starts wrong → **horizontal/vertical**.
4. **Length-prefixed always packed** – we never try strided for length-prefixed 8-bit, so when the file uses stride we get (1).

So the only **hidden** spot in the middle that matches the artifact is: **length-prefixed 8-bit is decoded as packed only**. If the file has 4-byte row alignment, that’s a stride mismatch and we get vertical lines.

---

## 3. Fix: try both packed and strided for length-prefixed 8-bit

For the **scan** path we already prefer packed but can use strided and score. For **length-prefixed** we only use packed. So we should:

- For length-prefixed, 8-bit, uncompressed: if **both** packed and strided fit in the chunk, decode **both**, **score** (identical rows + stripe columns), and **pick the lower score** (same as scan fallback). That removes the hidden stride mismatch for length-prefixed files that use 4-byte row alignment.

No change to the reference path (19-byte packed at trusted offset) or to RLE; only add a try-both + score for length-prefixed 8-bit uncompressed.

---

## 4. Summary table (backward propagation)

| Step | Input → Output | Stride/alignment assumption | If wrong → artifact? |
|------|----------------|-----------------------------|----------------------|
| tex2D(uv).r | texture, uv → value | None | Only if texture wrong |
| SetPixels32 / SetPixelData | grayscale[] → GPU | Packed, row length w | Only if grayscale wrong |
| CreateStampFromGrayscaleBytes | grayscale[], w, h → Texture2D | grayscale is w*h packed | No (decoder guarantees packed) |
| DecodeUncompressed(packed) | file bytes → pixels[] | File has w*h consecutive bytes | **Yes: vertical lines** if file has row stride |
| DecodeUncompressed(strided) | file bytes → pixels[] | File has row stride (w+3)&~3 | **Yes: vertical lines** if file is packed |
| dataStart | header → pixel start | Header is 19 or 21 bytes | **Yes: banding** if offset wrong |
| RLE row advance | rowLen → next row | No padding (or 4-byte if flag) | **Yes: lines** if padding mismatch |
| **Length-prefixed 8-bit** | chunk → pixels | **We only use packed** | **Yes: vertical lines** if file has stride |

The only **hidden** mismatch in the middle is the length-prefixed path assuming packed-only for 8-bit. Fix: try packed and strided when both fit, choose by score.

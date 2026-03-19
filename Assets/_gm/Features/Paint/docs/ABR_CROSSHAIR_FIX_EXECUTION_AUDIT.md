# ABR Crosshair Fix — Execution Path Audit

This audit verifies that **every part of the crosshair fix is on a live code path** when an ABR brush is loaded and used: no scaffolding-only code, no blocking conditions, and the same code runs in built executables.

---

## 1. Entry points (ABR load)

| Step | File:Line | Code | Executed when |
|------|-----------|------|----------------|
| 1 | BrushAlphas_MGR.cs:241 | `LoadCustomAlphasFromFolder();` | On Init (Awake) and on RebuildEntries() |
| 2 | BrushAlphas_MGR.cs:333 | `LoadSingleAbrFromPath(path);` | For each `.abr` in BrushAlphas folder |
| 3 | BrushAlphas_MGR.cs:384 | `LoadAbrFile(bytes, baseName, abrFilePath, abrGroupIndex);` | From LoadSingleAbrFromPath |
| 4 | BrushAlphas_MGR.cs:406–408 | `LoadAbr_V6Plus(data, ...)` | When `version >= 6 && version <= 12` (typical Photoshop ABR) |

**Alternative entry:** LoadFromExternalPath (dialog) → File.Copy → RefreshCustomAlphas() → RebuildEntries() → same LoadCustomAlphasFromFolder path above. **No other code path** loads ABR stamps.

---

## 2. V6 path (where crosshair fix runs)

| Step | File:Line | Code | Purpose |
|------|-----------|------|---------|
| 5 | 524–525 | `blockType == "samp"` → `ParseSampBlock(...)` | Only "samp" blocks carry brush tip image data |
| 6 | 547–548 | `while (pos < end)` → `TryExtractBrushFromSampleWithConsumed(data, pos, end)` | Every brush in block is extracted here |
| 7 | 552–563 | `_allEntries.Add(... texture = stamp ...)` | The stamp we create is the one stored; no later replacement |
| 8 | 563 | `pos += consumed;` | Next brush starts after decoded bytes (correct advance) |

**Conclusion:** For v6 ABR, every brush stamp is created by `TryExtractBrushFromSampleWithConsumed` and stored in `_allEntries`. No caching or alternate loader.

---

## 3. TryExtractBrushFromSampleWithConsumed — fix components (all must run)

### 3.1 v6.1 21-byte header (correct pixel start)

| Line | Code | When it runs | Blocks fix if skipped? |
|------|------|--------------|-------------------------|
| 658–659 | `offset + 21 <= end` | For every candidate offset with 21 bytes left | If false we never try 21-byte; fall back to 19-byte (can misalign) |
| 661–664 | Read depth, rect, depth2, comp at offset 0,2,6,10,14,18,20 | When 21-byte block present | — |
| 665 | d==d2, depth 1 or 8, comp 0 or 1, top6/left6 non-negative | Validation | Fails on malformed or wrong offset |
| 668 | w6/h6 in range; preferOrigin 0 or (top6==0 and left6==0) | Size + optional origin filter | If brush has top/left!=0, first pass skips; second pass (preferOrigin==0) still runs |
| 671–673 | `headerLen = 21; dataStart = offset + 21; goto try_decode` | On success | **Critical:** pixel data read starts at offset+21, not +19 |

If the file is v6.1 layout and we use 19-byte at this offset, we read 2 bytes of header as pixels → line artifacts. So **21-byte path must be taken** for v6.1 brushes. It is taken when the first 21 bytes at this offset pass the checks above (depth match, valid rect, comp 0/1).

### 3.2 19-byte fallback

| Line | Code | When it runs |
|------|------|--------------|
| 681–696 | Read top,left,bottom,right,depth,comp at offset 0..18; set headerLen=19, dataStart=offset+19 | When 21-byte block not used (wrong offset or validation failed) |

Used for v1/v2-style layout and when 21-byte validation fails. For a true v6.1 brush we want 21-byte to win at the correct offset.

### 3.3 try_decode (shared)

| Line | Code | Fix in play |
|------|------|--------------|
| 706–707 | `pixelBytes = depth==8 ? RowStride8Bit(w)*h : RowStride1Bit(w)*h` | **Row stride:** 4-byte alignment for both 8-bit and 1-bit (no packed rows in v6 path) |
| 709 | `DecodeUncompressed(..., use8BitStride: true)` | **8-bit:** row stride used (not packed). **1-bit:** RowStride1Bit used inside DecodeUncompressed |
| 710 | `consumed = headerLen + pixelBytes` | Advance uses correct header length (21 or 19) and stride-based size |
| 714 | `DecodeRLE(data, dataStart, end, w, h, depth)` | RLE path; 1-bit row alignment controlled by UseRle1BitRowAlignment |
| 715 | `SkipRLEBlock(..., depth, w)` | Same alignment as DecodeRLE so next brush starts correctly |
| 719 | `CreateStampFromGrayscaleBytes(pixels, w, h, flipY: true)` | Single place stamp is created; pixels are our decoded array |

**No #if / UNITY_EDITOR** in this file → same code in editor and in built exe.

---

## 4. DecodeUncompressed — row stride (fix in play)

| Line | Code | Executed for v6? |
|------|------|------------------|
| 791–796 | `use8BitStride` true → RowStride8Bit(w), read row by row | Yes: v6 path passes use8BitStride: true |
| 804–814 | depth!=8 → RowStride1Bit(w), byteIdx = dataStart + y*rowStride + rowByte | Yes: 1-bit uncompressed uses DWORD row stride |

Called from TryExtractBrushFromSampleWithConsumed with `use8BitStride: true` only (v6). So **v6 always gets strided decode**.

---

## 5. DecodeRLE — 1-bit row alignment

| Line | Code | Executed |
|------|------|----------|
| 863 | `p = (depth==1 && UseRle1BitRowAlignment) ? ((rowEnd+3)&~3) : rowEnd` | When comp==1 (RLE). Default UseRle1BitRowAlignment=false → no padding; set true if your ABR pads RLE rows |

So 1-bit RLE is **configurable**: false = back-to-back rows (no padding); true = 4-byte aligned rows. Toggle if crosshairs appear or disappear with one layout.

---

## 6. ReadBrushImageData (v1/v2 only)

Used only from LoadAbr_V1V2 (line 461). Not used for v6. For v1/v2:

- 8-bit: packed rows (`use8BitStride: false`), pixelBytes = w*h.
- 1-bit: RowStride1Bit(w)*h and DWORD stride in DecodeUncompressed.

So v1/v2 path does not use v6 row alignment; no conflict.

---

## 7. Stamp → painter (no bypass)

| Step | Code | Verified |
|------|------|----------|
| Storage | _allEntries[i].texture = stamp (from CreateStampFromGrayscaleBytes) | Same reference |
| Current | CurrentBrushStampTex → _allEntries[_currentIndex].texture | No copy |
| Paint | Projections_MaskPainter etc.: GetCurrentBrushStampTex() → SetTexture("_BrushStamp", stamp) | Same texture reference |

So the decoded stamp is the one painted. No alternate or cached texture.

---

## 8. Blocking / bypass checks

- **Version:** Only 1, 2, or 6–12 call our loaders; 6–12 use the v6 path above. No other version branch creates stamps.
- **Block type:** Only "samp" triggers ParseSampBlock; "desc" and others do not produce stamps.
- **preferOrigin:** When true we skip non-origin brushes once; when false we accept any valid header. So we do not permanently skip the Y brush.
- **Build:** No #if in BrushAlphas_MGR → fix runs in built executable.

---

## 9. Summary: is the fix “just scaffolding”?

| Fix component | Where it lives | When it runs | Fully executed in build? |
|---------------|----------------|--------------|---------------------------|
| v6.1 21-byte header | TryExtractBrushFromSampleWithConsumed 658–673 | When offset+21 valid and validation passes | Yes |
| Row stride 8-bit (v6) | DecodeUncompressed use8BitStride:true, RowStride8Bit | comp==0, depth==8, v6 path | Yes |
| Row stride 1-bit | DecodeUncompressed RowStride1Bit, pixelBytes | comp==0, depth==1, v6 path | Yes |
| RLE 1-bit alignment | DecodeRLE + SkipRLEBlock, UseRle1BitRowAlignment | comp==1, depth==1 | Yes (toggle: default no padding) |
| consumed = headerLen + pixelBytes | try_decode 710 | Uncompressed v6 | Yes |
| Stamp = CreateStampFromGrayscaleBytes(pixels,...) | 719 | Every successful decode | Yes |

**Conclusion:** The solution is not just scaffolding. Every listed line is on the execution path when loading a v6 ABR and decoding a brush; the same code runs in the built app. If crosshairs remain:

1. **Try toggling `UseRle1BitRowAlignment`** (default false). Set to true if the brush is 1-bit RLE and the file pads rows.
2. **Confirm 21-byte path:** Add a one-off Debug.Log when `headerLen==21` to verify your brush takes the v6.1 path.
3. **Inspect exported PNG** (abr_stamp_debug.png): if crosshairs are in the PNG, the bug is in decode (layout/stride/alignment); if not, the bug is downstream (shader/sampling).

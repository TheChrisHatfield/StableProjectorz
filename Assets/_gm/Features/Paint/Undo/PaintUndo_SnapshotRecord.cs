using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace spz {

	/// <summary>One paint undo step: lossless compressed RGBA slices + stack metadata.</summary>
	public class PaintUndo_SnapshotRecord {

		public int Width;
		public int Height;
		public int Slices;
		public int GraphicsFormatValue;
		public int ActiveLayerIndex;
		public int LayerCount;
		/// <summary>When <see cref="LayerCount"/> ≤ 0: which non-layer buffer (see <see cref="PaintUndoNonStackTarget"/>).</summary>
		public int NonStackTargetKind;
		public byte[] CompressedBytes;

		public GraphicsFormat Format => (GraphicsFormat)GraphicsFormatValue;

		/// <summary>Main thread only. Metadata + wire-format blob (same bytes Inflate produces). Compress off-thread via <see cref="PaintUndo_Compress.Deflate"/>.</summary>
		public static bool TryBuildUncompressedBlob(List<Texture2D> sliceTextures, int activeLayerIndex, int layerCount, int nonStackTargetKind, out PaintUndo_SnapshotRecord record, out byte[] uncompressed) {
			record = null;
			uncompressed = null;
			if (sliceTextures == null || sliceTextures.Count == 0) return false;
			var first = sliceTextures[0];
			if (first == null) return false;
			int w = first.width;
			int h = first.height;
			var fmt = first.graphicsFormat;
			// Keep InpaintNoColorMask with LayerCount > 0 so restore targets NoColorMask, not Content.
			if (layerCount > 0 && nonStackTargetKind != (int)PaintUndoNonStackTarget.InpaintNoColorMask)
				nonStackTargetKind = 0;
			using (var ms = new MemoryStream())
			using (var bw = new BinaryWriter(ms)) {
				bw.Write(w);
				bw.Write(h);
				bw.Write(sliceTextures.Count);
				bw.Write((int)fmt);
				bw.Write(activeLayerIndex);
				bw.Write(layerCount);
				bw.Write(nonStackTargetKind);
				for (int i = 0; i < sliceTextures.Count; i++) {
					var t = sliceTextures[i];
					if (t == null) return false;
					var na = t.GetRawTextureData<byte>();
					bw.Write(na.Length);
					var arr = new byte[na.Length];
					na.CopyTo(arr);
					bw.Write(arr);
				}
				uncompressed = ms.ToArray();
				record = new PaintUndo_SnapshotRecord {
					Width = w,
					Height = h,
					Slices = sliceTextures.Count,
					GraphicsFormatValue = (int)fmt,
					ActiveLayerIndex = activeLayerIndex,
					LayerCount = layerCount,
					NonStackTargetKind = nonStackTargetKind,
					CompressedBytes = null
				};
				return true;
			}
		}

		public static PaintUndo_SnapshotRecord PackFromTextures(List<Texture2D> sliceTextures, int activeLayerIndex, int layerCount, int nonStackTargetKind = 0) {
			if (!TryBuildUncompressedBlob(sliceTextures, activeLayerIndex, layerCount, nonStackTargetKind, out var rec, out var raw))
				return null;
			rec.CompressedBytes = PaintUndo_Compress.Deflate(raw);
			return rec;
		}

		/// <summary>Decompress to per-slice RGBA payloads (caller uploads to GPU).</summary>
		public bool TryUnpackSlices(out List<byte[]> sliceData, out string error) {
			sliceData = null;
			error = null;
			if (CompressedBytes == null || CompressedBytes.Length == 0) {
				error = "empty";
				return false;
			}
			byte[] raw;
			try {
				raw = PaintUndo_Compress.Inflate(CompressedBytes);
			} catch (Exception e) {
				error = e.Message;
				return false;
			}
			try {
				using (var ms = new MemoryStream(raw))
				using (var br = new BinaryReader(ms)) {
					int w = br.ReadInt32();
					int h = br.ReadInt32();
					int s = br.ReadInt32();
					int fmt = br.ReadInt32();
					int aix = br.ReadInt32();
					int lc = br.ReadInt32();
					int nstk = br.ReadInt32();
					if (w != Width || h != Height || s != Slices || fmt != GraphicsFormatValue || aix != ActiveLayerIndex || lc != LayerCount || nstk != NonStackTargetKind) {
						error = "metadata mismatch";
						return false;
					}
					sliceData = new List<byte[]>(s);
					for (int i = 0; i < s; i++) {
						int len = br.ReadInt32();
						sliceData.Add(br.ReadBytes(len));
					}
				}
				return true;
			} catch (Exception e) {
				error = e.Message;
				return false;
			}
		}

		/// <summary>Resolve the <see cref="RenderUdims"/> that this snapshot was captured for (by stored layer index), independent of current UI active layer.
		/// Does not require layer count to match exactly — layers may have been added since capture.</summary>
		public bool TryGetRestoreTarget(PaintLayerStack_MGR stack, out RenderUdims target) {
			target = null;
			if (LayerCount <= 0)
				return false;
			if (stack?.Layers == null)
				return false;
			if (ActiveLayerIndex < 0 || ActiveLayerIndex >= stack.Layers.Count)
				return false;
			var layer = stack.Layers[ActiveLayerIndex];
			if (layer == null)
				return false;

			// No Color strokes paint into NoColorMask; undo must restore that buffer, not Content RGB.
			if (NonStackTargetKind == (int)PaintUndoNonStackTarget.InpaintNoColorMask) {
				layer.EnsureNoColorMaskMatchesContent();
				var nc = layer.NoColorMask;
				if (nc == null)
					return false;
				if (nc.width != Width || nc.height != Height || nc.UdimsCount != Slices)
					return false;
				target = nc;
				return true;
			}

			var c = layer.Content;
			if (c == null)
				return false;
			if (c.width != Width || c.height != Height || c.UdimsCount != Slices)
				return false;
			target = c;
			return true;
		}

		/// <summary>Legacy single-buffer path when snapshot has no layer stack metadata (<see cref="LayerCount"/> ≤ 0).</summary>
		public bool MatchesNonStackTarget(RenderUdims target) {
			if (target == null || LayerCount > 0)
				return false;
			return target.width == Width && target.height == Height && target.UdimsCount == Slices
			       && (int)target.graphicsFormat == GraphicsFormatValue;
		}
	}
}

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
		public byte[] CompressedBytes;

		public GraphicsFormat Format => (GraphicsFormat)GraphicsFormatValue;

		/// <summary>Main thread only. Metadata + wire-format blob (same bytes Inflate produces). Compress off-thread via <see cref="PaintUndo_Compress.Deflate"/>.</summary>
		public static bool TryBuildUncompressedBlob(List<Texture2D> sliceTextures, int activeLayerIndex, int layerCount, out PaintUndo_SnapshotRecord record, out byte[] uncompressed) {
			record = null;
			uncompressed = null;
			if (sliceTextures == null || sliceTextures.Count == 0) return false;
			var first = sliceTextures[0];
			if (first == null) return false;
			int w = first.width;
			int h = first.height;
			var fmt = first.graphicsFormat;
			using (var ms = new MemoryStream())
			using (var bw = new BinaryWriter(ms)) {
				bw.Write(w);
				bw.Write(h);
				bw.Write(sliceTextures.Count);
				bw.Write((int)fmt);
				bw.Write(activeLayerIndex);
				bw.Write(layerCount);
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
					CompressedBytes = null
				};
				return true;
			}
		}

		public static PaintUndo_SnapshotRecord PackFromTextures(List<Texture2D> sliceTextures, int activeLayerIndex, int layerCount) {
			if (!TryBuildUncompressedBlob(sliceTextures, activeLayerIndex, layerCount, out var rec, out var raw))
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
					if (w != Width || h != Height || s != Slices || fmt != GraphicsFormatValue || aix != ActiveLayerIndex || lc != LayerCount) {
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

		public bool MatchesLiveStack(PaintLayerStack_MGR stack, RenderUdims target) {
			if (target == null) return false;
			if (target.width != Width || target.height != Height || target.UdimsCount != Slices) return false;
			if (LayerCount <= 0)
				return stack == null;
			if (stack == null || stack.Layers == null || stack.Layers.Count != LayerCount) return false;
			if (stack.ActiveLayerIndex != ActiveLayerIndex) return false;
			return true;
		}
	}
}

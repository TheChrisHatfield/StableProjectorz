using System.IO;
using System.IO.Compression;

namespace spz {

	public static class PaintUndo_Compress {

		public static byte[] Deflate(byte[] raw) {
			if (raw == null || raw.Length == 0) return raw;
			using (var outMs = new MemoryStream()) {
				using (var def = new DeflateStream(outMs, CompressionLevel.Fastest)) {
					def.Write(raw, 0, raw.Length);
				}
				return outMs.ToArray();
			}
		}

		public static byte[] Inflate(byte[] comp) {
			if (comp == null || comp.Length == 0) return comp;
			using (var inMs = new MemoryStream(comp))
			using (var inf = new DeflateStream(inMs, CompressionMode.Decompress))
			using (var outMs = new MemoryStream()) {
				inf.CopyTo(outMs);
				return outMs.ToArray();
			}
		}
	}
}

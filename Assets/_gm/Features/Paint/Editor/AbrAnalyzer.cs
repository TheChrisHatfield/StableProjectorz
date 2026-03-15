using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace spz.Editor
{
	/// <summary>
	/// Analyze an ABR file: dump samp block structure and export the first decoded brush stamp as PNG.
	/// Use this to see exactly how we decoded the brush and compare with Photoshop/GIMP to find decode vs program bugs.
	/// Pipeline: ABR file → samp block → TryExtractBrushFromSampleWithConsumedWithPath → stamp Texture2D → exported PNG.
	/// If the artifact appears in the PNG, the bug is in our decode. If it only appears when painting, the bug is downstream (UV/size/shader).
	/// </summary>
	public static class AbrAnalyzer
	{
		const string PreferStridedPrefKey = "StableProjectorz.AbrPreferStridedWhenBothFit";

		[UnityEditor.InitializeOnLoadMethod]
		static void LoadPreferStridedPref()
		{
			if (EditorPrefs.HasKey(PreferStridedPrefKey))
				BrushAlphas_MGR.AbrPreferStridedWhenBothFit = EditorPrefs.GetBool(PreferStridedPrefKey);
		}

		[MenuItem("StableProjectorz/Analyze ABR file...")]
		public static void AnalyzeAbrFile()
		{
			string path = EditorUtility.OpenFilePanel("Select ABR file", "", "abr");
			if (string.IsNullOrEmpty(path)) return;

			byte[] data;
			try
			{
				data = File.ReadAllBytes(path);
			}
			catch (System.Exception e)
			{
				Debug.LogError("[AbrAnalyzer] Failed to read file: " + e.Message);
				return;
			}

			if (data.Length < 8)
			{
				Debug.LogWarning("[AbrAnalyzer] File too short for ABR.");
				return;
			}

			// Find first "8BIM" + "samp" block (same logic as LoadAbr_V6Plus)
			int pos = 4;
			int blockStart = -1;
			int blockEnd = -1;
			while (pos + 12 <= data.Length)
			{
				if (data[pos] != 0x38 || data[pos + 1] != 0x42 || data[pos + 2] != 0x49 || data[pos + 3] != 0x4D)
				{
					pos++;
					continue;
				}
				pos += 4;
				string blockType = Encoding.ASCII.GetString(data, pos, 4);
				pos += 4;
				int blockSize = ReadInt32BE(data, pos);
				pos += 4;
				if (blockSize <= 0 || pos + blockSize > data.Length) break;
				blockStart = pos;
				blockEnd = pos + blockSize;
				if (blockType == "samp") break;
				pos = blockEnd;
				if (pos % 2 != 0) pos++;
			}

			if (blockStart < 0 || blockEnd <= blockStart)
			{
				Debug.LogWarning("[AbrAnalyzer] No 'samp' block found. File may be v1/v2 or unsupported.");
				return;
			}

			int sampLen = blockEnd - blockStart;
			// Structure dump: first 4 bytes often = length-prefix for first chunk
			int firstU32 = ReadInt32BE(data, blockStart);
			var hex = new System.Collections.Generic.List<string>();
			int hexBytes = Mathf.Min(64, sampLen);
			for (int i = 0; i < hexBytes; i++)
				hex.Add(data[blockStart + i].ToString("X2"));
			string hexLine = string.Join(" ", hex);

			Debug.Log($"[AbrAnalyzer] Samp block: start={blockStart} length={sampLen}. First 4 bytes (BE)={firstU32}. First 64 bytes hex: {hexLine}");

			// Try scan path first (concatenated brush data)
			var (stamp, consumed, decodePath) = BrushAlphas_MGR.TryExtractBrushFromSampleWithConsumedWithPath(data, blockStart, blockEnd);

			// If scan found nothing, try length-prefixed: [4-byte len][chunk]
			if (stamp == null && sampLen >= 8)
			{
				int chunkLen = ReadInt32BE(data, blockStart);
				if (chunkLen > 0 && blockStart + 4 + chunkLen <= blockEnd)
				{
					int chunkStart = blockStart + 4;
					int chunkEnd = blockStart + 4 + chunkLen;
					var (stampLP, consumedLP, pathLP) = BrushAlphas_MGR.TryExtractBrushFromLengthPrefixedChunkWithPath(data, chunkStart, chunkEnd);
					if (stampLP != null)
					{
						stamp = stampLP;
						consumed = consumedLP;
						decodePath = pathLP;
						Debug.Log($"[AbrAnalyzer] Used length-prefixed path. Chunk length={chunkLen}.");
					}
				}
			}

			if (stamp == null || consumed <= 0)
			{
				Debug.LogWarning("[AbrAnalyzer] No brush decoded from this path. v6 uses Eric Lamarque layout only (length-prefixed samp; packed 8-bit, RLE no padding). Load the ABR in the app (Paint tab → Load ABR/PNG) to decode and paint; set BrushAlphas_MGR.DebugExportAbrStampToPng = true and reload to export first stamp to PNG.");
				return;
			}

			Debug.Log($"[AbrAnalyzer] Decoded first brush: {stamp.width}x{stamp.height}, consumed={consumed}, path=\"{decodePath}\"");

			// Export stamp to PNG so we can see exactly what we decoded (if artifact is here, bug is decode)
			string outDir = Path.Combine(Application.persistentDataPath, "StableProjectorz");
			if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
			string baseName = Path.GetFileNameWithoutExtension(path);
			string pngPath = Path.Combine(outDir, $"abr_analyzer_{baseName}_stamp.png");
			try
			{
				byte[] pngBytes = stamp.EncodeToPNG();
				File.WriteAllBytes(pngPath, pngBytes);
				Debug.Log($"[AbrAnalyzer] Stamp exported: {pngPath}. Open this and compare with the same brush in Photoshop. If the artifact is in the PNG, the bug is in our decode; if not, it's in how we use the stamp (UV/size/shader).");
			}
			catch (System.Exception e)
			{
				Debug.LogError("[AbrAnalyzer] Export failed: " + e.Message);
			}

			if (File.Exists(pngPath))
				EditorUtility.RevealInFinder(pngPath);
		}

		[MenuItem("StableProjectorz/Analyze ABR (test: Resource Boy Stipple)")]
		public static void AnalyzeTestResourceBoy()
		{
			string path = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "Editor", "TestAbr", "Resource Boy - Stipple Brushes.abr");
			if (!File.Exists(path))
			{
				Debug.LogWarning("[AbrAnalyzer] Test file not found: " + path + " (copy from AppData BrushAlphas if needed).");
				return;
			}
			AnalyzeAbrFileAtPath(path);
		}

		[MenuItem("StableProjectorz/ABR: Prefer strided (unused after rebuild)")]
		public static void TogglePreferStrided()
		{
			BrushAlphas_MGR.AbrPreferStridedWhenBothFit = !BrushAlphas_MGR.AbrPreferStridedWhenBothFit;
			EditorPrefs.SetBool(PreferStridedPrefKey, BrushAlphas_MGR.AbrPreferStridedWhenBothFit);
			Debug.Log("[AbrAnalyzer] AbrPreferStridedWhenBothFit = " + BrushAlphas_MGR.AbrPreferStridedWhenBothFit + " (no longer used: v6 uses Eric Lamarque packed 8-bit only).");
		}

		[MenuItem("StableProjectorz/ABR: Prefer strided (unused after rebuild)", true)]
		public static bool ValidateTogglePreferStrided()
		{
			if (EditorPrefs.HasKey(PreferStridedPrefKey))
				BrushAlphas_MGR.AbrPreferStridedWhenBothFit = EditorPrefs.GetBool(PreferStridedPrefKey);
			return true;
		}

		[MenuItem("StableProjectorz/Analyze ABR (test: Splatter Brushes 8)")]
		public static void AnalyzeTestSplatter()
		{
			string path = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "Editor", "TestAbr", "Splatter Brushes 8.abr");
			if (!File.Exists(path))
			{
				Debug.LogWarning("[AbrAnalyzer] Test file not found: " + path);
				return;
			}
			AnalyzeAbrFileAtPath(path);
		}

		static void AnalyzeAbrFileAtPath(string path)
		{
			byte[] data;
			try { data = File.ReadAllBytes(path); }
			catch (System.Exception e) { Debug.LogError("[AbrAnalyzer] " + e.Message); return; }
			if (data.Length < 8) { Debug.LogWarning("[AbrAnalyzer] File too short."); return; }

			int pos = 4;
			int blockStart = -1, blockEnd = -1;
			while (pos + 12 <= data.Length)
			{
				if (data[pos] != 0x38 || data[pos + 1] != 0x42 || data[pos + 2] != 0x49 || data[pos + 3] != 0x4D)
				{ pos++; continue; }
				pos += 4;
				string blockType = Encoding.ASCII.GetString(data, pos, 4);
				pos += 4;
				int blockSize = ReadInt32BE(data, pos);
				pos += 4;
				if (blockSize <= 0 || pos + blockSize > data.Length) break;
				blockStart = pos;
				blockEnd = pos + blockSize;
				if (blockType == "samp") break;
				pos = blockEnd;
				if (pos % 2 != 0) pos++;
			}
			if (blockStart < 0 || blockEnd <= blockStart)
			{ Debug.LogWarning("[AbrAnalyzer] No samp block."); return; }

			int sampLen = blockEnd - blockStart;
			int firstU32 = ReadInt32BE(data, blockStart);
			var hex = new System.Collections.Generic.List<string>();
			for (int i = 0; i < Mathf.Min(64, sampLen); i++) hex.Add(data[blockStart + i].ToString("X2"));
			Debug.Log($"[AbrAnalyzer] Samp start={blockStart} len={sampLen} first4BE={firstU32} hex: {string.Join(" ", hex)}");

			var (stamp, consumed, decodePath) = BrushAlphas_MGR.TryExtractBrushFromSampleWithConsumedWithPath(data, blockStart, blockEnd);
			if (stamp == null && sampLen >= 8)
			{
				int chunkLen = ReadInt32BE(data, blockStart);
				if (chunkLen > 0 && blockStart + 4 + chunkLen <= blockEnd)
				{
					var (s, c, p) = BrushAlphas_MGR.TryExtractBrushFromLengthPrefixedChunkWithPath(data, blockStart + 4, blockStart + 4 + chunkLen);
					if (s != null) { stamp = s; consumed = c; decodePath = p; Debug.Log("[AbrAnalyzer] Used length-prefixed."); }
				}
			}
			if (stamp == null) { Debug.LogWarning("[AbrAnalyzer] No brush decoded."); return; }

			Debug.Log($"[AbrAnalyzer] Decoded: {stamp.width}x{stamp.height} consumed={consumed} path=\"{decodePath}\"");
			string outDir = Path.Combine(Application.persistentDataPath, "StableProjectorz");
			if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
			string baseName = Path.GetFileNameWithoutExtension(path);
			string pngPath = Path.Combine(outDir, "abr_analyzer_" + baseName + "_stamp.png");
			try
			{
				File.WriteAllBytes(pngPath, stamp.EncodeToPNG());
				Debug.Log("[AbrAnalyzer] Exported: " + pngPath);
			}
			catch (System.Exception e) { Debug.LogError("[AbrAnalyzer] " + e.Message); return; }
			if (File.Exists(pngPath)) EditorUtility.RevealInFinder(pngPath);
		}

		static int ReadInt32BE(byte[] d, int o)
		{
			if (o + 4 > d.Length) return 0;
			return (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];
		}
	}
}

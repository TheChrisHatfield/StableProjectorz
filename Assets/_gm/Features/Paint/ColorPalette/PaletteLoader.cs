using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace spz {

	/// <summary>
	/// Loads color palettes from ACO (Adobe Color), ASE (Adobe Swatch Exchange), and GPL (GIMP Palette) files.
	/// Use for brush color palettes: drop .aco, .ase, or .gpl files in the Palettes folder, or use "Load palette..." in the Paint tab.
	/// </summary>
	public static class PaletteLoader
	{
		public static string PalettesFolderPath =>
			Path.Combine(Application.persistentDataPath, "StableProjectorz", "Palettes");

		public static void EnsurePalettesFolderExists()
		{
			try
			{
				if (!Directory.Exists(PalettesFolderPath))
					Directory.CreateDirectory(PalettesFolderPath);
			}
			catch (Exception e)
			{
				Debug.LogWarning("Palettes: could not create folder: " + e.Message);
			}
		}

		/// <summary> Load a palette from file. Returns null on failure. Logs reason when load fails. </summary>
		public static List<Color> LoadFromFile(string filePath)
		{
			if (string.IsNullOrEmpty(filePath))
			{
				Debug.LogWarning("Palette load: path is empty.");
				return null;
			}
			if (!File.Exists(filePath))
			{
				Debug.LogWarning("Palette load: file not found: " + filePath);
				return null;
			}
			string ext = Path.GetExtension(filePath).ToLowerInvariant();
			try
			{
				byte[] bytes = File.ReadAllBytes(filePath);
				if (ext == ".aco") return ParseAco(bytes);
				if (ext == ".ase")
				{
					var aseList = ParseAse(bytes);
					if (aseList == null)
						Debug.LogWarning("Palette load: ASE file had no color entries or invalid format: " + filePath);
					return aseList;
				}
				if (ext == ".gpl") return ParseGpl(File.ReadAllText(filePath));
				Debug.LogWarning("Palette load: unsupported extension " + ext + " for " + filePath);
			}
			catch (Exception e)
			{
				Debug.LogWarning("Palette load failed " + filePath + ": " + e.Message);
			}
			return null;
		}

		/// <summary> ACO: version (2 BE), count (2 BE). Per color: 2 space + 4×2 component (BE) = 10 bytes; v2 adds 4-byte name len + Unicode name. </summary>
		static List<Color> ParseAco(byte[] b)
		{
			if (b == null || b.Length < 6) return null;
			int version = (b[0] << 8) | b[1];
			int count = (b[2] << 8) | b[3];
			if (count <= 0 || count > 1000) return null;
			var list = new List<Color>(count);
			int pos = 4;
			for (int i = 0; i < count && pos + 10 <= b.Length; i++)
			{
				int space = (b[pos] << 8) | b[pos + 1];
				int v0 = (b[pos + 2] << 8) | b[pos + 3];
				int v1 = (b[pos + 4] << 8) | b[pos + 5];
				int v2 = (b[pos + 6] << 8) | b[pos + 7];
				pos += 10;
				if (version == 2 && pos + 2 <= b.Length)
				{
					int nameLenChars = (b[pos] << 8) | b[pos + 1];
					pos += 2;
					if (nameLenChars > 0 && pos + nameLenChars * 2 <= b.Length)
						pos += nameLenChars * 2; // Unicode BE
				}
				float r = Mathf.Clamp01(v0 / 65535f);
				float g = Mathf.Clamp01(v1 / 65535f);
				float b_ = Mathf.Clamp01(v2 / 65535f);
				if (space == 1)
				{
					Color c = Color.HSVToRGB(r, g, b_);
					r = c.r; g = c.g; b_ = c.b;
				}
				list.Add(new Color(r, g, b_, 1f));
			}
			return list;
		}

		/// <summary> ASE: 4-byte sig "ASEF", version (4), block count (4). Each block: type (2), length (4), payload. Color block type 0x0001. Color space is 4-byte ASCII: "RGB ", "HSV ", "CMYK", "LAB ", "Gray". Many files add a 2-byte null after the name. </summary>
		static List<Color> ParseAse(byte[] b)
		{
			if (b == null || b.Length < 12) return null;
			if (b[0] != 'A' || b[1] != 'S' || b[2] != 'E' || b[3] != 'F') return null;
			int blockCount = (b[8] << 24) | (b[9] << 16) | (b[10] << 8) | b[11];
			var list = new List<Color>();
			int pos = 12;
			try
			{
				for (int i = 0; i < blockCount && pos + 6 <= b.Length; i++)
				{
					int blockType = (b[pos] << 8) | b[pos + 1];
					int blockLen = (b[pos + 2] << 24) | (b[pos + 3] << 16) | (b[pos + 4] << 8) | b[pos + 5];
					pos += 6;
					if (blockLen < 0 || pos + blockLen > b.Length) break;
					int blockEnd = pos + blockLen;
					if (blockType == 1) // color entry
					{
						if (pos + 2 > blockEnd) { pos = blockEnd; continue; }
						int nameLenChars = (b[pos] << 8) | b[pos + 1];
						pos += 2;
						if (nameLenChars > 32767) { pos = blockEnd; continue; }
						int nameByteLen = nameLenChars * 2;
						if (pos + nameByteLen > blockEnd) { pos = blockEnd; continue; }
						pos += nameByteLen;
						// Many ASE exporters write a 2-byte null terminator after the name; skip it so we don't read it as part of color space.
						if (pos + 2 <= blockEnd && b[pos] == 0 && b[pos + 1] == 0) pos += 2;
						if (pos + 4 > blockEnd) { pos = blockEnd; continue; }
						string cspace = Encoding.ASCII.GetString(b, pos, 4).Trim(); pos += 4;
						float f0 = 0, f1 = 0, f2 = 0, f3 = 0;
						if (pos + 12 <= blockEnd)
						{
							f0 = ReadFloat32BE(b, pos);
							f1 = ReadFloat32BE(b, pos + 4);
							f2 = ReadFloat32BE(b, pos + 8);
						}
						string c = cspace.Length >= 3 ? cspace.Substring(0, 3).ToUpperInvariant() : cspace;
						if (c == "RGB")
						{
							list.Add(new Color(Mathf.Clamp01(f0), Mathf.Clamp01(f1), Mathf.Clamp01(f2), 1f));
						}
						else if (c == "HSV")
						{
							list.Add(Color.HSVToRGB(Mathf.Clamp01(f0), Mathf.Clamp01(f1), Mathf.Clamp01(f2)));
						}
						else if (cspace.Length >= 4 && cspace.ToUpperInvariant().StartsWith("CMYK") && pos + 16 <= blockEnd)
						{
							f3 = ReadFloat32BE(b, pos + 12);
							float r = 1f - Mathf.Clamp01(f0) * (1f - Mathf.Clamp01(f3));
							float g = 1f - Mathf.Clamp01(f1) * (1f - Mathf.Clamp01(f3));
							float bl = 1f - Mathf.Clamp01(f2) * (1f - Mathf.Clamp01(f3));
							list.Add(new Color(r, g, bl, 1f));
						}
						else if (c == "LAB")
						{
							Color rgb = LabToRgb(f0, f1, f2);
							list.Add(rgb);
						}
						else if (c == "GRA" || cspace.ToUpperInvariant().StartsWith("GRAY"))
						{
							if (pos + 4 <= blockEnd)
								f0 = ReadFloat32BE(b, pos);
							float g = Mathf.Clamp01(f0);
							list.Add(new Color(g, g, g, 1f));
						}
						else
						{
							list.Add(new Color(Mathf.Clamp01(f0), Mathf.Clamp01(f1), Mathf.Clamp01(f2), 1f));
						}
					}
					pos = blockEnd;
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning("Palette ParseAse exception: " + ex.Message);
				return list.Count > 0 ? list : null;
			}
			return list.Count > 0 ? list : null;
		}

		/// <summary> LAB: L typically 0-1 or 0-100, a/b often -1..1 or 0-1. Assume L 0-1, a/b 0-1 (neutral 0.5). </summary>
		static Color LabToRgb(float L, float a, float b)
		{
			float y = (L <= 0.0808f) ? L / 9.032f : (float)Math.Pow((L + 0.16) / 1.16, 3);
			float x = (a * 0.5f + 0.5f) * 0.95f * y + 0.01f;
			float z = (0.5f - b * 0.5f) * 1.09f * y + 0.01f;
			float r = x * 3.2406f - y * 1.5372f - z * 0.4986f;
			float g = -x * 0.9689f + y * 1.8758f + z * 0.0415f;
			float bl = x * 0.0557f - y * 0.2040f + z * 1.0570f;
			r = Mathf.Clamp01(r); g = Mathf.Clamp01(g); bl = Mathf.Clamp01(bl);
			return new Color(r, g, bl, 1f);
		}

		static float ReadFloat32BE(byte[] b, int offset)
		{
			if (offset + 4 > b.Length) return 0;
			byte[] le = new byte[4];
			le[0] = b[offset + 3];
			le[1] = b[offset + 2];
			le[2] = b[offset + 1];
			le[3] = b[offset];
			return BitConverter.ToSingle(le, 0);
		}

		/// <summary> GPL: text; "GIMP Palette", then "Name: ...", "Columns: ...", "#" or empty, then "R G B\tName" lines. </summary>
		static List<Color> ParseGpl(string text)
		{
			if (string.IsNullOrEmpty(text)) return null;
			var list = new List<Color>();
			var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
			bool started = false;
			foreach (string line in lines)
			{
				string t = line.Trim();
				if (t.StartsWith("GIMP Palette") || t.StartsWith("Name:") || t.StartsWith("Columns:") || t == "#")
					started = true;
				else if (started && t.Length > 0 && char.IsDigit(t[0]))
				{
					string[] parts = t.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
					if (parts.Length >= 3 &&
					    int.TryParse(parts[0], out int r) &&
					    int.TryParse(parts[1], out int g) &&
					    int.TryParse(parts[2], out int b))
						list.Add(new Color(r / 255f, g / 255f, b / 255f, 1f));
				}
			}
			return list.Count > 0 ? list : null;
		}
	}
}

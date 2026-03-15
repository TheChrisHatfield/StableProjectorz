using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace spz {

	/// <summary>
	/// Manages brush alpha textures: built-in round brushes (soft/medium/hard) plus custom alphas
	/// loaded from a user folder. Drop PNGs (and optionally TGAs) into the BrushAlphas folder
	/// to use them as brush shapes (e.g. text, patterns, material details).
	/// </summary>
	public class BrushAlphas_MGR : MonoBehaviour
	{
		public static BrushAlphas_MGR instance { get; private set; }

		/// <summary> User folder for custom brush alphas. Create this folder and drop PNGs (and .tga) here. </summary>
		public static string BrushAlphasFolderPath =>
			Path.Combine(Application.persistentDataPath, "StableProjectorz", "BrushAlphas");

		[Serializable]
		public struct BrushAlphaEntry
		{
			public string name;
			public Texture2D texture;
			/// <summary> RGBA preview for UI thumbnails. Same as texture for built-in; grayscale copy for R8 stamps. </summary>
			public Texture2D preview;
			public bool isBuiltIn;
			/// <summary> Optional hint from ABR tip size (0 = none). When > 0 and user selects this brush, size slider can be set to this (0-1). </summary>
			public float suggestedSize01;
			/// <summary> ABR spacing 1–1000% (0 = not set / continuous). When > 0, app can apply as brush spacing. </summary>
			public int spacingPercent;
			/// <summary> Suggested hardness 0–1 from ABR (0 = not set). </summary>
			public float suggestedHardness01;
			/// <summary> Suggested angle in degrees from ABR (0 = not set). </summary>
			public float suggestedAngleDeg;
			/// <summary> Suggested roundness 0–1 from ABR (0 = not set). </summary>
			public float suggestedRoundness01;
			/// <summary> Full path to source file (BrushAlphas folder or ABR). Used for permanent delete. </summary>
			public string sourceFilePath;
			/// <summary> Index into _uiGroupDisplayNames. Set at load time: 0 = Built-in, 1 = Custom, 2+ = one per ABR file. One folder per group in preset UI. </summary>
			public int uiGroupIndex;
		}

		[SerializeField] List<Sprite> _builtInBrushShapes = new List<Sprite>(); // Soft, Medium, Hard (same as BrushRibbon_UI_Hardness)

		readonly List<BrushAlphaEntry> _allEntries = new List<BrushAlphaEntry>();
		/// <summary> Display names for preset UI folders, in order. Built at load time: [0]=Built-in, [1]=Custom (if any PNG/TGA), [2+]=one per ABR file (filename without extension). </summary>
		readonly List<string> _uiGroupDisplayNames = new List<string>();
		int _currentIndex;

		/// <summary> All brush alphas: first 3 are built-in (soft/medium/hard), then custom from folder. </summary>
		public IReadOnlyList<BrushAlphaEntry> AllEntries => _allEntries;

		/// <summary> Index 0,1,2 = built-in round; 3+ = custom. </summary>
		public int CurrentIndex
		{
			get => _currentIndex;
			set => _currentIndex = Mathf.Clamp(value, 0, Mathf.Max(0, _allEntries.Count - 1));
		}

		/// <summary> Current brush stamp texture used by all painters. Never null after Init. </summary>
		public Texture2D CurrentBrushStampTex
		{
			get
			{
				if (_allEntries.Count == 0) return null;
				int idx = Mathf.Clamp(_currentIndex, 0, _allEntries.Count - 1);
				if (idx != _currentIndex) _currentIndex = idx;
				return _allEntries[idx].texture;
			}
		}

		/// <summary> App-wide canonical brush stamp. Use this so stamp source is consistent regardless of which UI is open. Returns null if no manager or no entries. </summary>
		public static Texture2D GetCurrentBrushStampTex()
		{
			return instance != null ? instance.CurrentBrushStampTex : null;
		}

		/// <summary> Single system-level source for brush stamp: always BrushAlphas_MGR first, then one shared fallback (never ribbon or mixed sources). Use this in all painters to avoid crosshair/artifact from mixed setup. </summary>
		public static Texture2D GetCurrentBrushStampTexOrFallback()
		{
			Texture2D stamp = GetCurrentBrushStampTex();
			return stamp != null ? stamp : GetOrCreateFallbackStamp();
		}

		static Texture2D _fallbackStamp;
		static Texture2D GetOrCreateFallbackStamp()
		{
			if (_fallbackStamp != null) return _fallbackStamp;
			const int size = 64;
			_fallbackStamp = new Texture2D(size, size, GraphicsFormat.R8_UNorm, 1, TextureCreationFlags.None);
			_fallbackStamp.filterMode = FilterMode.Bilinear;
			_fallbackStamp.wrapMode = TextureWrapMode.Clamp;
			float center = (size - 1) * 0.5f;
			float radius = center - 1f;
			byte[] r8 = new byte[size * size];
			for (int y = 0; y < size; y++)
				for (int x = 0; x < size; x++)
				{
					float d = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
					r8[y * size + x] = (byte)(d <= radius ? 255 : 0);
				}
			_fallbackStamp.SetPixelData(r8, 0);
			_fallbackStamp.Apply(true);
			return _fallbackStamp;
		}

		public bool IsBuiltIn(int index) => index >= 0 && index < _allEntries.Count && _allEntries[index].isBuiltIn;
		public bool IsCustomAlpha(int index) => index >= 3 && index < _allEntries.Count;

		/// <summary> Groups for preset UI: one folder per group. Derived from entries so one ABR file = one folder (key by source path); Built-in and Custom grouped by type. Path/extension normalized so one file never splits. </summary>
		public List<(string groupName, List<int> indices)> GetGroupsForUI()
		{
			var result = new List<(string, List<int>)>();
			var keyOrder = new List<string>();
			var keyToGroup = new Dictionary<string, (string displayName, List<int> indices)>(StringComparer.OrdinalIgnoreCase);

			for (int i = 0; i < _allEntries.Count; i++)
			{
				var e = _allEntries[i];
				string key;
				string displayName;
				if (e.isBuiltIn)
				{
					key = "__builtin__";
					displayName = "Built-in";
				}
				else if (!string.IsNullOrEmpty(e.sourceFilePath))
				{
					string path = e.sourceFilePath.Trim();
					string ext = Path.GetExtension(path).ToLowerInvariant().Trim();
					if (ext == ".abr")
					{
						string fileName = (Path.GetFileName(path) ?? "").Trim();
						if (string.IsNullOrEmpty(fileName)) fileName = "unknown.abr";
						key = "__abr__" + fileName.ToLowerInvariant();
						displayName = Path.GetFileNameWithoutExtension(fileName);
						if (string.IsNullOrEmpty(displayName)) displayName = "ABR Brushes";
					}
					else
					{
						key = "__custom__";
						displayName = "Custom";
					}
				}
				else
				{
					key = "__custom__";
					displayName = "Custom";
				}

				if (!keyToGroup.TryGetValue(key, out var pair))
				{
					keyOrder.Add(key);
					pair = (displayName, new List<int>());
					keyToGroup[key] = pair;
				}
				keyToGroup[key].Item2.Add(i);
			}

			foreach (string k in keyOrder)
				if (keyToGroup[k].Item2.Count > 0)
					result.Add((keyToGroup[k].displayName, keyToGroup[k].Item2));
			return result;
		}

		/// <summary> Suggested brush size 0–1 from ABR tip (0 = no suggestion). Use when user selects this brush to match ABR intent. </summary>
		public float GetSuggestedSize01(int index)
		{
			if (index < 0 || index >= _allEntries.Count) return 0f;
			return _allEntries[index].suggestedSize01;
		}

		/// <summary> Suggested spacing 0–1 from ABR (0 = no suggestion / continuous). 1 = 100% (one stamp per diameter). </summary>
		public float GetSuggestedSpacing01(int index)
		{
			if (index < 0 || index >= _allEntries.Count) return 0f;
			int pct = _allEntries[index].spacingPercent;
			if (pct <= 0) return 0f;
			return Mathf.Clamp01(pct / 1000f);
		}

		/// <summary> Suggested brush angle in degrees from ABR (0 = no suggestion). </summary>
		public float GetSuggestedAngleDeg(int index)
		{
			if (index < 0 || index >= _allEntries.Count) return 0f;
			return _allEntries[index].suggestedAngleDeg;
		}

		/// <summary> Suggested brush roundness 0–1 from ABR (0 = no suggestion; use 1). </summary>
		public float GetSuggestedRoundness01(int index)
		{
			if (index < 0 || index >= _allEntries.Count) return 1f;
			float r = _allEntries[index].suggestedRoundness01;
			return r > 0f ? Mathf.Clamp01(r) : 1f;
		}

		void Awake()
		{
			if (instance != null) { DestroyImmediate(this); return; }
			instance = this;
			EnsureBrushAlphasFolderExists();
			RebuildEntries();
		}

		void OnDestroy()
		{
			if (instance == this) instance = null;
			DestroyCustomTextures();
		}

		public static void EnsureBrushAlphasFolderExists()
		{
			try
			{
				string dir = BrushAlphasFolderPath;
				if (!Directory.Exists(dir))
					Directory.CreateDirectory(dir);
			}
			catch (Exception e)
			{
				Debug.LogWarning("BrushAlphas: could not create folder: " + e.Message);
			}
		}

		/// <summary> Rebuild list: 3 built-in from sprites (or procedural fallback if none), then load custom from folder. UI groups are built at load time: one per Built-in, Custom, and each ABR file. </summary>
		public void RebuildEntries()
		{
			foreach (var e in _allEntries)
			{
				if (!e.isBuiltIn && e.texture != null) DestroyImmediate(e.texture);
				if (!e.isBuiltIn && e.preview != null && e.preview != e.texture) DestroyImmediate(e.preview);
				if (e.isBuiltIn && e.texture != null && !IsSpriteTexture(e.texture)) DestroyImmediate(e.texture);
			}
			_allEntries.Clear();
			_uiGroupDisplayNames.Clear();

			// Group 0: Built-in
			_uiGroupDisplayNames.Add("Built-in");
			const int builtInGroupIndex = 0;
			string[] builtInNames = { "Soft Round", "Medium Round", "Hard Round" };
			for (int i = 0; i < _builtInBrushShapes.Count && i < 3; i++)
			{
				var s = _builtInBrushShapes[i];
				if (s != null && s.texture != null)
				{
					_allEntries.Add(new BrushAlphaEntry
					{
						name = builtInNames[i],
						texture = s.texture,
						preview = s.texture,
						isBuiltIn = true,
						uiGroupIndex = builtInGroupIndex
					});
				}
			}

			if (_allEntries.Count < 3)
			{
				float[] softness = { 0.35f, 0.6f, 0.95f };
				for (int i = _allEntries.Count; i < 3; i++)
				{
					var tex = CreateProceduralRoundBrush(64, softness[i]);
					if (tex != null)
						_allEntries.Add(new BrushAlphaEntry { name = builtInNames[i], texture = tex, preview = tex, isBuiltIn = true, uiGroupIndex = builtInGroupIndex });
				}
			}

			LoadCustomAlphasFromFolder();

			_currentIndex = Mathf.Clamp(_currentIndex, 0, Mathf.Max(0, _allEntries.Count - 1));

			// When brushes are loaded, apply default brush size 32 (0–100 display) so brushes start in a consistent state.
			if (_allEntries.Count > 0)
				StartCoroutine(ApplyDefaultBrushSize32NextFrame());
		}

		const float DefaultBrushSize01 = 32f / 100f; // 32 on 0–100 UI

		System.Collections.IEnumerator ApplyDefaultBrushSize32NextFrame()
		{
			yield return null;
			// Write to the single source of truth so brush state is universal across the app.
			var canonical = BrushRibbon_UI_Size.instance;
			if (canonical != null)
			{
				canonical.SetBrushSize(DefaultBrushSize01);
				canonical.SetBrushSpacing(0f);
				canonical.SetBrushAngle(0f);
				canonical.SetBrushRoundness(1f);
			}
			else if (SD_WorkflowOptionsRibbon_UI.instance != null)
			{
				SD_WorkflowOptionsRibbon_UI.instance.SetBrushSize(DefaultBrushSize01);
				SD_WorkflowOptionsRibbon_UI.instance.SetBrushSpacing(0f);
				SD_WorkflowOptionsRibbon_UI.instance.SetBrushAngle(0f);
				SD_WorkflowOptionsRibbon_UI.instance.SetBrushRoundness(1f);
			}
			else if (BrushRibbon_UI.instance != null)
			{
				BrushRibbon_UI.instance.SetBrushSize(DefaultBrushSize01);
				BrushRibbon_UI.instance.SetBrushSpacing(0f);
				BrushRibbon_UI.instance.SetBrushAngle(0f);
				BrushRibbon_UI.instance.SetBrushRoundness(1f);
			}
		}

		/// <summary> Create a simple round brush stamp when no built-in sprites are assigned (e.g. runtime-created manager). </summary>
		static Texture2D CreateProceduralRoundBrush(int size, float softEdge)
		{
			var tex = new Texture2D(size, size, GraphicsFormat.R8_UNorm, 1, TextureCreationFlags.None);
			tex.filterMode = FilterMode.Bilinear;
			tex.wrapMode = TextureWrapMode.Clamp;
			float center = (size - 1) * 0.5f;
			float radius = center - 1f;
			byte[] r8 = new byte[size * size];
			for (int y = 0; y < size; y++)
				for (int x = 0; x < size; x++)
				{
					float dx = x - center;
					float dy = y - center;
					float d = Mathf.Sqrt(dx * dx + dy * dy);
					float t = (d - radius * (1f - softEdge)) / (radius * softEdge);
					float v = Mathf.Clamp01(1f - t);
					r8[y * size + x] = (byte)(v * 255f);
				}
			tex.SetPixelData(r8, 0);
			tex.Apply(true);
			return tex;
		}

		/// <summary> Load PNG, TGA, and ABR from BrushAlphas folder. One "Custom" group for all PNG/TGA; one group per ABR file (same path as LoadSingleAbrFromPath). </summary>
		void LoadCustomAlphasFromFolder()
		{
			string folder = BrushAlphasFolderPath;
			if (!Directory.Exists(folder)) return;

			var files = new List<string>();
			try
			{
				files.AddRange(Directory.GetFiles(folder, "*.png", SearchOption.TopDirectoryOnly));
				files.AddRange(Directory.GetFiles(folder, "*.tga", SearchOption.TopDirectoryOnly));
				files.AddRange(Directory.GetFiles(folder, "*.abr", SearchOption.TopDirectoryOnly));
			}
			catch (Exception e)
			{
				Debug.LogWarning("BrushAlphas: could not scan folder: " + e.Message);
				return;
			}

			int customGroupIndex = -1; // index of "Custom" group, created on first PNG/TGA

			foreach (string path in files)
			{
				try
				{
					string ext = Path.GetExtension(path).ToLowerInvariant();
					if (ext == ".abr")
					{
						// One group per ABR file; use same path as dialog load so structure stays consistent
						LoadSingleAbrFromPath(path);
						continue;
					}

					// PNG or TGA: one "Custom" group for all
					if (customGroupIndex < 0)
					{
						_uiGroupDisplayNames.Add("Custom");
						customGroupIndex = _uiGroupDisplayNames.Count - 1;
					}
					byte[] imgBytes = File.ReadAllBytes(path);
					var tex = new Texture2D(2, 2);
					if (!tex.LoadImage(imgBytes))
					{
						DestroyImmediate(tex);
						continue;
					}
					Texture2D stamp = ToBrushStampTexture(tex);
					DestroyImmediate(tex);
					if (stamp == null) continue;

					string name = Path.GetFileNameWithoutExtension(path);
					_allEntries.Add(new BrushAlphaEntry
					{
						name = name,
						texture = stamp,
						preview = MakeGrayscalePreview(stamp),
						isBuiltIn = false,
						sourceFilePath = path,
						uiGroupIndex = customGroupIndex
					});
				}
				catch (Exception e)
				{
					Debug.LogWarning("BrushAlphas: failed to load " + path + ": " + e.Message);
				}
			}
		}

		/// <summary> Load a single ABR file and append to entries with one new UI group. Same path used by LoadCustomAlphasFromFolder (auto-load) and LoadFromExternalPath (dialog). One folder per ABR. </summary>
		internal void LoadSingleAbrFromPath(string abrFilePath)
		{
			if (string.IsNullOrEmpty(abrFilePath) || !File.Exists(abrFilePath)) return;
			if (Path.GetExtension(abrFilePath).ToLowerInvariant() != ".abr") return;
			try
			{
				string baseName = Path.GetFileNameWithoutExtension(abrFilePath);
				if (string.IsNullOrEmpty(baseName)) baseName = "ABR Brushes";
				_uiGroupDisplayNames.Add(baseName);
				int abrGroupIndex = _uiGroupDisplayNames.Count - 1;
				byte[] bytes = File.ReadAllBytes(abrFilePath);
				LoadAbrFile(bytes, baseName, abrFilePath, abrGroupIndex);
			}
			catch (Exception e)
			{
				Debug.LogWarning("BrushAlphas: failed to load ABR " + abrFilePath + ": " + e.Message);
			}
		}

		#region ABR Parsing

		/// <summary> Parse an Adobe ABR brush file and add extracted brush tips to _allEntries. All brushes from this file get the same uiGroupIndex and sourcePath (one folder per ABR). </summary>
		void LoadAbrFile(byte[] data, string baseName, string sourcePath, int uiGroupIndex)
		{
			if (data == null || data.Length < 4) return;
			// Ensure all brushes from this file share the same path for grouping; fallback so sourceFilePath is never null
			string pathForGrouping = !string.IsNullOrEmpty(sourcePath) ? sourcePath.Trim() : (baseName + ".abr");

			int version = ReadInt16BE(data, 0);

			if (version == 1 || version == 2)
			{
				LoadAbr_V1V2(data, baseName, version, pathForGrouping, uiGroupIndex);
			}
			else if (version >= 6 && version <= 12)
			{
				LoadAbr_V6Plus(data, baseName, pathForGrouping, uiGroupIndex);
			}
			else
			{
				Debug.LogWarning($"BrushAlphas: ABR version {version} not supported for '{baseName}'. " +
					"Try exporting as PNG or using Photoshop ABR v1/v2/v6.");
			}
		}

		void LoadAbr_V1V2(byte[] data, string baseName, int version, string sourcePath, int uiGroupIndex)
		{
			if (data.Length < 4) return;
			int count = ReadInt16BE(data, 2);
			if (count <= 0 || count > 500) return;

			int pos = 4;
			int added = 0;

			for (int i = 0; i < count && pos + 6 < data.Length; i++)
			{
				int brushType = ReadInt16BE(data, pos); pos += 2;
				int brushSize = ReadInt32BE(data, pos); pos += 4;
				int brushEnd = pos + brushSize;

				if (brushSize <= 0 || brushEnd > data.Length) break;

				if (brushType == 1)
				{
					pos = brushEnd;
					continue;
				}

				if (brushType == 2 && brushSize > 28)
				{
					try
					{
						pos += 4; // misc
						int spacingPercent = ReadInt16BE(data, pos); pos += 2; // ABR 1–1000%; 0 = continuous
						if (spacingPercent < 0 || spacingPercent > 1000) spacingPercent = 0;

						string brushName = null;
						if (version == 2 && pos + 4 <= brushEnd)
						{
							int nameLen = ReadInt32BE(data, pos); pos += 4;
							if (nameLen > 0 && nameLen < 1000 && pos + nameLen * 2 <= brushEnd)
							{
								brushName = DecodeUtf16BE(data, pos, nameLen);
								pos += nameLen * 2;
							}
						}

						if (pos < brushEnd) pos += 1; // antialiased flag

						Texture2D stamp = ReadBrushImageData(data, ref pos, brushEnd);
						if (stamp != null)
						{
							added++;
							string name = !string.IsNullOrEmpty(brushName) ? brushName : (count > 1 ? baseName + " " + added : baseName);
							_allEntries.Add(new BrushAlphaEntry
							{
								name = name, texture = stamp,
								preview = MakeGrayscalePreview(stamp), isBuiltIn = false,
								suggestedSize01 = AbrTipSizeToSuggested01(stamp.width, stamp.height),
								spacingPercent = spacingPercent,
								sourceFilePath = sourcePath,
								uiGroupIndex = uiGroupIndex
							});
						}
					}
					catch
					{
						/* skip malformed brush */
					}
					pos = brushEnd;
				}
				else
				{
					pos = brushEnd;
				}
			}

			if (added == 0)
				Debug.LogWarning($"BrushAlphas: ABR v{version} '{baseName}' – parsed {count} brush headers but extracted 0 images.");
		}

		struct AbrDescSettings
		{
			public int spacingPercent;
			public float suggestedHardness01;
			public float suggestedAngleDeg;
			public float suggestedRoundness01;
		}

		void LoadAbr_V6Plus(byte[] data, string baseName, string sourcePath, int uiGroupIndex)
		{
			if (data.Length < 8) return;
			int subversion = ReadInt16BE(data, 2); // Eric Lamarque: subversion 1 = skip 10 after 37, else skip 264
			int pos = 4;
			int added = 0;
			AbrDescSettings descDefaults = default;

			while (pos + 12 <= data.Length)
			{
				if (data[pos] != 0x38 || data[pos + 1] != 0x42 ||
					data[pos + 2] != 0x49 || data[pos + 3] != 0x4D) // "8BIM"
				{
					pos++;
					continue;
				}
				pos += 4;
				string blockType = Encoding.ASCII.GetString(data, pos, 4); pos += 4;
				int blockSize = ReadInt32BE(data, pos); pos += 4;
				if (blockSize <= 0 || pos + blockSize > data.Length) break;

				int blockEnd = pos + blockSize;

				if (blockType == "desc")
					descDefaults = TryParseDescBlock(data, pos, blockEnd);
				else if (blockType == "samp")
					added += ParseSampBlock_EricLamarque(data, pos, blockEnd, subversion, baseName, descDefaults, sourcePath, uiGroupIndex);

				pos = blockEnd;
				if (pos % 2 != 0) pos++;
			}

			if (added == 0)
				Debug.LogWarning($"BrushAlphas: ABR v6+ '{baseName}' – no brushes decoded (Eric Lamarque layout only). Try PNG export.");
			else
				Debug.Log($"BrushAlphas: loaded {added} brush(es) from '{baseName}.abr'.");
		}

		/// <summary> Parse v6 samp block exactly like Eric Lamarque abr.c: [brush_size(4)][chunk(padded to 4)][...]. Chunk: skip 37, subver 1 skip 10 else 264, 19-byte header, packed 8-bit or RLE (no row padding). 8-bit only to avoid crosshairs. </summary>
		int ParseSampBlock_EricLamarque(byte[] data, int start, int end, int subversion, string baseName, AbrDescSettings desc, string sourcePath, int uiGroupIndex)
		{
			int pos = start;
			int added = 0;
			int skipAfter37 = (subversion == 1) ? 10 : 264;

			while (pos + 4 <= end)
			{
				int brush_size = ReadInt32BE(data, pos);
				if (brush_size <= 0)
				{
					pos += 4;
					continue;
				}
				int brush_end = (brush_size + 3) & ~3;
				int chunkStart = pos + 4;
				int chunkEnd = Mathf.Min(chunkStart + brush_size, end);
				if (chunkStart + brush_end > end) break;

				if (brush_size >= 37 + skipAfter37 + 19)
				{
					int headerStart = chunkStart + 37 + skipAfter37;
					if (headerStart + 19 <= chunkEnd)
					{
						int top = ReadInt32BE(data, headerStart), left = ReadInt32BE(data, headerStart + 4);
						int bottom = ReadInt32BE(data, headerStart + 8), right = ReadInt32BE(data, headerStart + 12);
						int depth = ReadInt16BE(data, headerStart + 16), comp = data[headerStart + 18];
						int w = right - left, h = bottom - top;

						if (w >= 1 && h >= 1 && w <= 4096 && h <= 4096 && depth == 8 && (comp == 0 || comp == 1))
						{
							int dataStart = headerStart + 19;
							Texture2D stamp = null;
							if (comp == 0)
							{
								bool packedFits = dataStart + w * h <= chunkEnd;
								int strideBytes = RowStride8Bit(w) * h;
								bool stridedFits = dataStart + strideBytes <= chunkEnd;
								byte[] pixels = null;
								if (packedFits && stridedFits)
								{
									byte[] packed = DecodeUncompressed(data, dataStart, w, h, 8, use8BitStride: false);
									byte[] strided = DecodeUncompressed(data, dataStart, w, h, 8, use8BitStride: true);
									float sp = packed != null ? ScoreDecodedBrush(packed, w, h) : 0f;
									float ss = strided != null ? ScoreDecodedBrush(strided, w, h) : 0f;
									pixels = ss > sp ? strided : packed;
									LastDecodePath = "Eric Lamarque v6 (packed vs strided by score)";
								}
								else if (stridedFits)
								{
									pixels = DecodeUncompressed(data, dataStart, w, h, 8, use8BitStride: true);
									LastDecodePath = "Eric Lamarque v6 (strided 8-bit)";
								}
								else if (packedFits)
								{
									pixels = DecodeUncompressed(data, dataStart, w, h, 8, use8BitStride: false);
									LastDecodePath = "Eric Lamarque v6 (packed 8-bit)";
								}
								if (pixels != null)
									stamp = CreateStampFromGrayscaleBytes(pixels, w, h, flipY: true, invertGrayscale: InvertAbrGrayscale);
							}
							else if (comp == 1)
							{
								byte[] pixels = DecodeRLE(data, dataStart, chunkEnd, w, h, 8);
								if (pixels != null)
								{
									stamp = CreateStampFromGrayscaleBytes(pixels, w, h, flipY: true, invertGrayscale: InvertAbrGrayscale);
									LastDecodePath = "Eric Lamarque v6 (RLE no padding)";
								}
							}
							if (stamp != null)
							{
								added++;
								_allEntries.Add(new BrushAlphaEntry
								{
									name = baseName + " " + added, texture = stamp,
									preview = MakeGrayscalePreview(stamp), isBuiltIn = false,
									suggestedSize01 = AbrTipSizeToSuggested01(stamp.width, stamp.height),
									spacingPercent = desc.spacingPercent,
									suggestedHardness01 = desc.suggestedHardness01,
									suggestedAngleDeg = desc.suggestedAngleDeg,
									suggestedRoundness01 = desc.suggestedRoundness01,
									sourceFilePath = sourcePath,
									uiGroupIndex = uiGroupIndex
								});
							}
						}
					}
				}
				pos += 4 + brush_end;
			}
			return added;
		}

		/// <summary> Best-effort parse of ABR v6+ descriptor block for spacing, hardness, angle, roundness. </summary>
		AbrDescSettings TryParseDescBlock(byte[] data, int start, int end)
		{
			var s = new AbrDescSettings();
			if (end - start < 16) return s;
			// Scan for OSType key (4 bytes) + type "doub" (4 bytes) + 8-byte double. Common keys: spacing, hardness, angle, roundness.
			for (int i = start; i + 16 <= end; i++)
			{
				string key = Encoding.ASCII.GetString(data, i, 4);
				string typ = i + 4 + 4 <= end ? Encoding.ASCII.GetString(data, i + 4, 4) : "";
				if (typ != "doub" && typ != "Doub") continue;
				if (i + 16 > end) break;
				double val = ReadDoubleBE(data, i + 8);
				if (key == "spac" || key == "Spac") { s.spacingPercent = (int)Mathf.Clamp((float)val, 0f, 1000f); }
				else if (key == "hard" || key == "Hard") { s.suggestedHardness01 = (float)Mathf.Clamp01((float)val); }
				else if (key == "angl" || key == "Angl") { s.suggestedAngleDeg = (float)val; }
				else if (key == "rnd " || key == "Rnd ") { s.suggestedRoundness01 = (float)Mathf.Clamp01((float)val); }
			}
			return s;
		}

		static double ReadDoubleBE(byte[] d, int o)
		{
			if (o + 8 > d.Length) return 0;
			byte[] le = new byte[8];
			for (int i = 0; i < 8; i++) le[i] = d[o + 7 - i];
			return BitConverter.ToDouble(le, 0);
		}

		// Old scan/score/length-prefixed extractors removed; v6 uses ParseSampBlock_EricLamarque only (Eric Lamarque abr.c layout).
		internal static string LastDecodePath { get; private set; } // Set to "Eric Lamarque" when using new path; kept for Editor analyzer.

		/// <summary> Same as TryExtractBrushFromSampleWithConsumed but returns which decode path was used (for ABR analyzer / diagnostics). </summary>
		/// <summary> Deprecated: v6 now uses ParseSampBlock_EricLamarque only. Kept for Editor analyzer; returns (null,0,null). </summary>
		public static (Texture2D stamp, int bytesConsumed, string decodePath) TryExtractBrushFromSampleWithConsumedWithPath(byte[] data, int start, int end)
		{
			LastDecodePath = "Eric Lamarque (v6 length-prefixed only)";
			return (null, 0, null);
		}

		/// <summary> Deprecated: v6 uses ParseSampBlock_EricLamarque only. Kept for Editor analyzer; returns (null, 0, null). </summary>
		public static (Texture2D stamp, int bytesConsumed, string decodePath) TryExtractBrushFromLengthPrefixedChunkWithPath(byte[] data, int chunkStart, int chunkEnd)
		{
			return (null, 0, null);
		}

		/// <summary> Read brush image data (bounds + depth + compression + pixels) at current position.
		/// Supports uncompressed (comp 0) and RLE/PackBits (comp 1). </summary>
		Texture2D ReadBrushImageData(byte[] data, ref int pos, int limit)
		{
			if (pos + 19 > limit) return null;

			int top    = ReadInt32BE(data, pos); pos += 4;
			int left   = ReadInt32BE(data, pos); pos += 4;
			int bottom = ReadInt32BE(data, pos); pos += 4;
			int right  = ReadInt32BE(data, pos); pos += 4;
			int depth  = ReadInt16BE(data, pos); pos += 2;
			int comp   = data[pos]; pos += 1;

			int w = right - left;
			int h = bottom - top;

			if (w < 1 || h < 1 || w > 4096 || h > 4096) return null;
			if (depth != 8 && depth != 1) return null;
			if (comp != 0 && comp != 1) return null;

			byte[] pixels = null;
			if (comp == 0)
			{
				// v1/v2: packed rows (no row padding in old format)
				int pixelBytes = depth == 8 ? (w * h) : (RowStride1Bit(w) * h);
				if (pos + pixelBytes > limit) return null;
				pixels = DecodeUncompressed(data, pos, w, h, depth, use8BitStride: false);
				pos += pixelBytes;
			}
			else
			{
				int beforePos = pos;
				pixels = DecodeRLE(data, pos, limit, w, h, depth);
				if (pixels == null) return null;
				// Advance past the RLE data: row byte-counts (2B each) + compressed rows (no padding; Eric Lamarque)
				pos = SkipRLEBlock(data, beforePos, limit, h, depth, w);
			}

			if (pixels == null) return null;
			return CreateStampFromGrayscaleBytes(pixels, w, h, flipY: true, invertGrayscale: InvertAbrGrayscale);
		}

		/// <summary> Map ABR tip dimensions to app brush size 0–1. 256px tip ≈ 1; smaller tips get smaller suggested size. </summary>
		static float AbrTipSizeToSuggested01(int w, int h)
		{
			int maxDim = Math.Max(w, h);
			return Mathf.Clamp01(maxDim / 256f);
		}

		/// <summary> 1-bit bitmaps in ABR use DWORD (4-byte) row alignment like BMP. </summary>
		static int RowStride1Bit(int w)
		{
			int rowBytes = (w + 7) / 8;
			return (rowBytes + 3) & ~3;
		}

		/// <summary> 8-bit brush rows may be 4-byte aligned in ABR (reduces vertical line artifacts). </summary>
		static int RowStride8Bit(int w) => (w + 3) & ~3;

		/// <summary> Score decoded brush: higher = more natural (varying). Wrong row stride often causes vertical stripes (constant columns) = low score. Used to pick packed vs strided when both fit. </summary>
		static float ScoreDecodedBrush(byte[] pixels, int w, int h)
		{
			if (pixels == null || w < 1 || h < 1 || pixels.Length < w * h) return 0f;
			float score = 0f;
			for (int x = 0; x < w; x++)
			{
				byte min = 255, max = 0;
				for (int y = 0; y < h; y++)
				{
					byte v = pixels[y * w + x];
					if (v < min) min = v;
					if (v > max) max = v;
				}
				score += (max - min); // column range; stripes = low range
			}
			return score;
		}

		/// <summary> [Unused after rebuild] Kept for Editor compatibility. v6 uses packed/strided by score when both fit. </summary>
		public static bool AbrPreferStridedWhenBothFit = false;

		/// <summary> Decode uncompressed brush pixels. 8-bit: packed or row-aligned per use8BitStride. 1-bit: always DWORD row stride. </summary>
		/// <param name="use8BitStride">True for v6 (4-byte row alignment); false for v1/v2 (packed rows).</param>
		static byte[] DecodeUncompressed(byte[] data, int dataStart, int w, int h, int depth, bool use8BitStride = true)
		{
			if (data == null || dataStart < 0 || w < 1 || h < 1 || w > 4096 || h > 4096) return null;
			int requiredBytes;
			if (depth == 8)
				requiredBytes = use8BitStride ? (RowStride8Bit(w) * h) : (w * h);
			else
				requiredBytes = RowStride1Bit(w) * h;
			if (dataStart + requiredBytes > data.Length) return null;

			byte[] pixels = new byte[w * h];
			if (depth == 8)
			{
				if (use8BitStride)
				{
					int rowStride = RowStride8Bit(w);
					for (int y = 0; y < h; y++)
						for (int x = 0; x < w; x++)
							pixels[y * w + x] = data[dataStart + y * rowStride + x];
				}
				else
					Array.Copy(data, dataStart, pixels, 0, w * h);
			}
			else
			{
				int rowStride = RowStride1Bit(w);
				int pi = 0;
				for (int y = 0; y < h && pi < pixels.Length; y++)
				{
					int rowByte = 0;
					for (int x = 0; x < w && pi < pixels.Length; x++)
					{
						int byteIdx = dataStart + y * rowStride + rowByte;
						int bitIdx = 7 - (x % 8);
						pixels[pi++] = ((data[byteIdx] >> bitIdx) & 1) == 1 ? (byte)255 : (byte)0;
						if (x % 8 == 7) rowByte++;
					}
				}
			}
			return pixels;
		}

		/// <summary> Decode RLE (PackBits) compressed brush data.
		/// Layout: h × 2-byte row lengths (BE), then compressed scanlines. </summary>
		static byte[] DecodeRLE(byte[] data, int pos, int limit, int w, int h, int depth)
		{
			int rowWidth = depth == 8 ? w : ((w + 7) / 8);
			if (pos + h * 2 > limit) return null;

			int[] rowLens = new int[h];
			int p = pos;
			for (int y = 0; y < h; y++)
			{
				rowLens[y] = ReadInt16BE(data, p); p += 2;
			}

			byte[] raw = new byte[rowWidth * h];
			for (int y = 0; y < h; y++)
			{
				int rowLen = rowLens[y];
				if (rowLen < 0 || (limit - p) < rowLen) return null;
				int rowEnd = p + rowLen;
				int outOff = y * rowWidth;
				int outEnd = outOff + rowWidth;

				while (p < rowEnd && outOff < outEnd)
				{
					int n = (sbyte)data[p]; p++;
					if (n >= 0)
					{
						int count = n + 1;
						if (p + count > rowEnd) count = rowEnd - p;
						for (int j = 0; j < count && outOff < outEnd; j++)
							raw[outOff++] = data[p++];
					}
					else if (n != -128)
					{
						int count = -n + 1;
						byte val = (p < rowEnd) ? data[p] : (byte)0; p++;
						for (int j = 0; j < count && outOff < outEnd; j++)
							raw[outOff++] = val;
					}
				}
				// Eric Lamarque abr.c: no row padding between RLE rows
				p = rowEnd;
			}

			if (depth == 8)
				return raw;

			byte[] pixels = new byte[w * h];
			int pi = 0;
			for (int y = 0; y < h && pi < pixels.Length; y++)
				for (int x = 0; x < w && pi < pixels.Length; x++)
				{
					int byteIdx = y * rowWidth + (x / 8);
					int bitIdx = 7 - (x % 8);
					pixels[pi++] = ((raw[byteIdx] >> bitIdx) & 1) == 1 ? (byte)255 : (byte)0;
				}
			return pixels;
		}

		static int SkipRLEBlock(byte[] data, int pos, int limit, int h, int depth = 8, int w = 0)
		{
			if (pos + h * 2 > limit) return limit;
			int p = pos;
			int[] rowLens = new int[h];
			for (int y = 0; y < h; y++)
			{
				rowLens[y] = ReadInt16BE(data, p); p += 2;
			}
			for (int y = 0; y < h; y++)
				p += rowLens[y];
			return Mathf.Min(p, limit);
		}

		/// <summary> Set true to write decoded ABR stamp to PNG once (for comparison with Photoshop). Default false after ABR rebuild. </summary>
		public static bool DebugExportAbrStampToPng = false;

		/// <summary> If true, brush stamps are created as RGBA32 (R=G=B=gray, A=255). Default true to avoid R8 pipeline/crosshair artifacts on some GPUs. Set false to use R8. Shader samples .r so result is the same. </summary>
		public static bool UseRgba32ForBrushStamp = true;

		/// <summary> If true, brush stamp uses Point filter (no bilinear). Can reduce crosshair/line artifacts from sampling. Set false for smoother brush edges. </summary>
		public static bool UsePointFilterForBrushStamp = true;

		/// <summary> When true: invert decoded grayscale so file black→opaque (Photoshop convention). When false: file white=opaque, black=transparent (many ABR exports). Default false so brush shape is opaque and background transparent; set true if you see a white brush on black or the opposite. </summary>
		public static bool InvertAbrGrayscale = false;

		/// <summary> Create brush stamp from grayscale pixels (R8 or RGBA32). flipY: true for ABR. invertGrayscale: when true, file black→255 (opaque); when false, file white→255 (opaque). Shader uses high .r = more paint. </summary>
		static Texture2D CreateStampFromGrayscaleBytes(byte[] grayscale, int w, int h, bool flipY = false, bool invertGrayscale = false)
		{
			byte[] src = grayscale;
			if (flipY && h > 1)
			{
				src = new byte[grayscale.Length];
				for (int y = 0; y < h; y++)
				{
					int srcRow = (h - 1 - y) * w;
					int dstRow = y * w;
					for (int x = 0; x < w; x++)
						src[dstRow + x] = grayscale[srcRow + x];
				}
			}
			if (invertGrayscale)
			{
				byte[] inverted = new byte[src.Length];
				for (int i = 0; i < src.Length; i++)
					inverted[i] = (byte)(255 - src[i]);
				src = inverted;
			}
			Texture2D stamp;
			if (UseRgba32ForBrushStamp)
			{
				stamp = new Texture2D(w, h, TextureFormat.RGBA32, false);
				var colors = new Color32[w * h];
				for (int i = 0; i < src.Length && i < colors.Length; i++)
				{
					byte g = src[i];
					colors[i] = new Color32(g, g, g, 255);
				}
				stamp.SetPixels32(colors);
			}
			else
			{
				stamp = new Texture2D(w, h, GraphicsFormat.R8_UNorm, 1, TextureCreationFlags.None);
				stamp.SetPixelData(src, 0);
			}
			stamp.filterMode = UsePointFilterForBrushStamp ? FilterMode.Point : FilterMode.Bilinear;
			stamp.wrapMode = TextureWrapMode.Clamp;
			stamp.Apply(true);

			if (flipY && DebugExportAbrStampToPng)
			{
				DebugExportAbrStampToPng = false;
				try
				{
					string dir = Path.Combine(Application.persistentDataPath, "StableProjectorz");
					if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
					string path = Path.Combine(dir, "abr_stamp_debug.png");
					var rgba = new Texture2D(w, h, TextureFormat.RGBA32, false);
					if (UseRgba32ForBrushStamp)
						rgba.SetPixels32(stamp.GetPixels32());
					else
					{
						var raw = stamp.GetRawTextureData<byte>();
						var colors = new Color32[w * h];
						for (int i = 0; i < raw.Length && i < colors.Length; i++)
							colors[i] = new Color32(raw[i], raw[i], raw[i], 255);
						rgba.SetPixels32(colors);
					}
					rgba.Apply(true);
					File.WriteAllBytes(path, rgba.EncodeToPNG());
					DestroyImmediate(rgba);
					Debug.Log("[BrushAlphas] Decoded ABR stamp exported for comparison: " + path);
				}
				catch (Exception e) { Debug.LogWarning("[BrushAlphas] Debug export failed: " + e.Message); }
			}
			return stamp;
		}

		static int ReadInt16BE(byte[] d, int o)
		{
			if (d == null || o < 0 || o + 2 > d.Length) return 0;
			return (d[o] << 8) | d[o + 1];
		}
		static int ReadInt32BE(byte[] d, int o)
		{
			if (d == null || o < 0 || o + 4 > d.Length) return 0;
			return (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];
		}

		/// <summary> Decode UTF-16 BE bytes (ABR v2 brush name). </summary>
		static string DecodeUtf16BE(byte[] data, int start, int charCount)
		{
			if (charCount <= 0 || start + charCount * 2 > data.Length) return null;
			try
			{
				return Encoding.BigEndianUnicode.GetString(data, start, charCount * 2);
			}
			catch { return null; }
		}

		#endregion

		/// <summary> Convert loaded texture to brush stamp (R8 or RGBA32 per UseRgba32ForBrushStamp). Uses alpha as shape for PNG. </summary>
		static Texture2D ToBrushStampTexture(Texture2D source)
		{
			if (source == null || source.width < 1 || source.height < 1) return null;
			int w = source.width;
			int h = source.height;
			Color32[] pixels = source.GetPixels32();
			bool allOpaque = true;
			for (int i = 0; i < pixels.Length; i++)
				if (pixels[i].a < 255) { allOpaque = false; break; }

			byte[] gray = new byte[pixels.Length];
			for (int i = 0; i < pixels.Length; i++)
			{
				Color32 c = pixels[i];
				gray[i] = allOpaque ? (byte)((c.r + c.g + c.b) / 3) : c.a;
			}
			return CreateStampFromGrayscaleBytes(gray, w, h, flipY: false);
		}

		/// <summary> Create an RGBA grayscale preview from a brush stamp (R8 or RGBA32). </summary>
		static Texture2D MakeGrayscalePreview(Texture2D r8Stamp)
		{
			if (r8Stamp == null) return null;
			try
			{
				int w = Mathf.Min(r8Stamp.width, 128);
				int h = Mathf.Min(r8Stamp.height, 128);
				var preview = new Texture2D(w, h, TextureFormat.RGBA32, false);
				preview.filterMode = FilterMode.Bilinear;
				preview.wrapMode = TextureWrapMode.Clamp;
				var srcData = r8Stamp.GetPixelData<byte>(0);
				int srcW = r8Stamp.width;
				int srcH = r8Stamp.height;
				bool isRgba32 = srcData.Length >= srcW * srcH * 4;
				int bytesPerPixel = isRgba32 ? 4 : 1;
				var dstPixels = new Color32[w * h];
				for (int y = 0; y < h; y++)
				{
					int srcY = (srcH > h) ? y * srcH / h : y;
					for (int x = 0; x < w; x++)
					{
						int srcX = (srcW > w) ? x * srcW / w : x;
						int srcIdx = (srcY * srcW + srcX) * bytesPerPixel;
						byte v = (srcIdx < srcData.Length) ? srcData[srcIdx] : (byte)0;
						dstPixels[y * w + x] = new Color32(v, v, v, 255);
					}
				}
				preview.SetPixels32(dstPixels);
				preview.Apply(true);
				return preview;
			}
			catch
			{
				var fallback = new Texture2D(32, 32, TextureFormat.RGBA32, false);
				Color32 gray = new Color32(128, 128, 128, 255);
				var px = new Color32[32 * 32];
				for (int i = 0; i < px.Length; i++) px[i] = gray;
				fallback.SetPixels32(px);
				fallback.Apply(true);
				return fallback;
			}
		}

		bool IsSpriteTexture(Texture2D tex)
		{
			if (tex == null || _builtInBrushShapes == null) return false;
			foreach (var s in _builtInBrushShapes)
				if (s != null && s.texture == tex) return true;
			return false;
		}

		void DestroyCustomTextures()
		{
			foreach (var e in _allEntries)
			{
				if (!e.isBuiltIn)
				{
					if (e.texture != null) DestroyImmediate(e.texture);
					if (e.preview != null && e.preview != e.texture) DestroyImmediate(e.preview);
				}
			}
		}

		/// <summary> Remove a custom brush preset at index (index 0–2 are built-in and cannot be removed). If deleteFilePermanently is true and the entry has a source path, deletes the file from disk and removes all presets from that file (e.g. all brushes from one ABR). Returns true if any were removed. </summary>
		public bool RemoveCustomBrushAt(int index, bool deleteFilePermanently = false)
		{
			if (index < 3 || index >= _allEntries.Count) return false;
			var entry = _allEntries[index];
			string pathToDelete = deleteFilePermanently ? entry.sourceFilePath : null;
			if (deleteFilePermanently && !string.IsNullOrEmpty(pathToDelete) && File.Exists(pathToDelete))
			{
				try { File.Delete(pathToDelete); }
				catch (Exception e) { Debug.LogWarning("BrushAlphas: could not delete file " + pathToDelete + ": " + e.Message); }
			}
			if (!string.IsNullOrEmpty(pathToDelete))
			{
				for (int i = _allEntries.Count - 1; i >= 0; i--)
				{
					if (_allEntries[i].sourceFilePath != pathToDelete) continue;
					var e = _allEntries[i];
					if (e.texture != null && !e.isBuiltIn) DestroyImmediate(e.texture);
					if (e.preview != null && e.preview != e.texture && !e.isBuiltIn) DestroyImmediate(e.preview);
					_allEntries.RemoveAt(i);
					if (i <= _currentIndex) _currentIndex = Mathf.Max(0, _currentIndex - 1);
				}
			}
			else
			{
				if (entry.texture != null && !entry.isBuiltIn) DestroyImmediate(entry.texture);
				if (entry.preview != null && entry.preview != entry.texture && !entry.isBuiltIn) DestroyImmediate(entry.preview);
				_allEntries.RemoveAt(index);
				_currentIndex = Mathf.Clamp(_currentIndex, 0, Mathf.Max(0, _allEntries.Count - 1));
			}
			_currentIndex = Mathf.Clamp(_currentIndex, 0, Mathf.Max(0, _allEntries.Count - 1));
			return true;
		}

		/// <summary> Call after dropping new PNGs in the BrushAlphas folder. </summary>
		public void RefreshCustomAlphas()
		{
			int builtInCount = 0;
			foreach (var e in _allEntries)
				if (e.isBuiltIn) builtInCount++;
			bool wasBuiltIn = _currentIndex < builtInCount;
			RebuildEntries();
			if (wasBuiltIn)
				_currentIndex = Mathf.Clamp(_currentIndex, 0, 2);
		}

		/// <summary> Load a brush file (ABR, PNG, TGA) from an arbitrary path: copy to BrushAlphas folder then refresh. Folder scan auto-loads ABR via same path (LoadSingleAbrFromPath) so structure stays consistent. Returns true if successful. </summary>
		public bool LoadFromExternalPath(string sourcePath)
		{
			if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath)) return false;
			string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
			if (ext != ".abr" && ext != ".png" && ext != ".tga") return false;
			EnsureBrushAlphasFolderExists();
			string folder = BrushAlphasFolderPath;
			string fileName = Path.GetFileName(sourcePath);
			string destPath = Path.Combine(folder, fileName);
			int n = 0;
			while (File.Exists(destPath))
			{
				n++;
				string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
				destPath = Path.Combine(folder, nameNoExt + "_" + n + ext);
			}
			try
			{
				File.Copy(sourcePath, destPath);
				// Full refresh so LoadCustomAlphasFromFolder picks up the new file (PNG/TGA/ABR all use same load path)
				RefreshCustomAlphas();
				return true;
			}
			catch (Exception e)
			{
				Debug.LogWarning("BrushAlphas: failed to copy " + sourcePath + " to " + destPath + ": " + e.Message);
				return false;
			}
		}
	}
}

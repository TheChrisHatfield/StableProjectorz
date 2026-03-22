using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace spz {

	// =============================================================================
	// LAYER SYSTEM - MANAGER (stack of PaintLayer; single source of truth)
	// =============================================================================
	// Holds the ordered list of PaintLayer (index 0 = bottom). PaintTab_LayersPanel_UI
	// holds a reference to this and calls AddLayer(), SetActiveLayer(), SetLayerVisible(),
	// SetLayerName(), RemoveLayer(). Inpaint_MaskPainter subscribes to OnLayerAdded to inject scene into
	// new layers; uses ActiveLayerRenderUdims as the paint target; display blits each
	// visible layer's Content in order (see ApplyColorLayer_To_UV_Textures).
	// =============================================================================

	/// <summary>
	/// Layer stack: each layer is a container (scene + strokes). Display uses composite of all visible layers (never "active layer only" — that would override and hide others).
	/// Only the active layer receives new paint. When a new layer is added, we inject the composite of (scene + all layers below) into that layer so its data is in the layer instead of an empty override.
	/// Ordering: index 0 = bottom, last index = top.
	/// </summary>
	public class PaintLayerStack_MGR : MonoBehaviour
	{
		public static PaintLayerStack_MGR instance { get; private set; }

		[SerializeField] Shader _compositeBlendShader;
		Material _compositeBlendMat;

		readonly List<PaintLayer> _layers = new List<PaintLayer>();
		int _activeIndex;
		Vector2Int _resolution;
		int _udimsCount;
		/// <summary>Next cardinal number for default layer names (Layer 1, Layer 2, ...). Increments on each new layer; persists across deletes and save/load. </summary>
		int _nextLayerNumber = 1;
		/// <summary>Next default name for Collapse button merges: Collapse 1, Collapse 2, ... Persists across save/load.</summary>
		int _nextCollapseNumber = 1;
		RenderUdims _compositeTempA;
		RenderUdims _compositeTempB;
		/// <summary>Used only during in-place collapse: composite into this then copy to layer0 so we never use a layer as CompositeTo dest (avoids read-write hazard).</summary>
		RenderUdims _collapseResultTemp;

		/// <summary>Layers from bottom (index 0) to top. Do not modify list directly; use AddLayer/RemoveLayer/MoveLayer. </summary>
		public IReadOnlyList<PaintLayer> Layers => _layers;
		public int ActiveLayerIndex => _activeIndex;
		public PaintLayer ActiveLayer => _activeIndex >= 0 && _activeIndex < _layers.Count ? _layers[_activeIndex] : null;
		/// <summary>RenderUdims to paint into (active layer's Content — the display buffer). Compute shader
		/// writes strokes here directly; GPU command serialization ensures no timing gap. </summary>
		public RenderUdims ActiveLayerRenderUdims => ActiveLayer?.Content;
		/// <summary>Active layer's secondary data buffer. Not used during live painting; exists for save/load. </summary>
		public RenderUdims ActiveLayerDataRenderUdims => ActiveLayer?.Data;

		/// <summary>False if the layer compositor shader did not load; <see cref="CompositeTo"/> cannot run.</summary>
		public bool CanCompositeLayers => _compositeBlendMat != null;

		public event Action OnLayersChanged;
		public event Action OnActiveLayerChanged;
		/// <summary>Invoked when a new layer is added (the new layer is already active). Inpaint_MaskPainter subscribes to inject scene into it (OnLayerAdded_InjectScene).</summary>
		public static Action<PaintLayer> OnLayerAdded;

		// --- Singleton and lifecycle ---
		void Awake()
		{
			if (instance != null) { Destroy(gameObject); return; }
			instance = this;
			UnityEngine.Debug.Log("[PaintLayerStack] Awake: instance set.");
			if (_compositeBlendShader == null)
				_compositeBlendShader = Shader.Find("Unlit/PaintLayer_CompositeBlend");
			if (_compositeBlendShader != null)
				_compositeBlendMat = new Material(_compositeBlendShader);
			else
				UnityEngine.Debug.LogError("[PaintLayerStack] Awake: _compositeBlendShader is null! Compositor will not work.");
			EnsureAtLeastOneLayer();
			UnityEngine.Debug.Log($"[PaintLayerStack] Awake complete: {_layers.Count} layers, active={_activeIndex}.");
		}

		void OnDestroy()
		{
			if (_compositeBlendMat != null) { UnityEngine.Object.DestroyImmediate(_compositeBlendMat); _compositeBlendMat = null; }
			_compositeTempA?.Dispose();
			_compositeTempB?.Dispose();
			_collapseResultTemp?.Dispose();
			foreach (var l in _layers)
				l.Dispose();
			_layers.Clear();
			if (instance == this)
				instance = null;
		}

		// --- Resolution and layer allocation (called when scene buffer / mask resolution is known) ---
		/// <summary>Call when inpaint resolution is known (e.g. from Inpaint_MaskPainter.maskResolution). Ensures all layers have content. </summary>
		public void EnsureResolution(Vector3Int resolution)
		{
			int w = resolution.x;
			int h = resolution.y;
			int slices = resolution.z;
			if (w <= 0 || h <= 0 || slices <= 0) return;
			var udims = UDIMs_Helper._allKnownUdims;
			if (udims == null || udims.Count != slices) return;
			bool resChanged = _resolution.x != w || _resolution.y != h || _udimsCount != slices;
			if (resChanged)
				UnityEngine.Debug.Log($"[PaintLayerStack] EnsureResolution: {_resolution.x}x{_resolution.y}x{_udimsCount} → {w}x{h}x{slices} (will re-init {_layers.Count} layers).");
			_resolution = new Vector2Int(w, h);
			_udimsCount = slices;
			for (int i = 0; i < _layers.Count; i++)
				_layers[i].EnsureContent(udims, _resolution, GenData_Masks.colorBrushFormat, GenData_Masks.colorBrushFilter);
			EnsureAtLeastOneLayer();
		}

		void EnsureAtLeastOneLayer()
		{
			if (_layers.Count > 0) return;
			AddLayer("Layer 1");
		}

		// --- Layer list operations (Add / Remove / Move / Active / Visibility) ---
		/// <summary>Add a new layer. New layer is set active. Existing layers (including layer 0) are unchanged and remain visible. Scene/data injection runs via OnLayerAdded (e.g. Inpaint_MaskPainter).</summary>
		public PaintLayer AddLayer(string name = null)
		{
			int count = _layers.Count;
			string layerName = name ?? ("Layer " + (count + 1));
			var layer = new PaintLayer(layerName);
			layer.Visible = true;
			if (_resolution.x > 0 && _udimsCount > 0)
			{
				var udims = UDIMs_Helper._allKnownUdims;
				if (udims != null && udims.Count == _udimsCount)
					layer.EnsureContent(udims, _resolution, GenData_Masks.colorBrushFormat, GenData_Masks.colorBrushFilter);
			}
			_layers.Add(layer);
			_activeIndex = _layers.Count - 1;
			UnityEngine.Debug.Log($"[PaintLayerStack] AddLayer '{layerName}': total={_layers.Count}, active={_activeIndex}, hasContent={layer.Content != null}, OnLayerAdded subscribers={OnLayerAdded?.GetInvocationList()?.Length ?? 0}.");
			OnLayersChanged?.Invoke();
			OnActiveLayerChanged?.Invoke();
			OnLayerAdded?.Invoke(layer);
			return layer;
		}

		/// <summary>Ensure a single layer has Content when stack resolution is set (e.g. new layer added before first paint). Does not touch other layers.</summary>
		public void EnsureContentForLayerIfNeeded(PaintLayer layer)
		{
			if (layer == null || layer.Content != null) return;
			if (_resolution.x <= 0 || _udimsCount <= 0) return;
			var udims = UDIMs_Helper._allKnownUdims;
			if (udims == null || udims.Count != _udimsCount) return;
			layer.EnsureContent(udims, _resolution, GenData_Masks.colorBrushFormat, GenData_Masks.colorBrushFilter);
			UnityEngine.Debug.Log($"[PaintLayerStack] EnsureContentForLayerIfNeeded: gave Content to layer '{layer.Name}' ({_resolution.x}x{_resolution.y}x{_udimsCount}).");
		}

		/// <summary>Add a new paint layer filled with the art icon's image(s). Uses icon resolution if stack has none; otherwise scales into current resolution. </summary>
		public bool AddLayerFromArtIcon(IconUI icon)
		{
			if (icon == null || icon._genData == null) return false;
			var genData = icon._genData;
			Dictionary<Texture2D, UDIM_Sector> dict = genData.GetTextures2D_expensive(out bool destroyWhenDone);
			if (dict == null || dict.Count == 0) return false;
			var ordered = dict.OrderBy(kvp => kvp.Value, Comparer<UDIM_Sector>.Create(UDIM_Sector.SortComparer)).ToList();
			List<Texture2D> orderedTexList = ordered.Select(kvp => kvp.Key).ToList();
			bool ok = AddLayerFromTextures(orderedTexList, destroyWhenDone, "From Art");
			return ok;
		}

		/// <summary>Add a new paint layer filled with image texture(s). Order = first UDIM, second UDIM, etc. Single image fills first UDIM. </summary>
		public bool AddLayerFromTextures(IReadOnlyList<Texture2D> orderedTextures, bool destroyWhenDone, string layerName = "From Image")
		{
			if (orderedTextures == null || orderedTextures.Count == 0) return false;
			var orderedTexList = new List<Texture2D>(orderedTextures);
			int w = orderedTexList[0].width;
			int h = orderedTexList[0].height;
			var udims = UDIMs_Helper._allKnownUdims;
			if (udims == null || udims.Count == 0)
			{
				if (destroyWhenDone) foreach (var t in orderedTexList) if (t != null) Texture.DestroyImmediate(t);
				return false;
			}
			if (_resolution.x <= 0 || _udimsCount <= 0)
				EnsureResolution(new Vector3Int(w, h, udims.Count));
			PaintLayer layer = AddLayer(layerName);
			if (layer.Content == null)
			{
				if (destroyWhenDone) foreach (var t in orderedTexList) if (t != null) Texture.DestroyImmediate(t);
				return false;
			}
			int fillCount = Mathf.Min(orderedTexList.Count, layer.Content.UdimsCount);
			var toFill = orderedTexList.GetRange(0, fillCount);
			TextureTools_SPZ.TextureArray_Fill_N_Slices(layer.Content.texArray, toFill, 0);
			layer.SyncDataFromContent(); // keep Data mirror for save/load round-trips
			if (destroyWhenDone)
				foreach (var t in orderedTexList) if (t != null) Texture.DestroyImmediate(t);
			return true;
		}

		/// <summary>Remove the layer at index. Disposes the layer and adjusts active index. You can delete any layer including the last one; Add Layer adds more.</summary>
		public void RemoveLayer(int index)
		{
			if (index < 0 || index >= _layers.Count) return;
			_layers[index].Dispose();
			_layers.RemoveAt(index);
			if (_activeIndex >= _layers.Count)
				_activeIndex = Mathf.Max(0, _layers.Count - 1);
			else if (index < _activeIndex)
				_activeIndex--;
			OnLayersChanged?.Invoke();
			OnActiveLayerChanged?.Invoke();
		}

		public void MoveLayer(int fromIndex, int toIndex)
		{
			if (fromIndex < 0 || fromIndex >= _layers.Count || toIndex < 0 || toIndex >= _layers.Count || fromIndex == toIndex) return;
			var keepActive = ActiveLayer;
			var layer = _layers[fromIndex];
			_layers.RemoveAt(fromIndex);
			_layers.Insert(toIndex, layer);
			if (keepActive != null)
				_activeIndex = _layers.IndexOf(keepActive);
			else
				_activeIndex = Mathf.Clamp(_activeIndex, 0, _layers.Count - 1);
			OnLayersChanged?.Invoke();
			// Reorder does not repaint UV accumulation until ProcessMeshes runs; SD capture and same-frame Generate can read stale order without this.
			if (Objects_Renderer_MGR.instance != null)
				Objects_Renderer_MGR.instance.ReRenderAll_soon();
		}

		public void SetActiveLayer(int index)
		{
			if (index == _activeIndex || index < 0 || index >= _layers.Count) return;
			_activeIndex = index;
			OnActiveLayerChanged?.Invoke();
		}

		/// <summary>Set visibility of a layer (Photoshop-style: hidden layers are not shown in viewport). Fires OnLayersChanged. </summary>
		public void SetLayerVisible(int index, bool visible)
		{
			if (index < 0 || index >= _layers.Count) return;
			if (_layers[index].Visible == visible) return;
			_layers[index].Visible = visible;
			OnLayersChanged?.Invoke();
		}

		const int MaxLayerNameLength = 128;

		/// <summary>Rename the layer at index. Empty or whitespace becomes a default label. Persists via Save like <see cref="PaintLayer.Name"/>. Fires <see cref="OnLayersChanged"/> when the stored name changes.</summary>
		public void SetLayerName(int index, string name)
		{
			if (index < 0 || index >= _layers.Count) return;
			string n = string.IsNullOrWhiteSpace(name) ? DefaultLayerDisplayName(index) : name.Trim();
			if (n.Length > MaxLayerNameLength)
				n = n.Substring(0, MaxLayerNameLength);
			if (_layers[index].Name == n) return;
			_layers[index].Name = n;
			OnLayersChanged?.Invoke();
		}

		/// <summary>Fallback label when the user clears the name field (1-based index for display).</summary>
		public string DefaultLayerDisplayName(int index)
		{
			if (index < 0) index = 0;
			return "Layer " + (index + 1);
		}

		/// <summary>Next default name for merged layers: "Collapse 1", "Collapse 2", … Counter advances and is saved in <see cref="PaintLayerStack_SL.nextCollapseNumber"/>.</summary>
		public string ConsumeNextDefaultCollapseLayerName()
		{
			string name = "Collapse " + _nextCollapseNumber;
			_nextCollapseNumber++;
			return name;
		}

		/// <summary>Read ALL visible layers to CPU, alpha-blend them on CPU, write the merged result into a new layer.
		/// Bypasses all GPU compositing/shader issues. Does NOT remove any existing layer.</summary>
		public bool CollapseVisibleLayersIntoOne()
		{
			// Gather visible layers
			var visibleLayers = new List<PaintLayer>();
			foreach (var l in _layers)
				if (l.Visible && l.Content != null && l.Content.texArray != null)
					visibleLayers.Add(l);

			if (visibleLayers.Count == 0)
			{
				UnityEngine.Debug.Log("[PaintLayerStack] Collapse: no visible layer with content.");
				return false;
			}

			RenderUdims first = visibleLayers[0].Content;
			int w = first.width;
			int h = first.height;
			int slices = first.UdimsCount;

			// Read every visible layer's pixel data to CPU (list of Texture2D per layer, one per UDIM slice)
			var allLayerSlices = new List<List<Texture2D>>();
			var allLayerOpacities = new List<float>();
			foreach (var l in visibleLayers)
			{
				List<Texture2D> layerTextures = TextureTools_SPZ.TextureArray_to_Texture2DList(l.Content.texArray);
				if (layerTextures == null || layerTextures.Count == 0)
				{
					UnityEngine.Debug.LogWarning($"[PaintLayerStack] Collapse: failed to read layer '{l.Name}' to CPU.");
					foreach (var prevList in allLayerSlices)
						foreach (var t in prevList) if (t != null) UnityEngine.Object.DestroyImmediate(t);
					return false;
				}
				allLayerSlices.Add(layerTextures);
				allLayerOpacities.Add(Mathf.Clamp01(l.Opacity));
			}

			// CPU-side alpha blend: for each UDIM slice, blend all layers bottom-to-top
			var resultTextures = new List<Texture2D>();
			for (int s = 0; s < slices; s++)
			{
				Color[] accum = new Color[w * h];
				// Start with all-transparent
				for (int p = 0; p < accum.Length; p++)
					accum[p] = Color.clear;

				for (int li = 0; li < allLayerSlices.Count; li++)
				{
					var layerSlices = allLayerSlices[li];
					if (s >= layerSlices.Count) continue;
					Texture2D sliceTex = layerSlices[s];
					Color[] fg = sliceTex.GetPixels();
					float opacity = allLayerOpacities[li];

					for (int p = 0; p < accum.Length && p < fg.Length; p++)
					{
						float srcA = fg[p].a * opacity;
						float srcR = fg[p].r * opacity;
						float srcG = fg[p].g * opacity;
						float srcB = fg[p].b * opacity;

						accum[p].r = srcR + accum[p].r * (1f - srcA);
						accum[p].g = srcG + accum[p].g * (1f - srcA);
						accum[p].b = srcB + accum[p].b * (1f - srcA);
						accum[p].a = srcA + accum[p].a;
					}
				}

				Texture2D result = new Texture2D(w, h, TextureFormat.RGBA32, false);
				result.SetPixels(accum);
				result.Apply();
				resultTextures.Add(result);
			}

			// Destroy CPU textures from layers (no longer needed)
			foreach (var layerSlices in allLayerSlices)
				foreach (var t in layerSlices) if (t != null) UnityEngine.Object.DestroyImmediate(t);
			allLayerSlices.Clear();

			// Suppress scene injection, add new layer, write result
			var maskPainter = Inpaint_MaskPainter.instance;
			if (maskPainter != null) maskPainter.IsCollapsingLayers = true;

			PaintLayer newLayer = AddLayer(ConsumeNextDefaultCollapseLayerName());
			EnsureContentForLayerIfNeeded(newLayer);

			if (maskPainter != null) maskPainter.IsCollapsingLayers = false;

			if (newLayer.Content != null && newLayer.Content.texArray != null)
			{
				TextureTools_SPZ.TextureArray_Fill_N_Slices(newLayer.Content.texArray, resultTextures, 0);
				newLayer.SyncDataFromContent();
				newLayer.HasReceivedSceneInject = true;
			}
			else
				UnityEngine.Debug.LogWarning("[PaintLayerStack] Collapse: new layer has no Content; paste skipped.");

			// Dispose CPU result textures
			foreach (var t in resultTextures) if (t != null) UnityEngine.Object.DestroyImmediate(t);

			return true;
		}

		/// <summary>Remove all layers and add a single empty layer (e.g. after collapsing into scene buffer). Used by Inpaint_MaskPainter.CollapseLayersIntoScene.</summary>
		public void ReplaceLayersWithOneEmpty()
		{
			for (int i = _layers.Count - 1; i >= 0; i--)
				RemoveLayer(i);
			AddLayer("Layer 1");
			UnityEngine.Debug.Log("[PaintLayerStack] ReplaceLayersWithOneEmpty: one empty layer.");
		}

		/// <summary>Set opacity of a layer (0–1). Fires OnLayersChanged. </summary>
		public void SetLayerOpacity(int index, float opacity)
		{
			if (index < 0 || index >= _layers.Count) return;
			_layers[index].Opacity = Mathf.Clamp01(opacity);
			OnLayersChanged?.Invoke();
		}

		// --- Compositing (used by Inpaint_MaskPainter for new-layer injection and for APIs that need a flat image) ---
		/// <summary>Build composite of base + layers [0..layerIndexExclusive-1] into dest. Use to inject into a new layer so it starts with scene + everything below (no empty override).</summary>
		public void CompositeBelowInto(RenderUdims baseLayer, RenderUdims dest, int layerIndexExclusive)
		{
			if (dest == null || baseLayer == null) return;
			if (_compositeBlendMat == null) { Graphics.CopyTexture(baseLayer.texArray, dest.texArray); return; }
			RenderUdims.assertSameSize(dest, baseLayer);
			RenderUdims a = GetOrCreateCompositeTemp(ref _compositeTempA);
			RenderUdims b = GetOrCreateCompositeTemp(ref _compositeTempB);
			if (a == null || b == null)
			{
				Graphics.CopyTexture(baseLayer.texArray, dest.texArray);
				return;
			}
			Graphics.CopyTexture(baseLayer.texArray, a.texArray);
			for (int i = 0; i < layerIndexExclusive && i < _layers.Count; i++)
			{
				var l = _layers[i];
				if (!l.Visible || l.Content == null) continue;
				_compositeBlendMat.SetTexture("_Background", a.texArray);
				_compositeBlendMat.SetFloat("_Opacity", Mathf.Clamp01(l.Opacity));
				RenderUdims.SetNumUdims(a, _compositeBlendMat);
				Graphics.Blit(l.Content.texArray, b.texArray, _compositeBlendMat);
				var t = a; a = b; b = t;
			}
			Graphics.CopyTexture(a.texArray, dest.texArray);
		}

		/// <summary>Composite all visible layers over a base. Uses stack resolution for temps when set; otherwise creates temps from baseLayer so display still shows all layers (fixes blank when 2+ layers).</summary>
		public void CompositeToOnTopOfBase(RenderUdims baseLayer, RenderUdims dest)
		{
			if (dest == null || baseLayer == null) return;
			if (_compositeBlendMat == null) { Graphics.CopyTexture(baseLayer.texArray, dest.texArray); return; }
			RenderUdims.assertSameSize(dest, baseLayer);
			RenderUdims a = GetOrCreateCompositeTemp(ref _compositeTempA);
			RenderUdims b = GetOrCreateCompositeTemp(ref _compositeTempB);
			if (a == null || b == null)
			{
				a = GetOrCreateCompositeTempFromBase(ref _compositeTempA, baseLayer);
				b = GetOrCreateCompositeTempFromBase(ref _compositeTempB, baseLayer);
			}
			if (a == null || b == null)
			{
				UnityEngine.Debug.LogWarning("[PaintLayerStack] CompositeToOnTopOfBase: composite temps null (resolution not set?). Copying base only.");
				Graphics.CopyTexture(baseLayer.texArray, dest.texArray);
				return;
			}
			Graphics.CopyTexture(baseLayer.texArray, a.texArray);
			foreach (var l in _layers)
			{
				if (!l.Visible || l.Content == null) continue;
				_compositeBlendMat.SetTexture("_Background", a.texArray);
				_compositeBlendMat.SetFloat("_Opacity", Mathf.Clamp01(l.Opacity));
				RenderUdims.SetNumUdims(a, _compositeBlendMat);
				Graphics.Blit(l.Content.texArray, b.texArray, _compositeBlendMat);
				var t = a; a = b; b = t;
			}
			Graphics.CopyTexture(a.texArray, dest.texArray);
		}

		/// <summary>Composite all visible layers (bottom to top) into dest. No base – first visible layer is the bottom. Use CompositeToOnTopOfBase when you need scene/fallback as the bottom.</summary>
		public void CompositeTo(RenderUdims dest) => CompositeTo(dest, excludeLayerIndex: -1);

		/// <summary>Same as CompositeTo(dest) but skips the layer at excludeLayerIndex (e.g. when dest is that layer's Content and it should not be blended on top).</summary>
		public void CompositeTo(RenderUdims dest, int excludeLayerIndex)
		{
			if (dest == null) return;
			if (_compositeBlendMat == null) { dest.ClearTheTextures(Color.clear); return; }
			int visibleCount = 0;
			for (int i = 0; i < _layers.Count; i++)
			{
				if (i == excludeLayerIndex) continue;
				var l = _layers[i];
				if (l.Visible && l.Content != null) visibleCount++;
			}
			if (visibleCount == 0)
			{
				dest.ClearTheTextures(Color.clear);
				return;
			}
			RenderUdims first = null;
			for (int i = 0; i < _layers.Count; i++)
			{
				if (i == excludeLayerIndex) continue;
				var l = _layers[i];
				if (!l.Visible || l.Content == null) continue;
				first = l.Content;
				break;
			}
			if (first == null)
			{
				dest.ClearTheTextures(Color.clear);
				return;
			}
			// Stack resolution can still be unset if InitTextures hasn't run yet; temps require _resolution. Adopt from first visible layer (no layer realloc).
			if (_resolution.x <= 0 && first.width > 0 && first.height > 0 && first.UdimsCount > 0)
			{
				_resolution = new Vector2Int(first.width, first.height);
				_udimsCount = first.UdimsCount;
			}
			RenderUdims.assertSameSize(dest, first);
			RenderUdims a = GetOrCreateCompositeTemp(ref _compositeTempA);
			RenderUdims b = GetOrCreateCompositeTemp(ref _compositeTempB);
			if (a == null || b == null)
			{
				a = GetOrCreateCompositeTempFromBase(ref _compositeTempA, first);
				b = GetOrCreateCompositeTempFromBase(ref _compositeTempB, first);
			}
			if (a == null || b == null)
			{
				UnityEngine.Debug.LogWarning("[PaintLayerStack] CompositeTo: could not create composite temps (UDIMs/resolution?). Clearing dest so callers do not reuse stale mask data.");
				dest.ClearTheTextures(Color.clear);
				return;
			}
			bool foundFirst = false;
			RenderUdims tmp;
			for (int i = 0; i < _layers.Count; i++)
			{
				if (i == excludeLayerIndex) continue;
				var l = _layers[i];
				if (!l.Visible || l.Content == null) continue;
				if (!foundFirst)
				{
					// The runtime display path (`EntireColorLayer_BlitApply`) multiplies each layer by its
					// opacity (_TotalOpacity01) before blending. So the bottom-most visible layer cannot be
					// "true-copied" without opacity; it must be blended over a cleared background using
					// the same composite math.
					a.ClearTheTextures(Color.clear);
					_compositeBlendMat.SetTexture("_Background", a.texArray);
					_compositeBlendMat.SetFloat("_Opacity", Mathf.Clamp01(l.Opacity));
					RenderUdims.SetNumUdims(a, _compositeBlendMat);
					Graphics.Blit(l.Content.texArray, b.texArray, _compositeBlendMat);
					tmp = a; a = b; b = tmp;
					foundFirst = true;
					if (visibleCount == 1)
					{
						Graphics.CopyTexture(a.texArray, dest.texArray);
						return;
					}
					continue;
				}
				_compositeBlendMat.SetTexture("_Background", a.texArray);
				_compositeBlendMat.SetFloat("_Opacity", Mathf.Clamp01(l.Opacity));
				RenderUdims.SetNumUdims(a, _compositeBlendMat);
				Graphics.Blit(l.Content.texArray, b.texArray, _compositeBlendMat);
				tmp = a; a = b; b = tmp;
			}
			Graphics.CopyTexture(a.texArray, dest.texArray);
		}

		/// <summary>Direct GPU memory copy of all UDIM slices from src to dest. Uses Graphics.CopyTexture —
		/// no shader, no material, no blending. Requires matching format, size, and slice count.</summary>
		public void CopyAllSlices(RenderUdims src, RenderUdims dest)
		{
			if (src == null || dest == null) return;
			if (src.width != dest.width || src.height != dest.height || src.UdimsCount != dest.UdimsCount) return;
			Graphics.CopyTexture(src.texArray, dest.texArray);
		}

		// --- Composite temp buffers (ping-pong for blending; fallback from base when stack resolution not set) ---
		RenderUdims GetOrCreateCompositeTemp(ref RenderUdims field)
		{
			if (field != null && field.width == _resolution.x && field.height == _resolution.y && field.UdimsCount == _udimsCount)
				return field;
			field?.Dispose();
			var udims = UDIMs_Helper._allKnownUdims;
			if (udims == null || _resolution.x <= 0) return null;
			field = new RenderUdims(udims, _resolution, GenData_Masks.colorBrushFormat, GenData_Masks.colorBrushFilter, Color.clear, 0);
			return field;
		}

		/// <summary>Create or reuse composite temps matching baseLayer size/udims when stack resolution isn't set. Ensures 2+ layers can still composite for display.</summary>
		RenderUdims GetOrCreateCompositeTempFromBase(ref RenderUdims field, RenderUdims baseLayer)
		{
			if (baseLayer?.udims_sectors == null || baseLayer.udims_sectors.Count == 0) return null;
			if (field != null && field.width == baseLayer.width && field.height == baseLayer.height && field.UdimsCount == baseLayer.UdimsCount)
				return field;
			field?.Dispose();
			field = new RenderUdims(baseLayer.udims_sectors, baseLayer.widthHeight, GenData_Masks.colorBrushFormat, GenData_Masks.colorBrushFilter, Color.clear, 0);
			return field;
		}

		// --- Save / Load (called from ProjectSaveLoad_Helper) ---
		/// <summary>Save layer stack to project. Call from ProjectSaveLoad_Helper. </summary>
		public void Save(StableProjectorz_SL spz)
		{
			if (spz == null) return;
			if (_layers.Count == 0)
			{
				spz.paintLayerStack = null;
				return;
			}
			var sl = new PaintLayerStack_SL
			{
				activeLayerIndex = _activeIndex,
				resolutionWidth = _resolution.x,
				resolutionHeight = _resolution.y,
				udimsCount = _udimsCount,
				nextLayerNumber = _nextLayerNumber,
				layers = new List<PaintLayer_SL>()
			};
			for (int i = 0; i < _layers.Count; i++)
			{
				var layer = _layers[i];
				var layerSL = new PaintLayer_SL
				{
					name = layer.Name,
					visible = layer.Visible,
					opacity = layer.Opacity,
					blendMode = (int)layer.BlendMode,
					content = null
				};
				if (layer.Content != null)
				{
					try
					{
						layerSL.content = layer.Content.Save(spz.filepath_dataDir, "PaintLayer_", i.ToString());
					}
					catch (Exception ex)
					{
						Debug.LogWarning("[PaintLayerStack] Save layer " + i + " content failed: " + ex.Message);
					}
				}
				sl.layers.Add(layerSL);
			}
			spz.paintLayerStack = sl;
		}

		/// <summary>Load layer stack from project. Call from ProjectSaveLoad_Helper. </summary>
		public void Load(StableProjectorz_SL spz)
		{
			if (spz?.paintLayerStack == null) return;
			var sl = spz.paintLayerStack;
			// Clear existing layers
			foreach (var l in _layers)
				l.Dispose();
			_layers.Clear();
			_resolution = new Vector2Int(sl.resolutionWidth, sl.resolutionHeight);
			_udimsCount = sl.udimsCount;
			_nextLayerNumber = sl.nextLayerNumber > 0 ? sl.nextLayerNumber : InferNextLayerNumber(sl);
			_nextCollapseNumber = sl.nextCollapseNumber > 0 ? sl.nextCollapseNumber : InferNextCollapseNumber(sl);
			var format = GenData_Masks.colorBrushFormat;
			var filter = GenData_Masks.colorBrushFilter;
			if (sl.layers != null)
			{
				for (int i = 0; i < sl.layers.Count; i++)
				{
					var layerSL = sl.layers[i];
					var layer = new PaintLayer(layerSL?.name ?? ("Layer " + (i + 1)));
					if (layerSL != null)
					{
						layer.Visible = layerSL.visible;
						layer.Opacity = Mathf.Clamp01(layerSL.opacity);
						if (layerSL.blendMode >= 0 && layerSL.blendMode <= (int)PaintLayerBlendMode.Overlay)
							layer.BlendMode = (PaintLayerBlendMode)layerSL.blendMode;
						if (layerSL.content != null && layerSL.content.textures != null && layerSL.content.textures.Count > 0)
						{
							var ru = new RenderUdims();
							try
							{
								ru.Load(spz.filepath_dataDir, layerSL.content, format, filter);
								layer.SetContentFromLoad(ru);
							}
							catch (Exception ex)
							{
								Debug.LogWarning("[PaintLayerStack] Load layer " + i + " content failed: " + ex.Message);
								ru.Dispose();
							}
						}
					}
					_layers.Add(layer);
				}
			}
			_activeIndex = Mathf.Clamp(sl.activeLayerIndex, 0, Mathf.Max(0, _layers.Count - 1));
			OnLayersChanged?.Invoke();
			OnActiveLayerChanged?.Invoke();
		}

		static int InferNextLayerNumber(PaintLayerStack_SL sl)
		{
			if (sl?.layers == null || sl.layers.Count == 0) return 1;
			int max = 0;
			foreach (var layerSL in sl.layers)
			{
				if (layerSL?.name == null) continue;
				string n = layerSL.name.Trim();
				if (n.StartsWith("Layer ", StringComparison.OrdinalIgnoreCase) && n.Length > 6 &&
				    int.TryParse(n.Substring(6).Trim(), out int num) && num > max)
					max = num;
			}
			return max + 1;
		}

		static int InferNextCollapseNumber(PaintLayerStack_SL sl)
		{
			if (sl?.layers == null || sl.layers.Count == 0) return 1;
			int max = 0;
			foreach (var layerSL in sl.layers)
			{
				if (layerSL?.name == null) continue;
				string n = layerSL.name.Trim();
				if (n.Equals("Collapsed", StringComparison.OrdinalIgnoreCase))
				{
					if (max < 1) max = 1;
					continue;
				}

				if (n.StartsWith("Collapse ", StringComparison.OrdinalIgnoreCase) && n.Length > 9 &&
				    int.TryParse(n.Substring(9).Trim(), out int num) && num > max)
					max = num;
			}

			return max + 1;
		}
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace spz {

	/// <summary>
	/// Layer stack: each layer is a container (scene injected into it + strokes). Display shows the active layer's Content only (no "scene base + layers on top").
	/// Only the active layer receives new paint. Ordering: index 0 = bottom, last index = top.
	/// Layer panel UI is add/delete only; visibility/opacity exist in data for save/load but are not exposed in the minimal UI.
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
		RenderUdims _compositeTempA;
		RenderUdims _compositeTempB;

		/// <summary>Layers from bottom (index 0) to top. Do not modify list directly; use AddLayer/RemoveLayer/MoveLayer. </summary>
		public IReadOnlyList<PaintLayer> Layers => _layers;
		public int ActiveLayerIndex => _activeIndex;
		public PaintLayer ActiveLayer => _activeIndex >= 0 && _activeIndex < _layers.Count ? _layers[_activeIndex] : null;
		/// <summary>RenderUdims to paint into (active layer's Content — the display buffer). Compute shader
		/// writes strokes here directly; GPU command serialization ensures no timing gap. </summary>
		public RenderUdims ActiveLayerRenderUdims => ActiveLayer?.Content;
		/// <summary>Active layer's secondary data buffer. Not used during live painting; exists for save/load. </summary>
		public RenderUdims ActiveLayerDataRenderUdims => ActiveLayer?.Data;

		public event Action OnLayersChanged;
		public event Action OnActiveLayerChanged;
		/// <summary>Invoked when a new layer is added (the new layer is already active). Inpaint_MaskPainter subscribes to inject scene into it (OnLayerAdded_InjectScene).</summary>
		public static Action<PaintLayer> OnLayerAdded;

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
			foreach (var l in _layers)
				l.Dispose();
			_layers.Clear();
			if (instance == this)
				instance = null;
		}

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

		/// <summary>Add a new layer. New layer is set active. Scene/data injection runs via OnLayerAdded (e.g. Inpaint_MaskPainter).</summary>
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
			var layer = _layers[fromIndex];
			_layers.RemoveAt(fromIndex);
			_layers.Insert(toIndex, layer);
			if (_activeIndex == fromIndex)
				_activeIndex = toIndex;
			else if (fromIndex < _activeIndex && toIndex >= _activeIndex)
				_activeIndex--;
			else if (fromIndex > _activeIndex && toIndex <= _activeIndex)
				_activeIndex++;
			OnLayersChanged?.Invoke();
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

		/// <summary>Set opacity of a layer (0–1). Fires OnLayersChanged. </summary>
		public void SetLayerOpacity(int index, float opacity)
		{
			if (index < 0 || index >= _layers.Count) return;
			_layers[index].Opacity = Mathf.Clamp01(opacity);
			OnLayersChanged?.Invoke();
		}

		/// <summary>Composite all visible layers over a base. Used only for legacy paths; display now uses active layer Content only (container = scene + strokes).</summary>
		public void CompositeToOnTopOfBase(RenderUdims baseLayer, RenderUdims dest)
		{
			if (dest == null || baseLayer == null) return;
			if (_compositeBlendMat == null) { Graphics.Blit(baseLayer.texArray, dest.texArray); return; }
			RenderUdims.assertSameSize(dest, baseLayer);
			RenderUdims a = GetOrCreateCompositeTemp(ref _compositeTempA);
			RenderUdims b = GetOrCreateCompositeTemp(ref _compositeTempB);
			if (a == null || b == null) { Graphics.Blit(baseLayer.texArray, dest.texArray); return; }
			Graphics.Blit(baseLayer.texArray, a.texArray);
			foreach (var l in _layers)
			{
				if (!l.Visible || l.Content == null) continue;
				_compositeBlendMat.SetTexture("_Background", a.texArray);
				_compositeBlendMat.SetFloat("_Opacity", Mathf.Clamp01(l.Opacity));
				RenderUdims.SetNumUdims(a, _compositeBlendMat);
				Graphics.Blit(l.Content.texArray, b.texArray, _compositeBlendMat);
				var t = a; a = b; b = t;
			}
			Graphics.Blit(a.texArray, dest.texArray);
		}

		/// <summary>Composite all visible layers (bottom to top) into dest. No base – first visible layer is the bottom. Use CompositeToOnTopOfBase when you need scene/fallback as the bottom.</summary>
		public void CompositeTo(RenderUdims dest)
		{
			if (dest == null) return;
			if (_compositeBlendMat == null) { dest.ClearTheTextures(Color.clear); return; }
			int visibleCount = 0;
			foreach (var l in _layers)
				if (l.Visible && l.Content != null) visibleCount++;
			if (visibleCount == 0)
			{
				dest.ClearTheTextures(Color.clear);
				return;
			}
			RenderUdims first = null;
			foreach (var l in _layers)
			{
				if (!l.Visible || l.Content == null) continue;
				first = l.Content;
				break;
			}
			if (first == null)
			{
				dest.ClearTheTextures(Color.clear);
				return;
			}
			RenderUdims.assertSameSize(dest, first);
			RenderUdims a = GetOrCreateCompositeTemp(ref _compositeTempA);
			RenderUdims b = GetOrCreateCompositeTemp(ref _compositeTempB);
			if (a == null || b == null) return;
			a.ClearTheTextures(Color.clear);
			Graphics.Blit(first.texArray, a.texArray);
			if (visibleCount == 1)
			{
				Graphics.Blit(a.texArray, dest.texArray);
				return;
			}
			bool firstVisible = true;
			foreach (var l in _layers)
			{
				if (!l.Visible || l.Content == null) continue;
				if (firstVisible) { firstVisible = false; continue; }
				_compositeBlendMat.SetTexture("_Background", a.texArray);
				_compositeBlendMat.SetFloat("_Opacity", Mathf.Clamp01(l.Opacity));
				RenderUdims.SetNumUdims(a, _compositeBlendMat);
				Graphics.Blit(l.Content.texArray, b.texArray, _compositeBlendMat);
				var t = a; a = b; b = t;
			}
			Graphics.Blit(a.texArray, dest.texArray);
		}

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
	}
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace spz {

	// =============================================================================
	// LAYER SYSTEM - MANAGER (stack of PaintLayer; single source of truth)
	// =============================================================================
	// Holds the ordered list of PaintLayer (index 0 = bottom). PaintTab_LayersPanel_UI
	// holds a reference to this and calls AddLayer(), SetActiveLayer(), SetLayerVisible(),
	// SetLayerName() (rename only — does not fire OnLayersChanged; see RebuildList / undo). RemoveLayer().
	// Inpaint_MaskPainter subscribes to OnLayerAdded to inject scene into new layers; uses ActiveLayerRenderUdims as the paint target; display blits each
	// visible layer's Content in order (see ApplyColorLayer_To_UV_Textures). Paint undo clears on OnLayerStackStructureChanged (layer Content realloc, remove/move, load), not visibility/opacity or metadata-only EnsureResolution sync.
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
		Coroutine _collapseSliceCopyCrt;
		int _collapsePathObsBucket;
		int _collapsePathObsArm;

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
		/// <summary>Add/remove/reorder, full stack load, or resolution/UDIM count change — invalidates paint undo snapshots. Visibility does not fire <see cref="OnLayersChanged"/> (avoids RebuildList mid-eye-toggle); opacity still does.</summary>
		public event Action OnLayerStackStructureChanged;
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
			{
				_compositeBlendMat = new Material(_compositeBlendShader);
				// Ranged composite shader: _SliceCompositeEnd==0 means “all slices” until SetCompositeBlendSliceRange* runs on each Blit.
				_compositeBlendMat.SetInt("_SliceCompositeBegin", 0);
				_compositeBlendMat.SetInt("_SliceCompositeEnd", 0);
			}
			else
				UnityEngine.Debug.LogError("[PaintLayerStack] Awake: _compositeBlendShader is null! Compositor will not work.");
			EnsureAtLeastOneLayer();
			UnityEngine.Debug.Log($"[PaintLayerStack] Awake complete: {_layers.Count} layers, active={_activeIndex}.");
		}

		const string CollapseStickyMsg = "Collapsing layers — please wait…";
		static readonly Color CollapseStickyColor = new Color(1f, 0.92f, 0.55f, 1f);

		/// <summary>
		/// Clears scheduled-collapse coroutine ref, resets <see cref="Inpaint_MaskPainter.IsCollapsingLayers"/>, and nudges a full re-render.
		/// Call after <see cref="MonoBehaviour.StopCoroutine"/> on the scheduled collapse coroutine: Unity does not reliably run iterator <c>finally</c> when a coroutine is stopped.
		/// </summary>
		void CleanupAfterScheduledCollapse()
		{
			_collapseSliceCopyCrt = null;
			var mp = Inpaint_MaskPainter.instance;
			if (mp != null) mp.IsCollapsingLayers = false;
			// Always clear sticky if a scheduled collapse was stopped mid-flight.
			Viewport_StatusText.instance?.StopStickyMsg(CollapseStickyMsg);
			if (Objects_Renderer_MGR.instance != null)
				Objects_Renderer_MGR.instance.ReRenderAll_soon();
		}

		/// <summary>User-facing status so a long collapse (many UDIMs) is not mistaken for a hang/error.</summary>
		static void NotifyCollapseBegin(bool amortizedAcrossFrames, int visibleCount, int udimSlices)
		{
			var st = Viewport_StatusText.instance;
			if (st == null) return;
			if (amortizedAcrossFrames)
			{
				st.ShowStickyMsg(CollapseStickyMsg, CollapseStickyColor);
				st.ShowStatusText(
					$"Collapsing {visibleCount} layers ({udimSlices} UDIM slices). This can take a moment — not an error.",
					false, 12f, true);
				st.ReportProgress(0f);
			}
			else
			{
				st.ShowStatusText($"Collapsing {visibleCount} layers…", false, 4f, false);
			}
		}

		static void NotifyCollapseProgress(float progress01)
		{
			var st = Viewport_StatusText.instance;
			if (st == null) return;
			st.ReportProgress(Mathf.Clamp01(progress01));
		}

		static void NotifyCollapseEnd(bool ok)
		{
			var st = Viewport_StatusText.instance;
			if (st == null) return;
			st.StopStickyMsg(CollapseStickyMsg);
			// Always hide the progress bar on completion — leaving it on after a scheduled collapse looked stuck.
			st.ReportProgress(ok ? 1f : 0f);
			if (ok)
				st.ShowStatusText("Layers collapsed.", false, 2.5f, false);
			else
				st.ShowStatusText("Collapse could not finish.", false, 4f, false);
		}

		void OnDestroy()
		{
			if (_collapseSliceCopyCrt != null) {
				StopCoroutine(_collapseSliceCopyCrt);
				CleanupAfterScheduledCollapse();
			}
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
			// Only invalidate paint undo when at least one layer's GPU buffers were actually (re)allocated.
			// Cached _resolution can lag behind real Content (e.g. after SD / composite adopt paths); resChanged
			// alone would spuriously clear undo even though every EnsureContent no-ops.
			bool anyLayerContentRebuilt = false;
			for (int i = 0; i < _layers.Count; i++)
			{
				if (_layers[i].EnsureContent(udims, _resolution, GenData_Masks.colorBrushFormat, GenData_Masks.colorBrushFilter))
					anyLayerContentRebuilt = true;
			}
			EnsureAtLeastOneLayer();
			if (anyLayerContentRebuilt)
				OnLayerStackStructureChanged?.Invoke();
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
			// AddLayer appends; existing layer indices remain valid — don't clear undo (OnLayerStackStructureChanged).
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

		/// <summary>Remove the layer at index. Disposes the layer and adjusts active index.
		/// Deleting the last layer recreates a blank Layer 1 so paint always has an active Content/NoColorMask
		/// (an empty stack made strokes write the scene buffer while the viewport preferred Art UV and hid them).</summary>
		public void RemoveLayer(int index)
		{
			if (index < 0 || index >= _layers.Count) return;
			_layers[index].Dispose();
			_layers.RemoveAt(index);
			if (_layers.Count == 0)
			{
				// AddLayer fires OnLayersChanged / OnActiveLayerChanged / OnLayerAdded; also notify structure
				// so undo snapshots tied to the disposed layer are cleared.
				EnsureAtLeastOneLayer();
				OnLayerStackStructureChanged?.Invoke();
				if (Objects_Renderer_MGR.instance != null)
					Objects_Renderer_MGR.instance.ReRenderAll_soon();
				return;
			}
			if (_activeIndex >= _layers.Count)
				_activeIndex = Mathf.Max(0, _layers.Count - 1);
			else if (index < _activeIndex)
				_activeIndex--;
			OnLayersChanged?.Invoke();
			OnLayerStackStructureChanged?.Invoke();
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
			OnLayerStackStructureChanged?.Invoke();
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

		/// <summary>Index of the layer whose <see cref="PaintLayer.Content"/> reference-equals <paramref name="content"/>, or -1 (e.g. standalone inpaint buffer).</summary>
		public int IndexOfContent(RenderUdims content) {
			if (content == null) return -1;
			for (int i = 0; i < _layers.Count; i++) {
				var L = _layers[i];
				if (L != null && L.Content == content) return i;
			}
			return -1;
		}

		/// <summary>Index of the layer whose <see cref="PaintLayer.NoColorMask"/> reference-equals <paramref name="mask"/>, or -1.</summary>
		public int IndexOfNoColorMask(RenderUdims mask) {
			if (mask == null) return -1;
			for (int i = 0; i < _layers.Count; i++) {
				var L = _layers[i];
				if (L != null && L.NoColorMask == mask) return i;
			}
			return -1;
		}

		/// <summary>Set visibility of a layer (Photoshop-style: hidden layers are not shown in viewport).
		/// Does <b>not</b> fire <see cref="OnLayersChanged"/> — that rebuilt the entire layers UI (destroying rows mid-eye-toggle).
		/// Callers that own row chrome (panel eye button) update the glyph locally; viewport uses <see cref="Objects_Renderer_MGR.ReRenderAll_soon"/>.</summary>
		public void SetLayerVisible(int index, bool visible)
		{
			if (index < 0 || index >= _layers.Count) return;
			if (_layers[index].Visible == visible) return;
			_layers[index].Visible = visible;
			if (Objects_Renderer_MGR.instance != null)
				Objects_Renderer_MGR.instance.ReRenderAll_soon();
		}

		const int MaxLayerNameLength = 128;

		/// <summary>Rename the layer at index. Empty or whitespace becomes a default label. Persists via Save like <see cref="PaintLayer.Name"/>.
		/// Does <b>not</b> fire <see cref="OnLayersChanged"/> — rename is metadata only; firing it rebuilt the entire layers UI (destroying rows mid-submit) and cleared paint undo, and could disrupt viewport state.</summary>
		public void SetLayerName(int index, string name)
		{
			if (index < 0 || index >= _layers.Count) return;
			string n = string.IsNullOrWhiteSpace(name) ? DefaultLayerDisplayName(index) : name.Trim();
			if (n.Length > MaxLayerNameLength)
				n = n.Substring(0, MaxLayerNameLength);
			if (_layers[index].Name == n) return;
			_layers[index].Name = n;
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

		/// <summary>Merge visible layers into one new layer. Heavy stacks: scheduled coroutine composites UDIM slices in batches across frames using <see cref="PaintUndo_MGR.GetCollapseSliceScheduler"/> (Thompson slice arms persist across collapses; immediate vs scheduled chosen by contextual bandit after cold-start heuristics). Light stacks: full GPU composite then copy. CPU fallback if no blend shader.</summary>
		public bool CollapseVisibleLayersIntoOne()
		{
			var visibleLayers = new List<PaintLayer>();
			foreach (var l in _layers)
				if (l.Visible && l.Content != null && l.Content.texArray != null)
					visibleLayers.Add(l);

			if (visibleLayers.Count == 0)
			{
				UnityEngine.Debug.Log("[PaintLayerStack] Collapse: no visible layer with content.");
				Viewport_StatusText.instance?.ShowStatusText(
					"Nothing to collapse — no visible layers with paint.", false, 3f, false);
				return false;
			}

			if (_compositeBlendMat != null)
			{
				RenderUdims first = visibleLayers[0].Content;
				int visCount = visibleLayers.Count;
				var collapseSched = PaintUndo_MGR.GetCollapseSliceScheduler();
				bool useScheduled;
				int pathBucket, pathArm;
				if (collapseSched != null) {
					useScheduled = collapseSched.SelectCollapseScheduledVersusImmediate(
						first.width, first.height, first.UdimsCount, visCount, true, out pathBucket, out pathArm);
				} else {
					float refPx = 512f * 512f;
					useScheduled = PaintUndo_Scheduler.EvaluateCollapseScheduleHeuristic(
						first.width, first.height, first.UdimsCount, visCount, refPx);
					pathBucket = 0;
					pathArm = useScheduled ? 1 : 0;
				}

				// Amortized slice composite requires the shared collapse scheduler (same as undo).
				if (useScheduled && PaintUndo_MGR.GetCollapseSliceScheduler() == null) {
					useScheduled = false;
					pathArm = 0;
					pathBucket = 0;
				}

				if (useScheduled)
				{
					// Do not StopCoroutine mid-collapse: finally may not run and the destination layer is left half-composited.
					if (_collapseSliceCopyCrt != null)
					{
						Viewport_StatusText.instance?.ShowStatusText(
							"Collapse already in progress — please wait.", false, 3f, false);
						return false;
					}
					NotifyCollapseBegin(amortizedAcrossFrames: true, visCount, first.UdimsCount);
					_collapsePathObsBucket = pathBucket;
					_collapsePathObsArm = pathArm;
					_collapseSliceCopyCrt = StartCoroutine(CollapseVisibleLayersIntoOne_GpuScheduledCoroutine(first));
					return true;
				}

				NotifyCollapseBegin(amortizedAcrossFrames: false, visCount, first.UdimsCount);
				float tImmediate = Time.realtimeSinceStartup;
				EnsureCollapseResultTemp(first);
				CompositeTo(_collapseResultTemp);

				var maskPainter = Inpaint_MaskPainter.instance;
				PaintLayer newLayer = null;
				try {
					if (maskPainter != null) maskPainter.IsCollapsingLayers = true;
					newLayer = AddLayer(ConsumeNextDefaultCollapseLayerName());
					EnsureContentForLayerIfNeeded(newLayer);
				}
				finally {
					if (maskPainter != null) maskPainter.IsCollapsingLayers = false;
				}

				bool pasted = false;
				if (newLayer != null && newLayer.Content != null && newLayer.Content.texArray != null
				    && newLayer.Content.width == _collapseResultTemp.width
				    && newLayer.Content.height == _collapseResultTemp.height
				    && newLayer.Content.UdimsCount == _collapseResultTemp.UdimsCount)
				{
					CopyAllSlices(_collapseResultTemp, newLayer.Content);
					newLayer.SyncDataFromContent();
					newLayer.HasReceivedSceneInject = true;
					pasted = true;
					PasteCollapsedNoColorMasksInto(newLayer, _layers.IndexOf(newLayer));
				}
				else
					UnityEngine.Debug.LogWarning("[PaintLayerStack] Collapse: new layer has no Content; GPU path paste skipped.");

				if (collapseSched != null) {
					float elapsedMs = (Time.realtimeSinceStartup - tImmediate) * 1000f;
					PaintUndo_Scheduler.EvaluateWorkload(first.width, first.height, first.UdimsCount, collapseSched.referencePixelsPerSlice,
						out _, out float complexity01, out _);
					bool ok = PaintUndo_Scheduler.CollapseImmediateObservationSuccess(elapsedMs, complexity01, visCount);
					collapseSched.RegisterCollapsePathObservation(pathBucket, pathArm, ok);
				}

				NotifyCollapseEnd(pasted);
				return pasted;
			}

			return CollapseVisibleLayersIntoOne_CpuFallback(visibleLayers);
		}

		void EnsureCollapseResultTemp(RenderUdims first)
		{
			if (_collapseResultTemp == null
			    || _collapseResultTemp.width != first.width || _collapseResultTemp.height != first.height
			    || _collapseResultTemp.UdimsCount != first.UdimsCount)
			{
				_collapseResultTemp?.Dispose();
				_collapseResultTemp = new RenderUdims(first.udims_sectors, first.widthHeight,
					GenData_Masks.colorBrushFormat, GenData_Masks.colorBrushFilter, Color.clear, 0);
			}
		}

		IEnumerator CollapseVisibleLayersIntoOne_GpuScheduledCoroutine(RenderUdims firstRef)
		{
			var maskPainter = Inpaint_MaskPainter.instance;
			PaintLayer newLayer = null;
			float worstHitchMs = 0f;
			bool completedComposite = false;
			var collapseSched = PaintUndo_MGR.GetCollapseSliceScheduler();
			try {
				if (firstRef == null || firstRef.UdimsCount <= 0) {
					UnityEngine.Debug.LogWarning("[PaintLayerStack] Collapse (scheduled): invalid layer dimensions.");
					yield break;
				}

				if (maskPainter != null) maskPainter.IsCollapsingLayers = true;

				newLayer = AddLayer(ConsumeNextDefaultCollapseLayerName());
				if (newLayer == null) {
					UnityEngine.Debug.LogWarning("[PaintLayerStack] Collapse (scheduled): AddLayer returned null.");
					yield break;
				}
				EnsureContentForLayerIfNeeded(newLayer);

				if (newLayer.Content == null || newLayer.Content.texArray == null
				    || newLayer.Content.width != firstRef.width
				    || newLayer.Content.height != firstRef.height
				    || newLayer.Content.UdimsCount != firstRef.UdimsCount)
				{
					UnityEngine.Debug.LogWarning("[PaintLayerStack] Collapse (scheduled): new layer Content missing or size mismatch.");
					yield break;
				}

				int excludeIdx = _layers.IndexOf(newLayer);
				if (excludeIdx < 0) {
					UnityEngine.Debug.LogWarning("[PaintLayerStack] Collapse (scheduled): new layer not in stack.");
					yield break;
				}

				if (collapseSched == null) {
					UnityEngine.Debug.LogWarning("[PaintLayerStack] Collapse (scheduled): PaintUndo_MGR missing; cannot amortize slices.");
					yield break;
				}
				collapseSched.BeginCollapseCompositeSession(firstRef.width, firstRef.height, firstRef.UdimsCount);
				int totalSlices = firstRef.UdimsCount;
				int cursor = 0;
				while (cursor < totalSlices) {
					float dt = Time.deltaTime;
					if (dt <= 1e-4f) dt = 1f / 60f;
					collapseSched.BeginRestoreTick(dt);
					int remaining = totalSlices - cursor;
					collapseSched.GetFrameBudget(remaining, out float budgetMs, out int maxSlices);
					float start = Time.realtimeSinceStartup;
					int sliceBegin = cursor;
					int batch = 0;
					while (batch < maxSlices && cursor < totalSlices) {
						if ((Time.realtimeSinceStartup - start) * 1000f >= budgetMs && batch > 0) break;
						batch++;
						cursor++;
					}
					if (batch > 0) {
						CompositeVisibleLayersIntoDestSliceRange(newLayer.Content, sliceBegin, sliceBegin + batch, excludeIdx);
						float hitchMs = Mathf.Max(0f, (Time.deltaTime - (1f / 60f)) * 1000f);
						worstHitchMs = Mathf.Max(worstHitchMs, hitchMs);
						collapseSched.RegisterRestoreBanditObservation(hitchMs, batch);
						NotifyCollapseProgress(totalSlices > 0 ? cursor / (float)totalSlices : 1f);
					}
					if (cursor < totalSlices)
						yield return null;
				}

				newLayer.SyncDataFromContent();
				newLayer.HasReceivedSceneInject = true;
				PasteCollapsedNoColorMasksInto(newLayer, excludeIdx);
				completedComposite = true;
			}
			finally {
				if (maskPainter != null)
					maskPainter.IsCollapsingLayers = false;
				if (collapseSched != null) {
					bool ok = completedComposite && collapseSched.CollapseScheduledObservationSuccess(worstHitchMs);
					collapseSched.RegisterCollapsePathObservation(_collapsePathObsBucket, _collapsePathObsArm, ok);
				}
				// Early yield-break after AddLayer left an empty/partial Collapse N layer in the Paint tab list.
				if (!completedComposite && newLayer != null) {
					int orphanIx = _layers.IndexOf(newLayer);
					if (orphanIx >= 0)
						RemoveLayer(orphanIx);
				}
				NotifyCollapseEnd(completedComposite);
				CleanupAfterScheduledCollapse();
			}
		}

		bool CollapseVisibleLayersIntoOne_CpuFallback(List<PaintLayer> visibleLayers)
		{
			RenderUdims first = visibleLayers[0].Content;
			int w = first.width;
			int h = first.height;
			int slices = first.UdimsCount;
			NotifyCollapseBegin(amortizedAcrossFrames: true, visibleLayers.Count, slices);

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
					NotifyCollapseEnd(ok: false);
					return false;
				}
				allLayerSlices.Add(layerTextures);
				allLayerOpacities.Add(Mathf.Clamp01(l.Opacity));
			}

			var resultTextures = new List<Texture2D>();
			for (int s = 0; s < slices; s++)
			{
				Color[] accum = new Color[w * h];
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

			foreach (var layerSlices in allLayerSlices)
				foreach (var t in layerSlices) if (t != null) UnityEngine.Object.DestroyImmediate(t);
			allLayerSlices.Clear();

			var maskPainter = Inpaint_MaskPainter.instance;
			PaintLayer newLayer = null;
			try {
				if (maskPainter != null) maskPainter.IsCollapsingLayers = true;
				newLayer = AddLayer(ConsumeNextDefaultCollapseLayerName());
				EnsureContentForLayerIfNeeded(newLayer);
			}
			finally {
				if (maskPainter != null) maskPainter.IsCollapsingLayers = false;
			}

			bool pasted = false;
			if (newLayer != null && newLayer.Content != null && newLayer.Content.texArray != null)
			{
				TextureTools_SPZ.TextureArray_Fill_N_Slices(newLayer.Content.texArray, resultTextures, 0);
				newLayer.SyncDataFromContent();
				newLayer.HasReceivedSceneInject = true;
				pasted = true;
				PasteCollapsedNoColorMasksInto_Cpu(newLayer, visibleLayers);
			}
			else
				UnityEngine.Debug.LogWarning("[PaintLayerStack] Collapse: new layer has no Content; paste skipped.");

			foreach (var t in resultTextures) if (t != null) UnityEngine.Object.DestroyImmediate(t);

			NotifyCollapseEnd(pasted);
			return pasted;
		}

		/// <summary>True if any visible layer (excluding <paramref name="excludeLayerIndex"/>) has a NoColorMask with paint data capacity.</summary>
		public bool AnyVisibleLayerHasNoColorMask(int excludeLayerIndex = -1)
		{
			for (int i = 0; i < _layers.Count; i++)
			{
				if (i == excludeLayerIndex) continue;
				var l = _layers[i];
				if (l != null && l.Visible && l.Content != null
				    && l.NoColorMask != null && l.NoColorMask.texArray != null)
					return true;
			}
			return false;
		}

		/// <summary>
		/// After Content collapse paste: merge visible layers' NoColorMask into the new layer so No-Color strokes survive Collapse.
		/// </summary>
		void PasteCollapsedNoColorMasksInto(PaintLayer newLayer, int excludeLayerIndex)
		{
			if (newLayer?.Content == null || _compositeBlendMat == null) return;
			if (!AnyVisibleLayerHasNoColorMask(excludeLayerIndex)) return;
			newLayer.EnsureNoColorMaskMatchesContent();
			if (newLayer.NoColorMask == null) return;
			CompositeNoColorMasksTo(newLayer.NoColorMask, excludeLayerIndex);
		}

		/// <summary>CPU-path counterpart when the blend shader is unavailable.</summary>
		void PasteCollapsedNoColorMasksInto_Cpu(PaintLayer newLayer, List<PaintLayer> sourceLayers)
		{
			if (newLayer?.Content == null || sourceLayers == null) return;
			bool any = false;
			foreach (var l in sourceLayers)
			{
				if (l != null && l.NoColorMask != null && l.NoColorMask.texArray != null) { any = true; break; }
			}
			if (!any) return;
			newLayer.EnsureNoColorMaskMatchesContent();
			if (newLayer.NoColorMask == null) return;

			int w = newLayer.Content.width;
			int h = newLayer.Content.height;
			int slices = newLayer.Content.UdimsCount;
			var allNcSlices = new List<List<Texture2D>>();
			var opacities = new List<float>();
			foreach (var l in sourceLayers)
			{
				if (l?.NoColorMask == null || l.NoColorMask.texArray == null)
				{
					allNcSlices.Add(null);
					opacities.Add(0f);
					continue;
				}
				List<Texture2D> layerTextures = TextureTools_SPZ.TextureArray_to_Texture2DList(l.NoColorMask.texArray);
				allNcSlices.Add(layerTextures);
				opacities.Add(Mathf.Clamp01(l.Opacity));
			}

			var resultTextures = new List<Texture2D>();
			for (int s = 0; s < slices; s++)
			{
				Color[] accum = new Color[w * h];
				for (int p = 0; p < accum.Length; p++)
					accum[p] = Color.clear;

				for (int li = 0; li < allNcSlices.Count; li++)
				{
					var layerSlices = allNcSlices[li];
					if (layerSlices == null || s >= layerSlices.Count) continue;
					Texture2D sliceTex = layerSlices[s];
					if (sliceTex == null) continue;
					Color[] fg = sliceTex.GetPixels();
					float opacity = opacities[li];
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

			foreach (var layerSlices in allNcSlices)
			{
				if (layerSlices == null) continue;
				foreach (var t in layerSlices) if (t != null) UnityEngine.Object.DestroyImmediate(t);
			}

			TextureTools_SPZ.TextureArray_Fill_N_Slices(newLayer.NoColorMask.texArray, resultTextures, 0);
			foreach (var t in resultTextures) if (t != null) UnityEngine.Object.DestroyImmediate(t);
		}

		/// <summary>Remove all layers and add a single empty layer (e.g. after collapsing into scene buffer). Used by Inpaint_MaskPainter.CollapseLayersIntoScene.</summary>
		public void ReplaceLayersWithOneEmpty()
		{
			// Dispose directly — do not call RemoveLayer in a loop (that recreates a blank layer when
			// count hits 0, and a trailing AddLayer would leave two layers).
			for (int i = _layers.Count - 1; i >= 0; i--)
			{
				_layers[i].Dispose();
				_layers.RemoveAt(i);
			}
			_activeIndex = 0;
			EnsureAtLeastOneLayer();
			OnLayerStackStructureChanged?.Invoke();
			UnityEngine.Debug.Log("[PaintLayerStack] ReplaceLayersWithOneEmpty: one empty layer.");
		}

		/// <summary>Set opacity of a layer (0–1). Fires OnLayersChanged. </summary>
		public void SetLayerOpacity(int index, float opacity)
		{
			if (index < 0 || index >= _layers.Count) return;
			_layers[index].Opacity = Mathf.Clamp01(opacity);
			OnLayersChanged?.Invoke();
			if (Objects_Renderer_MGR.instance != null)
				Objects_Renderer_MGR.instance.ReRenderAll_soon();
		}

		// --- Compositing (used by Inpaint_MaskPainter for new-layer injection and for APIs that need a flat image) ---
		void SetCompositeBlendSliceRange(RenderUdims udimsCountSource, int sliceBegin, int sliceEndExclusive)
		{
			if (_compositeBlendMat == null || udimsCountSource == null) return;
			RenderUdims.SetNumUdims(udimsCountSource, _compositeBlendMat);
			_compositeBlendMat.SetInt("_SliceCompositeBegin", sliceBegin);
			_compositeBlendMat.SetInt("_SliceCompositeEnd", sliceEndExclusive);
		}

		void SetCompositeBlendSliceRangeFull(RenderUdims udimsCountSource)
		{
			if (udimsCountSource == null) return;
			SetCompositeBlendSliceRange(udimsCountSource, 0, udimsCountSource.UdimsCount);
		}

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
				SetCompositeBlendSliceRangeFull(a);
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
				SetCompositeBlendSliceRangeFull(a);
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
					SetCompositeBlendSliceRangeFull(a);
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
				SetCompositeBlendSliceRangeFull(a);
				Graphics.Blit(l.Content.texArray, b.texArray, _compositeBlendMat);
				tmp = a; a = b; b = tmp;
			}
			Graphics.CopyTexture(a.texArray, dest.texArray);
		}

		/// <summary>
		/// Composite visible layers' <see cref="PaintLayer.NoColorMask"/> into dest (bottom→top, same opacity blend as Content).
		/// Layers without a NoColorMask are skipped. Used so Collapse preserves No-Color strokes.
		/// </summary>
		public void CompositeNoColorMasksTo(RenderUdims dest, int excludeLayerIndex = -1)
		{
			if (dest == null) return;
			if (_compositeBlendMat == null) { dest.ClearTheTextures(Color.clear); return; }

			int ncCount = 0;
			RenderUdims firstNc = null;
			for (int i = 0; i < _layers.Count; i++)
			{
				if (i == excludeLayerIndex) continue;
				var l = _layers[i];
				if (l == null || !l.Visible || l.Content == null) continue;
				if (l.NoColorMask == null || l.NoColorMask.texArray == null) continue;
				ncCount++;
				if (firstNc == null) firstNc = l.NoColorMask;
			}
			if (ncCount == 0 || firstNc == null)
			{
				dest.ClearTheTextures(Color.clear);
				return;
			}

			if (_resolution.x <= 0 && firstNc.width > 0 && firstNc.height > 0 && firstNc.UdimsCount > 0)
			{
				_resolution = new Vector2Int(firstNc.width, firstNc.height);
				_udimsCount = firstNc.UdimsCount;
			}
			RenderUdims.assertSameSize(dest, firstNc);
			RenderUdims a = GetOrCreateCompositeTemp(ref _compositeTempA);
			RenderUdims b = GetOrCreateCompositeTemp(ref _compositeTempB);
			if (a == null || b == null)
			{
				a = GetOrCreateCompositeTempFromBase(ref _compositeTempA, firstNc);
				b = GetOrCreateCompositeTempFromBase(ref _compositeTempB, firstNc);
			}
			if (a == null || b == null)
			{
				UnityEngine.Debug.LogWarning("[PaintLayerStack] CompositeNoColorMasksTo: could not create composite temps. Clearing dest.");
				dest.ClearTheTextures(Color.clear);
				return;
			}

			bool foundFirst = false;
			RenderUdims tmp;
			for (int i = 0; i < _layers.Count; i++)
			{
				if (i == excludeLayerIndex) continue;
				var l = _layers[i];
				if (l == null || !l.Visible || l.Content == null) continue;
				if (l.NoColorMask == null || l.NoColorMask.texArray == null) continue;
				if (!foundFirst)
				{
					a.ClearTheTextures(Color.clear);
					_compositeBlendMat.SetTexture("_Background", a.texArray);
					_compositeBlendMat.SetFloat("_Opacity", Mathf.Clamp01(l.Opacity));
					SetCompositeBlendSliceRangeFull(a);
					Graphics.Blit(l.NoColorMask.texArray, b.texArray, _compositeBlendMat);
					tmp = a; a = b; b = tmp;
					foundFirst = true;
					if (ncCount == 1)
					{
						Graphics.CopyTexture(a.texArray, dest.texArray);
						return;
					}
					continue;
				}
				_compositeBlendMat.SetTexture("_Background", a.texArray);
				_compositeBlendMat.SetFloat("_Opacity", Mathf.Clamp01(l.Opacity));
				SetCompositeBlendSliceRangeFull(a);
				Graphics.Blit(l.NoColorMask.texArray, b.texArray, _compositeBlendMat);
				tmp = a; a = b; b = tmp;
			}
			if (foundFirst)
				Graphics.CopyTexture(a.texArray, dest.texArray);
			else
				dest.ClearTheTextures(Color.clear);
		}

		/// <summary>GPU composite of visible layers into <paramref name="dest"/> for UDIM slices <c>[sliceBegin, sliceEndExclusive)</c>. Skips <paramref name="excludeLayerIndex"/> (e.g. the new collapse layer). Restores full-slice shader range after the call.</summary>
		public void CompositeVisibleLayersIntoDestSliceRange(RenderUdims dest, int sliceBegin, int sliceEndExclusive, int excludeLayerIndex)
		{
			if (dest == null || sliceBegin < 0 || sliceEndExclusive <= sliceBegin || sliceEndExclusive > dest.UdimsCount) return;
			if (_compositeBlendMat == null)
			{
				for (int s = sliceBegin; s < sliceEndExclusive; s++)
				{
					Graphics.SetRenderTarget(dest.texArray, 0, CubemapFace.Unknown, s);
					GL.Clear(false, true, Color.clear);
				}
				RenderTexture.active = null;
				return;
			}

			int visibleCount = 0;
			for (int i = 0; i < _layers.Count; i++)
			{
				if (i == excludeLayerIndex) continue;
				var l = _layers[i];
				if (l.Visible && l.Content != null) visibleCount++;
			}
			if (visibleCount == 0)
			{
				for (int s = sliceBegin; s < sliceEndExclusive; s++)
				{
					Graphics.SetRenderTarget(dest.texArray, 0, CubemapFace.Unknown, s);
					GL.Clear(false, true, Color.clear);
				}
				RenderTexture.active = null;
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
			if (first == null) return;

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
				UnityEngine.Debug.LogWarning("[PaintLayerStack] CompositeVisibleLayersIntoDestSliceRange: temps null; clearing dest slices.");
				for (int s = sliceBegin; s < sliceEndExclusive; s++)
				{
					Graphics.SetRenderTarget(dest.texArray, 0, CubemapFace.Unknown, s);
					GL.Clear(false, true, Color.clear);
				}
				RenderTexture.active = null;
				return;
			}

			bool foundFirst = false;
			RenderUdims tmp;
			try
			{
				for (int i = 0; i < _layers.Count; i++)
				{
					if (i == excludeLayerIndex) continue;
					var l = _layers[i];
					if (!l.Visible || l.Content == null) continue;
					if (!foundFirst)
					{
						for (int s = sliceBegin; s < sliceEndExclusive; s++)
						{
							Graphics.SetRenderTarget(a.texArray, 0, CubemapFace.Unknown, s);
							GL.Clear(false, true, Color.clear);
						}
						RenderTexture.active = null;

						SetCompositeBlendSliceRange(a, sliceBegin, sliceEndExclusive);
						_compositeBlendMat.SetTexture("_Background", a.texArray);
						_compositeBlendMat.SetFloat("_Opacity", Mathf.Clamp01(l.Opacity));
						Graphics.Blit(l.Content.texArray, b.texArray, _compositeBlendMat);
						tmp = a; a = b; b = tmp;
						foundFirst = true;
						if (visibleCount == 1)
						{
							for (int s = sliceBegin; s < sliceEndExclusive; s++)
								Graphics.CopyTexture(a.texArray, s, 0, dest.texArray, s, 0);
							return;
						}
						continue;
					}
					SetCompositeBlendSliceRange(a, sliceBegin, sliceEndExclusive);
					_compositeBlendMat.SetTexture("_Background", a.texArray);
					_compositeBlendMat.SetFloat("_Opacity", Mathf.Clamp01(l.Opacity));
					Graphics.Blit(l.Content.texArray, b.texArray, _compositeBlendMat);
					tmp = a; a = b; b = tmp;
				}
				for (int s = sliceBegin; s < sliceEndExclusive; s++)
					Graphics.CopyTexture(a.texArray, s, 0, dest.texArray, s, 0);
			}
			finally
			{
				SetCompositeBlendSliceRangeFull(first);
			}
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
				nextCollapseNumber = _nextCollapseNumber,
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
					content = null,
					noColorMask = null
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
				if (layer.NoColorMask != null)
				{
					try
					{
						layerSL.noColorMask = layer.NoColorMask.Save(spz.filepath_dataDir, "PaintLayerNoColor_", i.ToString());
					}
					catch (Exception ex)
					{
						Debug.LogWarning("[PaintLayerStack] Save layer " + i + " noColorMask failed: " + ex.Message);
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
			bool anyLayerHadSavedPaintContent = false;
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
								anyLayerHadSavedPaintContent = true;
							}
							catch (Exception ex)
							{
								Debug.LogWarning("[PaintLayerStack] Load layer " + i + " content failed: " + ex.Message);
								ru.Dispose();
							}
						}
						if (layerSL.noColorMask != null && layerSL.noColorMask.textures != null && layerSL.noColorMask.textures.Count > 0
						    && layer.Content == null)
							Debug.LogWarning("[PaintLayerStack] Load layer " + i + ": saved noColorMask skipped because layer content failed or is missing.");
						if (layerSL.noColorMask != null && layerSL.noColorMask.textures != null && layerSL.noColorMask.textures.Count > 0
						    && layer.Content != null)
						{
							var ruNc = new RenderUdims();
							try
							{
								ruNc.Load(spz.filepath_dataDir, layerSL.noColorMask, format, filter);
								layer.SetNoColorMaskFromLoad(ruNc);
								anyLayerHadSavedPaintContent = true;
							}
							catch (Exception ex)
							{
								Debug.LogWarning("[PaintLayerStack] Load layer " + i + " noColorMask failed: " + ex.Message);
								ruNc.Dispose();
							}
						}
					}
					_layers.Add(layer);
				}
			}
			_activeIndex = Mathf.Clamp(sl.activeLayerIndex, 0, Mathf.Max(0, _layers.Count - 1));
			Inpaint_MaskPainter.instance?.NotifyPaintLayersRestoredFromDisk(anyLayerHadSavedPaintContent);
			OnLayersChanged?.Invoke();
			OnLayerStackStructureChanged?.Invoke();
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

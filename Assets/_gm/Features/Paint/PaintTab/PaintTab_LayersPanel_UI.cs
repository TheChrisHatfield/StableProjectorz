using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace spz {

	// =============================================================================
	// LAYER SYSTEM - UI ONLY (layer list panel; no layer data lives here)
	// =============================================================================
	// This script is the Paint Tab layer list UI: rows (one per layer), Add Layer
	// button, visibility eye, layer name (read-only label → click to edit with TMP_InputField), delete.
	// Name: solid display by default (stack default names e.g. Layer 1); click label to enter edit mode;
	// Enter submits SetLayerName; Escape or focus loss cancels without saving. Active-layer changes only
	// refresh row tint (RefreshActiveHighlight). Scene injection and paint target are handled by Inpaint_MaskPainter.
	// =============================================================================

	/// <summary>
	/// Layer panel: Add Layer, visibility toggle per row, click name to rename (Enter confirms), Delete. Click row background to set active.
	/// Visibility: toggle button — dark blue when layer visible, light when hidden. Scene data is injected by Inpaint_MaskPainter.
	/// </summary>
	public class PaintTab_LayersPanel_UI : MonoBehaviour
	{
		[SerializeField] PaintLayerStack_MGR _layerStack;
		[SerializeField] RectTransform _listRoot;
		[SerializeField] Button _addLayerButton;
		[SerializeField] Button _collapseButton;
		[SerializeField] float _rowHeight = 28f;

		readonly List<GameObject> _rows = new List<GameObject>();
		int _renameRowIndex = -1;
		bool _suppressRenameEndEdit;

		// Drag reorder state
		int _dragFromIndex = -1;
		int _dragInsertIndex = -1;
		GameObject _dragInsertIndicator;

		// --- Wiring to PaintLayerStack_MGR (set stack ref and subscribe to layer/active changes) ---
		public void SetAddLayerButton(Button btn)
		{
			_addLayerButton = btn;
			if (_addLayerButton != null && _layerStack != null)
			{
				_addLayerButton.onClick.RemoveAllListeners();
				_addLayerButton.onClick.AddListener(OnAddLayer);
			}
			// Collapse button lives in the same row as Add Layer
			if (_addLayerButton != null)
			{
				var row = _addLayerButton.transform.parent;
				if (row != null)
				{
					var collapseT = row.Find("CollapseBtn");
					_collapseButton = collapseT != null ? collapseT.GetComponent<Button>() : null;
					if (_collapseButton != null && _layerStack != null)
					{
						_collapseButton.onClick.RemoveAllListeners();
						_collapseButton.onClick.AddListener(OnCollapse);
					}
				}
			}
		}

		public void SetLayerStack(PaintLayerStack_MGR stack)
		{
			if (_layerStack == stack) return;
			if (_layerStack != null)
			{
				_layerStack.OnLayersChanged -= RebuildList;
				_layerStack.OnActiveLayerChanged -= RefreshActiveHighlight;
			}
			_layerStack = stack;
			if (_layerStack != null)
			{
				_layerStack.OnLayersChanged += RebuildList;
				_layerStack.OnActiveLayerChanged += RefreshActiveHighlight;
				if (_listRoot == null) _listRoot = transform as RectTransform;
				if (_addLayerButton != null)
				{
					_addLayerButton.onClick.RemoveAllListeners();
					_addLayerButton.onClick.AddListener(OnAddLayer);
				}
				if (_collapseButton == null && _addLayerButton != null)
				{
					var row = _addLayerButton.transform.parent;
					if (row != null)
					{
						var collapseT = row.Find("CollapseBtn");
						_collapseButton = collapseT != null ? collapseT.GetComponent<Button>() : null;
					}
				}
				if (_collapseButton != null)
				{
					_collapseButton.onClick.RemoveAllListeners();
					_collapseButton.onClick.AddListener(OnCollapse);
				}
				RebuildList();
			}
		}

		static void RequestReRender()
		{
			if (Objects_Renderer_MGR.instance != null)
				Objects_Renderer_MGR.instance.ReRenderAll_soon();
		}

		void OnEnable()
		{
			SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
			SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
			// Theme may have changed while disabled — re-assert chrome (Collect may not run yet).
			ApplyThemeTokens();
		}

		void OnDestroy()
		{
			SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
			if (_layerStack != null)
			{
				_layerStack.OnLayersChanged -= RebuildList;
				_layerStack.OnActiveLayerChanged -= RefreshActiveHighlight;
			}
			if (_dragInsertIndicator != null) Destroy(_dragInsertIndicator);
		}

		public void ApplyThemeTokens()
		{
			if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
				ApplyAuthoredRowPalette();
				SpzUiThemeOps.RestoreBoundChromeUnder(transform);
				if (_listRoot != null && !ReferenceEquals(_listRoot, transform))
					SpzUiThemeOps.RestoreBoundChromeUnder(_listRoot);
				if (_addLayerButton != null)
					SpzUiThemeOps.RestoreBoundChromeUnder(_addLayerButton.transform);
				if (_collapseButton != null)
					SpzUiThemeOps.RestoreBoundChromeUnder(_collapseButton.transform);
				for (int i = 0; i < _rows.Count; i++) {
					var row = _rows[i];
					if (row == null) continue;
					SpzUiThemeOps.RestoreBoundChromeUnder(row.transform);
				}
				RefreshActiveHighlight();
				RefreshVisibilityColors();
				return;
			}
			var t = SpzUiThemeOps.Active;
			_visOn = t.accent; _visOn.a = 1f;
			_visOff = t.controlBg; _visOff.a = 0.95f;
			_rowDefault = t.panelBg; _rowDefault.a = 0.2f;
			_rowActive = t.selection; _rowActive.a = 0.45f;
			if (_addLayerButton != null) {
				SpzUiThemeOps.EnsureSelectableHitFace(_addLayerButton);
				SpzUiThemeOps.ApplyBoundChromeSelectable(_addLayerButton, t.success, t.accent);
				foreach (var tmp in _addLayerButton.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)) {
					if (tmp != null)
						SpzUiThemeOps.ApplyBoundChromeCompactToolLabelTmp(tmp, t.textPrimary, 11f);
				}
				SpzUiThemeOps.ClearNonFaceRaycastsForTheme(_addLayerButton);
			}
			if (_collapseButton != null) {
				SpzUiThemeOps.EnsureSelectableHitFace(_collapseButton);
				SpzUiThemeOps.ApplyBoundChromeSelectable(_collapseButton, t.controlBg, t.accent);
				foreach (var tmp in _collapseButton.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)) {
					if (tmp != null)
						SpzUiThemeOps.ApplyBoundChromeCompactToolLabelTmp(tmp, t.textPrimary, 11f);
				}
				SpzUiThemeOps.ClearNonFaceRaycastsForTheme(_collapseButton);
			}
			RefreshActiveHighlight();
			for (int i = 0; i < _rows.Count; i++)
			{
				var row = _rows[i];
				if (row == null) continue;
				var del = row.transform.Find("Delete");
				if (del == null) del = row.transform.Find("DeleteBtn");
				if (del != null)
				{
					var delBtn = del.GetComponent<Button>();
					if (delBtn != null) {
						SpzUiThemeOps.EnsureSelectableHitFace(delBtn);
						SpzUiThemeOps.ApplyBoundChromeSelectable(delBtn, t.danger, t.accent);
						foreach (var tmp in delBtn.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)) {
							if (tmp != null)
								SpzUiThemeOps.ApplyBoundChromeCompactToolLabelTmp(tmp, t.textPrimary, 11f);
						}
						SpzUiThemeOps.ClearNonFaceRaycastsForTheme(delBtn);
					}
					// Lead Trash — centered Monolith stamps Delete caption.
					SpzUiThemeOps.ApplyControlLineIconLeading(del, StudioLineIcon.Trash, 16f);
				}
				var vis = row.transform.Find("Visibility");
				if (vis != null) {
					var visBtn = vis.GetComponent<Button>();
					if (visBtn != null) {
						// Prefer authored Visibility Image as face — Ensure synthetic face + ClearNonFace
						// can bury the eye glyph and make layer hide/show feel dead under Nomad.
						if (visBtn.targetGraphic == null) {
							var authored = vis.GetComponent<Image>();
							if (authored != null)
								visBtn.targetGraphic = authored;
						}
						if (visBtn.targetGraphic == null)
							SpzUiThemeOps.EnsureSelectableHitFace(visBtn);
						else if (visBtn.targetGraphic != null)
							visBtn.targetGraphic.raycastTarget = true;
					}
				}
			}
			RefreshVisibilityColors();
		}

		void RefreshVisibilityColors()
		{
			if (_layerStack == null) return;
			for (int i = 0; i < _rows.Count; i++)
			{
				var row = _rows[i];
				if (row == null) continue;
				var vis = row.transform.Find("Visibility");
				if (vis == null) continue;
				var visBtn = vis.GetComponent<Button>();
				var visImg = (visBtn != null ? visBtn.targetGraphic as Image : null)
					?? vis.GetComponent<Image>();
				if (visImg == null) continue;
				int layerIx = LayerIndexFromDisplay(i);
				bool visible = layerIx >= 0 && layerIx < _layerStack.Layers.Count
					&& _layerStack.Layers[layerIx].Visible;
				visImg.color = visible ? _visOn : _visOff;
			}
		}

		void Start()
		{
			if (_layerStack == null) _layerStack = FindObjectOfType<PaintLayerStack_MGR>(true);
			if (_listRoot == null) _listRoot = transform as RectTransform;
			if (_layerStack == null) return;
			_layerStack.OnLayersChanged -= RebuildList;
			_layerStack.OnActiveLayerChanged -= RefreshActiveHighlight;
			_layerStack.OnLayersChanged += RebuildList;
			_layerStack.OnActiveLayerChanged += RefreshActiveHighlight;
			if (_addLayerButton != null)
			{
				_addLayerButton.onClick.RemoveAllListeners();
				_addLayerButton.onClick.AddListener(OnAddLayer);
			}
			if (_collapseButton == null && _addLayerButton != null)
			{
				var row = _addLayerButton.transform.parent;
				if (row != null)
				{
					var collapseT = row.Find("CollapseBtn");
					_collapseButton = collapseT != null ? collapseT.GetComponent<Button>() : null;
				}
			}
			if (_collapseButton != null)
			{
				_collapseButton.onClick.RemoveAllListeners();
				_collapseButton.onClick.AddListener(OnCollapse);
			}
			RebuildList();
		}

		void RefreshActiveHighlight()
		{
			if (_layerStack == null) return;
			for (int i = 0; i < _rows.Count; i++)
			{
				var row = _rows[i];
				if (row == null) continue;
				var rowBg = row.GetComponent<Image>();
				if (rowBg == null) continue;
				int layerIx = LayerIndexFromDisplay(i);
				bool isActive = layerIx >= 0 && _layerStack.ActiveLayerIndex == layerIx;
				rowBg.color = isActive ? _rowActive : _rowDefault;
			}
		}

		void Update()
		{
			if (_renameRowIndex < 0) return;
			if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
				CancelLayerRename(_renameRowIndex);
		}

		void BeginLayerRename(int index)
		{
			if (_layerStack == null || index < 0 || index >= _layerStack.Layers.Count) return;
			if (_renameRowIndex >= 0 && _renameRowIndex != index)
				CancelLayerRename(_renameRowIndex);

			_renameRowIndex = index;
			_layerStack.SetActiveLayer(index);

			int displayIx = DisplayIndexFromLayer(index);
			if (displayIx < 0 || displayIx >= _rows.Count || _rows[displayIx] == null) return;
			Transform nameRoot = _rows[displayIx].transform.Find("Name");
			if (nameRoot == null) return;
			Transform disp = nameRoot.Find("DisplayBlock");
			Transform edit = nameRoot.Find("EditBlock");
			if (disp != null) disp.gameObject.SetActive(false);
			if (edit != null)
			{
				edit.gameObject.SetActive(true);
				TMP_InputField input = edit.GetComponentInChildren<TMP_InputField>(true);
				if (input != null)
				{
					input.text = DisplayNameForLayer(_layerStack.Layers[index], index);
					input.ActivateInputField();
				}
			}
		}

		void OnRenameEndEditLostFocus(int index)
		{
			if (_suppressRenameEndEdit) return;
			if (_renameRowIndex != index) return;
			CancelLayerRename(index);
		}

		void CommitLayerRename(int index, string text)
		{
			if (_layerStack == null || index < 0 || index >= _layerStack.Layers.Count) return;
			_suppressRenameEndEdit = true;
			_renameRowIndex = -1;
			TMP_InputField input = FindRenameInput(index);
			if (input != null && input.isFocused)
				input.DeactivateInputField(false);

			_layerStack.SetLayerName(index, text);
			ExitRenameDisplayMode(index);
			_suppressRenameEndEdit = false;
		}

		void CancelLayerRename(int index)
		{
			_suppressRenameEndEdit = true;
			_renameRowIndex = -1;
			TMP_InputField input = FindRenameInput(index);
			if (input != null && input.isFocused)
				input.DeactivateInputField(false);
			ExitRenameDisplayMode(index);
			_suppressRenameEndEdit = false;
		}

		void ExitRenameDisplayMode(int index)
		{
			int displayIx = DisplayIndexFromLayer(index);
			if (displayIx < 0 || displayIx >= _rows.Count || _rows[displayIx] == null) return;
			Transform nameRoot = _rows[displayIx].transform.Find("Name");
			if (nameRoot == null) return;
			Transform disp = nameRoot.Find("DisplayBlock");
			Transform edit = nameRoot.Find("EditBlock");
			if (edit != null) edit.gameObject.SetActive(false);
			if (disp != null)
			{
				disp.gameObject.SetActive(true);
				var label = disp.GetComponentInChildren<TextMeshProUGUI>();
				if (label != null && _layerStack != null && index < _layerStack.Layers.Count)
					label.text = DisplayNameForLayer(_layerStack.Layers[index], index);
			}
		}

		string DisplayNameForLayer(PaintLayer layer, int index)
		{
			if (layer == null) return _layerStack != null ? _layerStack.DefaultLayerDisplayName(index) : "Layer " + (index + 1);
			if (!string.IsNullOrEmpty(layer.Name)) return layer.Name;
			return _layerStack != null ? _layerStack.DefaultLayerDisplayName(index) : "Layer " + (index + 1);
		}

		TMP_InputField FindRenameInput(int index)
		{
			int displayIx = DisplayIndexFromLayer(index);
			if (displayIx < 0 || displayIx >= _rows.Count || _rows[displayIx] == null) return null;
			Transform edit = _rows[displayIx].transform.Find("Name/EditBlock");
			return edit != null ? edit.GetComponentInChildren<TMP_InputField>(true) : null;
		}

		// --- Add Layer and list rebuild (UI calls stack; RebuildList repopulates rows from stack.Layers) ---
		void OnAddLayer()
		{
			_layerStack?.AddLayer();
		}

		void OnCollapse()
		{
			if (_layerStack == null)
			{
				UnityEngine.Debug.LogWarning("[PaintTab_LayersPanel_UI] Collapse: _layerStack is null.");
				return;
			}
			// Copy composite to a new layer only. No layer removal.
			bool didAdd = _layerStack.CollapseVisibleLayersIntoOne();
			if (didAdd)
				RebuildList();
			RequestReRender();
		}

		void RebuildList()
		{
			if (_layerStack == null || _listRoot == null) return;
			_renameRowIndex = -1;
			// Builtin keeps authored row palette; themed chrome may use Active tokens.
			if (SpzUiThemeOps.ShouldRecolorBoundChrome) {
				var snap = SpzUiThemeOps.Active;
				_visOn = snap.accent; _visOn.a = 1f;
				_visOff = snap.controlBg; _visOff.a = 0.95f;
				_rowDefault = snap.panelBg; _rowDefault.a = 0.2f;
				_rowActive = snap.selection; _rowActive.a = 0.45f;
			} else {
				ApplyAuthoredRowPalette();
			}
			foreach (var go in _rows)
			{
				if (go != null) DestroyImmediate(go);
			}
			_rows.Clear();

			var layers = _layerStack.Layers;
			for (int displayIx = 0; displayIx < layers.Count; displayIx++)
			{
				int layerIx = LayerIndexFromDisplay(displayIx);
				GameObject row = BuildRow(layers[layerIx], layerIx);
				if (row != null)
					_rows.Add(row);
			}

			LayoutRebuilder.ForceRebuildLayoutImmediate(_listRoot);
			ApplyThemeTokens();
		}

		// Visibility / row colors: authored SPZ defaults unless a non-builtin theme is active.
		static readonly Color kAuthoredVisOn = new Color(0.18f, 0.35f, 0.58f, 1f);
		static readonly Color kAuthoredVisOff = new Color(0.55f, 0.6f, 0.7f, 0.95f);
		static readonly Color kAuthoredRowDefault = new Color(0, 0, 0, 0.2f);
		static readonly Color kAuthoredRowActive = new Color(0.2f, 0.38f, 0.55f, 0.45f);

		void ApplyAuthoredRowPalette()
		{
			_visOn = kAuthoredVisOn;
			_visOff = kAuthoredVisOff;
			_rowDefault = kAuthoredRowDefault;
			_rowActive = kAuthoredRowActive;
		}

		Color _visOn = kAuthoredVisOn;
		Color _visOff = kAuthoredVisOff;
		Color _rowDefault = kAuthoredRowDefault;
		Color _rowActive = kAuthoredRowActive;

		// --- Drag-to-reorder: grip handle on each row drives MoveLayer ---

		internal void OnRowBeginDrag(int fromIndex)
		{
			if (_layerStack == null || fromIndex < 0 || fromIndex >= _layerStack.Layers.Count) return;
			if (_renameRowIndex >= 0) CancelLayerRename(_renameRowIndex);
			_dragFromIndex = fromIndex;
			_dragInsertIndex = fromIndex;
			EnsureDragInsertIndicator();
		}

		internal void OnRowDrag(int fromIndex, PointerEventData eventData)
		{
			if (_dragFromIndex < 0 || _listRoot == null || _layerStack == null) return;
			RectTransformUtility.ScreenPointToLocalPointInRectangle(_listRoot, eventData.position, eventData.pressEventCamera, out Vector2 localPos);
			int insertIdx = CalcInsertLayerIndex(localPos);
			if (insertIdx != _dragInsertIndex)
			{
				_dragInsertIndex = insertIdx;
				PositionInsertIndicator(insertIdx);
			}
		}

		internal void OnRowEndDrag(int fromIndex)
		{
			if (_dragFromIndex < 0 || _layerStack == null) { HideInsertIndicator(); _dragFromIndex = -1; return; }
			HideInsertIndicator();
			int to = _dragInsertIndex;
			_dragFromIndex = -1;
			_dragInsertIndex = -1;
			if (to < 0 || to >= _layerStack.Layers.Count || to == fromIndex) return;
			_layerStack.MoveLayer(fromIndex, to);
			// MoveLayer schedules ReRender; also push layer composite to accumulation now so img2img matches new order if Generate runs before next OnUpdate.
			if (Objects_Renderer_MGR.instance != null)
				Objects_Renderer_MGR.instance.EnsureInpaintColorLayerAppliedForCapture();
		}

		int CalcInsertLayerIndex(Vector2 localPos)
		{
			if (_rows.Count == 0 || _layerStack == null) return 0;
			float spacing = 2f;
			float totalH = _rowHeight + spacing;
			int displayIdx = Mathf.FloorToInt((-localPos.y) / totalH);
			displayIdx = Mathf.Clamp(displayIdx, 0, _layerStack.Layers.Count - 1);
			return LayerIndexFromDisplay(displayIdx);
		}

		void EnsureDragInsertIndicator()
		{
			if (_dragInsertIndicator != null) { _dragInsertIndicator.SetActive(true); return; }
			_dragInsertIndicator = new GameObject("DragInsertIndicator");
			_dragInsertIndicator.transform.SetParent(_listRoot, false);
			var rect = _dragInsertIndicator.AddComponent<RectTransform>();
			rect.anchorMin = new Vector2(0, 1);
			rect.anchorMax = new Vector2(1, 1);
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.sizeDelta = new Vector2(0, 2);
			var img = _dragInsertIndicator.AddComponent<Image>();
			img.color = new Color(0.3f, 0.7f, 1f, 0.9f);
			img.raycastTarget = false;
			var ign = _dragInsertIndicator.AddComponent<LayoutElement>();
			ign.ignoreLayout = true;
		}

		void PositionInsertIndicator(int layerIndex)
		{
			if (_dragInsertIndicator == null || _listRoot == null) return;
			_dragInsertIndicator.SetActive(true);
			var rect = _dragInsertIndicator.GetComponent<RectTransform>();
			float spacing = 2f;
			int displayIdx = DisplayIndexFromLayer(layerIndex);
			float y = -(displayIdx * (_rowHeight + spacing)) - _rowHeight * 0.5f;
			rect.anchoredPosition = new Vector2(0, y);
		}

		int LayerIndexFromDisplay(int displayIndex)
		{
			if (_layerStack == null) return displayIndex;
			int n = _layerStack.Layers.Count;
			return n - 1 - displayIndex;
		}

		int DisplayIndexFromLayer(int layerIndex)
		{
			if (_layerStack == null) return layerIndex;
			int n = _layerStack.Layers.Count;
			return n - 1 - layerIndex;
		}

		void HideInsertIndicator()
		{
			if (_dragInsertIndicator != null) _dragInsertIndicator.SetActive(false);
		}

		// --- Build one row: row bg = SetActiveLayer; eye = select + SetLayerVisible; name = solid label, click → edit (Enter commit, blur/Escape cancel); Delete ---
		/// <summary>One row: click row background for active layer; eye toggles visibility and selects; click name to rename (Enter confirms); Delete removes.</summary>
		GameObject BuildRow(PaintLayer layer, int index)
		{
			GameObject row = new GameObject("LayerRow_" + index);
			row.transform.SetParent(_listRoot, false);

			var rect = row.AddComponent<RectTransform>();
			rect.sizeDelta = new Vector2(0, _rowHeight);
			var le = row.AddComponent<LayoutElement>();
			le.preferredHeight = _rowHeight;
			le.flexibleWidth = 1;
			var h = row.AddComponent<HorizontalLayoutGroup>();
			h.spacing = 4;
			h.childAlignment = TextAnchor.MiddleLeft;
			h.childControlWidth = false;
			h.childControlHeight = true;
			h.childForceExpandWidth = false;

			bool isActive = _layerStack != null && _layerStack.ActiveLayerIndex == index;
			var rowBg = row.AddComponent<Image>();
			rowBg.color = isActive ? _rowActive : _rowDefault;
			rowBg.raycastTarget = true;
			var rowBtn = row.AddComponent<Button>();
			rowBtn.targetGraphic = rowBg;
			rowBtn.transition = Selectable.Transition.None;
			int idx = index;
			rowBtn.onClick.AddListener(() =>
			{
				if (_layerStack == null || idx < 0 || idx >= _layerStack.Layers.Count) return;
				_layerStack.SetActiveLayer(idx);
				RequestReRender();
			});

			// Drag handle (grip area) — left of the eye; drag to reorder
			var gripGo = new GameObject("DragGrip");
			gripGo.transform.SetParent(row.transform, false);
			gripGo.AddComponent<RectTransform>().sizeDelta = new Vector2(16, _rowHeight - 4);
			var gripLE = gripGo.AddComponent<LayoutElement>();
			gripLE.minWidth = 16;
			gripLE.preferredWidth = 16;
			var gripImg = gripGo.AddComponent<Image>();
			gripImg.color = new Color(0.45f, 0.45f, 0.5f, 0.6f);
			gripImg.raycastTarget = true;
			var gripLabelGo = new GameObject("GripLabel");
			gripLabelGo.transform.SetParent(gripGo.transform, false);
			var gripLabelRect = gripLabelGo.AddComponent<RectTransform>();
			gripLabelRect.anchorMin = Vector2.zero;
			gripLabelRect.anchorMax = Vector2.one;
			gripLabelRect.sizeDelta = Vector2.zero;
			var gripTmp = gripLabelGo.AddComponent<TextMeshProUGUI>();
			gripTmp.text = "\u2261";
			gripTmp.fontSize = 14;
			gripTmp.color = new Color(1f, 1f, 1f, 0.7f);
			gripTmp.alignment = TextAlignmentOptions.Center;
			gripTmp.raycastTarget = false;
			var dragHandler = gripGo.AddComponent<LayerRowDragHandler>();
			dragHandler.Init(this, idx);

			// Visibility toggle button — dark blue when visible, light when hidden
			var visGo = new GameObject("Visibility");
			visGo.transform.SetParent(row.transform, false);
			visGo.AddComponent<RectTransform>().sizeDelta = new Vector2(24, _rowHeight - 4);
			var visLE = visGo.AddComponent<LayoutElement>();
			visLE.minWidth = 24;
			visLE.preferredWidth = 24;
			var visImg = visGo.AddComponent<Image>();
			visImg.color = layer.Visible ? _visOn : _visOff;
			visImg.raycastTarget = true;
			var visBtn = visGo.AddComponent<Button>();
			visBtn.targetGraphic = visImg;
			visBtn.transition = Selectable.Transition.None;
			visBtn.onClick.AddListener(() =>
			{
				if (_layerStack == null || idx < 0 || idx >= _layerStack.Layers.Count) return;
				_layerStack.SetActiveLayer(idx);
				bool newVisible = !_layerStack.Layers[idx].Visible;
				_layerStack.SetLayerVisible(idx, newVisible);
				visImg.color = newVisible ? _visOn : _visOff;
				RequestReRender();
			});

			// Layer name: DisplayBlock = solid label (like legacy Text); EditBlock = TMP_InputField after click. Enter = commit; blur/Escape = cancel.
			var nameGo = new GameObject("Name");
			nameGo.transform.SetParent(row.transform, false);
			nameGo.AddComponent<RectTransform>().sizeDelta = new Vector2(120, _rowHeight - 4);
			var nameLE = nameGo.AddComponent<LayoutElement>();
			nameLE.flexibleWidth = 1;
			nameLE.minWidth = 40;

			void StretchToParent(RectTransform r)
			{
				r.anchorMin = Vector2.zero;
				r.anchorMax = Vector2.one;
				r.sizeDelta = Vector2.zero;
				r.offsetMin = Vector2.zero;
				r.offsetMax = Vector2.zero;
			}

			var displayBlock = new GameObject("DisplayBlock");
			displayBlock.transform.SetParent(nameGo.transform, false);
			StretchToParent(displayBlock.AddComponent<RectTransform>());
			var dispImg = displayBlock.AddComponent<Image>();
			dispImg.color = new Color(0, 0, 0, 0.12f);
			dispImg.raycastTarget = true;
			var dispBtn = displayBlock.AddComponent<Button>();
			dispBtn.targetGraphic = dispImg;
			dispBtn.transition = Selectable.Transition.None;
			int idxCapture = idx;
			dispBtn.onClick.AddListener(() => BeginLayerRename(idxCapture));

			var labelGo = new GameObject("Label");
			labelGo.transform.SetParent(displayBlock.transform, false);
			StretchToParent(labelGo.AddComponent<RectTransform>());
			var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
			labelTmp.text = DisplayNameForLayer(layer, idx);
			labelTmp.fontSize = 12;
			labelTmp.color = Color.white;
			labelTmp.raycastTarget = false;
			labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
			labelTmp.margin = new Vector4(6, 0, 4, 0);

			var editBlock = new GameObject("EditBlock");
			editBlock.transform.SetParent(nameGo.transform, false);
			StretchToParent(editBlock.AddComponent<RectTransform>());
			editBlock.SetActive(false);

			var inputRoot = new GameObject("InputRoot");
			inputRoot.transform.SetParent(editBlock.transform, false);
			StretchToParent(inputRoot.AddComponent<RectTransform>());
			var editBg = inputRoot.AddComponent<Image>();
			editBg.color = new Color(0.12f, 0.12f, 0.14f, 0.92f);
			editBg.raycastTarget = true;

			var textArea = new GameObject("Text Area");
			textArea.transform.SetParent(inputRoot.transform, false);
			var textAreaRect = textArea.AddComponent<RectTransform>();
			textAreaRect.anchorMin = Vector2.zero;
			textAreaRect.anchorMax = Vector2.one;
			textAreaRect.offsetMin = new Vector2(4, 2);
			textAreaRect.offsetMax = new Vector2(-4, -2);
			textArea.AddComponent<RectMask2D>();

			var placeholderGo = new GameObject("Placeholder");
			placeholderGo.transform.SetParent(textArea.transform, false);
			StretchToParent(placeholderGo.AddComponent<RectTransform>());
			var placeholderTmp = placeholderGo.AddComponent<TextMeshProUGUI>();
			placeholderTmp.text = _layerStack != null ? _layerStack.DefaultLayerDisplayName(idx) : "Layer " + (idx + 1);
			placeholderTmp.fontSize = 11;
			placeholderTmp.color = new Color(1f, 1f, 1f, 0.35f);
			placeholderTmp.raycastTarget = false;

			var textGo = new GameObject("Text");
			textGo.transform.SetParent(textArea.transform, false);
			StretchToParent(textGo.AddComponent<RectTransform>());
			var editTmp = textGo.AddComponent<TextMeshProUGUI>();
			editTmp.text = DisplayNameForLayer(layer, idx);
			editTmp.fontSize = 12;
			editTmp.color = Color.white;
			editTmp.raycastTarget = false;

			var nameInput = inputRoot.AddComponent<TMP_InputField>();
			nameInput.textViewport = textAreaRect;
			nameInput.textComponent = editTmp;
			nameInput.placeholder = placeholderTmp;
			nameInput.targetGraphic = editBg;
			nameInput.lineType = TMP_InputField.LineType.SingleLine;
			nameInput.characterLimit = 128;
			nameInput.onFocusSelectAll = true;
			nameInput.text = editTmp.text;
			nameInput.onSubmit.AddListener(s => CommitLayerRename(idxCapture, s));
			nameInput.onEndEdit.AddListener(_ => OnRenameEndEditLostFocus(idxCapture));

			// Red Delete button
			var deleteGo = new GameObject("Delete");
			deleteGo.transform.SetParent(row.transform, false);
			deleteGo.AddComponent<RectTransform>().sizeDelta = new Vector2(28, _rowHeight - 4);
			var delLE = deleteGo.AddComponent<LayoutElement>();
			delLE.minWidth = 28;
			delLE.preferredWidth = 28;
			var delImg = deleteGo.AddComponent<Image>();
			delImg.color = new Color(0.65f, 0.2f, 0.2f, 0.95f);
			delImg.raycastTarget = true;
			var deleteBtn = deleteGo.AddComponent<Button>();
			deleteBtn.targetGraphic = delImg;
			deleteBtn.transition = Selectable.Transition.None;
			deleteBtn.onClick.AddListener(() =>
			{
				if (_layerStack == null || idx < 0 || idx >= _layerStack.Layers.Count) return;
				_layerStack.RemoveLayer(idx);
				RequestReRender();
			});
			if (SpzUiThemeOps.ShouldRecolorBoundChrome)
				SpzUiThemeOps.ApplyControlLineIconLeading(deleteGo.transform, StudioLineIcon.Trash, 16f);

			return row;
		}
	}

	/// <summary>Attached to the grip handle of each layer row. Forwards drag events to PaintTab_LayersPanel_UI for reorder.</summary>
	internal class LayerRowDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
	{
		PaintTab_LayersPanel_UI _panel;
		int _index;

		public void Init(PaintTab_LayersPanel_UI panel, int index) { _panel = panel; _index = index; }
		public void OnBeginDrag(PointerEventData e) { _panel?.OnRowBeginDrag(_index); }
		public void OnDrag(PointerEventData e) { _panel?.OnRowDrag(_index, e); }
		public void OnEndDrag(PointerEventData e) { _panel?.OnRowEndDrag(_index); }
	}
}

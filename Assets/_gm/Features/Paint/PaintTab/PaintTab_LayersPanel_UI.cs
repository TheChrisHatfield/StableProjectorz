using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace spz {

	// =============================================================================
	// LAYER SYSTEM - UI ONLY (layer list panel; no layer data lives here)
	// =============================================================================
	// This script is the Paint Tab layer list UI: rows (one per layer), Add Layer
	// button, visibility eye, delete. It does NOT hold layer data. It holds a
	// reference to PaintLayerStack_MGR and calls AddLayer(), SetActiveLayer(idx),
	// SetLayerVisible(idx), RemoveLayer(idx). RebuildList() repopulates rows from
	// stack.Layers. Scene injection and paint target are handled by Inpaint_MaskPainter.
	// =============================================================================

	/// <summary>
	/// Layer panel: Add Layer, visibility toggle per row, Delete per row. Click row to set active (if wired).
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
				_layerStack.OnActiveLayerChanged -= RebuildList;
			}
			_layerStack = stack;
			if (_layerStack != null)
			{
				_layerStack.OnLayersChanged += RebuildList;
				_layerStack.OnActiveLayerChanged += RebuildList;
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

		void Start()
		{
			if (_layerStack == null) _layerStack = FindObjectOfType<PaintLayerStack_MGR>();
			if (_listRoot == null) _listRoot = transform as RectTransform;
			if (_layerStack == null) return;
			_layerStack.OnLayersChanged -= RebuildList;
			_layerStack.OnActiveLayerChanged -= RebuildList;
			_layerStack.OnLayersChanged += RebuildList;
			_layerStack.OnActiveLayerChanged += RebuildList;
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

		void OnDestroy()
		{
			if (_layerStack != null)
			{
				_layerStack.OnLayersChanged -= RebuildList;
				_layerStack.OnActiveLayerChanged -= RebuildList;
			}
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
			foreach (var go in _rows)
			{
				if (go != null) Destroy(go);
			}
			_rows.Clear();

			var layers = _layerStack.Layers;
			for (int i = 0; i < layers.Count; i++)
			{
				GameObject row = BuildRow(layers[i], i);
				if (row != null)
					_rows.Add(row);
			}

			LayoutRebuilder.ForceRebuildLayoutImmediate(_listRoot);
		}

		// Visibility button colors: discernible on/off
		static readonly Color VisibilityOnColor  = new Color(0.18f, 0.35f, 0.58f, 1f);   // dark blue when visible
		static readonly Color VisibilityOffColor = new Color(0.55f, 0.6f, 0.7f, 0.95f); // light when hidden
		static readonly Color RowBgDefault      = new Color(0, 0, 0, 0.2f);
		static readonly Color RowBgActive       = new Color(0.2f, 0.38f, 0.55f, 0.45f);  // blue tint so user sees which layer is active

		// --- Build one row: click row = SetActiveLayer, eye = SetLayerVisible, red button = RemoveLayer ---
		/// <summary>One row: click row to set active layer; visibility toggle; layer name; Delete. Active row has blue tint.</summary>
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
			rowBg.color = isActive ? RowBgActive : RowBgDefault;
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

			// Visibility toggle button — dark blue when visible, light when hidden
			var visGo = new GameObject("Visibility");
			visGo.transform.SetParent(row.transform, false);
			visGo.AddComponent<RectTransform>().sizeDelta = new Vector2(24, _rowHeight - 4);
			var visLE = visGo.AddComponent<LayoutElement>();
			visLE.minWidth = 24;
			visLE.preferredWidth = 24;
			var visImg = visGo.AddComponent<Image>();
			visImg.color = layer.Visible ? VisibilityOnColor : VisibilityOffColor;
			visImg.raycastTarget = true;
			var visBtn = visGo.AddComponent<Button>();
			visBtn.targetGraphic = visImg;
			visBtn.transition = Selectable.Transition.None;
			visBtn.onClick.AddListener(() =>
			{
				if (_layerStack == null || idx < 0 || idx >= _layerStack.Layers.Count) return;
				bool newVisible = !_layerStack.Layers[idx].Visible;
				_layerStack.SetLayerVisible(idx, newVisible);
				visImg.color = newVisible ? VisibilityOnColor : VisibilityOffColor;
				RequestReRender();
			});

			// Label: layer name (read-only, no raycast)
			var nameGo = new GameObject("Name");
			nameGo.transform.SetParent(row.transform, false);
			var nameRect = nameGo.AddComponent<RectTransform>();
			nameRect.sizeDelta = new Vector2(120, _rowHeight - 4);
			var nameLE = nameGo.AddComponent<LayoutElement>();
			nameLE.flexibleWidth = 1;
			nameLE.minWidth = 40;
			var nameText = nameGo.AddComponent<Text>();
			nameText.text = layer.Name;
			nameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			nameText.fontSize = 12;
			nameText.color = Color.white;
			nameText.raycastTarget = false;

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

			return row;
		}
	}
}

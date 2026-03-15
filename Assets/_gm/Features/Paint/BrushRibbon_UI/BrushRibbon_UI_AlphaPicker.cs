using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SimpleFileBrowser;

namespace spz {

	/// <summary>
	/// UI to pick a custom brush alpha from the BrushAlphas folder. Shows a grid of thumbnails;
	/// click to select. Optional "Round brushes", "Refresh", and "Load ABR/PNG..." buttons.
	/// </summary>
	public class BrushRibbon_UI_AlphaPicker : MonoBehaviour
	{
		[SerializeField] BrushAlphas_MGR _brushAlphasMGR;
		[SerializeField] BrushRibbon_UI_Hardness _hardness;
		[Space(10)]
		[SerializeField] Transform _gridRoot;
		[SerializeField] GameObject _thumbnailTemplate;
		[SerializeField] Button _roundBrushesButton;
		[SerializeField] Button _refreshButton;
		[Tooltip("Optional. Opens file dialog to load ABR, PNG, or TGA from anywhere.")]
		[SerializeField] Button _loadBrushFileButton;
		[Tooltip("Optional. Removes the currently selected custom brush from the preset list.")]
		[SerializeField] Button _deleteBrushButton;
		[SerializeField] int _thumbnailSize = 32;
		[Tooltip("Optional. Shows the selected brush name (and optionally attributes from ABR).")]
		[SerializeField] TMPro.TextMeshProUGUI _selectedBrushNameLabel;
		[Tooltip("Optional. Shows e.g. 'Size: 32 | Spacing: 25%' for the selected brush.")]
		[SerializeField] TMPro.TextMeshProUGUI _selectedBrushAttrsLabel;

		/// <summary> Assign the brush manager at runtime (e.g. when created by PaintTab_CollectPaintUI). Ensures default brushes appear. </summary>
		public void SetBrushAlphasMGR(BrushAlphas_MGR mgr)
		{
			_brushAlphasMGR = mgr;
		}

		readonly List<GameObject> _thumbInstances = new List<GameObject>();
		/// <summary> Global brush index per thumbnail (same order as _thumbInstances). -1 for non-brush items (e.g. placeholder). </summary>
		readonly List<int> _thumbInstanceGlobalIndices = new List<int>();

		// Layout: scroll content VLG provides the only edge offset; picker root and sections use 0 padding for flush layout.
		const int kRootPaddingLeft = 0;
		const int kRootPaddingRight = 0;
		const int kRootPaddingTopBottom = 0;
		/// <summary> Space above the first section's dropdown arrow (chevron row). 0.75 + 0.25 = 1 unit (8px) so users have room and don't over-click thumbnail/icon. Single source of truth: PaintTab_CollectPaintUI must use this. </summary>
		public const int PickerTopSpacingPx = 8;
		const int kSpacingFromTopOfDropdownArrowPx = PickerTopSpacingPx;
		const int kThumbGridSpacing = 2; // Photoshop-style compact grid
		/// <summary> Vertical space between brush groups (Built-in, Custom, each ABR). Stacks dynamically so opening one group doesn't overlap the next; scroll to see all. </summary>
		const int kSectionSpacingPx = 8;
		/// <summary> 0 = full thumbnail clickable (Photoshop-style); prevents "can't select another" when inset was too aggressive. </summary>
		const int kThumbHitInset = 0;

		/// <summary> Bottom padding for scroll content so when scrolled to end the last row of thumbnails is visible. Adapts to thumbnail size and grid spacing. </summary>
		int GetScrollBottomPaddingPx()
		{
			return _thumbnailSize + kThumbGridSpacing + 4; // one row height + small buffer
		}

		void Start()
		{
			if (_brushAlphasMGR == null) _brushAlphasMGR = BrushAlphas_MGR.instance;
			if (_brushAlphasMGR == null) _brushAlphasMGR = FindObjectOfType<BrushAlphas_MGR>(true);

			if (_hardness == null) _hardness = FindObjectOfType<BrushRibbon_UI_Hardness>(true);

			if (_gridRoot == null) _gridRoot = transform;
			if (_thumbnailTemplate == null) _thumbnailTemplate = CreateDefaultThumbnailTemplate();
			if (_thumbnailTemplate != null)
				_thumbnailTemplate.SetActive(false);
			if (_roundBrushesButton != null)
				_roundBrushesButton.onClick.AddListener(OnRoundBrushesClicked);
			if (_refreshButton != null)
				_refreshButton.onClick.AddListener(OnRefreshClicked);
			if (_loadBrushFileButton != null)
				_loadBrushFileButton.onClick.AddListener(OnLoadBrushFileClicked);
			if (_deleteBrushButton != null)
				_deleteBrushButton.onClick.AddListener(() => OnDeleteBrushClicked(false));
			BrushRibbon_UI_Size.OnBrushSizeChanged += RefreshSelectedBrushLabelsFromBrushSystem;
			BrushRibbon_UI_Size.OnBrushSettingsChanged += RefreshSelectedBrushLabelsFromBrushSystem;
			RebuildGrid();
		}

		void OnEnable()
		{
			// Re-apply compact layout when brush panel is shown (fixes layout not updating after build)
			if (_gridRoot == null) _gridRoot = transform;
			if (_thumbnailTemplate == null && _gridRoot != null) _thumbnailTemplate = CreateDefaultThumbnailTemplate();
			if (_thumbnailTemplate != null) _thumbnailTemplate.SetActive(false);
			if (_gridRoot != null)
			{
				EnsureVerticalLayoutOnGridRoot();
				RebuildGrid();
			}
			// Force scroll container (our parent) to use compact padding + adaptive bottom so last brush thumbnail is visible when scrolled to end
			const int kEdgePad = 2;
			var parent = transform.parent;
			if (parent != null)
			{
				var parentVlg = parent.GetComponent<VerticalLayoutGroup>();
				if (parentVlg != null)
				{
					parentVlg.padding = new RectOffset(kEdgePad, kEdgePad, kEdgePad, kEdgePad + GetScrollBottomPaddingPx());
					parentVlg.spacing = PickerTopSpacingPx; // gap between button row and dropdown row (must not be 1 or gap disappears)
					parentVlg.childAlignment = TextAnchor.UpperLeft;
				}
				var parentRect = parent as RectTransform;
				if (parentRect != null)
					LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
			}
		}

		void OnDestroy()
		{
			BrushRibbon_UI_Size.OnBrushSizeChanged -= RefreshSelectedBrushLabelsFromBrushSystem;
			BrushRibbon_UI_Size.OnBrushSettingsChanged -= RefreshSelectedBrushLabelsFromBrushSystem;
		}

		/// <summary> Keep brush preset "eyes" (selected brush label) in sync with actual brush system size when user changes the size slider. </summary>
		void RefreshSelectedBrushLabelsFromBrushSystem()
		{
			if (_brushAlphasMGR != null)
				UpdateSelectedBrushLabels(_brushAlphasMGR.CurrentIndex);
		}

		GameObject CreateDefaultThumbnailTemplate()
		{
			var go = new GameObject("ThumbTemplate");
			go.transform.SetParent(transform, false);
			var rect = go.AddComponent<RectTransform>();
			rect.sizeDelta = new Vector2(_thumbnailSize, _thumbnailSize);
			var img = go.AddComponent<UnityEngine.UI.Image>();
			img.color = new Color(0.3f, 0.3f, 0.3f, 1f);
			img.raycastTarget = false; // only the inner hit area receives clicks for precise selection
			// Hit area: inset so clicks don't overlap adjacent thumbnails; only this region is clickable
			var hitAreaGo = new GameObject("HitArea");
			hitAreaGo.transform.SetParent(go.transform, false);
			var hitRect = hitAreaGo.AddComponent<RectTransform>();
			hitRect.anchorMin = Vector2.zero;
			hitRect.anchorMax = Vector2.one;
			hitRect.offsetMin = new Vector2(kThumbHitInset, kThumbHitInset);
			hitRect.offsetMax = new Vector2(-kThumbHitInset, -kThumbHitInset);
			var hitImg = hitAreaGo.AddComponent<UnityEngine.UI.Image>();
			hitImg.color = new Color(0, 0, 0, 0);
			hitImg.raycastTarget = true;
			var btn = hitAreaGo.AddComponent<Button>();
			btn.targetGraphic = hitImg;
			var rawGo = new GameObject("RawImage");
			rawGo.transform.SetParent(go.transform, false);
			var rawRect = rawGo.AddComponent<RectTransform>();
			rawRect.anchorMin = Vector2.zero;
			rawRect.anchorMax = Vector2.one;
			rawRect.offsetMin = new Vector2(2, 2);
			rawRect.offsetMax = new Vector2(-2, -2);
			var raw = rawGo.AddComponent<RawImage>();
			raw.raycastTarget = false;
			go.SetActive(false);
			return go;
		}

		/// <summary> Ensures the thumbnail has an inset hit area so only the inner region is clickable (implemented at runtime for both default and prefab templates). </summary>
		void EnsureThumbnailHitArea(GameObject thumbGo)
		{
			if (thumbGo == null) return;
			var hitArea = thumbGo.transform.Find("HitArea");
			if (hitArea != null)
			{
				var hitRect = hitArea.GetComponent<RectTransform>();
				if (hitRect != null)
				{
					hitRect.offsetMin = new Vector2(kThumbHitInset, kThumbHitInset);
					hitRect.offsetMax = new Vector2(-kThumbHitInset, -kThumbHitInset);
				}
				if (hitArea.GetComponent<Button>() != null) return;
				var hitImg = hitArea.GetComponent<UnityEngine.UI.Image>();
				if (hitImg != null) hitImg.raycastTarget = true;
				hitArea.gameObject.AddComponent<Button>().targetGraphic = hitImg;
				return;
			}
			// No HitArea: add one so precise click is implemented even when template came from prefab
			var rootImg = thumbGo.GetComponent<UnityEngine.UI.Image>();
			if (rootImg != null) rootImg.raycastTarget = false;
			foreach (var raw in thumbGo.GetComponentsInChildren<RawImage>(true))
				raw.raycastTarget = false;
			var rootBtn = thumbGo.GetComponent<Button>();
			if (rootBtn != null) { rootBtn.onClick.RemoveAllListeners(); rootBtn.enabled = false; }
			var hitAreaGo = new GameObject("HitArea");
			hitAreaGo.transform.SetParent(thumbGo.transform, false);
			var haRect = hitAreaGo.AddComponent<RectTransform>();
			haRect.anchorMin = Vector2.zero;
			haRect.anchorMax = Vector2.one;
			haRect.offsetMin = new Vector2(kThumbHitInset, kThumbHitInset);
			haRect.offsetMax = new Vector2(-kThumbHitInset, -kThumbHitInset);
			var haImg = hitAreaGo.AddComponent<UnityEngine.UI.Image>();
			haImg.color = new Color(0, 0, 0, 0);
			haImg.raycastTarget = true;
			hitAreaGo.AddComponent<Button>().targetGraphic = haImg;
		}

		/// <summary> Call to open the file dialog for loading ABR, PNG, or TGA brush files (e.g. from a runtime-created button). </summary>
		public void OpenLoadBrushDialog()
		{
			OnLoadBrushFileClicked();
		}

		void OnLoadBrushFileClicked()
		{
			var mgr = _brushAlphasMGR ?? BrushAlphas_MGR.instance ?? FindObjectOfType<BrushAlphas_MGR>(true);
			if (mgr == null) return;
			FileBrowser.SetFilters(true,
				new FileBrowser.Filter("Brush / Alpha", "abr", "png", "tga"),
				new FileBrowser.Filter("ABR (Photoshop brush)", "abr"),
				new FileBrowser.Filter("Images", "png", "tga"));
			FileBrowser.SetDefaultFilter("abr");
			FileBrowser.ShowLoadDialog(
				(paths) => {
					if (paths == null || paths.Length == 0) return;
					bool ok = mgr.LoadFromExternalPath(paths[0]);
					_brushAlphasMGR = mgr;
					RebuildGrid();
					if (Viewport_StatusText.instance != null)
					{
						int countAfter = mgr.AllEntries.Count;
						if (ok && countAfter > 0)
							Viewport_StatusText.instance.ShowStatusText("Brush file loaded. Select from the grid.", false, 2f, false);
						else if (ok && countAfter == 0)
							Viewport_StatusText.instance.ShowStatusText("File copied but no brush tips could be read. Try PNG or another ABR.", false, 3f, false);
					}
					if (ok && mgr.AllEntries.Count > 3)
						SelectBrushAtIndex(3);
				},
				null,
				FileBrowser.PickMode.Files,
				false,
				null,
				null,
				"Load brush (ABR / PNG / TGA)",
				"Load");
		}

		void OnRoundBrushesClicked()
		{
			SelectBrushAtIndex(0);
		}

		void OnRefreshClicked()
		{
			RefreshFromFolder();
		}

		void OnDeleteBrushClicked(bool deleteFilePermanently)
		{
			if (_brushAlphasMGR == null) return;
			int idx = _brushAlphasMGR.CurrentIndex;
			if (!_brushAlphasMGR.IsCustomAlpha(idx))
			{
				if (Viewport_StatusText.instance != null)
					Viewport_StatusText.instance.ShowStatusText("Built-in brushes cannot be deleted.", false, 2f, false);
				return;
			}
			if (_brushAlphasMGR.RemoveCustomBrushAt(idx, deleteFilePermanently))
			{
				RebuildGrid();
				if (Viewport_StatusText.instance != null)
					Viewport_StatusText.instance.ShowStatusText(
						deleteFilePermanently ? "Brush file deleted and removed from presets." : "Brush removed from presets.",
						false, 2f, false);
			}
		}

		/// <summary> Remove the currently selected custom brush from presets (session only). Call from a runtime Delete button. </summary>
		public void DeleteSelectedBrush() => OnDeleteBrushClicked(false);

		/// <summary> Permanently delete the brush file from disk and remove from presets. For ABR, deletes the file and removes all brushes from that ABR. </summary>
		public void DeleteSelectedBrushPermanently() => OnDeleteBrushClicked(true);

		/// <summary> Rebuild layout bottom-up after a section expand/collapse so scroll content height updates and the next group shifts down (responsive; no overlap). </summary>
		void RefreshBrushPresetsLayoutAfterExpandCollapse()
		{
			if (_gridRoot == null) return;
			// 1) Rebuild each grid so collapsed grids have 0 height and expanded grids have content height
			for (int i = 0; i < _gridRoot.childCount; i++)
			{
				var section = _gridRoot.GetChild(i);
				for (int j = 0; j < section.childCount; j++)
				{
					var ch = section.GetChild(j);
					if (ch.name.StartsWith("Grid_"))
					{
						var gridRect = ch as RectTransform;
						if (gridRect != null)
							LayoutRebuilder.ForceRebuildLayoutImmediate(gridRect);
						break;
					}
				}
			}
			// 2) Rebuild each section so section height = header + grid (pushes following sections down)
			for (int i = 0; i < _gridRoot.childCount; i++)
			{
				var section = _gridRoot.GetChild(i);
				var sectionRect = section as RectTransform;
				if (sectionRect != null)
					LayoutRebuilder.ForceRebuildLayoutImmediate(sectionRect);
			}
			// 3) Rebuild root so total height = sum of sections + spacing
			var rootRect = _gridRoot as RectTransform;
			if (rootRect != null)
				LayoutRebuilder.ForceRebuildLayoutImmediate(rootRect);
			// 4) Rebuild scroll content so ScrollRect gets correct content height and can scroll
			var scrollRect = _gridRoot.GetComponentInParent<ScrollRect>();
			var scrollContent = scrollRect != null ? scrollRect.content : (_gridRoot.parent as RectTransform);
			if (scrollContent != null)
			{
				LayoutRebuilder.ForceRebuildLayoutImmediate(scrollContent);
				var scrollVlg = scrollContent.GetComponent<VerticalLayoutGroup>();
				if (scrollVlg != null)
				{
					int bottom = 2 + GetScrollBottomPaddingPx();
					scrollVlg.padding = new RectOffset(scrollVlg.padding.left, scrollVlg.padding.right, scrollVlg.padding.top, bottom);
				}
			}
			Canvas.ForceUpdateCanvases();
			StartCoroutine(RefreshBrushPresetsLayoutDelayed());
		}

		System.Collections.IEnumerator RefreshBrushPresetsLayoutDelayed()
		{
			yield return null;
			if (_gridRoot == null) yield break;
			// Delayed pass so ContentSizeFitter (grid root + scroll content) has correct preferred size; prevents overlap
			for (int i = 0; i < _gridRoot.childCount; i++)
			{
				var section = _gridRoot.GetChild(i);
				for (int j = 0; j < section.childCount; j++)
				{
					var ch = section.GetChild(j);
					if (ch.name.StartsWith("Grid_"))
					{
						var gridRect = ch as RectTransform;
						if (gridRect != null)
							LayoutRebuilder.ForceRebuildLayoutImmediate(gridRect);
						break;
					}
				}
			}
			for (int i = 0; i < _gridRoot.childCount; i++)
			{
				var section = _gridRoot.GetChild(i);
				var sectionRect = section as RectTransform;
				if (sectionRect != null)
					LayoutRebuilder.ForceRebuildLayoutImmediate(sectionRect);
			}
			var rootRect = _gridRoot as RectTransform;
			if (rootRect != null)
				LayoutRebuilder.ForceRebuildLayoutImmediate(rootRect);
			var scrollRect = _gridRoot.GetComponentInParent<ScrollRect>();
			var scrollContent = scrollRect != null ? scrollRect.content : (_gridRoot.parent as RectTransform);
			if (scrollContent != null)
				LayoutRebuilder.ForceRebuildLayoutImmediate(scrollContent);
			Canvas.ForceUpdateCanvases();
		}

		/// <summary> Rescan BrushAlphas folder and rebuild the grid (e.g. after dropping ABR/PNG files on disk). Call from a runtime-created Refresh button. </summary>
		public void RefreshFromFolder()
		{
			if (_brushAlphasMGR != null)
			{
				_brushAlphasMGR.RefreshCustomAlphas();
				RebuildGrid();
			}
		}

		/// <summary> Select brush at absolute index and update the painting system. Size defaults to 40 when clicking any brush; spacing/angle/roundness from ABR when present. </summary>
		const float DefaultBrushSizeOnSelect01 = 40f / 100f; // 40 on 0–100 display when user clicks a brush

		void SelectBrushAtIndex(int index)
		{
			if (_brushAlphasMGR == null) return;
			_brushAlphasMGR.CurrentIndex = index;

			if (_hardness != null)
			{
				if (_brushAlphasMGR.IsCustomAlpha(index))
					_hardness.SetUsingCustomAlpha(index - 3);
				else if (_brushAlphasMGR.IsBuiltIn(index))
					_hardness.SetBuiltInOnly(index);
			}

			// Size always defaults to 40 when clicking any brush; apply ABR suggested spacing/angle/roundness when present.
			float suggestedSpacing = _brushAlphasMGR.GetSuggestedSpacing01(index);
			float suggestedAngle = _brushAlphasMGR.GetSuggestedAngleDeg(index);
			float suggestedRoundness = _brushAlphasMGR.GetSuggestedRoundness01(index);
			ApplyBrushOptionsToRibbon(DefaultBrushSizeOnSelect01, suggestedSpacing, suggestedAngle, suggestedRoundness);

			UpdateSelectedBrushLabels(index);
			HighlightSelected(index);
		}

		/// <summary> Apply size, spacing, angle, and roundness to the app-wide brush state. </summary>
		void ApplyBrushOptionsToRibbon(float suggestedSize01, float suggestedSpacing01, float suggestedAngleDeg, float suggestedRoundness01)
		{
			var canonical = BrushRibbon_UI_Size.instance;
			if (canonical != null)
			{
				if (suggestedSize01 > 0f) canonical.SetBrushSize(suggestedSize01);
				canonical.SetBrushSpacing(suggestedSpacing01);
				canonical.SetBrushAngle(suggestedAngleDeg);
				canonical.SetBrushRoundness(suggestedRoundness01 > 0f ? suggestedRoundness01 : 1f);
				return;
			}
			if (SD_WorkflowOptionsRibbon_UI.instance != null)
			{
				if (suggestedSize01 > 0f) SD_WorkflowOptionsRibbon_UI.instance.SetBrushSize(suggestedSize01);
				SD_WorkflowOptionsRibbon_UI.instance.SetBrushSpacing(suggestedSpacing01);
				SD_WorkflowOptionsRibbon_UI.instance.SetBrushAngle(suggestedAngleDeg);
				SD_WorkflowOptionsRibbon_UI.instance.SetBrushRoundness(suggestedRoundness01 > 0f ? suggestedRoundness01 : 1f);
			}
			else if (BrushRibbon_UI.instance != null)
			{
				if (suggestedSize01 > 0f) BrushRibbon_UI.instance.SetBrushSize(suggestedSize01);
				BrushRibbon_UI.instance.SetBrushSpacing(suggestedSpacing01);
				BrushRibbon_UI.instance.SetBrushAngle(suggestedAngleDeg);
				BrushRibbon_UI.instance.SetBrushRoundness(suggestedRoundness01 > 0f ? suggestedRoundness01 : 1f);
			}
		}

		void UpdateSelectedBrushLabels(int index)
		{
			if (_brushAlphasMGR == null || index < 0 || index >= _brushAlphasMGR.AllEntries.Count) return;
			var entry = _brushAlphasMGR.AllEntries[index];
			if (_selectedBrushNameLabel != null)
				_selectedBrushNameLabel.text = string.IsNullOrEmpty(entry.name) ? "Brush " + (index + 1) : entry.name;
			if (_selectedBrushAttrsLabel != null)
			{
				var parts = new System.Collections.Generic.List<string>();
				float size01 = BrushRibbon_UI_Size.GetBrushSize01();
				parts.Add("Size: " + Mathf.RoundToInt(size01 * 100f));
				float spacing01 = BrushRibbon_UI_Size.GetBrushSpacing01();
				parts.Add(spacing01 <= 0.001f ? "Spacing: continuous" : "Spacing: " + Mathf.RoundToInt(spacing01 * 100f) + "%");
				_selectedBrushAttrsLabel.text = string.Join(" | ", parts);
			}
		}

		/// <summary> Call when BrushAlphas_MGR may have new entries (e.g. after Refresh). Safe to call before Start (e.g. from CollectNow). </summary>
		public void RebuildGrid()
		{
			if (_gridRoot == null) _gridRoot = transform;
			if (_thumbnailTemplate == null) _thumbnailTemplate = CreateDefaultThumbnailTemplate();
			if (_thumbnailTemplate != null)
				_thumbnailTemplate.SetActive(false);
			if (_gridRoot == null || _thumbnailTemplate == null) return;
			if (_brushAlphasMGR == null) _brushAlphasMGR = BrushAlphas_MGR.instance ?? FindObjectOfType<BrushAlphas_MGR>(true);

			_thumbInstances.Clear();
			_thumbInstanceGlobalIndices.Clear();
			EnsureVerticalLayoutOnGridRoot();
			for (int i = _gridRoot.childCount - 1; i >= 0; i--)
			{
				var child = _gridRoot.GetChild(i).gameObject;
				if (child != _thumbnailTemplate)
					DestroyImmediate(child);
			}

			if (_brushAlphasMGR == null || _brushAlphasMGR.AllEntries.Count == 0)
			{
				AddPlaceholderText("Drop ABR/PNG/TGA into BrushAlphas folder, then Refresh.\nOr use \"Load ABR/PNG…\" to add from anywhere.");
				return;
			}

			var groups = _brushAlphasMGR.GetGroupsForUI();
			var entries = _brushAlphasMGR.AllEntries;
			if (groups == null || entries == null) return;

			foreach (var (groupName, indices) in groups)
			{
				if (indices == null || indices.Count == 0) continue;
				CreateCollapsibleSection(_gridRoot, groupName, indices, entries);
			}

			// Single source of truth: re-apply flush layout so nothing (prefab, CreateBrushPresetsRuntime, or order) can leave wrong spacing
			ReapplyFlushLayoutToAllSections();

			HighlightSelected(_brushAlphasMGR.CurrentIndex);
			UpdateSelectedBrushLabels(_brushAlphasMGR.CurrentIndex);
			var rootRect = _gridRoot as RectTransform;
			if (rootRect != null)
			{
				// Rebuild each section first so header-to-grid spacing (flush) and grid layout apply; then root and scroll (responsive)
				for (int i = 0; i < _gridRoot.childCount; i++)
				{
					var section = _gridRoot.GetChild(i);
					var sectionRect = section as RectTransform;
					if (sectionRect != null)
						LayoutRebuilder.ForceRebuildLayoutImmediate(sectionRect);
				}
				LayoutRebuilder.ForceRebuildLayoutImmediate(rootRect);
				// Force the actual ScrollRect.content to update height so scrolling works when more brushes are added.
				// Resolve via ScrollRect so we always get the real scroll content (not just _gridRoot.parent, which
				// can be the picker itself when _gridRoot is a child in prefab setups).
				var scrollRect = _gridRoot.GetComponentInParent<ScrollRect>();
				var scrollContent = scrollRect != null ? scrollRect.content : (_gridRoot.parent as RectTransform);
				if (scrollContent != null)
				{
					LayoutRebuilder.ForceRebuildLayoutImmediate(scrollContent);
					var csf = scrollContent.GetComponent<ContentSizeFitter>();
					if (csf != null)
						LayoutRebuilder.ForceRebuildLayoutImmediate(scrollContent);
					// Adaptive bottom padding: at least one row of thumbnails so user can scroll to see the last brush
					var scrollVlg = scrollContent.GetComponent<VerticalLayoutGroup>();
					if (scrollVlg != null)
					{
						int bottom = 2 + GetScrollBottomPaddingPx();
						scrollVlg.padding = new RectOffset(scrollVlg.padding.left, scrollVlg.padding.right, scrollVlg.padding.top, bottom);
					}
				}
				// Keep scroll at top so user sees Load ABR/PNG and first brushes instead of jumping to bottom
				if (scrollRect != null)
					scrollRect.verticalNormalizedPosition = 1f;
				Canvas.ForceUpdateCanvases();
				for (int i = 0; i < _gridRoot.childCount; i++)
				{
					var section = _gridRoot.GetChild(i);
					var header = section.Find("Header");
					if (header == null) continue;
					var folderIcon = header.Find("FolderIcon");
					if (folderIcon != null)
					{
						var iconRect = folderIcon as RectTransform;
						if (iconRect != null)
							iconRect.sizeDelta = new Vector2(kHeaderRowHeight, kHeaderRowHeight);
					}
				}
			}
		}

		/// <summary> Presets panel uses a single vertical grid: sections stack top-to-bottom, left-aligned; each section has header row + thumbnail GridLayoutGroup below. </summary>
		void EnsureVerticalLayoutOnGridRoot()
		{
			var glg = _gridRoot.GetComponent<GridLayoutGroup>();
			if (glg != null)
				DestroyImmediate(glg);

			var vlg = _gridRoot.GetComponent<VerticalLayoutGroup>();
			if (vlg == null)
				vlg = _gridRoot.gameObject.AddComponent<VerticalLayoutGroup>();

			vlg.spacing = kSectionSpacingPx; // dynamic: space between groups so opening one doesn't overlap the next
			vlg.padding = new RectOffset(kRootPaddingLeft, kRootPaddingRight, kRootPaddingTopBottom, kRootPaddingTopBottom); // gap above dropdown = parent (scroll) spacing
			vlg.childAlignment = TextAnchor.UpperLeft;
			vlg.childControlWidth = true;
			vlg.childControlHeight = false;
			vlg.childForceExpandWidth = true;
			vlg.childForceExpandHeight = false;

			// So scroll content height updates when sections expand/collapse (responsive; no overlap)
			var rootCsf = _gridRoot.GetComponent<ContentSizeFitter>();
			if (rootCsf == null)
				rootCsf = _gridRoot.gameObject.AddComponent<ContentSizeFitter>();
			rootCsf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
			rootCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
		}

		/// <summary> Re-apply flush layout to every section so thumbnails sit directly under chevron. Call after creating sections so no other code path can leave wrong spacing. </summary>
		void ReapplyFlushLayoutToAllSections()
		{
			if (_gridRoot == null) return;
			var rootVlg = _gridRoot.GetComponent<VerticalLayoutGroup>();
			if (rootVlg != null)
			{
				rootVlg.spacing = kSectionSpacingPx;
				rootVlg.padding = new RectOffset(kRootPaddingLeft, kRootPaddingRight, kRootPaddingTopBottom, kRootPaddingTopBottom);
			}
			for (int i = 0; i < _gridRoot.childCount; i++)
			{
				var section = _gridRoot.GetChild(i);
				var sectionRect = section as RectTransform;
				var sectionVLG = section.GetComponent<VerticalLayoutGroup>();
				if (sectionVLG != null)
				{
					sectionVLG.spacing = 0;
					sectionVLG.padding = new RectOffset(0, 0, 0, 0);
				}
				var header = section.Find("Header");
				if (header != null)
				{
					var headerLE = header.GetComponent<LayoutElement>();
					if (headerLE != null)
					{
						headerLE.preferredHeight = kHeaderRowHeight;
						headerLE.minHeight = kHeaderRowHeight;
						headerLE.flexibleHeight = 0f;
					}
					var headerHLG = header.GetComponent<HorizontalLayoutGroup>();
					if (headerHLG != null)
					{
						headerHLG.spacing = 1;
						headerHLG.padding = new RectOffset(0, 0, 0, 0);
					}
					// Force header rect to exact height so no gap appears under chevron
					var headerRect = header as RectTransform;
					if (headerRect != null)
					{
						headerRect.anchorMin = new Vector2(0, 1f);
						headerRect.anchorMax = new Vector2(1f, 1f);
						headerRect.pivot = new Vector2(0.5f, 1f);
						headerRect.sizeDelta = new Vector2(0f, kHeaderRowHeight);
					}
				}
				Transform grid = null;
				for (int j = 0; j < section.childCount; j++)
				{
					var ch = section.GetChild(j);
					if (ch.name.StartsWith("Grid_"))
					{
						grid = ch;
						break;
					}
				}
				if (grid != null)
				{
					var glg = grid.GetComponent<GridLayoutGroup>();
					if (glg != null)
						glg.padding = new RectOffset(0, 0, 0, 0);
					var gridLE = grid.GetComponent<LayoutElement>();
					if (gridLE != null)
						gridLE.flexibleHeight = 0f;
					// Rebuild grid first so it has correct height before section layout
					var gridRect = grid as RectTransform;
					if (gridRect != null)
						LayoutRebuilder.ForceRebuildLayoutImmediate(gridRect);
				}
				// Force section layout so gap is removed immediately (header 12px then grid flush below)
				if (sectionRect != null)
					LayoutRebuilder.ForceRebuildLayoutImmediate(sectionRect);
			}
		}

		static Font _cachedLegacyFont;
		static Font GetLegacyFont()
		{
			if (_cachedLegacyFont == null)
				_cachedLegacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			return _cachedLegacyFont;
		}

		/// <summary> Header and folder icon size; keep small so thumbnails sit flush under chevron. </summary>
		int HeaderIconSize => Mathf.Max(10, _thumbnailSize / 3);
		/// <summary> Fixed minimal header row height. </summary>
		const int kHeaderRowHeight = 12;
		/// <summary> Space between header row and first thumbnail row (0.5 units) so user can click dropdown without hitting thumbnails. </summary>
		const int kHeaderToGridSpacingPx = 4;
		/// <summary> Space between chevron and folder icon in header (0.5 units) so each is easy to click. </summary>
		const int kChevronToFolderSpacingPx = 4;

		void CreateCollapsibleSection(Transform parent, string groupName, List<int> indices, IReadOnlyList<BrushAlphas_MGR.BrushAlphaEntry> entries)
		{
			var sectionGo = new GameObject("Section_" + groupName);
			sectionGo.transform.SetParent(parent, false);
			sectionGo.AddComponent<RectTransform>();
			var sectionBg = sectionGo.AddComponent<Image>();
			sectionBg.color = new Color(0f, 0f, 0f, 0f);
			sectionBg.raycastTarget = false;
			var sectionVLG = sectionGo.AddComponent<VerticalLayoutGroup>();
			sectionVLG.spacing = 0; // flush: thumbnails directly under chevron row (conservative space, collapsible)
			sectionVLG.childAlignment = TextAnchor.UpperLeft;
			sectionVLG.childControlWidth = true;
			sectionVLG.childControlHeight = false;
			sectionVLG.childForceExpandWidth = true;
			sectionVLG.childForceExpandHeight = false;
			sectionVLG.padding = new RectOffset(0, 0, 0, 0);
			// Section sizes to header + grid so when grid collapses it shrinks and sections below shift down (no overlap)
			var sectionCsf = sectionGo.AddComponent<ContentSizeFitter>();
			sectionCsf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
			sectionCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			// Header row: [chevron][folder] [title] — minimal height so grid is flush under it
			var headerGo = new GameObject("Header");
			headerGo.transform.SetParent(sectionGo.transform, false);
			var headerRect = headerGo.AddComponent<RectTransform>();
			// Force header to exact height so no default 100px or layout drift adds gap under chevron
			headerRect.anchorMin = new Vector2(0, 1f);
			headerRect.anchorMax = new Vector2(1f, 1f);
			headerRect.pivot = new Vector2(0.5f, 1f);
			headerRect.sizeDelta = new Vector2(0f, kHeaderRowHeight);
			var headerLE = headerGo.AddComponent<LayoutElement>();
			headerLE.preferredHeight = kHeaderRowHeight;
			headerLE.minHeight = kHeaderRowHeight;
			headerLE.flexibleHeight = 0f;
			headerLE.flexibleWidth = 1f;
			var headerBg = headerGo.AddComponent<Image>();
			headerBg.color = new Color(1f, 1f, 1f, 0f);
			headerBg.raycastTarget = true;
			var headerHLG = headerGo.AddComponent<HorizontalLayoutGroup>();
			headerHLG.spacing = kChevronToFolderSpacingPx; // gap between chevron and folder icon for easier clicking
			headerHLG.childAlignment = TextAnchor.MiddleLeft;
			headerHLG.childControlWidth = false;
			headerHLG.childControlHeight = true;
			headerHLG.childForceExpandHeight = false;
			headerHLG.padding = new RectOffset(0, 0, 0, 0);

			// Chevron first (left): ▶ collapsed, ▼ expanded — light grey
			var arrowGo = new GameObject("Chevron");
			arrowGo.transform.SetParent(headerGo.transform, false);
			var arrowLE = arrowGo.AddComponent<LayoutElement>();
			arrowLE.preferredWidth = kHeaderRowHeight;
			arrowLE.preferredHeight = kHeaderRowHeight;
			var arrowText = arrowGo.AddComponent<UnityEngine.UI.Text>();
			arrowText.text = "▼";
			arrowText.font = GetLegacyFont();
			arrowText.fontSize = Mathf.Max(8, kHeaderRowHeight - 2);
			arrowText.color = new Color(0.72f, 0.74f, 0.78f, 1f);
			arrowText.alignment = TextAnchor.MiddleCenter;
			arrowText.raycastTarget = false;

			// Folder icon
			var iconGo = new GameObject("FolderIcon");
			iconGo.transform.SetParent(headerGo.transform, false);
			var iconLE = iconGo.AddComponent<LayoutElement>();
			iconLE.preferredWidth = kHeaderRowHeight;
			iconLE.preferredHeight = kHeaderRowHeight;
			var iconImage = iconGo.AddComponent<Image>();
			iconImage.sprite = GetFolderIconSprite();
			iconImage.color = new Color(0.72f, 0.74f, 0.78f, 1f);
			iconImage.raycastTarget = false;

			// Title
			var titleGo = new GameObject("Title");
			titleGo.transform.SetParent(headerGo.transform, false);
			var titleLE = titleGo.AddComponent<LayoutElement>();
			titleLE.flexibleWidth = 1f;
			titleLE.preferredHeight = kHeaderRowHeight;
			titleLE.flexibleHeight = 0f;
			var titleText = titleGo.AddComponent<UnityEngine.UI.Text>();
			titleText.text = groupName;
			titleText.font = GetLegacyFont();
			titleText.fontSize = Mathf.Max(8, kHeaderRowHeight - 2);
			titleText.color = new Color(0.7f, 0.75f, 0.8f, 1f);
			titleText.alignment = TextAnchor.MiddleLeft;
			titleText.raycastTarget = false;

			var headerBtn = headerGo.AddComponent<Button>();
			headerBtn.targetGraphic = headerBg;
			headerBtn.transition = Selectable.Transition.ColorTint;
			var colors = headerBtn.colors;
			colors.normalColor = Color.white;
			colors.highlightedColor = new Color(0.95f, 0.95f, 0.95f, 1f);
			headerBtn.colors = colors;

			// Grid of thumbnails (brush icons show here when section is expanded). Always in layout; hide via CanvasGroup when collapsed.
			var gridGo = new GameObject("Grid_" + groupName);
			gridGo.transform.SetParent(sectionGo.transform, false);
			var gridRect = gridGo.AddComponent<RectTransform>();
			// Top-anchor grid so it sits flush under header with no extra top space
			gridRect.anchorMin = new Vector2(0, 1f);
			gridRect.anchorMax = new Vector2(1f, 1f);
			gridRect.pivot = new Vector2(0.5f, 1f);
			var gridBg = gridGo.AddComponent<Image>();
			gridBg.color = new Color(0f, 0f, 0f, 0f);
			gridBg.raycastTarget = false;
			var gridLE = gridGo.AddComponent<LayoutElement>();
			gridLE.flexibleWidth = 1f;
			gridLE.minHeight = _thumbnailSize;
			var gridCg = gridGo.AddComponent<CanvasGroup>();
			var glg = gridGo.AddComponent<GridLayoutGroup>();
			glg.cellSize = new Vector2(_thumbnailSize, _thumbnailSize);
			glg.spacing = new Vector2(kThumbGridSpacing, kThumbGridSpacing);
			glg.constraint = GridLayoutGroup.Constraint.Flexible;
			glg.childAlignment = TextAnchor.UpperLeft;
			glg.startCorner = GridLayoutGroup.Corner.UpperLeft;
			glg.startAxis = GridLayoutGroup.Axis.Horizontal;
			glg.padding = new RectOffset(0, 0, 0, 0); // no inset: first row of thumbnails flush under header (left, right, top, bottom)
			// Let GridLayoutGroup determine height from actual width (fixes only-one-brush-visible when width is 0 at creation)
			var gridCsf = gridGo.AddComponent<ContentSizeFitter>();
			gridCsf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
			gridCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			bool expanded = true;
			headerBtn.onClick.AddListener(() =>
			{
				if (gridGo == null || gridLE == null || arrowText == null) return;
				expanded = !expanded;
				gridCsf.verticalFit = expanded ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
				gridLE.preferredHeight = expanded ? -1f : 0f;
				gridCg.alpha = expanded ? 1f : 0f;
				gridCg.blocksRaycasts = expanded;
				gridCg.interactable = expanded;
				arrowText.text = expanded ? "▼" : "▶";
				// Force collapsed grid to zero height so section and sections below shift down (no overlap)
				var gridRect = gridGo.GetComponent<RectTransform>();
				if (gridRect != null && !expanded)
					gridRect.sizeDelta = new Vector2(gridRect.sizeDelta.x, 0f);
				RefreshBrushPresetsLayoutAfterExpandCollapse();
			});

			foreach (int index in indices)
			{
				var entry = entries[index];
				var go = Instantiate(_thumbnailTemplate, gridGo.transform);
				go.SetActive(true);
				go.name = "Brush_" + index + "_" + entry.name;
				_thumbInstances.Add(go);
				_thumbInstanceGlobalIndices.Add(index);

				// Implement precise hit area at runtime (inset so clicks don't hit adjacent thumbnails)
				EnsureThumbnailHitArea(go);

				var raw = go.GetComponentInChildren<RawImage>();
				if (raw != null)
				{
					Texture previewTex = entry.preview != null ? entry.preview : entry.texture;
					if (previewTex != null)
					{
						raw.texture = previewTex;
						raw.uvRect = new Rect(0, 0, 1, 1);
					}
				}

				var btn = go.GetComponentInChildren<Button>();
				if (btn != null)
					btn.onClick.AddListener(() => SelectBrushAtIndex(index));

				var rect = go.GetComponent<RectTransform>();
				if (rect != null)
					rect.sizeDelta = new Vector2(_thumbnailSize, _thumbnailSize);
			}
		}

		static Sprite _cachedFolderSprite;
		static Sprite GetFolderIconSprite()
		{
			if (_cachedFolderSprite != null) return _cachedFolderSprite;
			const int size = 16;
			var tex = new Texture2D(size, size);
			tex.filterMode = FilterMode.Bilinear;
			// Clear
			for (int y = 0; y < size; y++)
				for (int x = 0; x < size; x++)
					tex.SetPixel(x, y, Color.clear);
			// Folder: horizontal body + small tab on top (light grey tint applied in UI)
			var c = Color.white;
			for (int y = 0; y < size; y++)
				for (int x = 0; x < size; x++)
				{
					bool tab = y >= size - 5 && x >= 2 && x <= size - 3;  // top tab
					bool body = y >= 2 && y <= size - 2 && x >= 1 && x <= size - 2;  // main body
					if (tab || body) tex.SetPixel(x, y, c);
				}
			tex.Apply();
			_cachedFolderSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
			return _cachedFolderSprite;
		}

		void HighlightSelected(int selectedIndex)
		{
			for (int i = 0; i < _thumbInstances.Count; i++)
			{
				var go = _thumbInstances[i];
				if (go == null) continue;
				int globalIndex = i < _thumbInstanceGlobalIndices.Count ? _thumbInstanceGlobalIndices[i] : -1;
				var bg = go.GetComponent<Image>();
				if (bg != null)
				{
					bg.color = (globalIndex == selectedIndex)
						? new Color(0.2f, 0.55f, 0.8f, 1f)
						: new Color(0.3f, 0.3f, 0.3f, 1f);
				}
			}
		}

		void AddPlaceholderText(string msg)
		{
			var go = new GameObject("Placeholder");
			go.transform.SetParent(_gridRoot, false);
			var rect = go.AddComponent<RectTransform>();
			rect.sizeDelta = new Vector2(200, 40);
			var le = go.AddComponent<LayoutElement>();
			le.preferredHeight = 40;
			le.flexibleWidth = 1;
			var text = go.AddComponent<UnityEngine.UI.Text>();
			text.font = GetLegacyFont();
			text.text = msg;
			text.fontSize = 11;
			text.color = new Color(0.6f, 0.6f, 0.65f, 1f);
			text.alignment = TextAnchor.MiddleCenter;
			_thumbInstances.Add(go);
			_thumbInstanceGlobalIndices.Add(-1);
		}
	}
}

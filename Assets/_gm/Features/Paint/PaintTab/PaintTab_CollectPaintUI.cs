using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace spz {

	/// <summary>
	/// Populates the Paint tab's Krita-style sections with paint UI.
	/// First tries to find existing instances (FindObjectOfType); if none exist,
	/// creates the managers and UI components at runtime so the sections have content.
	/// 
	/// IMPORTANT: The Paint panel is inactive most of the time (only active when user clicks Paint tab).
	/// Coroutines die on inactive GameObjects, so all waiting/retry logic lives in CommandRibbon_UI
	/// (which IS always active). This component only holds the synchronous CollectNow() method.
	/// OnEnable also calls CollectNow() so switching to the Paint tab retries if singletons loaded late.
	/// </summary>
	public class PaintTab_CollectPaintUI : MonoBehaviour
	{
		[SerializeField] PaintTab_KritaLayout_UI _layout;

		bool _collected;
		bool _toolchestCollected;

		public bool IsFullyCollected => _collected && _toolchestCollected;

		public void SetLayout(PaintTab_KritaLayout_UI layout) { _layout = layout; }

		/// <summary>For Layers section: returns (scroll content RectTransform, section root Transform). Panel goes in scroll content; Add button goes in section root so it stays visible. Handles both prefab (section root = LayersSection) and CreateSectionsIfMissing (LayersSection = ScrollContent).</summary>
		static void GetLayersScrollContentAndRoot(RectTransform layersSectionRef, out RectTransform scrollContent, out Transform sectionRoot)
		{
			scrollContent = null;
			sectionRoot = layersSectionRef != null ? layersSectionRef : null;
			if (layersSectionRef == null) return;
			// Prefab case: section ref is section root (e.g. 2_Layers) with child "Content" that has ScrollRect
			for (int i = 0; i < layersSectionRef.childCount; i++)
			{
				var ch = layersSectionRef.GetChild(i);
				if (ch.name == "Content")
				{
					var sr = ch.GetComponent<ScrollRect>();
					if (sr != null && sr.content != null)
					{
						scrollContent = sr.content;
						sectionRoot = layersSectionRef;
						return;
					}
					break;
				}
			}
			// CreateSectionsIfMissing case: section ref is ScrollContent; section root is Content.parent
			if (layersSectionRef.parent != null)
			{
				var content = layersSectionRef.parent;
				if (content.GetComponent<ScrollRect>() != null)
				{
					scrollContent = layersSectionRef as RectTransform;
					sectionRoot = content.parent;
					return;
				}
			}
			scrollContent = layersSectionRef;
			sectionRoot = layersSectionRef;
		}

		/// <summary>Returns the RectTransform that actually holds the scrollable content (ScrollContent). If BrushPresetsSection is the section root (prefab), finds Content -> ScrollRect.content; otherwise returns BrushPresetsSection (runtime-created section returns ScrollContent).</summary>
		static RectTransform GetBrushPresetsScrollContent(RectTransform brushPresetsSection)
		{
			if (brushPresetsSection == null) return null;
			for (int i = 0; i < brushPresetsSection.childCount; i++)
			{
				var child = brushPresetsSection.GetChild(i);
				if (child.name == "Content")
				{
					var scroll = child.GetComponent<ScrollRect>();
					if (scroll != null && scroll.content != null)
						return scroll.content;
					break;
				}
			}
			return brushPresetsSection;
		}

		/// <summary>Returns the Brush Presets section root (parent of Header + Content). Buttons stay here so they don't scroll.</summary>
		static Transform GetBrushPresetsSectionRoot(RectTransform scrollContent)
		{
			if (scrollContent == null) return null;
			var p = scrollContent.parent;
			if (p != null && p.name == "Content")
				p = p.parent;
			return p;
		}

		/// <summary>Finds the button row (BrushPresets_Buttons) inside scrollContent. Returns null if not found or already moved out.</summary>
		static Transform FindBrushPresetsButtonRow(Transform scrollContent)
		{
			if (scrollContent == null) return null;
			for (int i = 0; i < scrollContent.childCount; i++)
			{
				var c = scrollContent.GetChild(i);
				if (c.name == "BrushPresets_Buttons")
					return c;
			}
			return null;
		}

		/// <summary>Fallback bottom padding when picker has not run yet. Picker uses adaptive padding (thumbnail size + spacing + buffer) in RebuildGrid.</summary>
		const int kBrushPresetsScrollBottomPad = 14;
		// Picker top padding: use single source of truth so padding is never overwritten or out of sync (BrushRibbon_UI_AlphaPicker.PickerTopSpacingPx).

		/// <summary>Ensures the brush presets scroll content has ContentSizeFitter (vertical PreferredSize) and VLG so the content height grows with the picker and ScrollRect can scroll.</summary>
		static void EnsureBrushPresetsScrollContentCanGrow(RectTransform scrollContent)
		{
			if (scrollContent == null) return;
			var csf = scrollContent.GetComponent<ContentSizeFitter>();
			if (csf == null) csf = scrollContent.gameObject.AddComponent<ContentSizeFitter>();
			csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
			var vlg = scrollContent.GetComponent<VerticalLayoutGroup>();
			if (vlg == null)
			{
				vlg = scrollContent.gameObject.AddComponent<VerticalLayoutGroup>();
			}
			vlg.spacing = BrushRibbon_UI_AlphaPicker.PickerTopSpacingPx;
			vlg.padding = new RectOffset(2, 2, 2, 2 + kBrushPresetsScrollBottomPad);
			vlg.childForceExpandHeight = false;
			vlg.childControlHeight = false; // let picker drive its own height so ContentSizeFitter gets correct total and scroll works
		}

		void OnEnable()
		{
			if (_layout == null) _layout = GetComponent<PaintTab_KritaLayout_UI>();
			if (_layout != null)
			{
				CollectNow();
				StartCoroutine(RefreshBrushPresetsLayoutWhenReady());
			}
		}

		System.Collections.IEnumerator RefreshBrushPresetsLayoutWhenReady()
		{
			yield return null; // wait one frame so panel is active and has valid rect
			var scrollContent = GetBrushPresetsScrollContent(_layout != null ? _layout.BrushPresetsSection : null);
			if (scrollContent == null) yield break;
			var picker = FindObjectOfType<BrushRibbon_UI_AlphaPicker>(true);
			if (picker != null)
			{
				picker.RebuildGrid();
				LayoutRebuilder.ForceRebuildLayoutImmediate(scrollContent);
				Canvas.ForceUpdateCanvases();
			}
		}

		/// <summary>Synchronous populate. Safe to call multiple times from any context.</summary>
		public void CollectNow()
		{
			if (_layout == null) _layout = GetComponent<PaintTab_KritaLayout_UI>();
			if (_layout == null) return;
			if (_layout.BrushPresetsSection == null)
				_layout.SetCreateSectionsIfMissing(true);
			bool did = false;
			bool toolchestDid = false;

			// --- Toolchest row: workflow ribbons ---
			if (WorkflowRibbon_UI.instance != null && _layout.ToolchestRow != null)
			{
				var tr = (RectTransform)WorkflowRibbon_UI.instance.transform;
				tr.SetParent(_layout.ToolchestRow, false);
				tr.anchorMin = new Vector2(0, 0.5f);
				tr.anchorMax = new Vector2(0, 0.5f);
				tr.pivot = new Vector2(0, 0.5f);
				EnsureLayoutElement(tr, flexibleWidth: 0f);
				did = true;
				toolchestDid = true;
			}
			if (SD_WorkflowOptionsRibbon_UI.instance != null && _layout.ToolchestRow != null)
			{
				var tr = (RectTransform)SD_WorkflowOptionsRibbon_UI.instance.transform;
				tr.SetParent(_layout.ToolchestRow, false);
				tr.anchorMin = new Vector2(0, 0.5f);
				tr.anchorMax = new Vector2(0, 0.5f);
				tr.pivot = new Vector2(0, 0.5f);
				EnsureLayoutElement(tr, flexibleWidth: 1f);
				did = true;
				toolchestDid = true;
			}

			// --- Layers section ---
			if (_layout.LayersSection != null)
			{
				GetLayersScrollContentAndRoot(_layout.LayersSection, out var layersScrollContent, out var layersSectionRoot);
				// Ensure stack exists so panel can wire to it (whether panel is found or created)
				if (PaintLayerStack_MGR.instance == null)
				{
					var mgrGo = new GameObject("PaintLayerStack_MGR_Runtime");
					mgrGo.AddComponent<PaintLayerStack_MGR>();
				}
				var layersPanel = FindObjectOfType<PaintTab_LayersPanel_UI>(true);
				if (layersPanel == null && layersScrollContent != null)
					layersPanel = CreateLayersPanelRuntime(layersScrollContent, layersSectionRoot);
				// Always wire panel to stack and Add Layer button so all buttons work (found or created panel)
				if (layersPanel != null)
				{
					layersPanel.SetLayerStack(PaintLayerStack_MGR.instance);
					Button addBtn = null;
					Transform searchRoot = layersScrollContent != null ? layersScrollContent : layersPanel.transform.parent;
					if (searchRoot != null)
					{
						for (int si = 0; si < searchRoot.childCount; si++)
						{
							var row = searchRoot.GetChild(si);
							if (row != null && row.name == "LayerButtonsRow")
							{
								addBtn = row.Find("AddLayerBtn")?.GetComponent<Button>();
								if (addBtn == null) addBtn = row.GetComponentInChildren<Button>(true);
								break;
							}
						}
					}
					// If no Add Layer button (e.g. panel found but button row missing), create it so the feature is never missing.
					if (addBtn == null && layersScrollContent != null)
						addBtn = EnsureLayersAddButtonRow(layersScrollContent);
					if (addBtn != null)
						layersPanel.SetAddLayerButton(addBtn);
				}
				if (layersPanel != null && layersScrollContent != null && layersPanel.transform.parent != layersScrollContent)
				{
					var tr = (RectTransform)layersPanel.transform;
					var oldParent = tr.parent;
					tr.SetParent(layersScrollContent, false);
					tr.SetAsFirstSibling();
					tr.anchorMin = new Vector2(0, 1);
					tr.anchorMax = Vector2.one;
					tr.pivot = new Vector2(0.5f, 1);
					tr.offsetMin = Vector2.zero;
					tr.offsetMax = Vector2.zero;
					// Move LayerButtonsRow with panel so Delete/Visibility/+ Layer stay with the list
					if (oldParent != null)
					{
						for (int si = 0; si < oldParent.childCount; si++)
						{
							var sib = oldParent.GetChild(si);
							if (sib != null && sib.name == "LayerButtonsRow")
							{
								sib.SetParent(layersScrollContent, false);
								sib.SetSiblingIndex(1);
								break;
							}
						}
					}
				}
				if (layersPanel != null)
				{
					did = true;
					if (layersScrollContent != null)
						LayoutRebuilder.ForceRebuildLayoutImmediate(layersScrollContent);
				}
			}

			// --- Brush Presets section ---
			var scrollContent = GetBrushPresetsScrollContent(_layout.BrushPresetsSection);
			if (scrollContent != null)
			{
				// Ensure scroll content can grow vertically so ScrollRect actually scrolls when many brushes are added
				// (matches Layers section: ContentSizeFitter.PreferredSize + VLG with childForceExpandHeight = false)
				EnsureBrushPresetsScrollContentCanGrow(scrollContent);
				var alphaPicker = FindObjectOfType<BrushRibbon_UI_AlphaPicker>(true);
				if (alphaPicker == null)
					alphaPicker = CreateBrushPresetsRuntime(scrollContent);
				// Reparent picker into the actual scroll content so dropdown aligns with Load ABR/PNG (critical when BrushPresetsSection is section root from prefab)
				if (alphaPicker != null && !alphaPicker.transform.IsChildOf(scrollContent))
				{
					var tr = (RectTransform)alphaPicker.transform;
					tr.SetParent(scrollContent, false);
					tr.anchorMin = Vector2.zero;
					tr.anchorMax = Vector2.one;
					tr.offsetMin = Vector2.zero;
					tr.offsetMax = Vector2.zero;
				}
				// Keep button row static: move it out of scroll content into section root so only the picker scrolls
				var sectionRoot = GetBrushPresetsSectionRoot(scrollContent);
				var btnRow = FindBrushPresetsButtonRow(scrollContent);
				if (sectionRoot != null && btnRow != null && btnRow.parent == scrollContent)
				{
					btnRow.SetParent(sectionRoot, false);
					btnRow.SetSiblingIndex(1); // after Header (0), before Content (2)
					var scrollVlg = scrollContent.GetComponent<VerticalLayoutGroup>();
					if (scrollVlg != null)
						scrollVlg.spacing = 0; // only picker in scroll content now
					var sectionVlg = sectionRoot.GetComponent<VerticalLayoutGroup>();
					if (sectionVlg != null)
						sectionVlg.padding = new RectOffset(2, 0, 0, 2); // align button row left with picker (same as scroll content edge)
				}
				if (alphaPicker != null)
				{
					// --- Left-alignment fix ---
					// ScrollContent VLG left=3 is the ONLY left offset.
					// Button row HLG left=0, picker VLG left=0, section VLG left=0, header HLG left=0.
					// So both "Load ABR/PNG" and chevron/folder/name start at exactly 3px.
					const int kEdgePad = 2; // compact; aligns Load ABR/PNG and section headers
					var pickerParent = alphaPicker.transform.parent;
					// Scroll content: keep stretch anchors so it fills the viewport width
					if (pickerParent != null)
					{
						var parentRect = pickerParent as RectTransform;
						if (parentRect != null)
						{
							parentRect.anchorMin = new Vector2(0, 1);
							parentRect.anchorMax = new Vector2(1, 1); // stretch full width
							parentRect.pivot = new Vector2(0, 1);
							// Do NOT set sizeDelta on scroll content — ContentSizeFitter must control height so scrolling works when many brushes are added
							var parentVlg = pickerParent.GetComponent<VerticalLayoutGroup>();
							if (parentVlg != null)
							{
								parentVlg.padding = new RectOffset(kEdgePad, kEdgePad, kEdgePad, kEdgePad + kBrushPresetsScrollBottomPad);
								parentVlg.spacing = BrushRibbon_UI_AlphaPicker.PickerTopSpacingPx; // gap between button row and dropdown row
								parentVlg.childAlignment = TextAnchor.UpperLeft;
							}
						}
					}
					if (_layout.BrushPresetsSection != null && _layout.BrushPresetsSection != scrollContent)
					{
						var sectionRootForLayout = _layout.BrushPresetsSection;
						var sectionVlg = sectionRootForLayout.GetComponent<VerticalLayoutGroup>();
						if (sectionVlg != null)
						{
							bool hasStaticButtons = false;
							for (int j = 0; j < sectionRootForLayout.childCount; j++)
								if (sectionRootForLayout.GetChild(j).name == "BrushPresets_Buttons") { hasStaticButtons = true; break; }
							sectionVlg.padding = hasStaticButtons ? new RectOffset(2, 0, 0, 2) : new RectOffset(0, 0, 0, 0);
							sectionVlg.childAlignment = TextAnchor.UpperLeft;
						}
						_layout.BrushPresetsSection.pivot = new Vector2(0, 1);
					}
					// Picker VLG: left=0; top = spacing above dropdown arrow (single source of truth in AlphaPicker)
					var pickerVlg = alphaPicker.GetComponent<VerticalLayoutGroup>();
					if (pickerVlg != null)
						pickerVlg.padding = new RectOffset(0, 0, BrushRibbon_UI_AlphaPicker.PickerTopSpacingPx, 0);
					var pickerRect = alphaPicker.transform as RectTransform;
					if (pickerRect != null)
					{
						pickerRect.anchorMin = new Vector2(0, 0);
						pickerRect.anchorMax = new Vector2(1, 1);
						pickerRect.pivot = new Vector2(0, 1);
						pickerRect.offsetMin = Vector2.zero;
						pickerRect.offsetMax = Vector2.zero;
					}
					// Button row: left=0 so Load ABR/PNG starts at same position as chevron
					for (int i = 0; i < scrollContent.childCount; i++)
					{
						var child = scrollContent.GetChild(i);
						var hlg = child.GetComponent<HorizontalLayoutGroup>();
						if (hlg != null)
						{
							hlg.padding = new RectOffset(0, 0, 0, 0);
							hlg.childAlignment = TextAnchor.MiddleLeft;
							break;
						}
					}
					alphaPicker.RebuildGrid();
					// Re-apply picker VLG after RebuildGrid (gap is scroll spacing, not picker padding)
					pickerVlg = alphaPicker.GetComponent<VerticalLayoutGroup>();
					if (pickerVlg != null)
					{
						pickerVlg.padding = new RectOffset(0, 0, 0, 0);
						if (pickerRect != null)
							LayoutRebuilder.ForceRebuildLayoutImmediate(pickerRect);
					}
					if (pickerParent != null)
					{
						var parentRect = pickerParent as RectTransform;
						if (parentRect != null)
							LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
					}
					LayoutRebuilder.ForceRebuildLayoutImmediate(scrollContent);
					did = true;
				}
			}

			// --- Tool Options section ---
			if (_layout.ToolOptionsSection != null && _layout.ToolOptionsSection.childCount <= 1)
			{
				CreateToolOptionsRuntime(_layout.ToolOptionsSection);
				did = true;
			}

			// --- Color / Palette section ---
			if (_layout.ColorPaletteSection != null)
			{
				EnsurePaletteLoadButtonExists(_layout.ColorPaletteSection);
				var swatches = FindObjectOfType<PaletteSwatches_UI>(true);
				if (swatches == null)
					swatches = CreatePaletteSwatchesRuntime(_layout.ColorPaletteSection);
				if (swatches != null && swatches.transform.parent != _layout.ColorPaletteSection)
				{
					var tr = (RectTransform)swatches.transform;
					tr.SetParent(_layout.ColorPaletteSection, false);
					tr.anchorMin = Vector2.zero;
					tr.anchorMax = new Vector2(1, 0);
					tr.pivot = new Vector2(0.5f, 0);
					tr.offsetMin = Vector2.zero;
					tr.offsetMax = new Vector2(0, 120);
				}
				if (swatches != null) did = true;
			}

			if (did) _collected = true;
			if (toolchestDid) _toolchestCollected = true;

			var root = _layout.transform as RectTransform;
			if (root != null)
				LayoutRebuilder.ForceRebuildLayoutImmediate(root);
			if (_layout.ToolchestRow != null)
				LayoutRebuilder.ForceRebuildLayoutImmediate(_layout.ToolchestRow);
		}

		// ---- Runtime creation of missing UI components ----

		/// <summary>Creates the LayerButtonsRow with the "+ Layer" button. Call when panel is created or when panel exists but has no Add button.</summary>
		static Button EnsureLayersAddButtonRow(RectTransform scrollContent)
		{
			if (scrollContent == null) return null;
			var buttonsRowGo = new GameObject("LayerButtonsRow");
			buttonsRowGo.transform.SetParent(scrollContent, false);
			buttonsRowGo.transform.SetAsLastSibling();
			buttonsRowGo.AddComponent<RectTransform>();
			var rowLE = buttonsRowGo.AddComponent<LayoutElement>();
			rowLE.preferredHeight = 26;
			rowLE.flexibleWidth = 1;
			rowLE.flexibleHeight = 0;
			var rowHLG = buttonsRowGo.AddComponent<HorizontalLayoutGroup>();
			rowHLG.spacing = 4;
			rowHLG.childAlignment = TextAnchor.MiddleLeft;
			rowHLG.childControlWidth = true;
			rowHLG.childControlHeight = true;
			rowHLG.childForceExpandWidth = false;
			rowHLG.childForceExpandHeight = false;
			rowHLG.padding = new RectOffset(0, 2, 0, 0);

			var addBtnGo = new GameObject("AddLayerBtn");
			addBtnGo.transform.SetParent(buttonsRowGo.transform, false);
			var addLE = addBtnGo.AddComponent<LayoutElement>();
			addLE.preferredWidth = 80;
			addLE.preferredHeight = 24;
			addLE.flexibleWidth = 1;
			var addImg = addBtnGo.AddComponent<Image>();
			addImg.color = new Color(0.25f, 0.45f, 0.3f, 0.95f);
			addImg.raycastTarget = true;
			var addBtn = addBtnGo.AddComponent<Button>();
			addBtn.targetGraphic = addImg;
			var addTxtGo = new GameObject("Text");
			addTxtGo.transform.SetParent(addBtnGo.transform, false);
			var addTxtRect = addTxtGo.AddComponent<RectTransform>();
			addTxtRect.anchorMin = Vector2.zero;
			addTxtRect.anchorMax = Vector2.one;
			addTxtRect.offsetMin = Vector2.zero;
			addTxtRect.offsetMax = Vector2.zero;
			var addTxt = addTxtGo.AddComponent<TextMeshProUGUI>();
			addTxt.text = "+ Layer";
			addTxt.fontSize = 12;
			addTxt.color = Color.white;
			addTxt.alignment = TextAlignmentOptions.Center;
			addTxt.raycastTarget = false;
			return addBtn;
		}

		static PaintTab_LayersPanel_UI CreateLayersPanelRuntime(RectTransform scrollContent, Transform sectionRoot)
		{
			if (PaintLayerStack_MGR.instance == null)
			{
				var mgrGo = new GameObject("PaintLayerStack_MGR_Runtime");
				mgrGo.AddComponent<PaintLayerStack_MGR>();
			}

			// Panel lives inside scroll content so the layer list scrolls; list root = panel transform
			var go = new GameObject("PaintTab_LayersPanel_Runtime");
			go.transform.SetParent(scrollContent, false);
			go.transform.SetAsFirstSibling();
			var rect = go.AddComponent<RectTransform>();
			rect.anchorMin = new Vector2(0, 1);
			rect.anchorMax = Vector2.one;
			rect.pivot = new Vector2(0.5f, 1);
			rect.sizeDelta = Vector2.zero;
			var goLE = go.AddComponent<LayoutElement>();
			goLE.flexibleWidth = 1;
			goLE.flexibleHeight = 0;
			goLE.minHeight = 0;
			var vlg = go.AddComponent<VerticalLayoutGroup>();
			vlg.spacing = 2;
			vlg.childAlignment = TextAnchor.UpperLeft;
			vlg.childControlWidth = true;
			vlg.childControlHeight = false;
			vlg.childForceExpandWidth = true;
			vlg.childForceExpandHeight = false;
			vlg.padding = new RectOffset(0, 0, 0, 0);
			var csf = go.AddComponent<ContentSizeFitter>();
			csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

			var panel = go.AddComponent<PaintTab_LayersPanel_UI>();
			panel.SetLayerStack(PaintLayerStack_MGR.instance);

			// Add Layer button row (always present)
			var addBtn = EnsureLayersAddButtonRow(scrollContent);
			panel.SetAddLayerButton(addBtn);

			return panel;
		}

		static BrushRibbon_UI_AlphaPicker CreateBrushPresetsRuntime(RectTransform parent)
		{
			var mgr = BrushAlphas_MGR.instance;
			if (mgr == null)
			{
				var mgrGo = new GameObject("BrushAlphas_MGR_Runtime");
				mgr = mgrGo.AddComponent<BrushAlphas_MGR>();
				// Keep manager in same hierarchy so it stays findable and isn't unloaded
				mgrGo.transform.SetParent(parent.root, true);
			}

			const int brushPresetsContentMinHeight = 140;
			var btnRow = new GameObject("BrushPresets_Buttons");
			btnRow.transform.SetParent(parent, false);
			btnRow.transform.SetAsFirstSibling();
			var btnRowRect = btnRow.AddComponent<RectTransform>();
			btnRowRect.sizeDelta = new Vector2(0, 26);
			var btnRowLE = btnRow.AddComponent<LayoutElement>();
			btnRowLE.preferredHeight = 26;
			btnRowLE.minHeight = 26;
			btnRowLE.flexibleHeight = 0; // don't stretch row vertically — keeps buttons compact
			btnRowLE.flexibleWidth = 1;
			var btnRowH = btnRow.AddComponent<HorizontalLayoutGroup>();
			btnRowH.spacing = 6;
			btnRowH.childAlignment = TextAnchor.MiddleLeft;
			btnRowH.childControlWidth = false;
			btnRowH.childControlHeight = true;
			btnRowH.childForceExpandHeight = false; // don't stretch buttons vertically
			btnRowH.padding = new RectOffset(0, 0, 0, 0); // no padding; scroll content VLG handles the 3px edge

			var content = new GameObject("BrushPresets_Content");
			content.transform.SetParent(parent, false);
			var contentRect = content.AddComponent<RectTransform>();
			contentRect.anchorMin = new Vector2(0, 1);
			contentRect.anchorMax = Vector2.one;
			contentRect.pivot = new Vector2(0, 1); // left-aligned: consolidate with Load ABR/PNG button row
			contentRect.offsetMin = Vector2.zero;
			contentRect.offsetMax = Vector2.zero;
			var contentLE = content.AddComponent<LayoutElement>();
			contentLE.flexibleWidth = 1;
			contentLE.minWidth = 120;
			contentLE.minHeight = brushPresetsContentMinHeight;
			contentLE.flexibleHeight = 0f; // use preferred height only so scroll content height = button row + picker; enables scrolling when many brushes
			var csf = content.AddComponent<ContentSizeFitter>();
			csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; // full width so collapse/folder/name align under Load ABR/PNG
			var vlg = content.AddComponent<VerticalLayoutGroup>();
			vlg.spacing = 1; // must match AlphaPicker root spacing so section stack is tight; was 6 and blocked flush layout
			vlg.padding = new RectOffset(0, 0, 0, 0); // gap is scroll content spacing (PickerTopSpacingPx), not picker padding
			vlg.childAlignment = TextAnchor.UpperLeft;
			vlg.childControlWidth = true;
			vlg.childControlHeight = false;
			vlg.childForceExpandWidth = true;
			vlg.childForceExpandHeight = false;
			var picker = content.AddComponent<BrushRibbon_UI_AlphaPicker>();
			picker.SetBrushAlphasMGR(mgr);
			picker.RebuildGrid();

			AddBrushPresetButton(btnRow.transform, "Load ABR/PNG…", new Color(0.3f, 0.45f, 0.5f, 1f), () => picker.OpenLoadBrushDialog());
			AddBrushPresetButton(btnRow.transform, "Refresh", new Color(0.35f, 0.4f, 0.35f, 1f), () => picker.RefreshFromFolder());
			AddBrushPresetButton(btnRow.transform, "Delete", new Color(0.5f, 0.25f, 0.25f, 1f), () => picker.DeleteSelectedBrush());
			AddBrushPresetButton(btnRow.transform, "Delete permanently", new Color(0.55f, 0.2f, 0.2f, 1f), () => picker.DeleteSelectedBrushPermanently());

			return picker;
		}

		static void AddBrushPresetButton(Transform parent, string label, Color bgColor, UnityEngine.Events.UnityAction onClick)
		{
			var go = new GameObject("Btn_" + label.Replace(" ", "_").Replace("…", "").Replace("/", "_"));
			go.transform.SetParent(parent, false);
			var rect = go.AddComponent<RectTransform>();
			rect.sizeDelta = new Vector2(100, 22);
			var le = go.AddComponent<LayoutElement>();
			le.preferredWidth = 100;
			le.preferredHeight = 22;
			le.minHeight = 22;
			le.flexibleHeight = 0; // keep buttons short — avoid elongated look
			var img = go.AddComponent<Image>();
			img.color = bgColor;
			img.raycastTarget = true;
			var btn = go.AddComponent<Button>();
			btn.onClick.AddListener(onClick);
			var txtGo = new GameObject("Text");
			txtGo.transform.SetParent(go.transform, false);
			var txtRect = txtGo.AddComponent<RectTransform>();
			txtRect.anchorMin = Vector2.zero;
			txtRect.anchorMax = Vector2.one;
			txtRect.offsetMin = Vector2.zero;
			txtRect.offsetMax = Vector2.zero;
			var txt = txtGo.AddComponent<TextMeshProUGUI>();
			txt.text = label;
			txt.fontSize = 10;
			txt.color = Color.white;
			txt.alignment = TextAlignmentOptions.Center;
			txt.raycastTarget = false;
		}

		static void EnsurePaletteLoadButtonExists(RectTransform section)
		{
			for (int i = 0; i < section.childCount; i++)
			{
				if (section.GetChild(i).name == "PaletteLoadButtonRow")
					return;
			}
			var row = new GameObject("PaletteLoadButtonRow");
			row.transform.SetParent(section, false);
			row.transform.SetAsFirstSibling();
			var rowRect = row.AddComponent<RectTransform>();
			rowRect.sizeDelta = new Vector2(0, 28);
			var rowLE = row.AddComponent<LayoutElement>();
			rowLE.preferredHeight = 28;
			rowLE.flexibleWidth = 1;
			rowLE.flexibleHeight = 0;
			var hlg = row.AddComponent<HorizontalLayoutGroup>();
			hlg.spacing = 4;
			hlg.childAlignment = TextAnchor.MiddleLeft;
			hlg.childControlWidth = true;
			hlg.childControlHeight = true;
			hlg.childForceExpandWidth = false;
			hlg.childForceExpandHeight = true;
			hlg.padding = new RectOffset(0, 0, 0, 0);
		AddPaletteButton(row.transform, "Refresh", new Color(0.3f, 0.4f, 0.35f, 1f), () =>
		{
			if (ColorPalette_MGR.instance == null) return;
			if (ColorPalette_MGR.instance.ReloadCurrentPalette() && Viewport_StatusText.instance != null)
				Viewport_StatusText.instance.ShowStatusText("Palette reloaded: " + ColorPalette_MGR.instance.CurrentPaletteName, false, 2f, false);
			else if (Viewport_StatusText.instance != null)
				Viewport_StatusText.instance.ShowStatusText(ColorPalette_MGR.instance.HasPalette ? "Reload failed (file missing or invalid?)" : "No palette loaded to refresh", false, 2f, false);
		});
		AddPaletteButton(row.transform, "Load ASE/ACO/GPL…", new Color(0.35f, 0.45f, 0.5f, 1f), () =>
		{
			if (ColorPalette_MGR.instance == null)
			{
				var mgrGo = new GameObject("ColorPalette_MGR_Runtime");
				mgrGo.AddComponent<ColorPalette_MGR>();
			}
			ColorPalette_MGR.instance?.OpenLoadPaletteDialog();
		});
		AddPaletteButton(row.transform, "Add to current palette…", new Color(0.4f, 0.38f, 0.5f, 1f), () =>
		{
			if (ColorPalette_MGR.instance == null)
			{
				var mgrGo = new GameObject("ColorPalette_MGR_Runtime");
				mgrGo.AddComponent<ColorPalette_MGR>();
			}
			ColorPalette_MGR.instance?.OpenAddPaletteDialog();
		});
			// Square +/- buttons: add swatch (current brush color) or remove selected swatch
			AddPaletteSquareButton(row.transform, "+", new Color(0.25f, 0.45f, 0.3f, 1f), () =>
			{
				if (ColorPalette_MGR.instance == null) return;
				var brushColors = FindObjectOfType<BrushRibbon_UI_Colors>(true);
				Color c = brushColors != null ? brushColors._brushColor : Color.gray;
				ColorPalette_MGR.instance.AddSwatch(c);
				if (Viewport_StatusText.instance != null)
					Viewport_StatusText.instance.ShowStatusText("Swatch added", false, 1.5f, false);
			});
			AddPaletteSquareButton(row.transform, "−", new Color(0.5f, 0.25f, 0.25f, 1f), () =>
			{
				var swatches = FindObjectOfType<PaletteSwatches_UI>(true);
				if (swatches != null && swatches.SelectedSwatchIndex >= 0)
				{
					swatches.RemoveSelectedSwatch();
					if (Viewport_StatusText.instance != null)
						Viewport_StatusText.instance.ShowStatusText("Swatch removed", false, 1.5f, false);
				}
				else if (Viewport_StatusText.instance != null)
					Viewport_StatusText.instance.ShowStatusText("Select a swatch first", false, 1.5f, false);
			});
		}

		static void AddPaletteButton(Transform parent, string label, Color bgColor, System.Action onClick)
		{
			var go = new GameObject("Btn_" + label.Replace(" ", ""));
			go.transform.SetParent(parent, false);
			var rect = go.AddComponent<RectTransform>();
			rect.sizeDelta = new Vector2(140, 24);
			var le = go.AddComponent<LayoutElement>();
			le.preferredWidth = 140;
			le.preferredHeight = 24;
			var img = go.AddComponent<Image>();
			img.color = bgColor;
			img.raycastTarget = true;
			var btn = go.AddComponent<Button>();
			btn.targetGraphic = img;
			btn.onClick.AddListener(() => onClick?.Invoke());
			var txtGo = new GameObject("Text");
			txtGo.transform.SetParent(go.transform, false);
			var txtRect = txtGo.AddComponent<RectTransform>();
			txtRect.anchorMin = Vector2.zero;
			txtRect.anchorMax = Vector2.one;
			txtRect.offsetMin = Vector2.zero;
			txtRect.offsetMax = Vector2.zero;
			var txt = txtGo.AddComponent<TextMeshProUGUI>();
			txt.text = label;
			txt.fontSize = 11;
			txt.color = Color.white;
			txt.alignment = TextAlignmentOptions.Center;
			txt.raycastTarget = false;
		}

		/// <summary> Adds a square button (e.g. + or −) to the palette row. </summary>
		static void AddPaletteSquareButton(Transform parent, string symbol, Color bgColor, System.Action onClick)
		{
			const int size = 24;
			var go = new GameObject("Btn_" + (symbol == "−" ? "Minus" : "Plus"));
			go.transform.SetParent(parent, false);
			var rect = go.AddComponent<RectTransform>();
			rect.sizeDelta = new Vector2(size, size);
			var le = go.AddComponent<LayoutElement>();
			le.preferredWidth = size;
			le.preferredHeight = size;
			var img = go.AddComponent<Image>();
			img.color = bgColor;
			img.raycastTarget = true;
			var btn = go.AddComponent<Button>();
			btn.targetGraphic = img;
			btn.onClick.AddListener(() => onClick?.Invoke());
			var txtGo = new GameObject("Text");
			txtGo.transform.SetParent(go.transform, false);
			var txtRect = txtGo.AddComponent<RectTransform>();
			txtRect.anchorMin = Vector2.zero;
			txtRect.anchorMax = Vector2.one;
			txtRect.offsetMin = Vector2.zero;
			txtRect.offsetMax = Vector2.zero;
			var txt = txtGo.AddComponent<TextMeshProUGUI>();
			txt.text = symbol;
			txt.fontSize = 14;
			txt.color = Color.white;
			txt.alignment = TextAlignmentOptions.Center;
			txt.raycastTarget = false;
		}

		static PaletteSwatches_UI CreatePaletteSwatchesRuntime(RectTransform parent)
		{
			if (ColorPalette_MGR.instance == null)
			{
				var mgrGo = new GameObject("ColorPalette_MGR_Runtime");
				mgrGo.AddComponent<ColorPalette_MGR>();
			}
			var go = new GameObject("PaletteSwatches_Runtime");
			go.transform.SetParent(parent, false);
			var rect = go.AddComponent<RectTransform>();
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
			var glg = go.AddComponent<GridLayoutGroup>();
			glg.cellSize = new Vector2(24, 24);
			glg.spacing = new Vector2(2, 2);
			glg.constraint = GridLayoutGroup.Constraint.Flexible;
			glg.padding = new RectOffset(4, 4, 4, 4);
			var swatches = go.AddComponent<PaletteSwatches_UI>();
			return swatches;
		}

		static void CreateToolOptionsRuntime(RectTransform parent)
		{
			var rowGo = new GameObject("ToolOptionsRow");
			rowGo.transform.SetParent(parent, false);
			var rowRect = rowGo.AddComponent<RectTransform>();
			rowRect.sizeDelta = new Vector2(0, 0);
			var rowLE = rowGo.AddComponent<LayoutElement>();
			rowLE.flexibleWidth = 1;
			rowLE.flexibleHeight = 1;
			var glg = rowGo.AddComponent<GridLayoutGroup>();
			glg.cellSize = new Vector2(80, 28);
			glg.spacing = new Vector2(4, 4);
			glg.constraint = GridLayoutGroup.Constraint.Flexible;
			glg.padding = new RectOffset(2, 2, 2, 2);
			glg.childAlignment = TextAnchor.UpperLeft;

			MakeToolButton(rowGo.transform, "Bucket Fill", "Ctrl+F", new Color(0.28f, 0.38f, 0.5f, 1f),
				() => { BrushRibbon_UI_BucketFill._Act_onClicked?.Invoke(); ShowToolFeedback("Bucket Fill"); });
			MakeToolButton(rowGo.transform, "Invert Mask", "", new Color(0.4f, 0.35f, 0.5f, 1f),
				() => { BrushRibbon_UI_InvertMask.onClicked?.Invoke(); ShowToolFeedback("Invert Mask"); });
			MakeToolButton(rowGo.transform, "Clear Mask", "Ctrl+E", new Color(0.5f, 0.28f, 0.28f, 1f),
				() => { BrushRibbon_UI_DeleteButton.onClicked?.Invoke(); ShowToolFeedback("Clear Mask"); });
			MakeDepthLimitToggle(rowGo.transform);
			MakeDepthLimitSlider(rowGo.transform);
		}

		static void ShowToolFeedback(string toolName)
		{
			if (Viewport_StatusText.instance != null)
				Viewport_StatusText.instance.ShowStatusText(toolName + " triggered.", false, 1.2f, false);
			else
				UnityEngine.Debug.Log("[Paint Tab] " + toolName + " triggered.");
		}

		static void MakeToolButton(Transform parent, string label, string shortcut, Color bgColor, UnityEngine.Events.UnityAction onClick)
		{
			var go = new GameObject("Btn_" + label.Replace(" ", ""));
			go.transform.SetParent(parent, false);
			go.AddComponent<RectTransform>();
			var img = go.AddComponent<Image>();
			img.color = bgColor;
			img.raycastTarget = true;
			var btn = go.AddComponent<Button>();
			btn.onClick.AddListener(onClick);
			var colors = btn.colors;
			colors.highlightedColor = new Color(bgColor.r + 0.15f, bgColor.g + 0.15f, bgColor.b + 0.15f, 1f);
			colors.pressedColor = new Color(bgColor.r + 0.25f, bgColor.g + 0.25f, bgColor.b + 0.25f, 1f);
			btn.colors = colors;

			var txtGo = new GameObject("Label");
			txtGo.transform.SetParent(go.transform, false);
			var txtRect = txtGo.AddComponent<RectTransform>();
			txtRect.anchorMin = Vector2.zero;
			txtRect.anchorMax = Vector2.one;
			txtRect.offsetMin = new Vector2(4, 0);
			txtRect.offsetMax = new Vector2(-4, 0);
			var txt = txtGo.AddComponent<TextMeshProUGUI>();
			string display = string.IsNullOrEmpty(shortcut) ? label : label + "\n<size=8>" + shortcut + "</size>";
			txt.text = display;
			txt.fontSize = 10;
			txt.color = Color.white;
			txt.alignment = TextAlignmentOptions.Center;
			txt.raycastTarget = false;
			txt.enableWordWrapping = true;
			txt.overflowMode = TextOverflowModes.Ellipsis;
		}

		static void MakeDepthLimitToggle(Transform parent)
		{
			Color offCol = new Color(0.3f, 0.3f, 0.3f, 1f);
			Color onCol = new Color(0.2f, 0.55f, 0.35f, 1f);

			var go = new GameObject("Btn_DepthLimit");
			go.transform.SetParent(parent, false);
			go.AddComponent<RectTransform>();
			var img = go.AddComponent<Image>();
			img.color = offCol;
			img.raycastTarget = true;
			var btn = go.AddComponent<Button>();

			var txtGo = new GameObject("Label");
			txtGo.transform.SetParent(go.transform, false);
			var txtRect = txtGo.AddComponent<RectTransform>();
			txtRect.anchorMin = Vector2.zero;
			txtRect.anchorMax = Vector2.one;
			txtRect.offsetMin = new Vector2(4, 0);
			txtRect.offsetMax = new Vector2(-4, 0);
			var txt = txtGo.AddComponent<TextMeshProUGUI>();
			txt.fontSize = 10;
			txt.color = Color.white;
			txt.alignment = TextAlignmentOptions.Center;
			txt.raycastTarget = false;
			txt.enableWordWrapping = true;

			System.Action refreshButtonState = () =>
			{
				var ribbon = SD_WorkflowOptionsRibbon_UI.instance;
				bool isOn = ribbon != null && ribbon.brushDepthLimit01 > 0f;
				img.color = isOn ? onCol : offCol;
				txt.text = isOn ? "Depth Limit\n<size=8>ON</size>" : "Depth Limit\n<size=8>OFF</size>";
			};
			refreshButtonState();

			btn.onClick.AddListener(() =>
			{
				var ribbon = SD_WorkflowOptionsRibbon_UI.instance;
				if (ribbon == null) return;
				bool isOn = ribbon.brushDepthLimit01 > 0f;
				if (isOn)
				{
					ribbon.SetBrushDepthLimit(0f);
					ShowToolFeedback("Depth limit OFF — classic painting");
				}
				else
				{
					ribbon.SetBrushDepthLimit(SD_WorkflowOptionsRibbon_UI.DepthLimitDefaultRange);
					ShowToolFeedback("Depth limit ON — adjust slider for tight/loose");
				}
				refreshButtonState();
				SyncDepthLimitSliderFromRibbon(parent);
			});
		}

		/// <summary>Find the Depth Limit slider in the same tool row and set its value from ribbon (for toggle or init).</summary>
		static void SyncDepthLimitSliderFromRibbon(Transform toolRowTransform)
		{
			var ribbon = SD_WorkflowOptionsRibbon_UI.instance;
			if (ribbon == null) return;
			var slider = toolRowTransform.GetComponentInChildren<Slider>(true);
			if (slider != null && slider.gameObject.name.Contains("DepthLimit"))
			{
				slider.SetValueWithoutNotify(ribbon.GetBrushDepthLimitSlider01());
			}
		}

		/// <summary>Depth limit range slider: 0 = off, 0.01–1 = tight to loose. Gives user flexibility.</summary>
		static void MakeDepthLimitSlider(Transform parent)
		{
			var go = new GameObject("DepthLimitSlider");
			go.transform.SetParent(parent, false);
			go.AddComponent<RectTransform>();
			var le = go.AddComponent<LayoutElement>();
			le.minWidth = 80;
			le.preferredWidth = 80;

			var bg = go.AddComponent<Image>();
			bg.color = new Color(0.22f, 0.28f, 0.35f, 0.95f);
			bg.raycastTarget = true;

			var slider = go.AddComponent<Slider>();
			slider.minValue = 0f;
			slider.maxValue = 1f;
			slider.wholeNumbers = false;
			slider.fillRect = null;
			slider.handleRect = null;
			slider.direction = Slider.Direction.LeftToRight;
			slider.transition = Selectable.Transition.None;

			var fillArea = new GameObject("Fill Area");
			fillArea.transform.SetParent(go.transform, false);
			var fillAreaRect = fillArea.AddComponent<RectTransform>();
			fillAreaRect.anchorMin = new Vector2(0, 0.25f);
			fillAreaRect.anchorMax = new Vector2(1, 0.75f);
			fillAreaRect.offsetMin = new Vector2(4, 0);
			fillAreaRect.offsetMax = new Vector2(-4, 0);
			var fill = new GameObject("Fill");
			fill.transform.SetParent(fillArea.transform, false);
			var fillRect = fill.AddComponent<RectTransform>();
			fillRect.anchorMin = Vector2.zero;
			fillRect.anchorMax = Vector2.one;
			fillRect.offsetMin = Vector2.zero;
			fillRect.offsetMax = Vector2.zero;
			var fillImg = fill.AddComponent<Image>();
			fillImg.color = new Color(0.2f, 0.5f, 0.35f, 0.8f);
			slider.fillRect = fillRect;
			var handleArea = new GameObject("Handle Slide Area");
			handleArea.transform.SetParent(go.transform, false);
			var handleAreaRect = handleArea.AddComponent<RectTransform>();
			handleAreaRect.anchorMin = new Vector2(0, 0);
			handleAreaRect.anchorMax = new Vector2(1, 1);
			handleAreaRect.offsetMin = new Vector2(4, 0);
			handleAreaRect.offsetMax = new Vector2(-4, 0);
			var handle = new GameObject("Handle");
			handle.transform.SetParent(handleArea.transform, false);
			var handleRect = handle.AddComponent<RectTransform>();
			handleRect.sizeDelta = new Vector2(8, 20);
			var handleImg = handle.AddComponent<Image>();
			handleImg.color = Color.white;
			slider.handleRect = handleRect;
			slider.targetGraphic = handleImg;

			var labelGo = new GameObject("Label");
			labelGo.transform.SetParent(go.transform, false);
			var labelRect = labelGo.AddComponent<RectTransform>();
			labelRect.anchorMin = Vector2.zero;
			labelRect.anchorMax = Vector2.one;
			labelRect.offsetMin = new Vector2(2, 0);
			labelRect.offsetMax = new Vector2(-2, 0);
			var label = labelGo.AddComponent<TextMeshProUGUI>();
			label.text = "Depth";
			label.fontSize = 9;
			label.color = new Color(0.9f, 0.9f, 0.9f, 1f);
			label.alignment = TextAlignmentOptions.Left;
			label.raycastTarget = false;

			var ribbon = SD_WorkflowOptionsRibbon_UI.instance;
			if (ribbon != null)
				slider.SetValueWithoutNotify(ribbon.GetBrushDepthLimitSlider01());

			slider.onValueChanged.AddListener((float v) =>
			{
				SD_WorkflowOptionsRibbon_UI.instance?.SetBrushDepthLimitFromSlider01(v);
				SyncDepthLimitButtonState(parent);
			});
		}

		static void SyncDepthLimitButtonState(Transform toolRowTransform)
		{
			foreach (var btn in toolRowTransform.GetComponentsInChildren<Button>(true))
			{
				if (!btn.gameObject.name.Contains("Btn_DepthLimit")) continue;
				var img = btn.GetComponent<Image>();
				var txt = btn.GetComponentInChildren<TextMeshProUGUI>();
				if (img == null || txt == null) continue;
				var ribbon = SD_WorkflowOptionsRibbon_UI.instance;
				bool isOn = ribbon != null && ribbon.brushDepthLimit01 > 0f;
				img.color = isOn ? new Color(0.2f, 0.55f, 0.35f, 1f) : new Color(0.3f, 0.3f, 0.3f, 1f);
				txt.text = isOn ? "Depth Limit\n<size=8>ON</size>" : "Depth Limit\n<size=8>OFF</size>";
				break;
			}
		}

		static void EnsureLayoutElement(RectTransform rect, float flexibleWidth)
		{
			if (rect == null) return;
			var le = rect.GetComponent<LayoutElement>();
			if (le == null) le = rect.gameObject.AddComponent<LayoutElement>();
			le.flexibleWidth = flexibleWidth;
		}
	}
}

using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace spz {

	/// <summary>
	/// Paint-tab Value Assist UI — same Tool Options pattern as Brush options:
	/// compact row button ("Value Assist ▼") + full-width panel under the section when open.
	/// </summary>
	public sealed class PaintTab_ValueAssistPanel_UI : MonoBehaviour {

		const string RootName = "ValueAssistPanel";
		const string ExpandoName = "ValueAssistExpando";
		const float FontSize = 9.5f;
		const float DialRing = 18f;
		const float DialHit = 22f;
		const int UiChromeVersion = 8;

		// Match Brush options: start closed so the tool row stays usable.
		static bool _sessionCollapsed = true;
		static int _builtChromeVersion;

		IValuePaintAssist _assist;
		string _assistWhich = "";
		ValuePaintProposal _proposal;
		bool _hasProposal;

		TextMeshProUGUI _summaryTmp;
		TextMeshProUGUI _statusTmp;
		TextMeshProUGUI _headerLbl;
		Image _swatchImg;
		Button _headerBtn;
		Button _proposeBtn;
		Button _acceptBtn;
		Button _dismissBtn;
		Toggle _enabledToggle;
		Toggle _neuralToggle;
		Toggle _hardnessToggle;
		Toggle _liveToggle;
		ValueDial _blendDial;
		ValueDial _sizeDial;
		ValueDial _opacityDial;
		GameObject _bodyRoot;
		GameObject _knobRow;
		LayoutElement _panelLe;
		bool _suppressToggleSync;
		bool _proposalFromNeural;
		bool _haveSyncedNeuralPref;
		bool _collapsed;
		bool _headerWired;

		public static PaintTab_ValueAssistPanel_UI EnsureUnder(RectTransform toolOptionsSection) {
			if (toolOptionsSection == null) return null;

			Transform toolRow = null;
			for (int i = 0; i < toolOptionsSection.childCount; i++) {
				var ch = toolOptionsSection.GetChild(i);
				if (ch == null) continue;
				if (ch.name == "ToolOptionsRow") {
					toolRow = ch;
					var rowLe = ch.GetComponent<LayoutElement>();
					if (rowLe != null && rowLe.flexibleHeight > 0f)
						rowLe.flexibleHeight = 0f;
				}
			}
			if (toolRow == null) return null;

			EnsureExpandoButton(toolRow, out Button headerBtn, out TextMeshProUGUI headerLbl);

			PaintTab_ValueAssistPanel_UI panel = null;
			for (int i = 0; i < toolOptionsSection.childCount; i++) {
				var ch = toolOptionsSection.GetChild(i);
				if (ch == null || ch.name != RootName) continue;
				panel = ch.GetComponent<PaintTab_ValueAssistPanel_UI>();
				if (panel == null)
					panel = ch.gameObject.AddComponent<PaintTab_ValueAssistPanel_UI>();
				break;
			}
			if (panel == null) {
				var go = new GameObject(RootName);
				go.transform.SetParent(toolOptionsSection, false);
				go.transform.SetAsLastSibling();
				go.AddComponent<RectTransform>();
				panel = go.AddComponent<PaintTab_ValueAssistPanel_UI>();
			}

			panel.BindExpando(headerBtn, headerLbl);
			// Keep instance flag aligned with session before ApplyCollapsedChrome (default field is false).
			panel.SyncCollapsedFromSession();
			bool needsRebuild = panel.NeedsChromeRebuild();
			// Only activate for a real rebuild — activating every CollectNow flashed the panel and
			// jumped Tool Options scroll while collapsed (EnsureLayoutShell forced preferredHeight=-1).
			if (needsRebuild) {
				if (!panel.gameObject.activeSelf)
					panel.gameObject.SetActive(true);
				panel.BuildUi();
			}
			if (panel.gameObject.activeSelf) {
				panel.SyncControlsFromStore();
				panel.ApplyEnabledChrome();
				panel.RefreshStatusLine();
			} else {
				panel.RefreshHeaderLabel();
			}
			panel.ApplyCollapsedChrome();
			return panel;
		}

		void SyncCollapsedFromSession() {
			_collapsed = _sessionCollapsed;
		}

		bool NeedsChromeRebuild() {
			return _blendDial == null || _bodyRoot == null || _builtChromeVersion < UiChromeVersion;
		}

		static void EnsureExpandoButton(Transform toolRow, out Button headerBtn, out TextMeshProUGUI headerLbl) {
			headerBtn = null;
			headerLbl = null;
			Transform expando = null;
			for (int i = 0; i < toolRow.childCount; i++) {
				var ch = toolRow.GetChild(i);
				if (ch != null && ch.name == ExpandoName) {
					expando = ch;
					break;
				}
			}
			if (expando == null) {
				var root = new GameObject(ExpandoName);
				root.transform.SetParent(toolRow, false);
				root.AddComponent<RectTransform>();
				var rootLe = root.AddComponent<LayoutElement>();
				rootLe.minWidth = 96;
				rootLe.preferredWidth = 104;
				rootLe.flexibleWidth = 0;
				rootLe.minHeight = 28;
				rootLe.preferredHeight = 28;
				rootLe.flexibleHeight = 0;

				var headerGo = new GameObject("ValueAssistHeaderBtn");
				headerGo.transform.SetParent(root.transform, false);
				var headerRt = headerGo.AddComponent<RectTransform>();
				headerRt.anchorMin = Vector2.zero;
				headerRt.anchorMax = Vector2.one;
				headerRt.offsetMin = Vector2.zero;
				headerRt.offsetMax = Vector2.zero;
				var headerImg = headerGo.AddComponent<Image>();
				headerImg.color = new Color(0.25f, 0.32f, 0.4f, 1f);
				headerImg.raycastTarget = true;
				headerBtn = headerGo.AddComponent<Button>();
				headerBtn.targetGraphic = headerImg;
				var headerColors = headerBtn.colors;
				headerColors.highlightedColor = new Color(0.32f, 0.4f, 0.48f, 1f);
				headerColors.pressedColor = new Color(0.2f, 0.26f, 0.34f, 1f);
				headerBtn.colors = headerColors;

				var headerTxtGo = new GameObject("Label");
				headerTxtGo.transform.SetParent(headerGo.transform, false);
				var headerTxtRt = headerTxtGo.AddComponent<RectTransform>();
				headerTxtRt.anchorMin = Vector2.zero;
				headerTxtRt.anchorMax = Vector2.one;
				headerTxtRt.offsetMin = new Vector2(6, 0);
				headerTxtRt.offsetMax = new Vector2(-6, 0);
				headerLbl = headerTxtGo.AddComponent<TextMeshProUGUI>();
				headerLbl.font = TMP_Settings.defaultFontAsset;
				headerLbl.fontSize = 10f;
				headerLbl.color = new Color(0.92f, 0.93f, 0.95f, 1f);
				headerLbl.alignment = TextAlignmentOptions.Left;
				headerLbl.raycastTarget = false;
				headerLbl.text = "Value Assist ▼";
				AttachTip(headerGo, "Value Assist\nExpand for neural / live value brush settings.");
				return;
			}

			headerBtn = expando.GetComponentInChildren<Button>(true);
			headerLbl = expando.GetComponentInChildren<TextMeshProUGUI>(true);
		}

		void BindExpando(Button headerBtn, TextMeshProUGUI headerLbl) {
			if (_headerWired && _headerBtn != null && _headerBtn != headerBtn) {
				_headerBtn.onClick.RemoveListener(ToggleCollapsed);
				_headerWired = false;
			}
			_headerBtn = headerBtn;
			_headerLbl = headerLbl;
			if (_headerBtn != null && !_headerWired) {
				_headerBtn.onClick.AddListener(ToggleCollapsed);
				_headerWired = true;
			}
		}

		static void EnsureLayoutShell(RectTransform rect) {
			if (rect == null) return;
			rect.anchorMin = new Vector2(0, 1);
			rect.anchorMax = new Vector2(1, 1);
			rect.pivot = new Vector2(0.5f, 1);
			var le = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
			le.flexibleWidth = 1f;
			le.flexibleHeight = 0f;
			le.minHeight = 0f;
			// preferredHeight is owned by ApplyCollapsedChrome (open=-1, closed=0) — do not force -1 here.
			var csf = rect.GetComponent<ContentSizeFitter>() ?? rect.gameObject.AddComponent<ContentSizeFitter>();
			csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
			csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
		}

		void RebuildLayoutChain() {
			var rt = transform as RectTransform;
			if (rt != null)
				LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
			var parent = transform.parent as RectTransform;
			if (parent != null)
				LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
			var scroll = GetComponentInParent<ScrollRect>();
			if (scroll != null && scroll.content != null && scroll.content != parent)
				LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
		}

		void OnEnable() {
			PaintTab_ValueAssistOptions.Changed -= OnOptionsChanged;
			PaintTab_ValueAssistOptions.Changed += OnOptionsChanged;
			if (_blendDial != null) {
				SyncControlsFromStore();
				ApplyEnabledChrome();
				RefreshStatusLine();
			}
		}

		void OnDisable() {
			PaintTab_ValueAssistOptions.Changed -= OnOptionsChanged;
		}

		void Update() {
			if (_statusTmp != null) {
				if (ValuePaintLivePredictor.IsLiveActive && ValuePaintLivePredictor.HasLastProposal) {
					var p = ValuePaintLivePredictor.LastProposal;
					string liveLine = "Live " + p.CurrentBin + "→" + p.DesiredBin + " · " + ValuePaintLivePredictor.LastAssistWhich;
					if (_statusTmp.text == null || !_statusTmp.text.StartsWith("Live ") || _statusTmp.text != liveLine)
						_statusTmp.text = liveLine;
					if (_swatchImg != null)
						_swatchImg.color = ValuePaintProposalApplier.GrayForBand(p.DesiredBin);
				} else if (!ValuePaintLivePredictor.IsLiveActive
				           && _statusTmp.text != null && _statusTmp.text.StartsWith("Live ")) {
					// Live turned off — do not leave a stale Live line (Invalidate alone does not refresh UI).
					RefreshStatusLine();
				}
			}
			if (!ValuePaintProposalApplier.IsArmed || _statusTmp == null) return;
			if (!ValuePaintProposalApplier.SawApplyOnArmedTarget) return;
			if (_statusTmp.text != null && _statusTmp.text.IndexOf("stroke applied", System.StringComparison.Ordinal) >= 0)
				return;
			if (_statusTmp.text != null && _statusTmp.text.StartsWith("Live ")) return;
			RefreshStatusLine();
		}

		void OnOptionsChanged() {
			bool preferNeural = PaintTab_ValueAssistOptions.UseNeural;
			if (_assist != null) {
				bool taggedNeuralOff = _assistWhich != null
					&& _assistWhich.IndexOf("neural off", System.StringComparison.Ordinal) >= 0;
				bool taggedMlp = _assistWhich != null
					&& _assistWhich.IndexOf("MlpValuePaintAssist", System.StringComparison.Ordinal) >= 0;
				bool mismatch = preferNeural ? taggedNeuralOff : (taggedMlp || !taggedNeuralOff);
				if (mismatch) {
					_assist = null;
					_assistWhich = "";
				}
			}

			bool keepNeuralStatus = false;
			if (_hasProposal && _haveSyncedNeuralPref && preferNeural != _proposalFromNeural) {
				ClearPendingProposal("Neural mode changed — Propose again.");
				keepNeuralStatus = true;
				ValuePaintLivePredictor.InvalidateAssist();
			}

			_haveSyncedNeuralPref = true;
			SyncControlsFromStore();
			ApplyEnabledChrome();
			if (!PaintTab_ValueAssistOptions.Enabled) {
				ClearPendingProposal(null);
				ValuePaintLivePredictor.InvalidateAssist();
				if (_summaryTmp != null)
					_summaryTmp.text = "Value Assist off — open Value Assist ▼, turn On dial.";
				keepNeuralStatus = false;
			} else if (!PaintTab_ValueAssistOptions.LivePredict) {
				// InvalidateAssist already cleared HasLastProposal; always drop stale Live UI text.
				if (_statusTmp != null && _statusTmp.text != null && _statusTmp.text.StartsWith("Live "))
					_statusTmp.text = "Idle";
			}
			if (!keepNeuralStatus)
				RefreshStatusLine();
			RefreshHeaderLabel();
		}

		void ClearPendingProposal(string statusMsg) {
			_hasProposal = false;
			_proposal = default;
			if (_acceptBtn != null) _acceptBtn.interactable = false;
			if (_swatchImg != null) _swatchImg.color = new Color(0.35f, 0.35f, 0.38f, 1f);
			if (statusMsg != null && _statusTmp != null)
				_statusTmp.text = statusMsg;
		}

		void BuildUi() {
			if (!NeedsChromeRebuild()) {
				EnsureLayoutShell(transform as RectTransform);
				return;
			}
			for (int i = transform.childCount - 1; i >= 0; i--)
				UnityEngine.Object.DestroyImmediate(transform.GetChild(i).gameObject);
			_summaryTmp = null;
			_statusTmp = null;
			_swatchImg = null;
			_proposeBtn = null;
			_acceptBtn = null;
			_dismissBtn = null;
			_enabledToggle = null;
			_neuralToggle = null;
			_hardnessToggle = null;
			_liveToggle = null;
			_blendDial = null;
			_sizeDial = null;
			_opacityDial = null;
			_bodyRoot = null;
			_knobRow = null;
			_collapsed = _sessionCollapsed;
			_builtChromeVersion = UiChromeVersion;

			var t = SpzUiThemeOps.Active;
			var bg = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
			bg.sprite = UiRuntimeSprites.RoundedRectSliced;
			bg.type = Image.Type.Sliced;
			// Match BrushOptsPanel chrome
			bg.color = new Color(0.16f, 0.18f, 0.22f, 0.98f);
			bg.raycastTarget = true;

			var vlg = gameObject.GetComponent<VerticalLayoutGroup>() ?? gameObject.AddComponent<VerticalLayoutGroup>();
			vlg.padding = new RectOffset(6, 6, 6, 6);
			vlg.spacing = 4;
			vlg.childAlignment = TextAnchor.UpperLeft;
			vlg.childControlHeight = true;
			vlg.childControlWidth = true;
			vlg.childForceExpandHeight = false;
			vlg.childForceExpandWidth = true;
			EnsureLayoutShell(transform as RectTransform);
			_panelLe = gameObject.GetComponent<LayoutElement>();

			// Body is the panel itself (header lives in ToolOptionsRow like Brush options).
			_bodyRoot = gameObject;

			var toggleRow = MakeDialRow(transform, "ToggleDials", DialHit + 14f);
			_enabledToggle = MakeBoolDial(toggleRow.transform, "On", PaintTab_ValueAssistOptions.Enabled, isOn => {
				PaintTab_ValueAssistOptions.SetEnabled(isOn);
				ShowFeedback(isOn ? "Value Assist: on" : "Value Assist: off");
			}, "On\nTurn Value Assist on or off.");
			_neuralToggle = MakeBoolDial(toggleRow.transform, "Neural", PaintTab_ValueAssistOptions.UseNeural, isOn => {
				PaintTab_ValueAssistOptions.SetUseNeural(isOn);
				ValuePaintLivePredictor.InvalidateAssist();
				ShowFeedback(isOn ? "Value Assist: neural MLP" : "Value Assist: deterministic stub");
			}, "Neural\nUse the trained neural net (MLP).\nOff = simple fallback.");
			_liveToggle = MakeBoolDial(toggleRow.transform, "Live", PaintTab_ValueAssistOptions.LivePredict, isOn => {
				PaintTab_ValueAssistOptions.SetLivePredict(isOn);
				ShowFeedback(isOn ? "Value Assist: live on" : "Value Assist: live off");
			}, "Live\nWhile you hover or paint, update brush value from under the tip.");
			_hardnessToggle = MakeBoolDial(toggleRow.transform, "Hard", PaintTab_ValueAssistOptions.ApplyHardness, isOn => {
				PaintTab_ValueAssistOptions.SetApplyHardness(isOn);
			}, "Hard\nApply predicted tip hardness (soft / med / hard).");

			_knobRow = MakeDialRow(transform, "ValueDials", DialHit + 16f).gameObject;
			_blendDial = MakeValueDial(_knobRow.transform, "Blend", PaintTab_ValueAssistOptions.Blend01,
				v => PaintTab_ValueAssistOptions.SetBlend01(v),
				"Blend\nHow strongly to pull brush color toward the predicted gray.\n0% = keep yours · 100% = full prediction.");
			_sizeDial = MakeValueDial(_knobRow.transform, "Size", PaintTab_ValueAssistOptions.SizeInfluence01,
				v => PaintTab_ValueAssistOptions.SetSizeInfluence01(v),
				"Size\nHow much predicted brush size overrides yours.\n0% = keep yours · 100% = use prediction.");
			_opacityDial = MakeValueDial(_knobRow.transform, "Opacity", PaintTab_ValueAssistOptions.OpacityInfluence01,
				v => PaintTab_ValueAssistOptions.SetOpacityInfluence01(v),
				"Opacity\nHow much predicted opacity applies on Accept.\n0% = keep yours · 100% = use prediction.");

			_summaryTmp = MakeLabel(transform,
				"Hover dials for tips. Live predicts under tip · Propose/Accept locks a snapshot.",
				8.5f, t.textMuted);
			var summaryLe = _summaryTmp.GetComponent<LayoutElement>() ?? _summaryTmp.gameObject.AddComponent<LayoutElement>();
			summaryLe.minHeight = 20f;
			summaryLe.preferredHeight = 22f;

			var row = new GameObject("Actions");
			row.transform.SetParent(transform, false);
			row.AddComponent<RectTransform>();
			var rowLe = row.AddComponent<LayoutElement>();
			rowLe.minHeight = 24f;
			rowLe.preferredHeight = 24f;
			var hlg = row.AddComponent<HorizontalLayoutGroup>();
			hlg.spacing = 3;
			hlg.childAlignment = TextAnchor.MiddleLeft;
			hlg.childForceExpandWidth = false;
			hlg.childForceExpandHeight = true;
			hlg.childControlWidth = true;
			hlg.childControlHeight = true;

			_swatchImg = MakeSwatch(row.transform);
			_proposeBtn = MakeBtn(row.transform, "Propose", new Color(0.22f, 0.42f, 0.52f, 1f), OnPropose);
			_acceptBtn = MakeBtn(row.transform, "Accept", new Color(0.22f, 0.48f, 0.32f, 1f), OnAccept);
			_dismissBtn = MakeBtn(row.transform, "Dismiss", new Color(0.42f, 0.28f, 0.28f, 1f), OnDismiss);
			AttachTip(_proposeBtn.gameObject, "Propose\nSuggest a value setup from the current brush color.");
			AttachTip(_acceptBtn.gameObject, "Accept\nArm the brush with that suggestion, then paint normally.");
			AttachTip(_dismissBtn.gameObject, "Dismiss\nClear the suggestion and disarm.");
			AttachTip(_swatchImg.gameObject, "Swatch\nPredicted target gray (desired value).");

			_statusTmp = MakeLabel(transform, "Idle", 8.5f, t.textMuted);
			var statusLe = _statusTmp.GetComponent<LayoutElement>() ?? _statusTmp.gameObject.AddComponent<LayoutElement>();
			statusLe.minHeight = 15f;

			_acceptBtn.interactable = false;
			ApplyEnabledChrome();
			RefreshStatusLine();
			RefreshHeaderLabel();
			// Collapse applied by EnsureUnder after BuildUi — do not SetActive(false) mid-build.
		}

		static GameObject MakeDialRow(Transform parent, string name, float height) {
			var row = new GameObject(name);
			row.transform.SetParent(parent, false);
			row.AddComponent<RectTransform>();
			var le = row.AddComponent<LayoutElement>();
			le.minHeight = height;
			le.preferredHeight = height;
			le.flexibleWidth = 1f;
			var h = row.AddComponent<HorizontalLayoutGroup>();
			h.spacing = 6;
			h.padding = new RectOffset(2, 2, 0, 0);
			h.childAlignment = TextAnchor.MiddleLeft;
			h.childControlWidth = false;
			h.childControlHeight = true;
			h.childForceExpandWidth = false;
			h.childForceExpandHeight = false;
			return row;
		}

		void ToggleCollapsed() {
			_collapsed = !_collapsed;
			_sessionCollapsed = _collapsed;
			// Scroll only on user open — not when EnsureUnder reapplies an already-open panel.
			ApplyCollapsedChrome(scrollIntoView: !_collapsed);
		}

		void ApplyCollapsedChrome(bool scrollIntoView = false) {
			bool open = !_collapsed;
			if (_panelLe == null)
				_panelLe = GetComponent<LayoutElement>();
			if (_panelLe != null)
				_panelLe.preferredHeight = open ? -1f : 0f;
			// Same as BrushOptsPanel: hide whole panel when closed (header stays in the tool row).
			if (gameObject.activeSelf != open)
				gameObject.SetActive(open);
			RefreshHeaderLabel();
			var parent = transform.parent as RectTransform;
			if (parent != null)
				LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
			else
				RebuildLayoutChain();
			if (open && scrollIntoView) {
				// Match Brush options: after expand, refresh canvas and scroll so the panel is visible.
				Canvas.ForceUpdateCanvases();
				if (parent != null)
					LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
				var sr = GetComponentInParent<ScrollRect>();
				if (sr != null)
					sr.verticalNormalizedPosition = 0f;
			}
			// Block depth/tools while this panel (or Brush options) is open — same modal row pattern.
			if (parent != null)
				PaintTab_CollectPaintUI.SyncToolOptionsRowModalBlockForSection(parent);
		}

		void RefreshHeaderLabel() {
			if (_headerLbl == null) return;
			// Match Brush options ▼ / ▴ wording.
			_headerLbl.text = _collapsed ? "Value Assist ▼" : "Value Assist ▴";
		}

		void SyncControlsFromStore() {
			_suppressToggleSync = true;
			if (_enabledToggle != null)
				_enabledToggle.SetIsOnWithoutNotify(PaintTab_ValueAssistOptions.Enabled);
			if (_neuralToggle != null)
				_neuralToggle.SetIsOnWithoutNotify(PaintTab_ValueAssistOptions.UseNeural);
			if (_liveToggle != null)
				_liveToggle.SetIsOnWithoutNotify(PaintTab_ValueAssistOptions.LivePredict);
			if (_hardnessToggle != null)
				_hardnessToggle.SetIsOnWithoutNotify(PaintTab_ValueAssistOptions.ApplyHardness);
			_blendDial?.SetValueWithoutNotify(PaintTab_ValueAssistOptions.Blend01);
			_sizeDial?.SetValueWithoutNotify(PaintTab_ValueAssistOptions.SizeInfluence01);
			_opacityDial?.SetValueWithoutNotify(PaintTab_ValueAssistOptions.OpacityInfluence01);
			_suppressToggleSync = false;
			TintBoolDial(_enabledToggle, PaintTab_ValueAssistOptions.Enabled);
			TintBoolDial(_neuralToggle, PaintTab_ValueAssistOptions.UseNeural);
			TintBoolDial(_liveToggle, PaintTab_ValueAssistOptions.LivePredict);
			TintBoolDial(_hardnessToggle, PaintTab_ValueAssistOptions.ApplyHardness);
			RefreshHeaderLabel();
		}

		void ApplyEnabledChrome() {
			bool on = PaintTab_ValueAssistOptions.Enabled;
			if (_knobRow != null) _knobRow.SetActive(on);
			if (_proposeBtn != null) _proposeBtn.interactable = on;
			if (_dismissBtn != null) _dismissBtn.interactable = on;
			if (_acceptBtn != null) _acceptBtn.interactable = on && _hasProposal;
			if (_neuralToggle != null) _neuralToggle.interactable = on;
			if (_liveToggle != null) _liveToggle.interactable = on;
			if (_hardnessToggle != null) _hardnessToggle.interactable = on;
		}

		void OnPropose() {
			if (!PaintTab_ValueAssistOptions.Enabled) {
				SetStatus("Value Assist is off.");
				return;
			}
			EnsureAssist();
			Color sample = CurrentBrushColor();
			_proposal = _assist.ProposeFromColor(sample, default);
			_hasProposal = true;
			_proposalFromNeural = PaintTab_ValueAssistOptions.UseNeural;
			_haveSyncedNeuralPref = true;
			if (_acceptBtn != null) _acceptBtn.interactable = true;
			if (_swatchImg != null)
				_swatchImg.color = ValuePaintProposalApplier.GrayForBand(_proposal.DesiredBin);
			if (_summaryTmp != null) _summaryTmp.text = FormatProposal(_proposal);
			SetStatus("Proposed (" + _assistWhich + ") — Accept to arm brush.");
			ShowFeedback("Value Assist: proposal ready");
		}

		void OnAccept() {
			if (!PaintTab_ValueAssistOptions.Enabled) {
				SetStatus("Value Assist is off.");
				return;
			}
			if (!_hasProposal) {
				SetStatus("Propose first.");
				return;
			}
			bool ok = ValuePaintProposalApplier.TryAccept(_proposal, out string reason);
			if (ok) {
				SetStatus(PaintTab_ValueAssistOptions.ApplyHardness
					? "Armed — color/size/opacity/hardness."
					: "Armed — color/size/opacity (hardness unchanged).");
				ShowFeedback("Value Assist: accepted");
			} else {
				SetStatus("Accept refused — " + reason);
				ShowFeedback("Value Assist: " + reason);
				RefreshStatusLine(keepMessage: true);
			}
		}

		void OnDismiss() {
			ClearPendingProposal(null);
			ValuePaintProposalApplier.ClearArmed();
			if (_summaryTmp != null)
				_summaryTmp.text = "Hover dials for tips. Live predicts under tip · Propose/Accept locks a snapshot.";
			SetStatus("Dismissed.");
			ShowFeedback("Value Assist: cleared");
		}

		void SetStatus(string msg) {
			if (_statusTmp != null) _statusTmp.text = msg;
		}

		void EnsureAssist() {
			if (_assist != null) return;
			_assist = ValuePaintAssistFactory.Create(out _assistWhich);
		}

		void RefreshStatusLine(bool keepMessage = false) {
			if (_statusTmp == null) return;
			if (!PaintTab_ValueAssistOptions.Enabled) {
				_statusTmp.text = "Off";
				return;
			}
			if (keepMessage && !string.IsNullOrEmpty(_statusTmp.text) && _statusTmp.text.StartsWith("Accept refused"))
				return;
			// Live predict owns the status line while active — do not let options Changed
			// (Blend/Size/Opacity drag) overwrite it with Armed every frame.
			if (ValuePaintLivePredictor.IsLiveActive && ValuePaintLivePredictor.HasLastProposal) {
				var p = ValuePaintLivePredictor.LastProposal;
				_statusTmp.text = "Live " + p.CurrentBin + "→" + p.DesiredBin + " · " + ValuePaintLivePredictor.LastAssistWhich;
				return;
			}
			if (ValuePaintProposalApplier.IsArmed) {
				var a = ValuePaintProposalApplier.ArmedProposal;
				_statusTmp.text = "Armed " + a.DesiredBin + " / " + a.StrokeRole
				                  + (ValuePaintProposalApplier.SawApplyOnArmedTarget ? " · stroke applied" : "");
			} else if (!_hasProposal) {
				if (!keepMessage || string.IsNullOrEmpty(_statusTmp.text) || _statusTmp.text == "Dismissed.")
					_statusTmp.text = "Idle";
			}
		}

		static Color CurrentBrushColor() {
			var sd = SD_WorkflowOptionsRibbon_UI.instance;
			if (sd != null) return sd.brushColor;
			var colors = Object.FindObjectOfType<BrushRibbon_UI_Colors>(true);
			return colors != null ? colors._brushColor : new Color(0.5f, 0.5f, 0.5f, 1f);
		}

		static string FormatProposal(ValuePaintProposal p) {
			return p.CurrentBin + " → " + p.DesiredBin
			       + " · " + p.StrokeRole
			       + " · w=" + p.BrushWidthHint01.ToString("F2")
			       + " o=" + p.OpacityHint01.ToString("F2")
			       + " soft=" + p.EdgeSoftness01.ToString("F2")
			       + " · " + (string.IsNullOrEmpty(p.Source) ? "?" : p.Source);
		}

		static void ShowFeedback(string msg) {
			if (Viewport_StatusText.instance != null)
				Viewport_StatusText.instance.ShowStatusText(msg, false, 1.4f, false);
			else
				Debug.Log("[Paint Tab] " + msg);
		}

		static void AttachTip(GameObject go, string tip) {
			if (go == null || string.IsNullOrEmpty(tip)) return;
			var tipUi = go.GetComponent<CanShowTooltip_UI>() ?? go.AddComponent<CanShowTooltip_UI>();
			tipUi.set_overrideMessage(tip);
		}

		Toggle MakeBoolDial(Transform parent, string shortLabel, bool initialOn,
			UnityEngine.Events.UnityAction<bool> onChanged, string tip = null) {
			Color offRing = new Color(0.5f, 0.52f, 0.56f, 1f);
			Color onRing = new Color(0.45f, 0.72f, 0.82f, 1f);
			Color onFill = new Color(0.35f, 0.65f, 0.78f, 1f);

			var col = new GameObject("Dial_" + shortLabel);
			col.transform.SetParent(parent, false);
			col.AddComponent<RectTransform>();
			var colLe = col.AddComponent<LayoutElement>();
			colLe.minWidth = 40f;
			colLe.preferredWidth = 44f;
			colLe.minHeight = DialHit + 12f;
			var v = col.AddComponent<VerticalLayoutGroup>();
			v.spacing = 1;
			v.childAlignment = TextAnchor.UpperCenter;
			v.childControlWidth = true;
			v.childControlHeight = true;
			v.childForceExpandWidth = false;
			v.childForceExpandHeight = false;

			var dialGo = new GameObject("Circle");
			dialGo.transform.SetParent(col.transform, false);
			var dialLe = dialGo.AddComponent<LayoutElement>();
			dialLe.minWidth = DialHit;
			dialLe.preferredWidth = DialHit;
			dialLe.minHeight = DialHit;
			dialLe.preferredHeight = DialHit;
			var hitPad = dialGo.AddComponent<Image>();
			hitPad.color = Color.clear;
			hitPad.raycastTarget = true;

			var ringGo = new GameObject("Ring");
			ringGo.transform.SetParent(dialGo.transform, false);
			var ringRt = ringGo.AddComponent<RectTransform>();
			ringRt.anchorMin = ringRt.anchorMax = ringRt.pivot = new Vector2(0.5f, 0.5f);
			ringRt.sizeDelta = new Vector2(DialRing, DialRing);
			var ringImg = ringGo.AddComponent<Image>();
			ringImg.sprite = UiRuntimeSprites.CircleRing;
			ringImg.preserveAspect = true;
			ringImg.raycastTarget = false;
			ringImg.color = initialOn ? onRing : offRing;

			var fillGo = new GameObject("Fill");
			fillGo.transform.SetParent(ringGo.transform, false);
			var fillRt = fillGo.AddComponent<RectTransform>();
			fillRt.anchorMin = new Vector2(0.28f, 0.28f);
			fillRt.anchorMax = new Vector2(0.72f, 0.72f);
			fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
			var fillImg = fillGo.AddComponent<Image>();
			fillImg.sprite = UiRuntimeSprites.CircleFilled;
			fillImg.preserveAspect = true;
			fillImg.raycastTarget = false;
			fillImg.color = onFill;
			SetFillAlpha(fillImg, initialOn);

			var toggle = dialGo.AddComponent<Toggle>();
			toggle.targetGraphic = hitPad;
			toggle.graphic = null;
			toggle.transition = Selectable.Transition.None;
			toggle.toggleTransition = Toggle.ToggleTransition.None;
			toggle.SetIsOnWithoutNotify(initialOn);
			toggle.onValueChanged.AddListener(isOn => {
				if (_suppressToggleSync) return;
				ringImg.color = isOn ? onRing : offRing;
				SetFillAlpha(fillImg, isOn);
				onChanged?.Invoke(isOn);
			});

			var lblGo = new GameObject("Lbl");
			lblGo.transform.SetParent(col.transform, false);
			var lblLe = lblGo.AddComponent<LayoutElement>();
			lblLe.minHeight = 11f;
			lblLe.preferredHeight = 11f;
			var tmp = lblGo.AddComponent<TextMeshProUGUI>();
			tmp.font = TMP_Settings.defaultFontAsset;
			tmp.fontSize = 8f;
			tmp.color = new Color(0.85f, 0.87f, 0.9f, 1f);
			tmp.alignment = TextAlignmentOptions.Center;
			tmp.raycastTarget = false;
			tmp.enableWordWrapping = false;
			tmp.text = shortLabel;
			// Tip must live on the raycast target (Circle), not the column — EventSystem never hits col.
			if (!string.IsNullOrEmpty(tip))
				AttachTip(dialGo, tip);
			return toggle;
		}

		ValueDial MakeValueDial(Transform parent, string shortLabel, float initial,
			System.Action<float> onChanged, string tip = null) {
			var col = new GameObject("VDial_" + shortLabel);
			col.transform.SetParent(parent, false);
			col.AddComponent<RectTransform>();
			var colLe = col.AddComponent<LayoutElement>();
			colLe.minWidth = 48f;
			colLe.preferredWidth = 52f;
			colLe.minHeight = 40f;
			var v = col.AddComponent<VerticalLayoutGroup>();
			v.spacing = 1;
			v.childAlignment = TextAnchor.UpperCenter;
			v.childControlWidth = true;
			v.childControlHeight = true;
			v.childForceExpandWidth = false;
			v.childForceExpandHeight = false;

			var dialGo = new GameObject("Dial");
			dialGo.transform.SetParent(col.transform, false);
			var dialLe = dialGo.AddComponent<LayoutElement>();
			dialLe.minWidth = DialHit;
			dialLe.preferredWidth = DialHit;
			dialLe.minHeight = DialHit;
			dialLe.preferredHeight = DialHit;
			var hit = dialGo.AddComponent<Image>();
			hit.color = Color.clear;
			hit.raycastTarget = true;

			var trackGo = new GameObject("Track");
			trackGo.transform.SetParent(dialGo.transform, false);
			var trackRt = trackGo.AddComponent<RectTransform>();
			trackRt.anchorMin = trackRt.anchorMax = trackRt.pivot = new Vector2(0.5f, 0.5f);
			trackRt.sizeDelta = new Vector2(DialRing, DialRing);
			var trackImg = trackGo.AddComponent<Image>();
			trackImg.sprite = UiRuntimeSprites.CircleRing;
			trackImg.preserveAspect = true;
			trackImg.raycastTarget = false;
			trackImg.color = new Color(0.4f, 0.42f, 0.46f, 1f);

			var fillGo = new GameObject("Fill");
			fillGo.transform.SetParent(dialGo.transform, false);
			var fillRt = fillGo.AddComponent<RectTransform>();
			fillRt.anchorMin = fillRt.anchorMax = fillRt.pivot = new Vector2(0.5f, 0.5f);
			fillRt.sizeDelta = new Vector2(DialRing, DialRing);
			var fillImg = fillGo.AddComponent<Image>();
			fillImg.sprite = UiRuntimeSprites.CircleFilled;
			fillImg.type = Image.Type.Filled;
			fillImg.fillMethod = Image.FillMethod.Radial360;
			fillImg.fillOrigin = (int)Image.Origin360.Top;
			fillImg.fillClockwise = true;
			fillImg.preserveAspect = true;
			fillImg.raycastTarget = false;
			fillImg.color = new Color(0.35f, 0.62f, 0.72f, 0.95f);
			fillImg.fillAmount = Mathf.Clamp01(initial);

			var pctGo = new GameObject("Pct");
			pctGo.transform.SetParent(dialGo.transform, false);
			var pctRt = pctGo.AddComponent<RectTransform>();
			pctRt.anchorMin = Vector2.zero;
			pctRt.anchorMax = Vector2.one;
			pctRt.offsetMin = pctRt.offsetMax = Vector2.zero;
			var pctTmp = pctGo.AddComponent<TextMeshProUGUI>();
			pctTmp.font = TMP_Settings.defaultFontAsset;
			pctTmp.fontSize = 7.5f;
			pctTmp.color = new Color(0.95f, 0.96f, 0.98f, 1f);
			pctTmp.alignment = TextAlignmentOptions.Center;
			pctTmp.raycastTarget = false;
			pctTmp.text = Mathf.RoundToInt(Mathf.Clamp01(initial) * 100).ToString();

			var nameGo = new GameObject("Name");
			nameGo.transform.SetParent(col.transform, false);
			var nameLe = nameGo.AddComponent<LayoutElement>();
			nameLe.minHeight = 11f;
			nameLe.preferredHeight = 11f;
			var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
			nameTmp.font = TMP_Settings.defaultFontAsset;
			nameTmp.fontSize = 8f;
			nameTmp.color = new Color(0.85f, 0.87f, 0.9f, 1f);
			nameTmp.alignment = TextAlignmentOptions.Center;
			nameTmp.raycastTarget = false;
			nameTmp.text = shortLabel;

			var dial = dialGo.AddComponent<ValueDial>();
			dial.Bind(fillImg, pctTmp, initial, v01 => {
				if (_suppressToggleSync) return;
				onChanged?.Invoke(v01);
			});
			// Tip must live on the raycast target (Dial), not the column — EventSystem never hits col.
			if (!string.IsNullOrEmpty(tip))
				AttachTip(dialGo, tip);
			return dial;
		}

		static void SetFillAlpha(Image fill, bool on) {
			if (fill == null) return;
			var c = fill.color;
			c.a = on ? 1f : 0f;
			fill.color = c;
		}

		static void TintBoolDial(Toggle toggle, bool on) {
			if (toggle == null) return;
			var ringT = toggle.transform.Find("Ring");
			if (ringT == null) return;
			var ringImg = ringT.GetComponent<Image>();
			if (ringImg != null)
				ringImg.color = on
					? new Color(0.45f, 0.72f, 0.82f, 1f)
					: new Color(0.5f, 0.52f, 0.56f, 1f);
			var fillT = ringT.Find("Fill");
			if (fillT != null)
				SetFillAlpha(fillT.GetComponent<Image>(), on);
		}

		static TextMeshProUGUI MakeLabel(Transform parent, string text, float size, Color color) {
			var go = new GameObject("Label");
			go.transform.SetParent(parent, false);
			go.AddComponent<RectTransform>();
			var tmp = go.AddComponent<TextMeshProUGUI>();
			tmp.font = TMP_Settings.defaultFontAsset;
			tmp.fontSize = size;
			tmp.color = color;
			tmp.enableWordWrapping = true;
			tmp.overflowMode = TextOverflowModes.Ellipsis;
			tmp.alignment = TextAlignmentOptions.Left;
			tmp.raycastTarget = false;
			tmp.text = text;
			return tmp;
		}

		static Image MakeSwatch(Transform parent) {
			var go = new GameObject("DesiredSwatch");
			go.transform.SetParent(parent, false);
			go.AddComponent<RectTransform>();
			var le = go.AddComponent<LayoutElement>();
			le.minWidth = 18f;
			le.preferredWidth = 18f;
			le.minHeight = 18f;
			var img = go.AddComponent<Image>();
			img.sprite = UiRuntimeSprites.CircleFilled;
			img.preserveAspect = true;
			img.color = new Color(0.35f, 0.35f, 0.38f, 1f);
			img.raycastTarget = true; // needed for hover tooltip
			return img;
		}

		static Button MakeBtn(Transform parent, string label, Color bg, UnityEngine.Events.UnityAction onClick) {
			var go = new GameObject("Btn_" + label);
			go.transform.SetParent(parent, false);
			go.AddComponent<RectTransform>();
			var le = go.AddComponent<LayoutElement>();
			le.minWidth = 52f;
			le.preferredWidth = 56f;
			le.minHeight = 22f;
			var img = go.AddComponent<Image>();
			img.sprite = UiRuntimeSprites.RoundedRectSliced;
			img.type = Image.Type.Sliced;
			img.color = bg;
			img.raycastTarget = true;
			var btn = go.AddComponent<Button>();
			btn.targetGraphic = img;
			btn.onClick.AddListener(onClick);
			var colors = btn.colors;
			colors.normalColor = Color.white;
			colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
			colors.pressedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
			colors.selectedColor = Color.white;
			colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.55f);
			btn.colors = colors;

			var txtGo = new GameObject("Label");
			txtGo.transform.SetParent(go.transform, false);
			var tr = txtGo.AddComponent<RectTransform>();
			tr.anchorMin = Vector2.zero;
			tr.anchorMax = Vector2.one;
			tr.offsetMin = new Vector2(2, 0);
			tr.offsetMax = new Vector2(-2, 0);
			var tmp = txtGo.AddComponent<TextMeshProUGUI>();
			tmp.font = TMP_Settings.defaultFontAsset;
			tmp.fontSize = FontSize;
			tmp.color = Color.white;
			tmp.alignment = TextAlignmentOptions.Center;
			tmp.raycastTarget = false;
			tmp.text = label;
			return btn;
		}

		/// <summary>Compact drag dial: horizontal drag adjusts 0–1; radial fill shows value.</summary>
		public sealed class ValueDial : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler {
			Image _fill;
			TextMeshProUGUI _pct;
			System.Action<float> _onChanged;
			float _value;
			float _dragStartX;
			float _dragStartVal;
			bool _dragMoved;

			public float Value => _value;

			public void Bind(Image fill, TextMeshProUGUI pct, float initial, System.Action<float> onChanged) {
				_fill = fill;
				_pct = pct;
				_onChanged = onChanged;
				SetValueWithoutNotify(initial);
			}

			public void SetValueWithoutNotify(float v01) {
				_value = Mathf.Clamp01(v01);
				ApplyVisual();
			}

			void ApplyVisual() {
				if (_fill != null) _fill.fillAmount = _value;
				if (_pct != null) _pct.text = Mathf.RoundToInt(_value * 100).ToString();
			}

			public void OnBeginDrag(PointerEventData eventData) {
				// Any drag gesture (even sub-threshold movement after BeginDrag) must suppress the
				// following PointerClick — otherwise a tiny drag still snaps to 0/50/100.
				_dragMoved = true;
				_dragStartX = eventData.position.x;
				_dragStartVal = _value;
			}

			public void OnDrag(PointerEventData eventData) {
				float dx = eventData.position.x - _dragStartX;
				float next = Mathf.Clamp01(_dragStartVal + dx / 120f);
				if (Mathf.Approximately(next, _value)) return;
				_value = next;
				ApplyVisual();
				_onChanged?.Invoke(_value);
			}

			public void OnEndDrag(PointerEventData eventData) {
				// Keep _dragMoved until click is processed this frame; clear next frame.
				StartCoroutine(ClearDragMovedNextFrame());
			}

			System.Collections.IEnumerator ClearDragMovedNextFrame() {
				yield return null;
				_dragMoved = false;
			}

			public void OnPointerClick(PointerEventData eventData) {
				// After a drag, Unity often still fires click with dragging=false — do not cycle value.
				if (_dragMoved || eventData.dragging) return;
				// Click cycles 0 / 50 / 100 for quick set.
				if (_value < 0.25f) _value = 0.5f;
				else if (_value < 0.75f) _value = 1f;
				else _value = 0f;
				ApplyVisual();
				_onChanged?.Invoke(_value);
			}
		}
	}
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace spz {

	/// <summary>
	/// Paint-tab review UI for smart-value-paint (Spec R3): enable/settings + Propose → Accept/Dismiss.
	/// Accept arms the existing ribbon via <see cref="ValuePaintProposalApplier.TryAccept"/>; strokes stay on the normal paint path.
	/// </summary>
	public sealed class PaintTab_ValueAssistPanel_UI : MonoBehaviour {

		const string RootName = "ValueAssistPanel";
		const float FontSize = 10f;

		IValuePaintAssist _assist;
		string _assistWhich = "";
		ValuePaintProposal _proposal;
		bool _hasProposal;

		TextMeshProUGUI _summaryTmp;
		TextMeshProUGUI _statusTmp;
		Image _swatchImg;
		Button _proposeBtn;
		Button _acceptBtn;
		Button _dismissBtn;
		Toggle _enabledToggle;
		Toggle _neuralToggle;
		Toggle _hardnessToggle;
		Toggle _liveToggle;
		Slider _blendSlider;
		Slider _sizeInfSlider;
		Slider _opacityInfSlider;
		TextMeshProUGUI _blendLbl;
		TextMeshProUGUI _sizeInfLbl;
		TextMeshProUGUI _opacityInfLbl;
		GameObject _controlsRoot;
		bool _suppressToggleSync;
		bool _proposalFromNeural;
		bool _haveSyncedNeuralPref;

		public static PaintTab_ValueAssistPanel_UI EnsureUnder(RectTransform toolOptionsSection) {
			if (toolOptionsSection == null) return null;
			for (int i = 0; i < toolOptionsSection.childCount; i++) {
				var ch = toolOptionsSection.GetChild(i);
				if (ch == null || ch.name != RootName) continue;
				var existing = ch.GetComponent<PaintTab_ValueAssistPanel_UI>();
				if (existing != null) {
					// Re-entry / code upgrade: BuildUi is idempotent when settings chrome exists;
					// older panels built before settings must rebuild (guard was _summaryTmp only).
					existing.BuildUi();
					// CollectNow can hit EnsureUnder without OnEnable — keep chrome matched to store.
					existing.SyncControlsFromStore();
					existing.ApplyEnabledChrome();
					existing.RefreshStatusLine();
					return existing;
				}
				var repaired = ch.gameObject.AddComponent<PaintTab_ValueAssistPanel_UI>();
				EnsureLayoutShell(ch as RectTransform);
				repaired.BuildUi();
				return repaired;
			}
			var go = new GameObject(RootName);
			go.transform.SetParent(toolOptionsSection, false);
			go.transform.SetAsLastSibling();
			var rect = go.AddComponent<RectTransform>();
			EnsureLayoutShell(rect);
			var panel = go.AddComponent<PaintTab_ValueAssistPanel_UI>();
			panel.BuildUi();
			return panel;
		}

		static void EnsureLayoutShell(RectTransform rect) {
			if (rect == null) return;
			rect.anchorMin = new Vector2(0, 1);
			rect.anchorMax = new Vector2(1, 1);
			rect.pivot = new Vector2(0.5f, 1);
			var le = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
			le.flexibleWidth = 1f;
			le.minHeight = 310f;
			le.preferredHeight = 330f;
		}

		void OnEnable() {
			PaintTab_ValueAssistOptions.Changed -= OnOptionsChanged;
			PaintTab_ValueAssistOptions.Changed += OnOptionsChanged;
			BuildUi(); // upgrade path if panel existed before settings chrome
			SyncControlsFromStore();
			ApplyEnabledChrome();
			RefreshStatusLine();
		}

		void OnDisable() {
			PaintTab_ValueAssistOptions.Changed -= OnOptionsChanged;
		}

		void Update() {
			if (ValuePaintLivePredictor.IsLiveActive && ValuePaintLivePredictor.HasLastProposal && _statusTmp != null) {
				var p = ValuePaintLivePredictor.LastProposal;
				string liveLine = "Live " + p.CurrentBin + "→" + p.DesiredBin + " · " + ValuePaintLivePredictor.LastAssistWhich;
				if (_statusTmp.text == null || !_statusTmp.text.StartsWith("Live ") || _statusTmp.text != liveLine)
					_statusTmp.text = liveLine;
				if (_swatchImg != null)
					_swatchImg.color = ValuePaintProposalApplier.GrayForBand(p.DesiredBin);
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

			// Drop cached assist when it no longer matches the neural toggle.
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

			// Pending proposal was produced under a different neural preference — refuse stale Accept.
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
					_summaryTmp.text = "Value Assist off — enable to propose neural / value brush settings.";
				keepNeuralStatus = false;
			} else if (!PaintTab_ValueAssistOptions.LivePredict) {
				// Live off: drop live proposal UI state only (SetLivePredict already invalidates).
				if (!ValuePaintLivePredictor.HasLastProposal && _statusTmp != null
				    && _statusTmp.text != null && _statusTmp.text.StartsWith("Live "))
					_statusTmp.text = "Idle";
			}
			if (!keepNeuralStatus)
				RefreshStatusLine();
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
			// Settings chrome is the upgrade gate — rebuild when live-predict toggle missing.
			if (_enabledToggle != null && _liveToggle != null) return;
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
			_blendSlider = null;
			_sizeInfSlider = null;
			_opacityInfSlider = null;
			_blendLbl = null;
			_sizeInfLbl = null;
			_opacityInfLbl = null;
			_controlsRoot = null;

			var t = SpzUiThemeOps.Active;
			var bg = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
			bg.color = new Color(0.14f, 0.16f, 0.19f, 0.96f);
			bg.raycastTarget = true;

			var vlg = gameObject.GetComponent<VerticalLayoutGroup>() ?? gameObject.AddComponent<VerticalLayoutGroup>();
			vlg.padding = new RectOffset(6, 6, 4, 4);
			vlg.spacing = 4;
			vlg.childAlignment = TextAnchor.UpperLeft;
			vlg.childControlHeight = true;
			vlg.childControlWidth = true;
			vlg.childForceExpandHeight = false;
			vlg.childForceExpandWidth = true;

			_enabledToggle = MakeCheckboxRow(transform, "EnabledRow", "Value Assist (neural brush)",
				PaintTab_ValueAssistOptions.Enabled, isOn => {
					PaintTab_ValueAssistOptions.SetEnabled(isOn);
					ShowFeedback(isOn ? "Value Assist: on" : "Value Assist: off");
				});

			_controlsRoot = new GameObject("Controls");
			_controlsRoot.transform.SetParent(transform, false);
			_controlsRoot.AddComponent<RectTransform>();
			var controlsLe = _controlsRoot.AddComponent<LayoutElement>();
			controlsLe.flexibleWidth = 1f;
			controlsLe.minHeight = 160f;
			var controlsV = _controlsRoot.AddComponent<VerticalLayoutGroup>();
			controlsV.spacing = 4;
			controlsV.childAlignment = TextAnchor.UpperLeft;
			controlsV.childControlHeight = true;
			controlsV.childControlWidth = true;
			controlsV.childForceExpandHeight = false;
			controlsV.childForceExpandWidth = true;

			_neuralToggle = MakeCheckboxRow(_controlsRoot.transform, "NeuralRow", "Use neural (MLP)",
				PaintTab_ValueAssistOptions.UseNeural, isOn => {
					PaintTab_ValueAssistOptions.SetUseNeural(isOn);
					ValuePaintLivePredictor.InvalidateAssist();
					ShowFeedback(isOn ? "Value Assist: neural MLP" : "Value Assist: deterministic stub");
				});
			_liveToggle = MakeCheckboxRow(_controlsRoot.transform, "LiveRow", "Live predict under cursor",
				PaintTab_ValueAssistOptions.LivePredict, isOn => {
					PaintTab_ValueAssistOptions.SetLivePredict(isOn);
					ShowFeedback(isOn ? "Value Assist: live predict on" : "Value Assist: live predict off");
				});
			_hardnessToggle = MakeCheckboxRow(_controlsRoot.transform, "HardnessRow", "Apply hardness from edge soft",
				PaintTab_ValueAssistOptions.ApplyHardness, isOn => {
					PaintTab_ValueAssistOptions.SetApplyHardness(isOn);
				});

			_blendSlider = MakeSliderRow(_controlsRoot.transform, "BlendRow", "Blend strength",
				PaintTab_ValueAssistOptions.Blend01, v => PaintTab_ValueAssistOptions.SetBlend01(v), out _blendLbl);
			_sizeInfSlider = MakeSliderRow(_controlsRoot.transform, "SizeInfRow", "Size influence",
				PaintTab_ValueAssistOptions.SizeInfluence01, v => PaintTab_ValueAssistOptions.SetSizeInfluence01(v), out _sizeInfLbl);
			_opacityInfSlider = MakeSliderRow(_controlsRoot.transform, "OpacityInfRow", "Opacity influence",
				PaintTab_ValueAssistOptions.OpacityInfluence01, v => PaintTab_ValueAssistOptions.SetOpacityInfluence01(v), out _opacityInfLbl);

			_summaryTmp = MakeLabel(transform,
				"Live: hover/paint predicts value under cursor. Propose/Accept still arms a snapshot.",
				9f, t.textMuted);
			var summaryLe = _summaryTmp.GetComponent<LayoutElement>() ?? _summaryTmp.gameObject.AddComponent<LayoutElement>();
			summaryLe.minHeight = 36f;
			summaryLe.preferredHeight = 40f;

			var row = new GameObject("Actions");
			row.transform.SetParent(transform, false);
			row.AddComponent<RectTransform>();
			var rowLe = row.AddComponent<LayoutElement>();
			rowLe.minHeight = 28f;
			rowLe.preferredHeight = 28f;
			var hlg = row.AddComponent<HorizontalLayoutGroup>();
			hlg.spacing = 4;
			hlg.childAlignment = TextAnchor.MiddleLeft;
			hlg.childForceExpandWidth = false;
			hlg.childForceExpandHeight = true;
			hlg.childControlWidth = true;
			hlg.childControlHeight = true;

			_swatchImg = MakeSwatch(row.transform);
			_proposeBtn = MakeBtn(row.transform, "Propose", new Color(0.22f, 0.42f, 0.52f, 1f), OnPropose);
			_acceptBtn = MakeBtn(row.transform, "Accept", new Color(0.22f, 0.48f, 0.32f, 1f), OnAccept);
			_dismissBtn = MakeBtn(row.transform, "Dismiss", new Color(0.42f, 0.28f, 0.28f, 1f), OnDismiss);

			_statusTmp = MakeLabel(transform, "Idle", 9f, t.textMuted);
			var statusLe = _statusTmp.GetComponent<LayoutElement>() ?? _statusTmp.gameObject.AddComponent<LayoutElement>();
			statusLe.minHeight = 18f;

			_acceptBtn.interactable = false;
			ApplyEnabledChrome();
			RefreshStatusLine();
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
			if (_blendSlider != null)
				_blendSlider.SetValueWithoutNotify(PaintTab_ValueAssistOptions.Blend01);
			if (_sizeInfSlider != null)
				_sizeInfSlider.SetValueWithoutNotify(PaintTab_ValueAssistOptions.SizeInfluence01);
			if (_opacityInfSlider != null)
				_opacityInfSlider.SetValueWithoutNotify(PaintTab_ValueAssistOptions.OpacityInfluence01);
			// SetValueWithoutNotify skips onValueChanged — refresh % labels explicitly.
			SetSliderLabel(_blendLbl, "Blend strength", PaintTab_ValueAssistOptions.Blend01);
			SetSliderLabel(_sizeInfLbl, "Size influence", PaintTab_ValueAssistOptions.SizeInfluence01);
			SetSliderLabel(_opacityInfLbl, "Opacity influence", PaintTab_ValueAssistOptions.OpacityInfluence01);
			_suppressToggleSync = false;
			TintToggleBox(_enabledToggle, PaintTab_ValueAssistOptions.Enabled);
			TintToggleBox(_neuralToggle, PaintTab_ValueAssistOptions.UseNeural);
			TintToggleBox(_liveToggle, PaintTab_ValueAssistOptions.LivePredict);
			TintToggleBox(_hardnessToggle, PaintTab_ValueAssistOptions.ApplyHardness);
		}

		void ApplyEnabledChrome() {
			bool on = PaintTab_ValueAssistOptions.Enabled;
			if (_controlsRoot != null)
				_controlsRoot.SetActive(on);
			if (_proposeBtn != null) _proposeBtn.interactable = on;
			if (_dismissBtn != null) _dismissBtn.interactable = on;
			if (_acceptBtn != null) _acceptBtn.interactable = on && _hasProposal;
			var shell = GetComponent<LayoutElement>();
			if (shell != null) {
				shell.minHeight = on ? 310f : 72f;
				shell.preferredHeight = on ? 330f : 80f;
			}
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
			SetStatus("Proposed (" + _assistWhich + ") — review, then Accept to arm brush.");
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
				string armed = PaintTab_ValueAssistOptions.ApplyHardness
					? "Armed — paint strokes use ribbon color/size/opacity/hardness."
					: "Armed — paint strokes use ribbon color/size/opacity (hardness unchanged).";
				SetStatus(armed);
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
				_summaryTmp.text = PaintTab_ValueAssistOptions.Enabled
					? "Value Assist — Propose from brush color, Accept to arm ribbon."
					: "Value Assist off — enable to propose neural / value brush settings.";
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

		static void TintToggleBox(Toggle toggle, bool on) {
			if (toggle == null || toggle.targetGraphic == null) return;
			toggle.targetGraphic.color = on
				? new Color(0.22f, 0.45f, 0.55f, 1f)
				: new Color(0.34f, 0.36f, 0.4f, 1f);
		}

		Toggle MakeCheckboxRow(Transform parent, string rowName, string labelText, bool initialOn,
			UnityEngine.Events.UnityAction<bool> onChanged) {
			Color offCol = new Color(0.34f, 0.36f, 0.4f, 1f);
			Color onCol = new Color(0.22f, 0.45f, 0.55f, 1f);
			var row = new GameObject(rowName);
			row.transform.SetParent(parent, false);
			row.AddComponent<RectTransform>();
			var rowLe = row.AddComponent<LayoutElement>();
			rowLe.minHeight = 26f;
			rowLe.preferredHeight = 26f;
			rowLe.flexibleWidth = 1f;
			var h = row.AddComponent<HorizontalLayoutGroup>();
			h.spacing = 8;
			h.childAlignment = TextAnchor.MiddleLeft;
			h.childControlWidth = false;
			h.childControlHeight = true;
			h.childForceExpandWidth = false;

			var boxGo = new GameObject("Box");
			boxGo.transform.SetParent(row.transform, false);
			var boxLe = boxGo.AddComponent<LayoutElement>();
			boxLe.minWidth = 28f;
			boxLe.preferredWidth = 28f;
			var img = boxGo.AddComponent<Image>();
			img.color = initialOn ? onCol : offCol;
			var toggle = boxGo.AddComponent<Toggle>();
			toggle.targetGraphic = img;
			toggle.graphic = null;
			var cb = toggle.colors;
			cb.normalColor = Color.white;
			cb.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
			cb.pressedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
			cb.selectedColor = Color.white;
			toggle.colors = cb;
			// Set before listener — assigning isOn after AddListener re-enters SetEnabled/ShowFeedback during BuildUi.
			toggle.SetIsOnWithoutNotify(initialOn);
			img.color = initialOn ? onCol : offCol;
			toggle.onValueChanged.AddListener(isOn => {
				if (_suppressToggleSync) return;
				img.color = isOn ? onCol : offCol;
				onChanged?.Invoke(isOn);
			});

			var lblGo = new GameObject("Lbl");
			lblGo.transform.SetParent(row.transform, false);
			var lblLe = lblGo.AddComponent<LayoutElement>();
			lblLe.flexibleWidth = 1f;
			lblLe.minHeight = 22f;
			var tmp = lblGo.AddComponent<TextMeshProUGUI>();
			tmp.font = TMP_Settings.defaultFontAsset;
			tmp.fontSize = 9f;
			tmp.color = new Color(0.88f, 0.89f, 0.92f, 1f);
			tmp.alignment = TextAlignmentOptions.Left;
			tmp.raycastTarget = false;
			tmp.text = labelText;
			return toggle;
		}

		static void SetSliderLabel(TextMeshProUGUI lbl, string labelText, float v01) {
			if (lbl == null) return;
			lbl.text = labelText + "  " + Mathf.RoundToInt(Mathf.Clamp01(v01) * 100) + "%";
		}

		Slider MakeSliderRow(Transform parent, string rowName, string labelText, float initial,
			UnityEngine.Events.UnityAction<float> onChanged, out TextMeshProUGUI labelOut) {
			var row = new GameObject(rowName);
			row.transform.SetParent(parent, false);
			row.AddComponent<RectTransform>();
			var rowLe = row.AddComponent<LayoutElement>();
			rowLe.minHeight = 44f;
			rowLe.preferredHeight = 44f;
			rowLe.flexibleWidth = 1f;
			var v = row.AddComponent<VerticalLayoutGroup>();
			v.spacing = 2;
			v.childControlWidth = true;
			v.childControlHeight = true;
			v.childForceExpandWidth = true;
			v.childForceExpandHeight = false;

			var lblGo = new GameObject("Lbl");
			lblGo.transform.SetParent(row.transform, false);
			var lblLe = lblGo.AddComponent<LayoutElement>();
			lblLe.minHeight = 16f;
			lblLe.preferredHeight = 16f;
			var lbl = lblGo.AddComponent<TextMeshProUGUI>();
			lbl.font = TMP_Settings.defaultFontAsset;
			lbl.fontSize = 9f;
			lbl.color = new Color(0.88f, 0.89f, 0.92f, 1f);
			lbl.alignment = TextAlignmentOptions.Left;
			lbl.raycastTarget = false;
			SetSliderLabel(lbl, labelText, initial);
			labelOut = lbl;

			var trackGo = new GameObject("Track");
			trackGo.transform.SetParent(row.transform, false);
			var trackLe = trackGo.AddComponent<LayoutElement>();
			trackLe.minHeight = 18f;
			trackLe.preferredHeight = 18f;
			trackLe.flexibleWidth = 1f;
			var trackImg = trackGo.AddComponent<Image>();
			trackImg.color = new Color(0.22f, 0.24f, 0.28f, 1f);

			var fillArea = new GameObject("Fill Area");
			fillArea.transform.SetParent(trackGo.transform, false);
			var fillAreaRt = fillArea.AddComponent<RectTransform>();
			fillAreaRt.anchorMin = new Vector2(0, 0.25f);
			fillAreaRt.anchorMax = new Vector2(1, 0.75f);
			fillAreaRt.offsetMin = new Vector2(4, 0);
			fillAreaRt.offsetMax = new Vector2(-4, 0);
			var fillGo = new GameObject("Fill");
			fillGo.transform.SetParent(fillArea.transform, false);
			var fillRt = fillGo.AddComponent<RectTransform>();
			fillRt.anchorMin = Vector2.zero;
			fillRt.anchorMax = Vector2.one;
			fillRt.offsetMin = Vector2.zero;
			fillRt.offsetMax = Vector2.zero;
			var fillImg = fillGo.AddComponent<Image>();
			fillImg.color = new Color(0.28f, 0.5f, 0.58f, 1f);

			var handleArea = new GameObject("Handle Slide Area");
			handleArea.transform.SetParent(trackGo.transform, false);
			var handleAreaRt = handleArea.AddComponent<RectTransform>();
			handleAreaRt.anchorMin = Vector2.zero;
			handleAreaRt.anchorMax = Vector2.one;
			handleAreaRt.offsetMin = new Vector2(6, 0);
			handleAreaRt.offsetMax = new Vector2(-6, 0);
			var handleGo = new GameObject("Handle");
			handleGo.transform.SetParent(handleArea.transform, false);
			var handleLe = handleGo.AddComponent<LayoutElement>();
			handleLe.ignoreLayout = true;
			var handleRt = handleGo.AddComponent<RectTransform>();
			handleRt.sizeDelta = new Vector2(12, 16);
			var handleImg = handleGo.AddComponent<Image>();
			handleImg.color = new Color(0.85f, 0.88f, 0.92f, 1f);

			var slider = trackGo.AddComponent<Slider>();
			slider.fillRect = fillRt;
			slider.handleRect = handleRt;
			slider.targetGraphic = handleImg;
			slider.direction = Slider.Direction.LeftToRight;
			slider.minValue = 0f;
			slider.maxValue = 1f;
			slider.wholeNumbers = false;
			slider.value = initial;
			slider.onValueChanged.AddListener(v01 => {
				if (_suppressToggleSync) return;
				SetSliderLabel(lbl, labelText, v01);
				onChanged?.Invoke(v01);
			});
			return slider;
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
			le.minWidth = 22f;
			le.preferredWidth = 22f;
			le.minHeight = 22f;
			var img = go.AddComponent<Image>();
			img.color = new Color(0.35f, 0.35f, 0.38f, 1f);
			img.raycastTarget = false;
			return img;
		}

		static Button MakeBtn(Transform parent, string label, Color bg, UnityEngine.Events.UnityAction onClick) {
			var go = new GameObject("Btn_" + label);
			go.transform.SetParent(parent, false);
			go.AddComponent<RectTransform>();
			var le = go.AddComponent<LayoutElement>();
			le.minWidth = 64f;
			le.preferredWidth = 72f;
			le.minHeight = 26f;
			var img = go.AddComponent<Image>();
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
	}

}

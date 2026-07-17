using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace spz {

	/// <summary>
	/// Paint-tab review UI for smart-value-paint (Spec R3): Propose → review → Accept/Dismiss.
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
		Button _acceptBtn;

		public static PaintTab_ValueAssistPanel_UI EnsureUnder(RectTransform toolOptionsSection) {
			if (toolOptionsSection == null) return null;
			for (int i = 0; i < toolOptionsSection.childCount; i++) {
				var ch = toolOptionsSection.GetChild(i);
				if (ch != null && ch.name == RootName) {
					var existing = ch.GetComponent<PaintTab_ValueAssistPanel_UI>();
					if (existing != null) return existing;
				}
			}
			var go = new GameObject(RootName);
			go.transform.SetParent(toolOptionsSection, false);
			go.transform.SetAsLastSibling();
			var rect = go.AddComponent<RectTransform>();
			rect.anchorMin = new Vector2(0, 1);
			rect.anchorMax = new Vector2(1, 1);
			rect.pivot = new Vector2(0.5f, 1);
			var le = go.AddComponent<LayoutElement>();
			le.flexibleWidth = 1f;
			le.minHeight = 96f;
			le.preferredHeight = 108f;
			var panel = go.AddComponent<PaintTab_ValueAssistPanel_UI>();
			panel.BuildUi();
			return panel;
		}

		void OnEnable() {
			RefreshStatusLine();
		}

		void BuildUi() {
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

			_summaryTmp = MakeLabel(transform, "Value Assist — Propose from brush color, Accept to arm ribbon.", 9f, t.textMuted);
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
			MakeBtn(row.transform, "Propose", new Color(0.22f, 0.42f, 0.52f, 1f), OnPropose);
			_acceptBtn = MakeBtn(row.transform, "Accept", new Color(0.22f, 0.48f, 0.32f, 1f), OnAccept);
			MakeBtn(row.transform, "Dismiss", new Color(0.42f, 0.28f, 0.28f, 1f), OnDismiss);

			_statusTmp = MakeLabel(transform, "Idle", 9f, t.textMuted);
			var statusLe = _statusTmp.GetComponent<LayoutElement>() ?? _statusTmp.gameObject.AddComponent<LayoutElement>();
			statusLe.minHeight = 18f;

			_acceptBtn.interactable = false;
			RefreshStatusLine();
		}

		void OnPropose() {
			EnsureAssist();
			// Do not pass current brush hints — HasBrushHints would clobber MLP/stub width/opacity/blend
			// with the live ribbon (false assist: Accept would only recolor). Spec R2 stroke state is optional.
			Color sample = CurrentBrushColor();
			_proposal = _assist.ProposeFromColor(sample, default);
			_hasProposal = true;
			_acceptBtn.interactable = true;
			if (_swatchImg != null)
				_swatchImg.color = ValuePaintProposalApplier.GrayForBand(_proposal.DesiredBin);
			_summaryTmp.text = FormatProposal(_proposal);
			_statusTmp.text = "Proposed (" + _assistWhich + ") — review, then Accept to arm brush.";
			ShowFeedback("Value Assist: proposal ready");
		}

		void OnAccept() {
			if (!_hasProposal) {
				_statusTmp.text = "Propose first.";
				return;
			}
			bool ok = ValuePaintProposalApplier.TryAccept(_proposal, out string reason);
			if (ok) {
				_statusTmp.text = "Armed — paint strokes use ribbon color/size/opacity/hardness.";
				ShowFeedback("Value Assist: accepted");
			} else {
				_statusTmp.text = "Accept refused — " + reason;
				ShowFeedback("Value Assist: " + reason);
			}
			RefreshStatusLine(keepMessage: true);
		}

		void OnDismiss() {
			_hasProposal = false;
			_proposal = default;
			if (_acceptBtn != null) _acceptBtn.interactable = false;
			ValuePaintProposalApplier.ClearArmed();
			if (_swatchImg != null) _swatchImg.color = new Color(0.35f, 0.35f, 0.38f, 1f);
			if (_summaryTmp != null)
				_summaryTmp.text = "Value Assist — Propose from brush color, Accept to arm ribbon.";
			_statusTmp.text = "Dismissed.";
			ShowFeedback("Value Assist: cleared");
		}

		void EnsureAssist() {
			if (_assist != null) return;
			_assist = ValuePaintAssistFactory.Create(out _assistWhich);
		}

		void RefreshStatusLine(bool keepMessage = false) {
			if (_statusTmp == null) return;
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
			btn.onClick.AddListener(onClick);
			var colors = btn.colors;
			colors.highlightedColor = new Color(Mathf.Min(1f, bg.r + 0.12f), Mathf.Min(1f, bg.g + 0.12f), Mathf.Min(1f, bg.b + 0.12f), 1f);
			colors.pressedColor = new Color(Mathf.Min(1f, bg.r + 0.2f), Mathf.Min(1f, bg.g + 0.2f), Mathf.Min(1f, bg.b + 0.2f), 1f);
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

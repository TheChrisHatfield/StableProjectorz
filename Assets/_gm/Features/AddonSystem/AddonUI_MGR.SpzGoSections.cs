using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace spz {

	/// <summary>
	/// SPZ GO's multi-host shell (spz-go-multi-dcc phase 1): one collapsible section per DCC, each with a
	/// logo that runs the section's selected Import/Export mode.
	///
	/// The section is built here once and called from both the native fallback and the Python
	/// <c>create_panel</c> path, so R6 parity is structural rather than two builders kept in step by hand.
	/// </summary>
	public partial class AddonUI_MGR {

		const float SpzGoRowHeight = 32f;
		const float SpzGoLogoHeight = 46f;
		/// <summary>Same hit target as Addon Manager prefs expand — keeps the chevron readable.</summary>
		const float SpzGoExpandChevronHit = 18f;
		static readonly Color SpzGoExpandChevronArrowColor = new Color(0.88f, 0.88f, 0.92f, 1f);

		/// <summary>
		/// Collapsible container (spz-go-multi-dcc R7). Returns the element id of the CONTENT object, so
		/// callers add widgets straight into the drop-tab with the ordinary Add* methods.
		/// Header uses the Addon Manager prefs chevron (▶ closed / ▼ open), not unicode triangles —
		/// TMP fonts here often miss ▸/▾ and draw a missing-glyph box that reads as a checkbox.
		/// </summary>
		public string AddFoldout(string addonId, string panelId, string label, bool startOpen) {
			GameObject panelObj = FindUIElement(panelId);
			if (panelObj == null) {
				UnityEngine.Debug.LogError($"[AddonUI_MGR] Panel {panelId} not found");
				return null;
			}

			var root = NewStackContainer($"Foldout_{label}", panelObj.transform, spacing: 4f);

			var headerRt = CreateUiChild($"FoldoutHeader_{label}", root.transform);
			headerRt.sizeDelta = new Vector2(0f, SpzGoRowHeight);
			var header = headerRt.gameObject;
			var headerLe = header.AddComponent<LayoutElement>();
			headerLe.preferredHeight = SpzGoRowHeight;
			headerLe.minHeight = SpzGoRowHeight - 4f;
			headerLe.flexibleWidth = 1f;
			headerLe.flexibleHeight = 0f;
			var headerBg = header.AddComponent<Image>();
			headerBg.sprite = UiRuntimeSprites.SolidRect;
			headerBg.type = Image.Type.Simple;
			SpzUiThemeOps.ApplyRoundedControlSprite(headerBg, markEligible: true);
			headerBg.color = new Color(0.24f, 0.24f, 0.24f, 1f);
			headerBg.raycastTarget = true;
			var headerRow = header.AddComponent<HorizontalLayoutGroup>();
			headerRow.spacing = 8f;
			headerRow.padding = new RectOffset(6, 8, 4, 4);
			headerRow.childAlignment = TextAnchor.MiddleLeft;
			headerRow.childControlWidth = true;
			headerRow.childControlHeight = false;
			headerRow.childForceExpandWidth = false;
			headerRow.childForceExpandHeight = false;

			var expandObj = CreateUiChild("ExpandChevron", header.transform).gameObject;
			var expandLe = expandObj.AddComponent<LayoutElement>();
			expandLe.preferredWidth = SpzGoExpandChevronHit;
			expandLe.minWidth = SpzGoExpandChevronHit;
			expandLe.preferredHeight = SpzGoExpandChevronHit;
			expandLe.minHeight = SpzGoExpandChevronHit;
			expandLe.flexibleWidth = 0f;
			expandLe.flexibleHeight = 0f;
			var expandHit = expandObj.AddComponent<Image>();
			expandHit.sprite = UiRuntimeSprites.SolidRect;
			expandHit.type = Image.Type.Simple;
			expandHit.color = Color.clear;
			expandHit.raycastTarget = false;

			var titleRt = CreateUiChild("Text", header.transform);
			var titleLe = titleRt.gameObject.AddComponent<LayoutElement>();
			titleLe.flexibleWidth = 1f;
			titleLe.preferredHeight = SpzGoRowHeight - 8f;
			titleLe.minHeight = SpzGoRowHeight - 12f;
			var headerText = titleRt.gameObject.AddComponent<TextMeshProUGUI>();
			headerText.text = label;
			headerText.fontSize = 13f;
			headerText.color = Color.white;
			headerText.alignment = TextAlignmentOptions.MidlineLeft;
			headerText.raycastTarget = false;
			headerText.overflowMode = TextOverflowModes.Ellipsis;
			headerText.enableWordWrapping = false;
			ApplyRuntimeTmpFont(headerText);

			// Content spacing/padding is the leading between Settings widgets — 4px left labels sitting on
			// the next control's face; 8px + insets matches Addon Manager prefs density.
			var content = NewStackContainer($"FoldoutContent_{label}", root.transform, spacing: 8f);
			var contentLayout = content.GetComponent<VerticalLayoutGroup>();
			if (contentLayout != null)
				contentLayout.padding = new RectOffset(4, 4, 6, 8);

			void ApplyOpenState(bool open) {
				content.SetActive(open);
				ApplySpzGoExpandChevronVisual(expandObj.transform, open);
			}
			ApplyOpenState(startOpen);

			var headerButton = header.AddComponent<Button>();
			headerButton.targetGraphic = headerBg;
			headerButton.onClick.AddListener(() => {
				bool open = !content.activeSelf;
				ApplyOpenState(open);
				// Independent per host (R5): the state is stored under the host this foldout sits in.
				string hostId = SpzGoHostSection.HostIdForWidget(root.transform);
				if (hostId != null)
					SpzGoHostPrefs.SetSettingsOpen(hostId, open);
				// Nested ContentSizeFitters do not push sibling host sections down from a Mark alone —
				// rebuild the chain so the open Settings body owns vertical space instead of stacking.
				RebuildLayoutChain(root.transform);
			});

			RegisterAddonElement(addonId, root);
			RegisterAddonElement(addonId, content);
			SpzUiThemeOps.ApplyToAddonUiRoot(root);
			return content.GetInstanceID().ToString();
		}

		/// <summary>
		/// Hide a foldout's content after its widgets have been built. Used so sections can seed their
		/// Settings into a live hierarchy and then honour the collapsed-by-default contract (R4).
		/// </summary>
		static void CollapseFoldoutContent(GameObject content) {
			if (content == null) return;
			content.SetActive(false);
			var root = content.transform.parent;
			if (root == null) return;
			Transform header = null;
			for (int i = 0; i < root.childCount; i++) {
				var child = root.GetChild(i);
				if (child != null && child.name.StartsWith("FoldoutHeader_", System.StringComparison.Ordinal)) {
					header = child;
					break;
				}
			}
			var chevron = header != null ? header.Find("ExpandChevron") : null;
			if (chevron != null)
				ApplySpzGoExpandChevronVisual(chevron, false);
			RebuildLayoutChain(root);
		}

		/// <summary>
		/// Addon Manager prefs chevron: ChevronRight at 0° = closed (▶), −90° = open (▼). Image-based so
		/// Nomad/default TMP fonts cannot substitute a missing unicode box for the arrow.
		/// </summary>
		static void ApplySpzGoExpandChevronVisual(Transform expandT, bool expanded) {
			if (expandT == null) return;
			var rootRt = expandT as RectTransform;
			if (rootRt != null)
				rootRt.sizeDelta = new Vector2(SpzGoExpandChevronHit, SpzGoExpandChevronHit);

			Transform arrowT = expandT.Find("Arrow");
			if (arrowT == null) {
				var go = new GameObject("Arrow", typeof(RectTransform), typeof(Image));
				go.layer = expandT.gameObject.layer;
				go.transform.SetParent(expandT, false);
				arrowT = go.transform;
			}
			var arrowRt = arrowT as RectTransform;
			if (arrowRt != null) {
				arrowRt.anchorMin = arrowRt.anchorMax = new Vector2(0.5f, 0.5f);
				arrowRt.pivot = new Vector2(0.5f, 0.5f);
				arrowRt.anchoredPosition = Vector2.zero;
				arrowRt.sizeDelta = new Vector2(14f, 14f);
				arrowRt.localEulerAngles = new Vector3(0f, 0f, expanded ? -90f : 0f);
			}
			var arrowImg = arrowT.GetComponent<Image>();
			if (arrowImg == null)
				arrowImg = arrowT.gameObject.AddComponent<Image>();
			arrowImg.sprite = UiRuntimeSprites.GetLineIcon(StudioLineIcon.ChevronRight);
			arrowImg.type = Image.Type.Simple;
			arrowImg.preserveAspect = true;
			arrowImg.raycastTarget = false;
			arrowImg.enabled = true;
			arrowImg.color = SpzGoExpandChevronArrowColor;
		}

		/// <summary>
		/// Nested host-section / foldout ContentSizeFitters only report height after an immediate rebuild
		/// from the changed leaf outward. MarkLayoutForRebuild alone leaves open Settings stacked on the
		/// next host's logo.
		/// </summary>
		static void RebuildLayoutChain(Transform from) {
			if (from == null) return;
			var chain = new List<RectTransform>(8);
			for (var t = from as RectTransform; t != null; t = t.parent as RectTransform)
				chain.Add(t);
			// Bottom-up so each parent's preferred height sees the child's newly computed size.
			for (int i = 0; i < chain.Count; i++)
				LayoutRebuilder.ForceRebuildLayoutImmediate(chain[i]);
			Canvas.ForceUpdateCanvases();
			for (int i = 0; i < chain.Count; i++)
				LayoutRebuilder.ForceRebuildLayoutImmediate(chain[i]);
		}

		/// <summary>
		/// One DCC's whole section: logo activate, Import/Export mode toggles, and a collapsed Settings
		/// drop-tab holding the mandatory agnostic controls plus that host's extras (R3b, R15, R16).
		/// </summary>
		public string AddHostSection(string addonId, string panelId, string hostId) {
			GameObject panelObj = FindUIElement(panelId);
			if (panelObj == null) {
				UnityEngine.Debug.LogError($"[AddonUI_MGR] Panel {panelId} not found");
				return null;
			}
			var host = SpzGoHosts.Get(hostId);
			if (host == null) {
				UnityEngine.Debug.LogError($"[AddonUI_MGR] Unknown SPZ GO host '{hostId}'");
				return null;
			}

			var section = NewStackContainer(SpzGoHostSection.SectionName(host.Id), panelObj.transform, spacing: 6f);
			RegisterAddonElement(addonId, section);
			string sectionId = section.GetInstanceID().ToString();

			BuildHostLogo(addonId, section.transform, host, sectionId);
			BuildHostModeToggles(section.transform, host);

			// Build Settings while the drop-tab is open. Adding TMP / RectTransform widgets under an
			// inactive hierarchy leaves Labels without a usable rect, and the section never finishes.
			string settingsId = AddFoldout(addonId, sectionId, SpzGoHostSection.SettingsLabel, startOpen: true);
			if (!string.IsNullOrEmpty(settingsId)) {
				BuildHostSettings(addonId, settingsId, host);
				if (!SpzGoHostPrefs.GetSettingsOpen(host.Id))
					CollapseFoldoutContent(FindUIElement(settingsId));
				else
					RebuildLayoutChain(section.transform);
			}

			SpzUiThemeOps.ApplyToAddonUiRoot(section);
			return sectionId;
		}

		void BuildHostLogo(string addonId, Transform parent, SpzGoHost host, string sectionId) {
			var logoRt = CreateUiChild(SpzGoHostSection.LogoName(host.Id), parent);
			logoRt.sizeDelta = new Vector2(0f, SpzGoLogoHeight);
			var logo = logoRt.gameObject;
			var le = logo.AddComponent<LayoutElement>();
			le.preferredHeight = SpzGoLogoHeight;
			le.minHeight = SpzGoLogoHeight - 6f;
			le.flexibleWidth = 1f;

			var bg = logo.AddComponent<Image>();
			bg.sprite = UiRuntimeSprites.SolidRect;
			bg.type = Image.Type.Simple;
			SpzUiThemeOps.ApplyRoundedControlSprite(bg, markEligible: true);
			// A host with no bridge behind it reads dimmer, and says so when pressed (R13). Uses effective
			// readiness so a freshly installed ZBrush/Painter bridge lights up without a rebuild.
			bg.color = SpzGoHosts.IsBridgeReady(host.Id)
				? new Color(0.30f, 0.34f, 0.40f, 1f)
				: new Color(0.24f, 0.24f, 0.26f, 1f);
			bg.raycastTarget = true;

			// Placeholder glyph until host artwork / licensing is settled — the header is never empty.
			var glyph = NewStretchedLabel(logo.transform, "Glyph", host.Glyph, TextAlignmentOptions.Left);
			glyph.fontSize = 18f;
			glyph.fontStyle = FontStyles.Bold;
			glyph.margin = new Vector4(10f, 0f, 0f, 0f);
			var name = NewStretchedLabel(logo.transform, "Name", host.DisplayName, TextAlignmentOptions.Center);
			name.fontSize = 14f;

			var button = logo.AddComponent<Button>();
			button.targetGraphic = bg;
			button.onClick.AddListener(() => SpzGoActivateHost(host.Id, sectionId));
			RegisterAddonElement(addonId, logo);
		}

		void BuildHostModeToggles(Transform parent, SpzGoHost host) {
			var rowRt = CreateUiChild("ModeRow_" + host.Id, parent);
			rowRt.sizeDelta = new Vector2(0f, SpzGoRowHeight);
			var row = rowRt.gameObject;
			var rowLe = row.AddComponent<LayoutElement>();
			rowLe.preferredHeight = SpzGoRowHeight;
			rowLe.minHeight = SpzGoRowHeight - 4f;
			rowLe.flexibleWidth = 1f;
			var layout = row.AddComponent<HorizontalLayoutGroup>();
			layout.spacing = 4f;
			layout.childControlWidth = true;
			layout.childControlHeight = true;
			layout.childForceExpandWidth = true;
			layout.childForceExpandHeight = true;

			BuildHostModeToggle(row.transform, host, SpzGoMode.Import, SpzGoHostSection.ImportModeLabel);
			BuildHostModeToggle(row.transform, host, SpzGoMode.Export, SpzGoHostSection.ExportModeLabel);
			RefreshHostModeToggles(row.transform, host.Id);
		}

		void BuildHostModeToggle(Transform row, SpzGoHost host, SpzGoMode mode, string label) {
			var go = CreateUiChild(SpzGoHostSection.ModeToggleName(host.Id, mode), row).gameObject;
			var bg = go.AddComponent<Image>();
			bg.sprite = UiRuntimeSprites.SolidRect;
			bg.type = Image.Type.Simple;
			SpzUiThemeOps.ApplyRoundedControlSprite(bg, markEligible: true);
			bg.raycastTarget = true;
			NewStretchedLabel(go.transform, "Text", label, TextAlignmentOptions.Center);

			var button = go.AddComponent<Button>();
			button.targetGraphic = bg;
			button.onClick.AddListener(() => {
				// Pressing the mode that is already on re-selects it rather than clearing: a section with
				// no mode would leave the logo with nothing to run (R3d).
				SpzGoHostPrefs.SetMode(host.Id, mode);
				RefreshHostModeToggles(row, host.Id);
			});
		}

		/// <summary>Paints exactly one of the two toggles as selected, from the host's stored mode.</summary>
		void RefreshHostModeToggles(Transform row, string hostId) {
			if (row == null) return;
			var selected = SpzGoHostPrefs.GetMode(hostId);
			SetModeToggleSelected(row, hostId, SpzGoMode.Import, selected == SpzGoMode.Import);
			SetModeToggleSelected(row, hostId, SpzGoMode.Export, selected == SpzGoMode.Export);
		}

		static void SetModeToggleSelected(Transform row, string hostId, SpzGoMode mode, bool on) {
			var child = row.Find(SpzGoHostSection.ModeToggleName(hostId, mode));
			if (child == null) return;
			var img = child.GetComponent<Image>();
			if (img != null)
				img.color = on ? new Color(0.20f, 0.45f, 0.75f, 1f) : new Color(0.26f, 0.26f, 0.26f, 1f);
		}

		void BuildHostSettings(string addonId, string settingsId, SpzGoHost host) {
			SpzGoDefaultExchangePaths(ResolveDataDirOrNull(), host.Id,
				out string importDefault, out string exportDefault);
			string storedImport = SpzGoHostPrefs.GetPath(host.Id, import: true);
			string storedExport = SpzGoHostPrefs.GetPath(host.Id, import: false);

			AddDropdown(addonId, settingsId, ExportAxisSettings.AxisOrderLabel,
				new List<string>(ExportAxisSettings.AxisOrderNames), SpzGoHostPrefs.GetAxisOrderIndex(host.Id));
			AddDropdown(addonId, settingsId, ExportAxisSettings.FlipLabel,
				new List<string>(ExportAxisSettings.FlipNames), SpzGoHostPrefs.GetFlipIndex(host.Id));
			AddButton(addonId, settingsId, SpzGoHostSection.AutofillLabel,
				SpzGoHostSection.QualifyCallback("do_autofill_mesh_paths", host.Id));
			AddTextInput(addonId, settingsId, SpzGoHostSection.ImportPathLabel,
				string.IsNullOrEmpty(storedImport) ? importDefault : storedImport);
			AddTextInput(addonId, settingsId, SpzGoHostSection.ExportPathLabel,
				string.IsNullOrEmpty(storedExport) ? exportDefault : storedExport);

			// DCC-specific extras are additive and live only under their own host (R16).
			if (string.Equals(host.Id, SpzGoHosts.BlenderId, StringComparison.Ordinal)) {
				AddTextInput(addonId, settingsId, "Blender.exe (optional)", "");
				AddButton(addonId, settingsId, "Install into Blender",
					SpzGoHostSection.QualifyCallback("do_install_blender_addon_force", host.Id));
				AddButton(addonId, settingsId, "Refresh Blender",
					SpzGoHostSection.QualifyCallback("do_refresh_blender_path", host.Id));
				AddButton(addonId, settingsId, "Export with dialogs…",
					SpzGoHostSection.QualifyCallback("do_export_interactive", host.Id));
				AddButton(addonId, settingsId, "Print data_dir",
					SpzGoHostSection.QualifyCallback("do_show_data_dir", host.Id));
			} else if (string.Equals(host.Id, SpzGoHosts.ZBrushId, StringComparison.Ordinal)) {
				AddButton(addonId, settingsId, "Install into ZBrush",
					SpzGoHostSection.QualifyCallback("do_install_zbrush_bridge", host.Id));
			} else if (string.Equals(host.Id, SpzGoHosts.PainterId, StringComparison.Ordinal)) {
				AddButton(addonId, settingsId, "Install into Substance Painter",
					SpzGoHostSection.QualifyCallback("do_install_painter_bridge", host.Id));
			}
		}

		/// <summary>
		/// The logo press: run this host's selected mode once (R3c). Nothing else on the face transfers.
		/// </summary>
		void SpzGoActivateHost(string hostId, string sectionId) {
			var host = SpzGoHosts.Get(hostId);
			if (host == null) return;
			if (!SpzGoHosts.IsBridgeReady(hostId)) {
				SpzGoStatusLine($"{host.DisplayName}: {host.NotReadyReason}", false);
				return;
			}
			var mode = SpzGoHostPrefs.GetMode(hostId);
			// The writers snapshot one shared basis, so hand them this host's before anything runs.
			SpzGoHostPrefs.ApplyExportBasisToShared(hostId);
			if (mode == SpzGoMode.Import) {
				SpzGoRequestImportFromHost(host);
				return;
			}
			SpzGoRunHeadlessImportOrExportFromPanel(StableProjectorzGoAddonId, sectionId,
				SpzGoHostSection.QualifyCallback("do_export_to_path", hostId), isImport: false);
		}

		/// <summary>
		/// Marker SPZ writes into a host's exchange folder to ask that host to push its current model
		/// (spz-go-multi-dcc R9). Import is "the host hands SPZ its model", not a file load — the host
		/// bridge watches this folder, sees the request, exports its selection and POSTs SPZ to import.
		/// The literal is duplicated in each host bridge (separate C#/Python codebases) and pinned by
		/// contract tests on both sides.
		/// </summary>
		public const string SpzGoPullRequestFileName = "spz_go_pull_request.json";

		/// <summary>
		/// Import is "that application hands SPZ its current model" (R9) — it is not a file load. SPZ
		/// drops a request marker in the host's exchange folder; the host bridge's watcher pushes its
		/// live selection back over the existing import endpoint. Nothing is imported here, and the
		/// status says "requested" rather than claiming a transfer that only the host can complete
		/// (R12): a closed host simply never answers, and no unrelated file is loaded in its place.
		/// </summary>
		void SpzGoRequestImportFromHost(SpzGoHost host) {
			string dataDir = ResolveDataDirOrNull();
			if (string.IsNullOrEmpty(dataDir)) {
				SpzGoStatusLine("No project data_dir — save a project first, then request from " + host.DisplayName, false);
				return;
			}
			SpzGoDefaultExchangePaths(dataDir, host.Id, out string importPath, out _);
			string exchangeDir = Path.GetDirectoryName(importPath);
			if (string.IsNullOrEmpty(exchangeDir)) {
				SpzGoStatusLine("Could not resolve " + host.DisplayName + " exchange folder", false);
				return;
			}
			try {
				Directory.CreateDirectory(exchangeDir);
				string payload = "{\"ts\":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
					+ ",\"host\":\"" + JsonEscape(host.Id) + "\"}";
				File.WriteAllText(Path.Combine(exchangeDir, SpzGoPullRequestFileName), payload);
			} catch (Exception e) {
				UnityEngine.Debug.LogWarning("[AddonUI_MGR] SPZ GO pull request write failed: " + e.Message);
				SpzGoStatusLine("Could not write import request for " + host.DisplayName, false);
				return;
			}
			SpzGoStatusLine(
				$"Requested current model from {host.DisplayName} — it pushes when {host.DisplayName} is open with SPZ GO",
				true);
		}

		static string ResolveDataDirOrNull() {
			var fp = FastPath_API.instance;
			return fp != null ? fp.GetProjectDataDirOrSession() : null;
		}

		/// <summary>
		/// Exchange defaults for a host. Blender keeps the flat legacy names its shipped bridge already
		/// watches; later hosts get their own subfolder so concurrent handoffs cannot clobber each other.
		/// </summary>
		static void SpzGoDefaultExchangePaths(string dataDir, string hostId,
			out string importPath, out string exportPath) {
			if (string.IsNullOrEmpty(dataDir)) {
				importPath = "";
				exportPath = "";
				return;
			}
			string exchange = Path.Combine(dataDir, "StableProjectorzGO_exchange");
			bool legacyBlender = string.IsNullOrEmpty(hostId)
				|| string.Equals(hostId, SpzGoHosts.BlenderId, StringComparison.OrdinalIgnoreCase);
			if (!legacyBlender)
				exchange = Path.Combine(exchange, hostId);
			importPath = Path.Combine(exchange, legacyBlender ? "from_blender.fbx" : "from_" + hostId + ".fbx");
			exportPath = Path.Combine(exchange, "from_spz.fbx");
		}

		static void PersistSpzGoHostPathIfNeeded(string addonId, Transform widget, string label, string value) {
			if (!string.Equals(addonId, StableProjectorzGoAddonId, StringComparison.Ordinal)) return;
			string hostId = SpzGoHostSection.HostIdForWidget(widget);
			if (hostId == null) return;
			if (string.Equals(label, SpzGoHostSection.ImportPathLabel, StringComparison.Ordinal))
				SpzGoHostPrefs.SetPath(hostId, import: true, value: value);
			else if (string.Equals(label, SpzGoHostSection.ExportPathLabel, StringComparison.Ordinal))
				SpzGoHostPrefs.SetPath(hostId, import: false, value: value);
		}

		void RegisterAddonElement(string addonId, GameObject go) {
			if (go == null || string.IsNullOrEmpty(addonId)) return;
			if (!_addonUIElements.TryGetValue(addonId, out var list) || list == null) {
				list = new List<GameObject>();
				_addonUIElements[addonId] = list;
			}
			if (!list.Contains(go))
				list.Add(go);
		}

		/// <summary>
		/// Create a UI child that already owns a RectTransform. Parenting a plain Transform under a
		/// RectTransform converts it in place; calling AddComponent afterward returns null and the
		/// next sizeDelta write throws MissingComponentException.
		/// </summary>
		static RectTransform CreateUiChild(string name, Transform parent) {
			var go = new GameObject(name, typeof(RectTransform));
			go.transform.SetParent(parent, false);
			return (RectTransform)go.transform;
		}

		/// <summary>
		/// Vertical container that sizes itself to its children. Add-on panels drive child heights from
		/// LayoutElement rather than the group, so a nested stack needs the fitter to report any height
		/// at all — without it the section collapses to zero and nothing in it is visible.
		/// </summary>
		static GameObject NewStackContainer(string name, Transform parent, float spacing) {
			var go = CreateUiChild(name, parent).gameObject;
			var layout = go.AddComponent<VerticalLayoutGroup>();
			layout.spacing = spacing;
			layout.padding = new RectOffset(0, 0, 0, 0);
			layout.childControlWidth = true;
			layout.childControlHeight = false;
			layout.childForceExpandWidth = true;
			layout.childForceExpandHeight = false;
			var fitter = go.AddComponent<ContentSizeFitter>();
			fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
			fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			var le = go.AddComponent<LayoutElement>();
			le.flexibleWidth = 1f;
			return go;
		}

		static TextMeshProUGUI NewStretchedLabel(Transform parent, string name, string text,
			TextAlignmentOptions alignment) {
			var rt = CreateUiChild(name, parent);
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.sizeDelta = Vector2.zero;
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;
			var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
			tmp.text = text;
			tmp.fontSize = 13f;
			tmp.color = Color.white;
			tmp.alignment = alignment;
			tmp.raycastTarget = false;
			tmp.overflowMode = TextOverflowModes.Ellipsis;
			tmp.enableWordWrapping = false;
			ApplyRuntimeTmpFont(tmp);
			return tmp;
		}
	}
}

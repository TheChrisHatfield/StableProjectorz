using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// spz-go-multi-dcc phase 1: the SPZ GO panel is one section per DCC, and at rest a section shows
/// exactly three things — the logo that runs the selected mode, Import/Export (select + run), and a
/// collapsed Settings drop-tab. Host sections sit in a scroll view with a scrollbar. These build the
/// real widgets and press them.
/// </summary>
public sealed class SpzGoHostSectionContractTests {

	const string AddonId = "StableProjectorzGO";

	GameObject _host;
	GameObject _panel;
	AddonUI_MGR _mgr;
	readonly Dictionary<string, int> _savedInts = new Dictionary<string, int>();
	readonly Dictionary<string, string> _savedStrings = new Dictionary<string, string>();
	readonly HashSet<string> _absent = new HashSet<string>();

	[SetUp]
	public void SetUp() {
		foreach (var host in SpzGoHosts.All) {
			StashInt(SpzGoHostPrefs.AxisOrderKey(host.Id));
			StashInt(SpzGoHostPrefs.FlipKey(host.Id));
			StashInt(SpzGoHostPrefs.ModeKey(host.Id));
			StashInt(SpzGoHostPrefs.SettingsOpenKey(host.Id));
			StashString(SpzGoHostPrefs.ImportPathKey(host.Id));
			StashString(SpzGoHostPrefs.ExportPathKey(host.Id));
		}
		StashInt(ExportAxisSettings.AxisOrderPrefKey);
		StashInt(ExportAxisSettings.FlipXPrefKey);
		StashInt(ExportAxisSettings.FlipYPrefKey);
		StashInt(ExportAxisSettings.FlipZPrefKey);

		_host = new GameObject("SpzGoSectionHost");
		var canvas = _host.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		_host.AddComponent<GraphicRaycaster>();
		_mgr = _host.AddComponent<AddonUI_MGR>();

		var field = typeof(AddonUI_MGR).GetField("_addonUIElements",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(field, Is.Not.Null);
		var dict = (IDictionary)field.GetValue(_mgr);
		dict[AddonId] = new List<GameObject>();

		_panel = new GameObject("AddonPanel_StableProjectorzGO_SPZ GO");
		_panel.transform.SetParent(_host.transform, false);
		var panelRt = _panel.AddComponent<RectTransform>();
		panelRt.sizeDelta = new Vector2(320f, 0f);
		var panelLayout = _panel.AddComponent<VerticalLayoutGroup>();
		panelLayout.spacing = 6f;
		panelLayout.childControlHeight = false;
		panelLayout.childControlWidth = true;
		panelLayout.childForceExpandHeight = false;
		panelLayout.childForceExpandWidth = true;
		var panelFitter = _panel.AddComponent<ContentSizeFitter>();
		panelFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
		panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
		((List<GameObject>)dict[AddonId]).Add(_panel);

		foreach (var host in SpzGoHosts.All) {
			string sectionId = _mgr.AddHostSection(AddonId, _panel.GetInstanceID().ToString(), host.Id);
			Assert.That(sectionId, Is.Not.Null.And.Not.Empty, "section must build for " + host.Id);
		}
		// Same path as EnsureNativeSpzGoMissingWidgets — reparent into scroll + wire scrollbar.
		typeof(AddonUI_MGR).GetMethod("EnsureSpzGoPanelScroll",
				BindingFlags.Instance | BindingFlags.NonPublic)
			?.Invoke(_mgr, new object[] { _panel });
		LayoutRebuilder.ForceRebuildLayoutImmediate(panelRt);
		Canvas.ForceUpdateCanvases();
	}

	[TearDown]
	public void TearDown() {
		if (_host != null) UnityEngine.Object.DestroyImmediate(_host);
		foreach (var kv in _savedInts) PlayerPrefs.SetInt(kv.Key, kv.Value);
		foreach (var kv in _savedStrings) PlayerPrefs.SetString(kv.Key, kv.Value);
		foreach (string key in _absent) PlayerPrefs.DeleteKey(key);
		_savedInts.Clear();
		_savedStrings.Clear();
		_absent.Clear();
		PlayerPrefs.Save();
		SpzUiThemeOps.ResetTheme();
	}

	void StashInt(string key) {
		if (PlayerPrefs.HasKey(key)) _savedInts[key] = PlayerPrefs.GetInt(key);
		else _absent.Add(key);
		PlayerPrefs.DeleteKey(key);
	}

	void StashString(string key) {
		if (PlayerPrefs.HasKey(key)) _savedStrings[key] = PlayerPrefs.GetString(key);
		else _absent.Add(key);
		PlayerPrefs.DeleteKey(key);
	}

	Transform Section(string hostId) {
		var t = FindDescendant(_panel.transform, SpzGoHostSection.SectionName(hostId));
		Assert.That(t, Is.Not.Null, "missing section for " + hostId);
		return t;
	}

	static Transform FindDescendant(Transform root, string namePrefix) {
		foreach (var t in root.GetComponentsInChildren<Transform>(true)) {
			if (t != root && t.name.StartsWith(namePrefix, System.StringComparison.Ordinal))
				return t;
		}
		return null;
	}

	Transform SettingsContent(string hostId) {
		var content = FindDescendant(Section(hostId), "FoldoutContent_" + SpzGoHostSection.SettingsLabel);
		Assert.That(content, Is.Not.Null, "missing Settings drop-tab for " + hostId);
		return content;
	}

	[Test]
	public void EveryRegisteredHost_GetsItsOwnSection() {
		foreach (var host in SpzGoHosts.All)
			Assert.That(FindDescendant(_panel.transform, SpzGoHostSection.SectionName(host.Id)),
				Is.Not.Null, host.Id);
	}

	[Test]
	public void Panel_HasScrollViewAndWiredScrollbar() {
		var scrollT = _panel.transform.Find("SpzGoScrollView");
		Assert.That(scrollT, Is.Not.Null, "SPZ GO body must scroll when hosts overflow (R3e)");
		var scroll = scrollT.GetComponent<ScrollRect>();
		Assert.That(scroll, Is.Not.Null);
		Assert.That(scroll.vertical, Is.True);
		Assert.That(scroll.content, Is.Not.Null);
		Assert.That(scroll.verticalScrollbar, Is.Not.Null, "scrollbar must be assigned, not wheel-only");
		Assert.That(scroll.verticalScrollbar.handleRect, Is.Not.Null);
		Assert.That(_panel.transform.Find(SpzGoHostSection.SectionName(SpzGoHosts.BlenderId)), Is.Null,
			"host sections must live under scroll content, not the panel root");
		Assert.That(scroll.content.Find(SpzGoHostSection.SectionName(SpzGoHosts.BlenderId)), Is.Not.Null);

		var scrollLe = scrollT.GetComponent<LayoutElement>();
		Assert.That(scrollLe, Is.Not.Null);
		Assert.That(scrollLe.minHeight, Is.GreaterThanOrEqualTo(100f),
			"scroll body needs a real LayoutElement height — stretch anchors under VLG collapse to 0");
		Assert.That(scrollLe.flexibleHeight, Is.GreaterThan(0f));
		var scrollRt = scrollT as RectTransform;
		Assert.That(scrollRt, Is.Not.Null);
		Assert.That(scrollRt.anchorMin.y, Is.EqualTo(1f).Within(0.01f),
			"must not stretch-fill the AddonPanel VLG (that hid every host button)");
		Assert.That(scrollRt.anchorMax.y, Is.EqualTo(1f).Within(0.01f));
	}

	[Test]
	public void ScrollBody_KeepsPositiveHeightUnderRibbonStylePanel() {
		// Mirror the live ribbon: panel stretches inside a fixed shell; VLG must still give the
		// scroll view a non-zero height or RectMask2D clips every HostSection.
		var shell = new GameObject("RibbonShellStub", typeof(RectTransform));
		shell.transform.SetParent(_host.transform, false);
		var shellRt = shell.GetComponent<RectTransform>();
		shellRt.anchorMin = shellRt.anchorMax = new Vector2(0.5f, 0.5f);
		shellRt.sizeDelta = new Vector2(320f, 480f);

		_panel.transform.SetParent(shell.transform, false);
		var panelRt = _panel.GetComponent<RectTransform>();
		panelRt.anchorMin = Vector2.zero;
		panelRt.anchorMax = Vector2.one;
		panelRt.sizeDelta = Vector2.zero;
		panelRt.anchoredPosition = Vector2.zero;
		var fitter = _panel.GetComponent<ContentSizeFitter>();
		if (fitter != null)
			UnityEngine.Object.DestroyImmediate(fitter);

		typeof(AddonUI_MGR).GetMethod("EnsureSpzGoPanelScroll",
				BindingFlags.Instance | BindingFlags.NonPublic)
			?.Invoke(_mgr, new object[] { _panel });
		LayoutRebuilder.ForceRebuildLayoutImmediate(shellRt);
		LayoutRebuilder.ForceRebuildLayoutImmediate(panelRt);
		Canvas.ForceUpdateCanvases();

		var scrollRt = _panel.transform.Find("SpzGoScrollView") as RectTransform;
		Assert.That(scrollRt, Is.Not.Null);
		Assert.That(scrollRt.rect.height, Is.GreaterThan(80f),
			"scroll viewport must be tall enough to show host logos/Import/Export");
		var blender = FindDescendant(_panel.transform, SpzGoHostSection.SectionName(SpzGoHosts.BlenderId))
			as RectTransform;
		Assert.That(blender, Is.Not.Null);
		Assert.That(LayoutUtility.GetPreferredHeight(blender), Is.GreaterThan(40f));
	}

	[Test]
	public void ModeToggle_SelectsAndSharesActivatePathWithLogo() {
		var blender = Section(SpzGoHosts.BlenderId);
		var importToggle = blender.Find("ModeRow_" + SpzGoHosts.BlenderId)
			.Find(SpzGoHostSection.ModeToggleName(SpzGoHosts.BlenderId, SpzGoMode.Import));
		Assert.That(importToggle, Is.Not.Null);

		// Import click selects Import (and runs activate; not-ready honesty is covered elsewhere).
		importToggle.GetComponent<Button>().onClick.Invoke();
		Assert.That(SpzGoHostPrefs.GetMode(SpzGoHosts.BlenderId), Is.EqualTo(SpzGoMode.Import));
		AssertExactlyOneModeSelected(SpzGoHosts.BlenderId);

		// Source wiring: mode buttons must call SpzGoActivateHost after SetMode (dual path).
		string src = System.IO.File.ReadAllText(System.IO.Path.Combine(
			System.IO.Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonUI_MGR.SpzGoSections.cs"));
		int toggle = src.IndexOf("void BuildHostModeToggle(", System.StringComparison.Ordinal);
		Assert.That(toggle, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(toggle, Math.Min(1100, src.Length - toggle));
		Assert.That(body, Does.Contain("SpzGoHostPrefs.SetMode"));
		Assert.That(body, Does.Contain("SpzGoActivateHost(host.Id, sectionId)"));
	}

	[Test]
	public void AtRest_ASectionShowsOnlyLogoModeTogglesAndACollapsedDropTab() {
		foreach (var host in SpzGoHosts.All) {
			var section = Section(host.Id);
			Assert.That(section.childCount, Is.EqualTo(3),
				host.Id + " must show exactly logo, mode row, Settings drop-tab (R3b)");
			Assert.That(section.Find(SpzGoHostSection.LogoName(host.Id)), Is.Not.Null,
				host.Id + " needs a logo activate button");
			Assert.That(section.Find("ModeRow_" + host.Id), Is.Not.Null);
			Assert.That(SettingsContent(host.Id).gameObject.activeSelf, Is.False,
				host.Id + " Settings must start collapsed (R4)");
		}
	}

	[Test]
	public void EverySection_CarriesTheMandatoryAgnosticControls() {
		foreach (var host in SpzGoHosts.All) {
			var settings = SettingsContent(host.Id);
			Assert.That(FindDescendant(settings, "Dropdown_" + ExportAxisSettings.AxisOrderLabel), Is.Not.Null, host.Id);
			Assert.That(FindDescendant(settings, "Dropdown_" + ExportAxisSettings.FlipLabel), Is.Not.Null, host.Id);
			Assert.That(FindDescendant(settings, "Button_" + SpzGoHostSection.AutofillLabel), Is.Not.Null, host.Id);
			Assert.That(FindDescendant(settings, "TextInput_" + SpzGoHostSection.ImportPathLabel), Is.Not.Null, host.Id);
			Assert.That(FindDescendant(settings, "TextInput_" + SpzGoHostSection.ExportPathLabel), Is.Not.Null, host.Id);
		}
	}

	[Test]
	public void HostSpecificExtras_StayInsideTheirOwnHost() {
		Assert.That(FindDescendant(SettingsContent(SpzGoHosts.BlenderId), "Button_Install into Blender"),
			Is.Not.Null);
		Assert.That(FindDescendant(Section(SpzGoHosts.ZBrushId), "Button_Install into Blender"), Is.Null,
			"a Blender-only helper must not appear under another host (R16)");
		Assert.That(FindDescendant(Section(SpzGoHosts.PainterId), "TextInput_Blender.exe"), Is.Null);
	}

	[Test]
	public void ModeToggles_AreMutuallyExclusiveAndHostScoped() {
		var blender = Section(SpzGoHosts.BlenderId);
		var importToggle = blender.Find("ModeRow_" + SpzGoHosts.BlenderId)
			.Find(SpzGoHostSection.ModeToggleName(SpzGoHosts.BlenderId, SpzGoMode.Import));
		Assert.That(importToggle, Is.Not.Null);

		importToggle.GetComponent<Button>().onClick.Invoke();

		Assert.That(SpzGoHostPrefs.GetMode(SpzGoHosts.BlenderId), Is.EqualTo(SpzGoMode.Import));
		Assert.That(SpzGoHostPrefs.GetMode(SpzGoHosts.PainterId), Is.EqualTo(SpzGoMode.Export),
			"selecting a direction for one host must not move another (R5)");
		AssertExactlyOneModeSelected(SpzGoHosts.BlenderId);

		// Pressing the mode that is already on must re-select it, never clear the section to no mode.
		importToggle.GetComponent<Button>().onClick.Invoke();
		Assert.That(SpzGoHostPrefs.GetMode(SpzGoHosts.BlenderId), Is.EqualTo(SpzGoMode.Import));
		AssertExactlyOneModeSelected(SpzGoHosts.BlenderId);
	}

	void AssertExactlyOneModeSelected(string hostId) {
		var row = Section(hostId).Find("ModeRow_" + hostId);
		var selected = SpzGoHostPrefs.GetMode(hostId);
		var onImage = row.Find(SpzGoHostSection.ModeToggleName(hostId, selected)).GetComponent<Image>();
		var other = selected == SpzGoMode.Import ? SpzGoMode.Export : SpzGoMode.Import;
		var offImage = row.Find(SpzGoHostSection.ModeToggleName(hostId, other)).GetComponent<Image>();
		Assert.That(onImage.color, Is.Not.EqualTo(offImage.color),
			hostId + ": the selected mode must read differently from the unselected one");
	}

	[Test]
	public void OpeningOneHostsSettings_LeavesTheOthersCollapsed() {
		var header = FindDescendant(Section(SpzGoHosts.ZBrushId),
			"FoldoutHeader_" + SpzGoHostSection.SettingsLabel);
		Assert.That(header, Is.Not.Null);
		header.GetComponent<Button>().onClick.Invoke();

		Assert.That(SettingsContent(SpzGoHosts.ZBrushId).gameObject.activeSelf, Is.True);
		Assert.That(SettingsContent(SpzGoHosts.BlenderId).gameObject.activeSelf, Is.False);
		Assert.That(SpzGoHostPrefs.GetSettingsOpen(SpzGoHosts.ZBrushId), Is.True);
		Assert.That(SpzGoHostPrefs.GetSettingsOpen(SpzGoHosts.BlenderId), Is.False);
	}

	[Test]
	public void SettingsHeader_UsesAddonManagerStyleChevronNotUnicodeOrCheckbox() {
		foreach (var host in SpzGoHosts.All) {
			var header = FindDescendant(Section(host.Id),
				"FoldoutHeader_" + SpzGoHostSection.SettingsLabel);
			Assert.That(header, Is.Not.Null, host.Id);
			var chevron = header.Find("ExpandChevron");
			Assert.That(chevron, Is.Not.Null, host.Id + " needs the Addon Manager expand chevron");
			var arrow = chevron.Find("Arrow");
			Assert.That(arrow, Is.Not.Null, host.Id + " chevron must carry the image arrow");
			var arrowImg = arrow.GetComponent<Image>();
			Assert.That(arrowImg, Is.Not.Null);
			Assert.That(arrowImg.sprite, Is.Not.Null, host.Id + " arrow needs a real sprite, not a TMP glyph");
			var title = header.Find("Text").GetComponent<TMPro.TextMeshProUGUI>();
			Assert.That(title.text, Is.EqualTo(SpzGoHostSection.SettingsLabel),
				"label must be plain text — unicode ▸/▾ render as missing boxes on this TMP font");
			Assert.That(title.text, Does.Not.Contain("▾").And.Not.Contain("▸"));
		}
	}

	[Test]
	public void OpeningSettings_PushesTheNextHostSectionDownInsteadOfStacking() {
		// Nested ContentSizeFitters used to leave the open Settings body at zero claimed height, so the
		// next host's logo drew through the dropdowns. After open, Blender's section must own enough
		// vertical space that ZBrush starts below it.
		var blender = Section(SpzGoHosts.BlenderId) as RectTransform;
		var zbrush = Section(SpzGoHosts.ZBrushId) as RectTransform;
		Assert.That(blender, Is.Not.Null);
		Assert.That(zbrush, Is.Not.Null);

		float closedPreferred = LayoutUtility.GetPreferredHeight(blender);
		var header = FindDescendant(blender, "FoldoutHeader_" + SpzGoHostSection.SettingsLabel);
		header.GetComponent<Button>().onClick.Invoke();

		float openPreferred = LayoutUtility.GetPreferredHeight(blender);
		Assert.That(openPreferred, Is.GreaterThan(closedPreferred + 40f),
			"open Settings must grow the host section past logo+modes+header alone");

		var corners = new Vector3[4];
		blender.GetWorldCorners(corners);
		float blenderBottom = corners[0].y;
		zbrush.GetWorldCorners(corners);
		float zbrushTop = corners[1].y;
		Assert.That(zbrushTop, Is.LessThanOrEqualTo(blenderBottom + 0.5f),
			"ZBrush must start at or below Blender's open bottom — not overlap its Settings body");
	}

	[Test]
	public void PickingAnAxisInOneSection_OnlyMovesThatHostsStoredBasis() {
		var dropdown = FindDescendant(SettingsContent(SpzGoHosts.BlenderId),
			"Dropdown_" + ExportAxisSettings.AxisOrderLabel)
			.GetComponentInChildren<TMPro.TMP_Dropdown>(true);
		Assert.That(dropdown, Is.Not.Null);
		int zyx = System.Array.IndexOf(ExportAxisSettings.AxisOrderNames, "ZYX");

		dropdown.value = zyx;

		Assert.That(SpzGoHostPrefs.GetAxisOrderIndex(SpzGoHosts.BlenderId), Is.EqualTo(zyx));
		Assert.That(SpzGoHostPrefs.GetAxisOrderIndex(SpzGoHosts.ZBrushId), Is.EqualTo(0),
			"another host's basis must be untouched");
		Assert.That(ExportAxisSettings.AxisOrderIndex, Is.EqualTo(0),
			"the shared export basis only moves when that host actually runs a transfer");
	}

	[Test]
	public void AHostWithNoBridge_DoesNotEvenRepointTheExportPipeline() {
		var zbrush = SpzGoHosts.Get(SpzGoHosts.ZBrushId);
		Assert.That(zbrush.BridgeReady, Is.False, "this test covers the stub path");
		SpzGoHostPrefs.SetAxisOrderIndex(SpzGoHosts.ZBrushId, (int)ExportAxisSettings.AxisOrder.YZX);

		Section(SpzGoHosts.ZBrushId)
			.Find(SpzGoHostSection.LogoName(SpzGoHosts.ZBrushId))
			.GetComponent<Button>().onClick.Invoke();

		Assert.That(ExportAxisSettings.AxisOrderIndex, Is.EqualTo(0),
			"a not-ready host must report the prerequisite, not start setting up a transfer (R13)");
	}

	[Test]
	public void InstallingABridge_LightsThatHostsLogoWithoutARebuild() {
		// The Install button sits under the Settings drop-tab content, so the refresh is handed that
		// content's id — not the section. It still has to reach the logo, which is a sibling branch.
		var zbrush = SpzGoHosts.Get(SpzGoHosts.ZBrushId);
		var logo = Section(SpzGoHosts.ZBrushId)
			.Find(SpzGoHostSection.LogoName(SpzGoHosts.ZBrushId)).GetComponent<Image>();
		var notReadyColor = logo.color;

		string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SpzGoLogoRefresh_" + System.Guid.NewGuid().ToString("N"));
		string plugin = System.IO.Path.Combine(root, "plugin");
		string savedOverride = SpzGoBridgeInstall.InstallRootOverride;
		var savedProbe = SpzGoHosts.BridgeInstalledProbe;
		try {
			System.IO.Directory.CreateDirectory(plugin);
			SpzGoBridgeInstall.InstallRootOverride = root;
			SpzGoHosts.BridgeInstalledProbe = SpzGoBridgeInstall.IsInstalled;
			Assert.That(SpzGoHosts.IsBridgeReady(SpzGoHosts.ZBrushId), Is.False, "precondition: not installed yet");
			Assert.That(SpzGoBridgeInstall.MarkInstalled(SpzGoHosts.ZBrushId, plugin), Is.True);
			Assert.That(SpzGoHosts.IsBridgeReady(SpzGoHosts.ZBrushId), Is.True);

			string settingsContentId = SettingsContent(SpzGoHosts.ZBrushId).gameObject.GetInstanceID().ToString();
			var refresh = typeof(AddonUI_MGR).GetMethod("SpzGoRefreshHostReadiness",
				BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(refresh, Is.Not.Null);
			refresh.Invoke(_mgr, new object[] { settingsContentId, SpzGoHosts.ZBrushId });

			Assert.That(logo.color, Is.Not.EqualTo(notReadyColor),
				"an installed bridge must stop reading as not-ready straight away");
			// Only the installed host changes; a still-missing bridge stays dim.
			var painterLogo = Section(SpzGoHosts.PainterId)
				.Find(SpzGoHostSection.LogoName(SpzGoHosts.PainterId)).GetComponent<Image>();
			Assert.That(painterLogo.color, Is.EqualTo(notReadyColor),
				"installing one host must not light another host's logo");
		} finally {
			SpzGoBridgeInstall.ClearInstalled(SpzGoHosts.ZBrushId);
			SpzGoBridgeInstall.InstallRootOverride = savedOverride;
			SpzGoHosts.BridgeInstalledProbe = savedProbe;
			try { System.IO.Directory.Delete(root, true); } catch { }
		}
		Assert.That(zbrush.BridgeReady, Is.False, "the static stub flag itself must not be mutated");
	}

	[Test]
	public void ANativeExportFailure_DoesNotPostACallbackPythonCannotHave() {
		string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SpzGoNoFallback_" + System.Guid.NewGuid().ToString("N"));
		string plugin = System.IO.Path.Combine(root, "plugin");
		string savedOverride = SpzGoBridgeInstall.InstallRootOverride;
		var savedProbe = SpzGoHosts.BridgeInstalledProbe;
		var logs = new List<string>();
		Application.LogCallback handler = (msg, stack, type) => logs.Add(msg ?? "");
		try {
			System.IO.Directory.CreateDirectory(plugin);
			SpzGoBridgeInstall.InstallRootOverride = root;
			SpzGoHosts.BridgeInstalledProbe = SpzGoBridgeInstall.IsInstalled;
			SpzGoBridgeInstall.MarkInstalled(SpzGoHosts.ZBrushId, plugin);
			SpzGoHostPrefs.SetMode(SpzGoHosts.ZBrushId, SpzGoMode.Export);

			// Empty export path is the cheapest way to make the native run fail with a specific reason.
			var row = FindDescendant(SettingsContent(SpzGoHosts.ZBrushId),
				"TextInput_" + SpzGoHostSection.ExportPathLabel);
			Assert.That(row, Is.Not.Null);
			var input = row.GetComponentInChildren<TMPro.TMP_InputField>(true);
			Assert.That(input, Is.Not.Null);
			input.text = "";

			Application.logMessageReceived += handler;
			Section(SpzGoHosts.ZBrushId)
				.Find(SpzGoHostSection.LogoName(SpzGoHosts.ZBrushId))
				.GetComponent<Button>().onClick.Invoke();
		} finally {
			Application.logMessageReceived -= handler;
			SpzGoBridgeInstall.ClearInstalled(SpzGoHosts.ZBrushId);
			SpzGoBridgeInstall.InstallRootOverride = savedOverride;
			SpzGoHosts.BridgeInstalledProbe = savedProbe;
			try { System.IO.Directory.Delete(root, true); } catch { }
		}

		// Unity invents the "__zbrush" suffix, so a Python handler by that name cannot exist. Posting it
		// would only bury the real reason under a generic add-on failure.
		foreach (string msg in logs) {
			Assert.That(msg.Contains("Invoking addon callback"), Is.False,
				"native failure must not post a host-qualified callback: " + msg);
		}
	}

	[Test]
	public void ZBrushImportRequest_DoesNotClaimAutoPush() {
		string path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonUI_MGR.SpzGoSections.cs");
		string src = System.IO.File.ReadAllText(path);
		Assert.That(SpzGoHosts.ZBrush.AnswersPullAutomatically, Is.False,
			"ZBrush answers only via the Answer SPZ button — no exchange watcher");
		Assert.That(SpzGoHosts.Blender.AnswersPullAutomatically, Is.True);
		Assert.That(src, Does.Contain("AnswersPullAutomatically"),
			"Import status must branch on whether the host auto-answers");
		Assert.That(src, Does.Contain("Answer SPZ request"),
			"ZBrush Import must tell the user which in-DCC button to press");
	}

	[Test]
	public void SettingsStateSurvivesARebuiltPanel() {
		SpzGoHostPrefs.SetSettingsOpen(SpzGoHosts.PainterId, true);
		SpzGoHostPrefs.SetMode(SpzGoHosts.PainterId, SpzGoMode.Import);

		string rebuiltId = _mgr.AddHostSection(AddonId, _panel.GetInstanceID().ToString(), SpzGoHosts.PainterId);
		Assert.That(rebuiltId, Is.Not.Null);
		// AddHostSection appends under scroll content; the newest section is the last match.
		Transform rebuilt = null;
		foreach (var t in _panel.GetComponentsInChildren<Transform>(true)) {
			if (t != null && t.name == SpzGoHostSection.SectionName(SpzGoHosts.PainterId))
				rebuilt = t;
		}
		Assert.That(rebuilt, Is.Not.Null);

		var content = FindDescendant(rebuilt, "FoldoutContent_" + SpzGoHostSection.SettingsLabel);
		Assert.That(content.gameObject.activeSelf, Is.True,
			"a drop-tab the user opened must come back open");
		var row = rebuilt.Find("ModeRow_" + SpzGoHosts.PainterId);
		var importImage = row.Find(SpzGoHostSection.ModeToggleName(SpzGoHosts.PainterId, SpzGoMode.Import))
			.GetComponent<Image>();
		// Blender is still on its default Export, so its Export face is the "selected" colour to match.
		var selectedReference = Section(SpzGoHosts.BlenderId)
			.Find("ModeRow_" + SpzGoHosts.BlenderId)
			.Find(SpzGoHostSection.ModeToggleName(SpzGoHosts.BlenderId, SpzGoMode.Export))
			.GetComponent<Image>();
		Assert.That(importImage.color, Is.EqualTo(selectedReference.color),
			"the remembered mode must come back selected, not just different from its neighbour");
	}
}

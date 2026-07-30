using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Command ribbon strip tabs: only the Button face may raycast — dividers/labels steal clicks under Nomad.
/// Prefab tabs often ship with null targetGraphic; clearing must still keep TabBg hittable.
/// </summary>
public sealed class CommandRibbonStripRaycastThemeTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void ThemeStripTabCell_SourceClearsNonFaceRaycasts() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Layouts/RightPanel/CommandRibbon_UI.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ClearStripTabNonFaceRaycasts"));
		Assert.That(src, Does.Contain("HideMonolithOverlaysUnder"));
		Assert.That(src, Does.Contain("Button.targetGraphic == null"));
		int theme = src.IndexOf("void ThemeStripTabCell", System.StringComparison.Ordinal);
		Assert.That(theme, Is.GreaterThan(0));
		int leave = src.IndexOf("if (!recolorChrome)", theme, System.StringComparison.Ordinal);
		int nomad = src.IndexOf("Color fill = FlatStripTabFill", theme, System.StringComparison.Ordinal);
		Assert.That(leave, Is.GreaterThan(0));
		Assert.That(nomad, Is.GreaterThan(leave));
		string leaveBody = src.Substring(leave, nomad - leave);
		Assert.That(leaveBody, Does.Not.Contain("ClearStripTabNonFaceRaycasts"));
		Assert.That(leaveBody, Does.Not.Contain("EnsureStripTabHitFace"),
			"Leave must not Ensure TabBg after Restore SPZ (sticky synthetic face)");
		Assert.That(src.IndexOf("ClearStripTabNonFaceRaycasts(cell)", nomad, System.StringComparison.Ordinal), Is.GreaterThan(0));
	}

	[Test]
	public void ClearStripTabNonFaceRaycasts_NullTargetGraphic_KeepsTabBgHittable() {
		var cell = new GameObject("art list", typeof(RectTransform), typeof(Button));
		cell.SetActive(false);
		try {
			var btn = cell.GetComponent<Button>();
			btn.targetGraphic = null; // prefab Art/BG/Mesh/Control pattern

			var tabBgGo = new GameObject("TabBg", typeof(RectTransform), typeof(Image));
			tabBgGo.transform.SetParent(cell.transform, false);
			var tabBg = tabBgGo.GetComponent<Image>();
			tabBg.raycastTarget = true;

			var labelGo = new GameObject("Label", typeof(RectTransform));
			labelGo.transform.SetParent(cell.transform, false);
			var label = labelGo.AddComponent<TextMeshProUGUI>();
			label.raycastTarget = true;

			var pillGo = new GameObject("go active", typeof(RectTransform));
			pillGo.transform.SetParent(cell.transform, false);
			var pillImgGo = new GameObject("image", typeof(RectTransform), typeof(Image));
			pillImgGo.transform.SetParent(pillGo.transform, false);
			var pill = pillImgGo.GetComponent<Image>();
			pill.raycastTarget = true;

			var clear = typeof(CommandRibbon_UI).GetMethod(
				"ClearStripTabNonFaceRaycasts",
				BindingFlags.Static | BindingFlags.NonPublic);
			Assert.That(clear, Is.Not.Null);
			clear.Invoke(null, new object[] { cell.transform });

			Assert.That(btn.targetGraphic, Is.SameAs(tabBg), "null targetGraphic must wire TabBg");
			Assert.That(tabBg.raycastTarget, Is.True, "TabBg must remain the hit face");
			Assert.That(label.raycastTarget, Is.False);
			Assert.That(pill.raycastTarget, Is.False);
		}
		finally {
			Object.DestroyImmediate(cell);
		}
	}

	[Test]
	public void ThemeStripTabCell_Nomad_PrefabNullTargetStillClickableFace() {
		var cell = new GameObject("mesh", typeof(RectTransform), typeof(Button), typeof(TabsGroupElem_UI));
		cell.SetActive(false);
		try {
			var btn = cell.GetComponent<Button>();
			btn.targetGraphic = null;

			var tabBgGo = new GameObject("TabBg", typeof(RectTransform), typeof(Image));
			tabBgGo.transform.SetParent(cell.transform, false);
			var tabBg = tabBgGo.GetComponent<Image>();
			tabBg.type = Image.Type.Sliced;
			tabBg.raycastTarget = true;

			var labelGo = new GameObject("Label", typeof(RectTransform));
			labelGo.transform.SetParent(cell.transform, false);
			var label = labelGo.AddComponent<TextMeshProUGUI>();
			label.text = "MESH";
			label.raycastTarget = true;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["text_primary"] = "#E3E2E7FF",
					["ribbon_icon_only"] = 1,
				},
				"replace",
				out string error), Is.True, error);

			var theme = typeof(CommandRibbon_UI).GetMethod(
				"ThemeStripTabCell",
				BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(theme, Is.Not.Null);
			var host = new GameObject("RibbonHost").AddComponent<CommandRibbon_UI>();
			try {
				theme.Invoke(host, new object[] {
					cell.transform,
					SpzUiThemeOps.Active,
					true,
					true,
				});
			}
			finally {
				Object.DestroyImmediate(host.gameObject);
			}

			Assert.That(btn.targetGraphic, Is.SameAs(tabBg));
			Assert.That(tabBg.raycastTarget, Is.True);
			Assert.That(label.raycastTarget, Is.False);
		}
		finally {
			Object.DestroyImmediate(cell);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ThemeStripTabCell_Nomad_PrefabNoTabBg_CreatesHitFaceAfterLabelClear() {
		// Prefab Art/Control pattern: Button + TMP only, no TabBg, null targetGraphic.
		var cell = new GameObject("Tab: art list", typeof(RectTransform), typeof(Button), typeof(TabsGroupElem_UI));
		cell.SetActive(false);
		try {
			var btn = cell.GetComponent<Button>();
			btn.targetGraphic = null;

			var labelGo = new GameObject("Input (text)", typeof(RectTransform));
			labelGo.transform.SetParent(cell.transform, false);
			var label = labelGo.AddComponent<TextMeshProUGUI>();
			label.text = "ART";
			label.raycastTarget = true;

			var activeGo = new GameObject("go active", typeof(RectTransform));
			activeGo.transform.SetParent(cell.transform, false);
			activeGo.SetActive(false); // inactive tab — pill cannot receive hits
			var pillGo = new GameObject("image", typeof(RectTransform), typeof(Image));
			pillGo.transform.SetParent(activeGo.transform, false);
			pillGo.GetComponent<Image>().raycastTarget = true;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["text_primary"] = "#E3E2E7FF",
				},
				"replace",
				out string error), Is.True, error);

			var theme = typeof(CommandRibbon_UI).GetMethod(
				"ThemeStripTabCell",
				BindingFlags.Instance | BindingFlags.NonPublic);
			var host = new GameObject("RibbonHost").AddComponent<CommandRibbon_UI>();
			try {
				theme.Invoke(host, new object[] {
					cell.transform,
					SpzUiThemeOps.Active,
					true,
					false,
				});
			}
			finally {
				Object.DestroyImmediate(host.gameObject);
			}

			Assert.That(btn.targetGraphic, Is.Not.Null, "must create/wire a hit face");
			Assert.That(btn.targetGraphic.raycastTarget, Is.True);
			Assert.That(label.raycastTarget, Is.False, "Nomad strip labels clear raycast");
			var created = cell.transform.Find("TabBg")?.GetComponent<Image>();
			Assert.That(created, Is.Not.Null);
			Assert.That(created.raycastTarget, Is.True);
			Assert.That(ReferenceEquals(btn.targetGraphic, created), Is.True);
		}
		finally {
			Object.DestroyImmediate(cell);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ClearStripTabNonFaceRaycasts_NullFace_DoesNotMassClear() {
		var cell = new GameObject("orphan tab", typeof(RectTransform), typeof(Button));
		cell.SetActive(false);
		try {
			var btn = cell.GetComponent<Button>();
			btn.targetGraphic = null;
			// No TabBg — FindStripTabFaceImage returns null.
			var labelGo = new GameObject("Label", typeof(RectTransform));
			labelGo.transform.SetParent(cell.transform, false);
			var label = labelGo.AddComponent<TextMeshProUGUI>();
			label.raycastTarget = true;

			var clear = typeof(CommandRibbon_UI).GetMethod(
				"ClearStripTabNonFaceRaycasts",
				BindingFlags.Static | BindingFlags.NonPublic);
			Assert.That(clear, Is.Not.Null);
			clear.Invoke(null, new object[] { cell.transform });

			Assert.That(label.raycastTarget, Is.True, "must not clear when face unresolved");
		}
		finally {
			Object.DestroyImmediate(cell);
		}
	}

	[Test]
	public void ClearStripTabNonFaceRaycasts_SnapshotsRaycastForLeaveRestore() {
		var cell = new GameObject("art list", typeof(RectTransform), typeof(Button));
		cell.SetActive(false);
		try {
			var tabBgGo = new GameObject("TabBg", typeof(RectTransform), typeof(Image));
			tabBgGo.transform.SetParent(cell.transform, false);
			var tabBg = tabBgGo.GetComponent<Image>();
			tabBg.raycastTarget = true;
			cell.GetComponent<Button>().targetGraphic = tabBg;

			var labelGo = new GameObject("Label", typeof(RectTransform));
			labelGo.transform.SetParent(cell.transform, false);
			var label = labelGo.AddComponent<TextMeshProUGUI>();
			label.raycastTarget = true;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject { ["control_bg"] = "#292A2EFF", ["accent"] = "#F2CA50FF" },
				"replace",
				out string error), Is.True, error);

			var clear = typeof(CommandRibbon_UI).GetMethod(
				"ClearStripTabNonFaceRaycasts",
				BindingFlags.Static | BindingFlags.NonPublic);
			clear.Invoke(null, new object[] { cell.transform });
			Assert.That(label.raycastTarget, Is.False);

			SpzUiThemeOps.RestoreBoundChromeUnder(cell.transform);
			Assert.That(label.raycastTarget, Is.True, "Restore SPZ must unwind ClearStrip raycast clears");
		}
		finally {
			Object.DestroyImmediate(cell);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void SettingsLauncher_SourceClearsChildGraphicRaycasts() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/Settings/Settings_UI.cs"));
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("static void ThemeFlatLauncherButton", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThan(0));
		string body = src.Substring(idx, System.Math.Min(900, src.Length - idx));
		Assert.That(body, Does.Contain("EnsureSelectableHitFace"));
		Assert.That(body, Does.Contain("ClearNonFaceRaycastsForTheme"));
		Assert.That(body, Does.Not.Contain("ReferenceEquals(g, btn.targetGraphic)"));
	}
}

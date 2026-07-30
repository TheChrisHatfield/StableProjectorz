using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top-strip SD SERV / 3D SERV + folder picker must leave Unity default chrome under Nomad.
/// </summary>
public sealed class RestartWebuiChromeThemeTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
		UiRuntimeSprites.ClearLineIconCache();
	}

	[Test]
	public void Source_WiresThemeChangedOnLaunchAndFileButtons() {
		string src = System.IO.File.ReadAllText(
			"Assets/_gm/Features/StableDiffusion/Webui/RestartTheWebui.cs");
		Assert.That(src, Does.Contain("ThemeChanged += ApplyThemeTokens"));
		Assert.That(src, Does.Contain("ApplyBoundChromeSelectable"));
		Assert.That(src, Does.Contain("ApplyBoundChromeCompactToolLabelTmp"));
		Assert.That(src, Does.Contain("StudioLineIcon.Folder"));
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder"));
	}

	[Test]
	public void ThemeCatalog_TopStripServSurfaceNamesRestartOwnerNotConnectionPanel() {
		var method = typeof(SpzUiThemeOps).GetMethod(
			"BuildSurfaces", BindingFlags.Static | BindingFlags.NonPublic);
		Assert.That(method, Is.Not.Null);
		var surfaces = (JArray)method.Invoke(null, null);
		JToken serv = null;
		JToken connection = null;
		foreach (var s in surfaces) {
			if ((string)s["id"] == "top_strip_serv") serv = s;
			if ((string)s["id"] == "connection_panels") connection = s;
		}
		Assert.That(serv, Is.Not.Null);
		Assert.That((bool)serv["bound"], Is.True);
		Assert.That((string)serv["notes"], Does.Contain("RestartTheWebui"));
		Assert.That((string)connection["notes"], Does.Not.Contain("SD SERV").IgnoreCase);
	}

	[Test]
	public void ApplyThemeTokens_FlattensServAndFolderAndKeepsHitFaces() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject {
				["control_bg"] = "#292A2EFF",
				["accent"] = "#F2CA50FF",
				["text_primary"] = "#E3E2E7FF",
				["icon_tint"] = "#D0C5AFFF",
				["corner_radius"] = 4,
			},
			"replace",
			out string error), Is.True, error);

		var root = new GameObject("RestartWebui", typeof(RectTransform));
		root.SetActive(false);
		try {
			Button MakeBtn(string name, string labelText, out TextMeshProUGUI label) {
				var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
				go.transform.SetParent(root.transform, false);
				var face = go.GetComponent<Image>();
				face.type = Image.Type.Sliced;
				face.color = new Color(0.85f, 0.85f, 0.85f, 1f);
				var btn = go.GetComponent<Button>();
				btn.targetGraphic = face;
				var labelGo = new GameObject("Text", typeof(RectTransform));
				labelGo.transform.SetParent(go.transform, false);
				label = labelGo.AddComponent<TextMeshProUGUI>();
				label.text = labelText;
				label.color = Color.black;
				label.raycastTarget = true;
				return btn;
			}

			var launch = MakeBtn("SD SERV", "SD SERV", out var launchLabel);
			var file = MakeBtn("Folder", "", out var fileLabel);
			fileLabel.text = "";

			var ui = root.AddComponent<RestartTheWebui>();
			var flags = BindingFlags.Instance | BindingFlags.NonPublic;
			typeof(RestartTheWebui).GetField("_launchButton", flags).SetValue(ui, launch);
			typeof(RestartTheWebui).GetField("_fileButton", flags).SetValue(ui, file);

			typeof(RestartTheWebui).GetMethod("ApplyThemeTokens",
				BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
				.Invoke(ui, null);

			Assert.That(launch.targetGraphic.color, Is.EqualTo(SpzUiThemeOps.Active.controlBg));
			Assert.That(file.targetGraphic.color, Is.EqualTo(SpzUiThemeOps.Active.controlBg));
			Assert.That(UiRuntimeSprites.IsSolidRect(((Image)launch.targetGraphic).sprite), Is.True);
			Assert.That(launchLabel.color, Is.EqualTo(SpzUiThemeOps.Active.textPrimary));
			Assert.That(launchLabel.raycastTarget, Is.False);
			Assert.That(launchLabel.characterSpacing, Is.LessThan(4f), "SD SERV must not use strip tracking 18");
			Assert.That(launchLabel.enableWordWrapping, Is.False);
			Assert.That(launch.targetGraphic.raycastTarget, Is.True);

			var folderIcon = SpzUiThemeOps.FindDirectChildIncludingInactive(file.transform, "MonolithLineIcon");
			Assert.That(folderIcon, Is.Not.Null);
			Assert.That(folderIcon.GetComponent<Image>().sprite,
				Is.EqualTo(UiRuntimeSprites.GetLineIcon(StudioLineIcon.Folder)));
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void BuiltinLeave_RestoresServChrome() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject {
				["control_bg"] = "#292A2EFF",
				["text_primary"] = "#E3E2E7FF",
			},
			"replace",
			out string error), Is.True, error);

		var root = new GameObject("RestartLeave", typeof(RectTransform));
		root.SetActive(false);
		try {
			var go = new GameObject("SERV", typeof(RectTransform), typeof(Image), typeof(Button));
			go.transform.SetParent(root.transform, false);
			var face = go.GetComponent<Image>();
			Color light = new Color(0.85f, 0.85f, 0.85f, 1f);
			face.color = light;
			var btn = go.GetComponent<Button>();
			btn.targetGraphic = face;

			var ui = root.AddComponent<RestartTheWebui>();
			var flags = BindingFlags.Instance | BindingFlags.NonPublic;
			typeof(RestartTheWebui).GetField("_launchButton", flags).SetValue(ui, btn);
			typeof(RestartTheWebui).GetField("_fileButton", flags).SetValue(ui, btn);

			typeof(RestartTheWebui).GetMethod("ApplyThemeTokens",
				BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
				.Invoke(ui, null);
			Assert.That(face.color, Is.EqualTo(SpzUiThemeOps.Active.controlBg));

			SpzUiThemeOps.ResetTheme();
			typeof(RestartTheWebui).GetMethod("ApplyThemeTokens",
				BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
				.Invoke(ui, null);
			Assert.That(face.color.r, Is.EqualTo(light.r).Within(0.001f));
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}
}

using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// After Nomad control_bg was lightened, PROJ MASK / COLOR / ENTIRE kept painting text_primary
/// (light) on control faces → washed-out / unreadable mode cells when loading the theme.
/// </summary>
public sealed class WorkflowRibbonModeInkOnLightControlContractTests {

	[Test]
	public void InkOnControlFace_PicksDarkInkWhenControlBgIsLight() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-light-control",
			new JObject {
				["control_bg"] = "#9A9EAAFF",
				["field_bg"] = "#121317FF",
				["text_primary"] = "#E3E2E7FF",
				["accent"] = "#F2CA50FF",
			},
			"replace",
			out string error), Is.True, error);
		try {
			Color ink = SpzUiThemeOps.InkOnControlFace(SpzUiThemeOps.Active);
			Assert.That(ink, Is.EqualTo(SpzUiThemeOps.Active.fieldBg),
				"light control plates need dark field_bg ink");
		}
		finally {
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ThemeModeToggle_UsesInkOnControlFaceNotRawTextPrimary() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "WorkflowToolsRibbon SD", "WorkflowRibbon_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int i = src.IndexOf("static void ThemeModeToggle(", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i, System.Math.Min(4500, src.Length - i));
		Assert.That(body, Does.Contain("InkOnControlFace(t)"));
		Assert.That(body, Does.Contain("ApplyNomadStackedToolCell"));
		Assert.That(body, Does.Contain("selectionChromeOnly"));
	}

	[Test]
	public void ThemeModeToggle_AppliesDarkInkOnLightControlFace() {
		var root = new GameObject("ColorModeInk");
		root.SetActive(false);
		try {
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-light-mode-ink",
				new JObject {
					["control_bg"] = "#9A9EAAFF",
					["field_bg"] = "#121317FF",
					["text_primary"] = "#E3E2E7FF",
					["accent"] = "#F2CA50FF",
					["corner_radius"] = 0,
				},
				"replace",
				out string error), Is.True, error);

			var mode = root.AddComponent<WorkflowRibbon_Colors_UI>();
			var faceGo = new GameObject("Toggle", typeof(RectTransform), typeof(Image), typeof(Toggle));
			faceGo.transform.SetParent(root.transform, false);
			var face = faceGo.GetComponent<Image>();
			face.color = Color.magenta;
			var toggle = faceGo.GetComponent<Toggle>();
			toggle.targetGraphic = face;
			var labelGo = new GameObject("Label", typeof(RectTransform));
			labelGo.transform.SetParent(faceGo.transform, false);
			var tmp = labelGo.AddComponent<TextMeshProUGUI>();
			tmp.text = "COLOR";
			tmp.font = TMP_Settings.defaultFontAsset;
			tmp.color = Color.cyan;

			typeof(WorkflowRibbon_Colors_UI)
				.GetField("_toggle", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(mode, toggle);

			var themeMode = typeof(WorkflowRibbon_UI).GetMethod(
				"ThemeModeToggle", BindingFlags.Static | BindingFlags.NonPublic);
			themeMode.Invoke(null, new object[] { mode, false, StudioLineIcon.Brush, SpzUiThemeOps.Active, false });

			Assert.That(tmp.color, Is.EqualTo(SpzUiThemeOps.Active.fieldBg).Within(0.02f),
				"COLOR / PROJ MASK / ENTIRE labels must use dark ink on light Nomad control faces");
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}
}

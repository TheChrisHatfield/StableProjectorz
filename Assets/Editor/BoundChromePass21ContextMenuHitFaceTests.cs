using System.IO;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pass 21: context-menu / addon chrome must Ensure hit faces (SAVE/LOAD/DELETE under Nomad).
/// </summary>
public sealed class BoundChromePass21ContextMenuHitFaceTests {

	[Test]
	public void ApplyContextMenuChrome_SourceDropsNullTargetGraphicGate() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath, "_gm/Features/AddonSystem/SpzUiThemeOps.cs"));
		string src = File.ReadAllText(path);
		int fn = src.IndexOf("public static void ApplyContextMenuChrome", System.StringComparison.Ordinal);
		Assert.That(fn, Is.GreaterThan(0));
		string body = src.Substring(fn, System.Math.Min(2200, src.Length - fn));
		Assert.That(body, Does.Contain("ApplyBoundChromeSelectable(button"));
		Assert.That(body, Does.Not.Contain("button.targetGraphic == null)"));
		Assert.That(body, Does.Contain("ThemeCheckboxToggle(toggle"));
	}

	[Test]
	public void ApplyToAddonUiRoot_SourceThemesNullFaceButtons() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath, "_gm/Features/AddonSystem/SpzUiThemeOps.cs"));
		string src = File.ReadAllText(path);
		int fn = src.IndexOf("public static void ApplyToAddonUiRoot", System.StringComparison.Ordinal);
		Assert.That(fn, Is.GreaterThan(0));
		string body = src.Substring(fn, System.Math.Min(2800, src.Length - fn));
		Assert.That(body, Does.Not.Contain("button == null || button.targetGraphic == null)"));
		Assert.That(body, Does.Contain("button.targetGraphic != null && button.targetGraphic.color.a < 0.08f"));
		Assert.That(body, Does.Not.Contain("toggle == null || toggle.targetGraphic == null)"));
	}

	[Test]
	public void ApplyContextMenuChrome_EnsuresFaceWhenNullThenClearsLabelRaycast() {
		SpzUiThemeOps.ResetTheme();
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"pass21-ctx",
			new Newtonsoft.Json.Linq.JObject {
				["control_bg"] = "#292A2EFF",
				["accent"] = "#F2CA50FF",
				["text_primary"] = "#E3E2D6FF",
				["panel_bg"] = "#1E1F23F2",
			},
			"replace",
			out string error), Is.True, error);

		var root = new GameObject("CtxMenu", typeof(RectTransform), typeof(Image));
		try {
			var btnGo = new GameObject("Save", typeof(RectTransform), typeof(Button));
			btnGo.transform.SetParent(root.transform, false);
			var btn = btnGo.GetComponent<Button>();
			btn.targetGraphic = null;
			var labelGo = new GameObject("Label", typeof(RectTransform));
			labelGo.transform.SetParent(btnGo.transform, false);
			var tmp = labelGo.AddComponent<TMPro.TextMeshProUGUI>();
			tmp.text = "SAVE";
			tmp.raycastTarget = true;

			SpzUiThemeOps.ApplyContextMenuChrome(root);

			Assert.That(btn.targetGraphic, Is.Not.Null, "Ensure must wire a hit face");
			Assert.That(btn.targetGraphic.raycastTarget, Is.True);
			Assert.That(tmp.raycastTarget, Is.False, "label must not steal hits after BoundChrome");
		} finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}
}

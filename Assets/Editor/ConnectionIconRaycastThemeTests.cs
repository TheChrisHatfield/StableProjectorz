using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Connection status icon must not steal open-panel clicks under Nomad,
/// and must re-assert raycast on Restore SPZ (gen connection litmus).
/// </summary>
public sealed class ConnectionIconRaycastThemeTests {

	[Test]
	public void ApplyThemeTokens_SourceClearsConnectionIconRaycastUnderNomad() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/Connection/ConnectionPanel_UI.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("_connectionIcon.raycastTarget = false"));
		Assert.That(src, Does.Contain("_connectionIcon.raycastTarget = true"));
		int apply = src.IndexOf("void ApplyThemeTokens()", System.StringComparison.Ordinal);
		Assert.That(apply, Is.GreaterThan(0));
		int leave = src.IndexOf("if (!SpzUiThemeOps.ShouldRecolorBoundChrome)", apply, System.StringComparison.Ordinal);
		int clear = src.IndexOf("_connectionIcon.raycastTarget = false", apply, System.StringComparison.Ordinal);
		int restore = src.IndexOf("_connectionIcon.raycastTarget = true", apply, System.StringComparison.Ordinal);
		Assert.That(restore, Is.GreaterThan(leave));
		Assert.That(restore, Is.LessThan(clear));
	}

	[Test]
	public void ApplyThemeTokens_Leave_ReassertsConnectionIconRaycastAfterNomad() {
		SpzUiThemeOps.ResetTheme();
		var root = new GameObject("ConnPanelRaycast", typeof(RectTransform));
		root.SetActive(false);
		try {
			var openGo = new GameObject("connection (button)", typeof(RectTransform));
			openGo.transform.SetParent(root.transform, false);
			var openImg = openGo.AddComponent<Image>();
			var openBtn = openGo.AddComponent<Button>();
			openBtn.targetGraphic = openImg;

			var iconGo = new GameObject("icon", typeof(RectTransform));
			iconGo.transform.SetParent(openGo.transform, false);
			var icon = iconGo.AddComponent<Image>();
			icon.raycastTarget = true;

			var ui = root.AddComponent<ConnectionPanel_UI>();
			typeof(ConnectionPanel_UI).GetField(
				"_openPanel_button", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(ui, openBtn);
			typeof(ConnectionPanel_UI).GetField(
				"_connectionIcon", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(ui, icon);

			var apply = typeof(ConnectionPanel_UI).GetMethod(
				"ApplyThemeTokens", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(apply, Is.Not.Null);

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["text_primary"] = "#E3E2E7FF",
				},
				"replace",
				out string error), Is.True, error);
			apply.Invoke(ui, null);
			Assert.That(icon.raycastTarget, Is.False, "Nomad: status icon must not steal SD SERV open hits");

			SpzUiThemeOps.ResetTheme();
			apply.Invoke(ui, null);
			Assert.That(icon.raycastTarget, Is.True, "Restore SPZ: re-assert connection icon raycast");
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}
}

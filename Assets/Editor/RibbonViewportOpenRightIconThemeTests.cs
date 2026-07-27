using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Nomad fullscreen dock: OPEN/HIDE RIGHT stays text-only (line icon must not reappear).</summary>
public sealed class RibbonViewportOpenRightIconThemeTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void ApplyDockFaceChromeHidesOpenRightIconUnderNomad() {
		var faceGo = new GameObject("OpenRightDock", typeof(RectTransform), typeof(Image));
		faceGo.SetActive(false);
		try {
			var labelGo = new GameObject("Label", typeof(RectTransform));
			labelGo.transform.SetParent(faceGo.transform, false);
			var label = labelGo.AddComponent<TextMeshProUGUI>();
			label.text = "OPEN\nRIGHT";

			Image iconImg = null;
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject { ["control_bg"] = "#292A2EFF", ["text_primary"] = "#E3E2E7FF" },
				"replace",
				out string error), Is.True, error);

			var apply = typeof(RibbonViewportFullViewOnScreen_Toggle_UI).GetMethod(
				"ApplyDockFaceChrome",
				BindingFlags.Static | BindingFlags.NonPublic);
			Assert.That(apply, Is.Not.Null);

			var faceRt = faceGo.GetComponent<RectTransform>();
			object[] args = {
				faceRt,
				iconImg,
				StudioLineIcon.ChevronRight,
				true,
				SpzUiThemeOps.Active,
				false,
			};
			apply.Invoke(null, args);
			iconImg = args[1] as Image;
			Assert.That(iconImg, Is.Not.Null);
			Assert.That(iconImg.gameObject.activeSelf, Is.False);
			Assert.That(label.text, Does.Contain("OPEN").IgnoreCase);
			Assert.That(label.text, Does.Not.Contain("FULL").IgnoreCase);
		}
		finally {
			Object.DestroyImmediate(faceGo);
			SpzUiThemeOps.ResetTheme();
		}
	}
}

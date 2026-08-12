using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Leave SPZ must restore authored Selectable.targetGraphic after BoundChromeHitFace teardown
/// (null is valid — prefab tabs/tools hit via TMP before Nomad Ensure).
/// </summary>
public sealed class BoundChromeHitFaceLeaveTargetGraphicTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void RestoreBoundChromeUnder_RestoresNullTargetGraphic_AfterSyntheticHitFace() {
		var go = new GameObject("Tool", typeof(RectTransform), typeof(Button));
		go.SetActive(false);
		try {
			var btn = go.GetComponent<Button>();
			btn.targetGraphic = null;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
				},
				"replace",
				out string error), Is.True, error);

			var face = SpzUiThemeOps.EnsureSelectableHitFace(btn);
			Assert.That(face, Is.Not.Null);
			Assert.That(btn.targetGraphic, Is.SameAs(face));
			Assert.That(face.GetComponent<SpzUiThemeSyntheticHitFace>(), Is.Not.Null);

			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.RestoreBoundChromeUnder(go.transform);

			Assert.That(btn.targetGraphic, Is.Null,
				"authored null targetGraphic must be restored after synthetic HitFace destroy");
			Assert.That(go.GetComponentInChildren<SpzUiThemeSyntheticHitFace>(true), Is.Null);
		}
		finally {
			Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void SpzUiThemeOps_ExposesAuthoredTargetGraphicSnapshotApi_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "SpzUiThemeOps.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("SnapshotAuthoredTargetGraphic"));
		Assert.That(src, Does.Contain("RestoreAuthoredTargetGraphic"));
		Assert.That(src, Does.Contain("AuthoredTargetGraphics"));
	}
}

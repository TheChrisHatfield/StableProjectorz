using System.IO;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pass 18: DimensionMode SD/3D/UV must EnsureSelectableHitFace before ClearNonFace
/// (gen path — mode switch dies when labels lose raycasts and face is null).
/// </summary>
public sealed class BoundChromePass18DimensionModeHitFaceTests {

	[Test]
	public void DimensionMode_SourceEnsuresHitFaceBeforeClearNonFace() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Layouts/Viewport (MainView)/DimensionMode_MGR.cs"));
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("EnsureDimChoiceHitFace(_3d_choice_button)"));
		Assert.That(src, Does.Contain("EnsureDimChoiceHitFace(_sd_choice_button)"));
		Assert.That(src, Does.Contain("EnsureSelectableHitFace(btn)"));
		int ensure = src.IndexOf("EnsureDimChoiceHitFace(_3d_choice_button)", System.StringComparison.Ordinal);
		int clear = src.IndexOf("ClearNonFaceRaycastsForTheme(_3d_choice_button)", System.StringComparison.Ordinal);
		Assert.That(ensure, Is.GreaterThan(0));
		Assert.That(clear, Is.GreaterThan(ensure),
			"ClearNonFace must run after Ensure (null face no-ops ClearNonFace)");
	}

	[Test]
	public void DimensionMode_SourceKeepsMainChoiceHoverRaycast() {
		string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
			Application.dataPath,
			"_gm/Layouts/Viewport (MainView)/DimensionMode_MGR.cs"));
		string src = System.IO.File.ReadAllText(path);
		Assert.That(src, Does.Contain("_mainChoice_text.raycastTarget = false"));
		Assert.That(src, Does.Contain("_mainChoiceHoverSurf"));
		Assert.That(src, Does.Contain("hoverImg.raycastTarget = true"),
			"Main choice open must keep hover sensor Graphic hittable after TMP clear");
	}

	[Test]
	public void EnsureThenClearNonFace_KeepsHittableFaceWhenLabelsCleared() {
		var go = new GameObject("DimChoice", typeof(RectTransform), typeof(Button));
		try {
			var btn = go.GetComponent<Button>();
			btn.targetGraphic = null;
			var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
			labelGo.transform.SetParent(go.transform, false);
			var tmp = labelGo.GetComponent<TMPro.TextMeshProUGUI>();
			tmp.raycastTarget = true;

			SpzUiThemeOps.EnsureSelectableHitFace(btn);
			Assert.That(btn.targetGraphic, Is.Not.Null);
			tmp.raycastTarget = false;
			SpzUiThemeOps.ClearNonFaceRaycastsForTheme(btn);
			Assert.That(btn.targetGraphic.raycastTarget, Is.True);
			Assert.That(tmp.raycastTarget, Is.False);
		} finally {
			Object.DestroyImmediate(go);
		}
	}
}

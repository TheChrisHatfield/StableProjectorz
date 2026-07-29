using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Restore SPZ litmus: shape/align unwind (preserveAspect + spacing), not tint alone.
/// </summary>
public sealed class RestoreSpzShapeAlignTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void RestoreBoundChrome_UnwindsPreserveAspectAfterSolidSquare() {
		var go = new GameObject("ShapeFace", typeof(RectTransform), typeof(Image), typeof(Button));
		go.SetActive(false);
		try {
			var face = go.GetComponent<Image>();
			face.type = Image.Type.Sliced;
			face.preserveAspect = true;
			var authored = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
			face.sprite = authored;
			var btn = go.GetComponent<Button>();
			btn.targetGraphic = face;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["spacing_scale"] = 0.9f,
				},
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ApplyBoundChromeSelectable(btn, SpzUiThemeOps.Active.controlBg, SpzUiThemeOps.Active.accent);
			Assert.That(face.preserveAspect, Is.False, "Nomad solid-square forces preserveAspect off");

			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.RestoreBoundChromeUnder(go.transform);

			Assert.That(ReferenceEquals(face.sprite, authored), Is.True);
			Assert.That(face.type, Is.EqualTo(Image.Type.Sliced));
			Assert.That(face.preserveAspect, Is.True,
				"Restore SPZ must unwind preserveAspect or button images look shifted");
		}
		finally {
			Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void RestoreBoundChrome_UnwindsScaledLayoutSpacing() {
		var root = new GameObject("LayoutRoot", typeof(RectTransform));
		root.SetActive(false);
		try {
			var hlg = root.AddComponent<HorizontalLayoutGroup>();
			hlg.spacing = 10f;
			hlg.padding = new RectOffset(4, 4, 4, 4);

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject { ["spacing_scale"] = 0.8f },
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ApplyScaledLayoutGroup(hlg);
			Assert.That(hlg.spacing, Is.EqualTo(8f).Within(0.01f));

			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.RestoreBoundChromeUnder(root.transform);

			Assert.That(hlg.spacing, Is.EqualTo(10f).Within(0.01f),
				"Restore SPZ must rewind spacing_scale via RestoreBoundChromeUnder");
			Assert.That(hlg.padding.left, Is.EqualTo(4));
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}
}

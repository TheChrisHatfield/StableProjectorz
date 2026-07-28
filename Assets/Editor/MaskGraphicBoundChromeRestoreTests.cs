using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mask2D Images (showMaskGraphic / soft 9-slice PPU) must not get SolidRect flatten —
/// that left white capsule artifacts on hardness + workflow mode strip after Restore SPZ.
/// </summary>
public sealed class MaskGraphicBoundChromeRestoreTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void ApplyRoundedControlSprite_SkipsUiMaskGraphic() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["control_bg"] = "#292A2EFF", ["corner_radius"] = 8 },
			"replace",
			out string error), Is.True, error);

		var go = new GameObject("MaskFace", typeof(RectTransform), typeof(Image), typeof(Mask));
		try {
			var img = go.GetComponent<Image>();
			var authored = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
			img.sprite = authored;
			img.type = Image.Type.Sliced;
			img.pixelsPerUnitMultiplier = 11f;
			img.color = Color.white;
			go.GetComponent<Mask>().showMaskGraphic = true;

			Assert.That(SpzUiThemeOps.IsUiMaskGraphic(img), Is.True);
			SpzUiThemeOps.ApplyRoundedControlSprite(img, markEligible: true);
			SpzUiThemeOps.ApplyBoundChromeGraphic(img, SpzUiThemeOps.Active.controlBg);

			Assert.That(ReferenceEquals(img.sprite, authored), Is.True, "Mask sprite must stay authored");
			Assert.That(img.type, Is.EqualTo(Image.Type.Sliced));
			Assert.That(img.pixelsPerUnitMultiplier, Is.EqualTo(11f).Within(0.01f));
			Assert.That(img.color, Is.EqualTo(Color.white), "Mask color must not be retinted");
			Assert.That(go.GetComponent<SpzUiThemeRoundedControl>(), Is.Null);
		}
		finally {
			Object.DestroyImmediate(go);
		}
	}

	[Test]
	public void RestoreRoundedControlSprites_RestoresPixelsPerUnitMultiplier() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["control_bg"] = "#292A2EFF" },
			"replace",
			out string error), Is.True, error);

		var go = new GameObject("ChromeFace", typeof(RectTransform), typeof(Image));
		try {
			var img = go.GetComponent<Image>();
			var authored = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
			img.sprite = authored;
			img.type = Image.Type.Sliced;
			img.pixelsPerUnitMultiplier = 5f;

			SpzUiThemeOps.ApplyRoundedControlSprite(img, markEligible: true);
			Assert.That(UiRuntimeSprites.IsSolidRect(img.sprite), Is.True);
			Assert.That(img.pixelsPerUnitMultiplier, Is.EqualTo(1f).Within(0.01f));

			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.RestoreBoundChromeUnder(go.transform);

			Assert.That(ReferenceEquals(img.sprite, authored), Is.True);
			Assert.That(img.type, Is.EqualTo(Image.Type.Sliced));
			Assert.That(img.pixelsPerUnitMultiplier, Is.EqualTo(5f).Within(0.01f));
		}
		finally {
			Object.DestroyImmediate(go);
		}
	}
}

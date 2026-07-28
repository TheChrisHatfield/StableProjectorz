using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Litmus: SAVE 2K / solid-square chrome = opaque Simple rect, not 9-slice bevel.
/// </summary>
public sealed class SolidSquareChromeLitmusTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void ApplySolidSquareChromeUsesOpaqueSimpleRectNotSlicedBevel() {
		var root = new GameObject("Save2KLitmus");
		root.SetActive(false);
		try {
			var go = new GameObject("SAVE", typeof(RectTransform), typeof(Image), typeof(Button));
			go.transform.SetParent(root.transform, false);
			var face = go.GetComponent<Image>();
			face.type = Image.Type.Sliced;
			var authored = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
			face.sprite = authored;
			var btn = go.GetComponent<Button>();
			btn.targetGraphic = face;

			var tickGo = new GameObject("triangle_corner", typeof(RectTransform), typeof(Image));
			tickGo.transform.SetParent(go.transform, false);
			var tick = tickGo.GetComponent<Image>();
			tick.enabled = true;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject { ["success"] = "#7BC96FFF", ["accent"] = "#F2CA50FF" },
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ApplySolidSquareChrome(btn, SpzUiThemeOps.Active.success, SpzUiThemeOps.Active.accent);

			Assert.That(UiRuntimeSprites.IsSolidRect(face.sprite), Is.True);
			Assert.That(face.type, Is.EqualTo(Image.Type.Simple));
			Assert.That(face.color, Is.EqualTo(SpzUiThemeOps.Active.success));
			Assert.That(tick.enabled, Is.False, "corner chevron overlays must hide");

			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.RestoreBoundChromeUnder(go.transform);
			Assert.That(ReferenceEquals(face.sprite, authored), Is.True);
			Assert.That(face.type, Is.EqualTo(Image.Type.Sliced));
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}
}

using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Litmus expanded: Nomad BoundChrome selectables + rounded API → opaque Simple solid squares.
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

	[Test]
	public void ApplyBoundChromeSelectableDelegatesToSolidSquareLitmus() {
		var root = new GameObject("BoundChromeSolidLitmus");
		root.SetActive(false);
		try {
			var go = new GameObject("Btn", typeof(RectTransform), typeof(Image), typeof(Button));
			go.transform.SetParent(root.transform, false);
			var face = go.GetComponent<Image>();
			face.type = Image.Type.Sliced;
			var btn = go.GetComponent<Button>();
			btn.targetGraphic = face;
			var tri = new GameObject("triangle", typeof(RectTransform), typeof(Image));
			tri.transform.SetParent(go.transform, false);
			tri.GetComponent<Image>().enabled = true;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["corner_radius"] = 8,
				},
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ApplyBoundChromeSelectable(btn, SpzUiThemeOps.Active.controlBg, SpzUiThemeOps.Active.accent);

			Assert.That(UiRuntimeSprites.IsSolidRect(face.sprite), Is.True,
				"even with corner_radius>0, BoundChrome must stay solid-square litmus");
			Assert.That(face.type, Is.EqualTo(Image.Type.Simple));
			Assert.That(tri.GetComponent<Image>().enabled, Is.False);
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void ApplyRoundedControlSpriteForcesSolidSquareIgnoringRadius() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["corner_radius"] = 8 },
			"replace",
			out string error), Is.True, error);

		var go = new GameObject("RoundedApiSolid");
		try {
			var img = go.AddComponent<Image>();
			img.type = Image.Type.Sliced;
			SpzUiThemeOps.ApplyRoundedControlSprite(img, markEligible: true);
			Assert.That(UiRuntimeSprites.IsSolidRect(img.sprite), Is.True);
			Assert.That(img.type, Is.EqualTo(Image.Type.Simple));
		}
		finally {
			Object.DestroyImmediate(go);
		}
	}
}

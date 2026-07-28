using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pass 8: BoundChrome Selectables must keep a hittable face after hide-Checkmark theming
/// (workflow / ControlNet / Multiview litmus — generation-adjacent dead clicks).
/// </summary>
public sealed class BoundChromePass8FunctionalTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void ApplySolidSquareChrome_ForcesFaceRaycast_EvenWhenAuthoredFalse() {
		var go = new GameObject("ModeCell", typeof(RectTransform), typeof(Image), typeof(Toggle));
		go.SetActive(false);
		try {
			var face = go.GetComponent<Image>();
			face.raycastTarget = false; // authored Background not the hit face
			var toggle = go.GetComponent<Toggle>();
			toggle.targetGraphic = face;

			var checkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
			checkGo.transform.SetParent(go.transform, false);
			var check = checkGo.GetComponent<Image>();
			check.raycastTarget = true;
			toggle.graphic = check;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
				},
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ApplyBoundChromeSelectable(toggle, SpzUiThemeOps.Active.controlBg, SpzUiThemeOps.Active.accent);
			SpzUiThemeOps.HideAuthoredGraphicForTheme(check);

			Assert.That(face.raycastTarget, Is.True, "Nomad litmus: ColorTint face must receive hits after check hide");
			Assert.That(check.enabled, Is.False);

			// Leave path (Restore SPZ): unwind raycast before clearing theme id.
			SpzUiThemeOps.RestoreBoundChromeUnder(go.transform);
			Assert.That(face.raycastTarget, Is.False, "Restore SPZ must unwind authored face raycast");
			SpzUiThemeOps.ResetTheme();
		}
		finally {
			Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ApplyToAddonUiRoot_SkipsNearTransparentHitPadButtons() {
		var root = new GameObject("AddonPanel_Test", typeof(RectTransform), typeof(Image));
		root.SetActive(false);
		try {
			var padGo = new GameObject("HitPad", typeof(RectTransform), typeof(Image), typeof(Button));
			padGo.transform.SetParent(root.transform, false);
			var padImg = padGo.GetComponent<Image>();
			padImg.color = new Color(1f, 1f, 1f, 0.01f);
			padImg.sprite = null;
			var padBtn = padGo.GetComponent<Button>();
			padBtn.targetGraphic = padImg;

			var realGo = new GameObject("Button_Apply", typeof(RectTransform), typeof(Image), typeof(Button));
			realGo.transform.SetParent(root.transform, false);
			var realImg = realGo.GetComponent<Image>();
			realImg.color = new Color(0.5f, 0.5f, 0.5f, 1f);
			var realBtn = realGo.GetComponent<Button>();
			realBtn.targetGraphic = realImg;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
				},
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ApplyToAddonUiRoot(root);

			Assert.That(UiRuntimeSprites.IsSolidRect(padImg.sprite), Is.False,
				"transparent hit pad must not become solid square");
			Assert.That(UiRuntimeSprites.IsSolidRect(realImg.sprite), Is.True);
			Assert.That(realImg.raycastTarget, Is.True);
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ThemeFlatToolToggle_FaceStaysHittable() {
		var go = new GameObject("SoftCell", typeof(RectTransform), typeof(Image), typeof(Toggle));
		go.SetActive(false);
		try {
			var face = go.GetComponent<Image>();
			face.raycastTarget = false;
			var toggle = go.GetComponent<Toggle>();
			toggle.targetGraphic = face;
			var checkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
			checkGo.transform.SetParent(go.transform, false);
			toggle.graphic = checkGo.GetComponent<Image>();

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["text_primary"] = "#E3E2E7FF",
				},
				"replace",
				out string error), Is.True, error);

			var t = SpzUiThemeOps.Active;
			SpzUiThemeOps.ThemeFlatToolToggle(toggle, t.controlBg, t.accent, t.textPrimary);
			Assert.That(face.raycastTarget, Is.True);
		}
		finally {
			Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}
}

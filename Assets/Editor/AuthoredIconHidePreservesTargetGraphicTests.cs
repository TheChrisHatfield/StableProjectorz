using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// BoundChrome must never disable a Button/Toggle targetGraphic when swapping to Monolith line icons.
/// Default litmus: clicks still work; Nomad only hides silhouette child Images.
/// </summary>
public sealed class AuthoredIconHidePreservesTargetGraphicTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
		UiRuntimeSprites.ClearLineIconCache();
	}

	[Test]
	public void ApplyControlLineIcon_DoesNotDisableTargetGraphicNamedWithIcon() {
		var root = new GameObject("IconButtonRoot", typeof(RectTransform), typeof(Image), typeof(Button));
		root.SetActive(false);
		try {
			var face = root.GetComponent<Image>();
			var btn = root.GetComponent<Button>();
			btn.targetGraphic = face;
			face.enabled = true;

			var childIcon = new GameObject("BrushIcon", typeof(RectTransform), typeof(Image));
			childIcon.transform.SetParent(root.transform, false);
			var childImg = childIcon.GetComponent<Image>();
			childImg.enabled = true;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["icon_tint"] = "#D0C5AFFF",
					["text_primary"] = "#E3E2E7FF",
				},
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ApplyControlLineIcon(root.transform, StudioLineIcon.Brush, 22f);

			Assert.That(face.enabled, Is.True, "targetGraphic named *Icon* must stay enabled for clicks");
			Assert.That(SpzUiThemeOps.IsSelectableTargetGraphic(face), Is.True);
			Assert.That(childImg.enabled, Is.False, "authored silhouette child Icon should hide under BoundChrome");
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ApplyControlLineIcon_HidesPreserveAspectGlyphWithoutIconName() {
		var root = new GameObject("ServFace", typeof(RectTransform), typeof(Image), typeof(Button));
		root.SetActive(false);
		try {
			var face = root.GetComponent<Image>();
			var btn = root.GetComponent<Button>();
			btn.targetGraphic = face;
			face.enabled = true;

			var folderGo = new GameObject("Image", typeof(RectTransform), typeof(Image));
			folderGo.transform.SetParent(root.transform, false);
			var folder = folderGo.GetComponent<Image>();
			folder.sprite = UiRuntimeSprites.GetLineIcon(StudioLineIcon.Folder);
			folder.preserveAspect = true;
			folder.enabled = true;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["icon_tint"] = "#D0C5AFFF",
				},
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ApplyControlLineIconLeading(root.transform, StudioLineIcon.Bullseye, 16f);

			Assert.That(face.enabled, Is.True);
			Assert.That(folder.enabled, Is.False, "SERV folder sprites without Icon in name must hide");
			var mono = SpzUiThemeOps.FindDirectChildIncludingInactive(root.transform, "MonolithLineIcon") as RectTransform;
			Assert.That(mono, Is.Not.Null);
			Assert.That(mono.anchorMin.x, Is.EqualTo(0f).Within(0.001f));
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}
}

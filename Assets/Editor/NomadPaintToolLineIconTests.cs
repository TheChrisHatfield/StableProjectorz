using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Nomad Flatten litmus: paint-strip glyphs are thin outline-only sprites (Brush/Smudge/Bucket/Trash)
/// and trash wires onto delete controls.
/// </summary>
public sealed class NomadPaintToolLineIconTests {

	[TearDown]
	public void TearDown() {
		UiRuntimeSprites.ClearLineIconCache();
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void NomadPaintToolStroke_MatchesFlattenLitmusWeight() {
		Assert.That(UiRuntimeSprites.NomadPaintToolStroke, Is.EqualTo(2.4f).Within(0.01f));
	}

	[Test]
	public void PaintToolLineIcons_AreDistinct64pxSprites() {
		UiRuntimeSprites.ClearLineIconCache();
		var brush = UiRuntimeSprites.GetLineIcon(StudioLineIcon.Brush);
		var smudge = UiRuntimeSprites.GetLineIcon(StudioLineIcon.Smudge);
		var bucket = UiRuntimeSprites.GetLineIcon(StudioLineIcon.Bucket);
		var trash = UiRuntimeSprites.GetLineIcon(StudioLineIcon.Trash);
		var flatten = UiRuntimeSprites.GetLineIcon(StudioLineIcon.Flatten);
		var eraser = UiRuntimeSprites.GetLineIcon(StudioLineIcon.Eraser);

		Assert.That(brush, Is.Not.Null);
		Assert.That(smudge, Is.Not.Null);
		Assert.That(bucket, Is.Not.Null);
		Assert.That(trash, Is.Not.Null);
		Assert.That(flatten, Is.Not.Null);
		Assert.That(eraser, Is.Not.Null);

		Assert.That(brush.rect.width, Is.EqualTo(64f));
		Assert.That(smudge.rect.width, Is.EqualTo(64f));
		Assert.That(bucket.rect.width, Is.EqualTo(64f));
		Assert.That(trash.rect.width, Is.EqualTo(64f));
		Assert.That(flatten.rect.width, Is.EqualTo(64f));

		Assert.That(brush, Is.Not.SameAs(smudge));
		Assert.That(brush, Is.Not.SameAs(bucket));
		Assert.That(brush, Is.Not.SameAs(trash));
		Assert.That(brush, Is.Not.SameAs(flatten));
		Assert.That(smudge, Is.Not.SameAs(bucket));
		Assert.That(trash, Is.Not.SameAs(bucket));
	}

	[Test]
	public void FlattenAndTrash_ParseInIconPack() {
		Assert.That(SpzUiThemeOps.TryParseStudioLineIcon("Flatten", out StudioLineIcon flatten, out string error), Is.True, error);
		Assert.That(flatten, Is.EqualTo(StudioLineIcon.Flatten));
		Assert.That(SpzUiThemeOps.TryParseStudioLineIcon("Trash", out StudioLineIcon trash, out error), Is.True, error);
		Assert.That(trash, Is.EqualTo(StudioLineIcon.Trash));
		string names = SpzUiThemeOps.ListLineIconNames().ToString();
		Assert.That(names, Does.Contain("Flatten"));
		Assert.That(names, Does.Contain("Brush"));
		Assert.That(names, Does.Contain("Smudge"));
		Assert.That(names, Does.Contain("Bucket"));
		Assert.That(names, Does.Contain("Trash"));
	}

	[Test]
	public void ApplyControlLineIcon_WiresTrashOntoDeleteRoot() {
		var root = new GameObject("DeleteTrashIcon", typeof(RectTransform), typeof(Image), typeof(Button));
		root.SetActive(false);
		try {
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["text_primary"] = "#E3E2E7FF",
					["icon_tint"] = "#D0C5AFFF",
					["danger"] = "#B33A3AFF",
					["corner_radius"] = 0,
				},
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ApplyControlLineIcon(root.transform, StudioLineIcon.Trash, 16f);
			Transform iconT = SpzUiThemeOps.FindDirectChildIncludingInactive(root.transform, "MonolithLineIcon");
			Assert.That(iconT, Is.Not.Null);
			var img = iconT.GetComponent<Image>();
			Assert.That(img, Is.Not.Null);
			Assert.That(img.sprite, Is.EqualTo(UiRuntimeSprites.GetLineIcon(StudioLineIcon.Trash)));
			Assert.That(iconT.gameObject.activeSelf, Is.True);
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ThemeDirectionTools_AppliesBrushSmudgeEraserLineIcons() {
		var root = new GameObject("DirPaintIcons");
		root.SetActive(false);
		try {
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["text_primary"] = "#E3E2E7FF",
					["icon_tint"] = "#D0C5AFFF",
					["corner_radius"] = 0,
				},
				"replace",
				out string error), Is.True, error);

			Toggle paint = MakeToolToggle(root.transform, "Paint");
			Toggle smudge = MakeToolToggle(root.transform, "Smudge");
			Toggle erase = MakeToolToggle(root.transform, "Erase");

			var themeToggle = typeof(BrushRibbon_UI).GetMethod(
				"ThemeToolToggle",
				BindingFlags.Static | BindingFlags.NonPublic);
			Assert.That(themeToggle, Is.Not.Null);
			themeToggle.Invoke(null, new object[] { paint, StudioLineIcon.Brush, SpzUiThemeOps.Active, 22f });
			themeToggle.Invoke(null, new object[] { smudge, StudioLineIcon.Smudge, SpzUiThemeOps.Active, 22f });
			themeToggle.Invoke(null, new object[] { erase, StudioLineIcon.Eraser, SpzUiThemeOps.Active, 22f });

			AssertGlyph(paint.transform, StudioLineIcon.Brush);
			AssertGlyph(smudge.transform, StudioLineIcon.Smudge);
			AssertGlyph(erase.transform, StudioLineIcon.Eraser);
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	static Toggle MakeToolToggle(Transform parent, string name) {
		var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Toggle));
		go.transform.SetParent(parent, false);
		var face = go.GetComponent<Image>();
		face.type = Image.Type.Sliced;
		var toggle = go.GetComponent<Toggle>();
		toggle.targetGraphic = face;
		return toggle;
	}

	static void AssertGlyph(Transform owner, StudioLineIcon expected) {
		Transform iconT = SpzUiThemeOps.FindDirectChildIncludingInactive(owner, "MonolithLineIcon");
		Assert.That(iconT, Is.Not.Null, owner.name);
		var img = iconT.GetComponent<Image>();
		Assert.That(img, Is.Not.Null);
		Assert.That(img.sprite, Is.EqualTo(UiRuntimeSprites.GetLineIcon(expected)));
	}
}

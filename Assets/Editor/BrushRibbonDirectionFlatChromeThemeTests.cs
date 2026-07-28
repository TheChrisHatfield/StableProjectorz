using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Paint / Smudge / Erase cells: Nomad uses flat Simple faces (not sliced bevel + corner chevrons)
/// and hides tick (+/−) plates. BrushRibbon_UI_MGR is childless — ResolveDirection must still find tools.
/// </summary>
public sealed class BrushRibbonDirectionFlatChromeThemeTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void ApplyPaintSmudgeEraseGaps_NomadLeavesVisibleBreakBetweenEqualCells() {
		var root = new GameObject("DirGaps", typeof(RectTransform), typeof(LayoutElement));
		root.SetActive(false);
		try {
			var le = root.GetComponent<LayoutElement>();
			le.minHeight = 140f;

			Toggle MakeToggle(string name) {
				var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Toggle));
				go.transform.SetParent(root.transform, false);
				var rt = go.GetComponent<RectTransform>();
				rt.anchorMin = Vector2.zero;
				rt.anchorMax = Vector2.one;
				rt.offsetMin = Vector2.zero;
				rt.offsetMax = Vector2.zero;
				return go.GetComponent<Toggle>();
			}

			var paint = MakeToggle("Paint");
			var smudge = MakeToggle("Smudge");
			var erase = MakeToggle("Erase");

			var dir = root.AddComponent<BrushRibbon_UI_Direction>();
			typeof(BrushRibbon_UI_Direction)
				.GetField("_brushAdd_Toggle", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(dir, paint);
			typeof(BrushRibbon_UI_Direction)
				.GetField("_brushErase_Toggle", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(dir, erase);
			typeof(BrushRibbon_UI_Direction)
				.GetField("_brushSmudge_Toggle", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(dir, smudge);

			BrushRibbon_UI_Direction.ApplyPaintSmudgeEraseGaps(dir, nomadGaps: false);
			BrushRibbon_UI_Direction.ApplyPaintSmudgeEraseGaps(dir, nomadGaps: true);

			var paintRt = paint.transform as RectTransform;
			var smudgeRt = smudge.transform as RectTransform;
			var eraseRt = erase.transform as RectTransform;

			float gapPaintSmudge = paintRt.anchorMin.y - smudgeRt.anchorMax.y;
			float gapSmudgeErase = smudgeRt.anchorMin.y - eraseRt.anchorMax.y;
			Assert.That(gapPaintSmudge, Is.EqualTo(0.08f).Within(0.0001f));
			Assert.That(gapSmudgeErase, Is.EqualTo(0.08f).Within(0.0001f));

			float paintH = paintRt.anchorMax.y - paintRt.anchorMin.y;
			float smudgeH = smudgeRt.anchorMax.y - smudgeRt.anchorMin.y;
			float eraseH = eraseRt.anchorMax.y - eraseRt.anchorMin.y;
			Assert.That(paintH, Is.EqualTo(smudgeH).Within(0.0001f));
			Assert.That(smudgeH, Is.EqualTo(eraseH).Within(0.0001f));
			Assert.That(le.minHeight, Is.EqualTo(280f));
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void FlattenToolFaceImage_DoesNotStretchSelectableRootAnchors() {
		var parent = new GameObject("DirHost", typeof(RectTransform));
		parent.SetActive(false);
		try {
			var cell = new GameObject("Paint", typeof(RectTransform), typeof(Image), typeof(Toggle));
			cell.transform.SetParent(parent.transform, false);
			var rt = cell.GetComponent<RectTransform>();
			rt.anchorMin = new Vector2(0f, 0.7f);
			rt.anchorMax = new Vector2(1f, 1f);
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;
			var face = cell.GetComponent<Image>();
			face.type = Image.Type.Sliced;
			cell.GetComponent<Toggle>().targetGraphic = face;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["icon_tint"] = "#D0C5AFFF",
				},
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ApplyRoundedControlSprite(face, markEligible: true);
			SpzUiThemeOps.FlattenToolFaceImage(face);

			Assert.That(UiRuntimeSprites.IsSolidRect(face.sprite), Is.True);
			Assert.That(rt.anchorMin.y, Is.EqualTo(0.7f).Within(0.0001f),
				"root-face Selectable must keep gap anchors");
			Assert.That(rt.anchorMax.y, Is.EqualTo(1f).Within(0.0001f));
		}
		finally {
			Object.DestroyImmediate(parent);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ThemeToolToggle_CreatesVisibleMonolithIconAndKeepsGaps() {
		var root = new GameObject("DirIcons", typeof(RectTransform), typeof(LayoutElement));
		root.SetActive(false);
		try {
			Toggle MakeToggle(string name) {
				var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Toggle));
				go.transform.SetParent(root.transform, false);
				var face = go.GetComponent<Image>();
				face.type = Image.Type.Sliced;
				var toggle = go.GetComponent<Toggle>();
				toggle.targetGraphic = face;
				var textGo = new GameObject("text", typeof(RectTransform));
				textGo.transform.SetParent(go.transform, false);
				textGo.AddComponent<TMPro.TextMeshProUGUI>().text = name;
				return toggle;
			}

			var paint = MakeToggle("Paint");
			var smudge = MakeToggle("Smudge");
			var erase = MakeToggle("Erase");
			var dir = root.AddComponent<BrushRibbon_UI_Direction>();
			typeof(BrushRibbon_UI_Direction)
				.GetField("_brushAdd_Toggle", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(dir, paint);
			typeof(BrushRibbon_UI_Direction)
				.GetField("_brushErase_Toggle", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(dir, erase);
			typeof(BrushRibbon_UI_Direction)
				.GetField("_brushSmudge_Toggle", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(dir, smudge);

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["text_primary"] = "#E3E2E7FF",
					["icon_tint"] = "#D0C5AFFF",
				},
				"replace",
				out string error), Is.True, error);

			BrushRibbon_UI_Direction.ApplyPaintSmudgeEraseGaps(dir, nomadGaps: true);

			var themeToggle = typeof(BrushRibbon_UI).GetMethod(
				"ThemeToolToggle",
				BindingFlags.Static | BindingFlags.NonPublic);
			Assert.That(themeToggle, Is.Not.Null);
			themeToggle.Invoke(null, new object[] { paint, StudioLineIcon.Brush, SpzUiThemeOps.Active, 24f });
			themeToggle.Invoke(null, new object[] { smudge, StudioLineIcon.Smudge, SpzUiThemeOps.Active, 24f });
			themeToggle.Invoke(null, new object[] { erase, StudioLineIcon.Eraser, SpzUiThemeOps.Active, 24f });
			BrushRibbon_UI_Direction.ApplyPaintSmudgeEraseGaps(dir, nomadGaps: true);

			var paintRt = paint.transform as RectTransform;
			var smudgeRt = smudge.transform as RectTransform;
			Assert.That(paintRt.anchorMin.y - smudgeRt.anchorMax.y, Is.EqualTo(0.08f).Within(0.0001f));

			void AssertIcon(Toggle toggle, StudioLineIcon glyph) {
				var iconT = SpzUiThemeOps.FindDirectChildIncludingInactive(toggle.transform, "MonolithLineIcon");
				Assert.That(iconT, Is.Not.Null, toggle.name);
				Assert.That(iconT.gameObject.activeSelf, Is.True);
				var img = iconT.GetComponent<Image>();
				Assert.That(img, Is.Not.Null);
				Assert.That(img.enabled, Is.True);
				Assert.That(img.sprite, Is.SameAs(UiRuntimeSprites.GetLineIcon(glyph)));
			}

			AssertIcon(paint, StudioLineIcon.Brush);
			AssertIcon(smudge, StudioLineIcon.Smudge);
			AssertIcon(erase, StudioLineIcon.Eraser);
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ThemeToolToggleReplacesSlicedBevelWithFlatSimpleAndHidesTick() {
		var root = new GameObject("BrushDirFlatChrome");
		root.SetActive(false);
		try {
			var faceGo = new GameObject("Face", typeof(RectTransform), typeof(Image), typeof(Toggle));
			faceGo.transform.SetParent(root.transform, false);
			var face = faceGo.GetComponent<Image>();
			face.type = Image.Type.Sliced;
			face.color = new Color(0.55f, 0.55f, 0.52f, 1f);

			var tickGo = new GameObject("tick", typeof(RectTransform), typeof(Image));
			tickGo.transform.SetParent(faceGo.transform, false);
			var tick = tickGo.GetComponent<Image>();
			tick.type = Image.Type.Sliced;
			tick.enabled = true;

			var toggle = faceGo.GetComponent<Toggle>();
			toggle.targetGraphic = face;
			toggle.graphic = tick;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["text_primary"] = "#E3E2E7FF",
					["corner_radius"] = 5,
				},
				"replace",
				out string error), Is.True, error);

			var themeToggle = typeof(BrushRibbon_UI).GetMethod(
				"ThemeToolToggle",
				BindingFlags.Static | BindingFlags.NonPublic);
			Assert.That(themeToggle, Is.Not.Null);
			themeToggle.Invoke(null, new object[] { toggle, StudioLineIcon.Brush, SpzUiThemeOps.Active, 22f });

			Assert.That(face.type, Is.EqualTo(Image.Type.Simple), "Nomad litmus: solid square tool face");
			Assert.That(face.color, Is.EqualTo(SpzUiThemeOps.Active.controlBg));
			Assert.That(tick.enabled, Is.False);
			Assert.That(UiRuntimeSprites.IsSolidRect(face.sprite), Is.True);
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ResolveDirectionFindsSiblingWhenMgrHasNoChildren() {
		var mgrGo = new GameObject("BrushRibbon_UI_MGR (script)");
		var dirGo = new GameObject("Brush Direction (Toggle Group + Mask2D)");
		mgrGo.SetActive(false);
		dirGo.SetActive(false);
		try {
			var dir = dirGo.AddComponent<BrushRibbon_UI_Direction>();
			Assert.That(mgrGo.transform.childCount, Is.EqualTo(0));
			Assert.That(mgrGo.GetComponentInChildren<BrushRibbon_UI_Direction>(true), Is.Null);

			var found = BrushRibbon_UI.ResolveDirection(mgrGo.transform);
			Assert.That(found, Is.SameAs(dir));
		}
		finally {
			Object.DestroyImmediate(mgrGo);
			Object.DestroyImmediate(dirGo);
		}
	}

	[Test]
	public void ResolveDirectionPrefersSdOverGen3d() {
		var mgrGo = new GameObject("BrushRibbon_UI_MGR (empty)");
		var gen3dGo = new GameObject("Gen3D Direction");
		var sdGo = new GameObject("SD Direction");
		mgrGo.SetActive(false);
		gen3dGo.SetActive(false);
		sdGo.SetActive(false);
		try {
			var gen3d = gen3dGo.AddComponent<Gen3D_BrushRibbon_UI_Direction>();
			var sd = sdGo.AddComponent<SD_BrushRibbon_UI_Direction>();
			var found = BrushRibbon_UI.ResolveDirection(mgrGo.transform);
			Assert.That(found, Is.SameAs(sd));
			Assert.That(found, Is.Not.SameAs(gen3d));
		}
		finally {
			Object.DestroyImmediate(mgrGo);
			Object.DestroyImmediate(gen3dGo);
			Object.DestroyImmediate(sdGo);
		}
	}
}

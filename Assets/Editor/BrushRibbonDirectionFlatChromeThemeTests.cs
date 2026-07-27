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

			Assert.That(face.type, Is.EqualTo(Image.Type.Simple));
			Assert.That(face.color, Is.EqualTo(SpzUiThemeOps.Active.controlBg));
			Assert.That(tick.enabled, Is.False);
			Assert.That(UiRuntimeSprites.IsCachedRoundedRect(face.sprite)
				|| face.sprite == null
				|| face.type == Image.Type.Simple, Is.True);
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

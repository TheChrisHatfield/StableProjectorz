using System.Collections.Generic;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Paint tab adjacent section splitters.
/// Micro: docs/delta/20_micro/paint-tab-section-splitters.md
/// </summary>
public sealed class PaintTabSectionSplitterTests {

	readonly List<GameObject> _owned = new List<GameObject>();
	string _prefsPrefix;

	[SetUp]
	public void SetUp() {
		_prefsPrefix = "ut.paintSplit." + System.Guid.NewGuid().ToString("N") + ".";
		PaintTab_KritaLayout_UI.PrefsKeyPrefixOverride = _prefsPrefix;
	}

	[TearDown]
	public void TearDown() {
		PaintTab_KritaLayout_UI.PrefsKeyPrefixOverride = null;
		DeletePref(PaintTab_KritaLayout_UI.PrefKeyLayers);
		DeletePref(PaintTab_KritaLayout_UI.PrefKeyBrush);
		DeletePref(PaintTab_KritaLayout_UI.PrefKeyTool);
		DeletePref(PaintTab_KritaLayout_UI.PrefKeyColor);
		for (int i = 0; i < _owned.Count; i++) {
			if (_owned[i] != null)
				Object.DestroyImmediate(_owned[i]);
		}
		_owned.Clear();
	}

	void DeletePref(string productionKey) {
		string key = _prefsPrefix + productionKey;
		if (PlayerPrefs.HasKey(key))
			PlayerPrefs.DeleteKey(key);
	}

	GameObject Own(GameObject go) {
		_owned.Add(go);
		return go;
	}

	[Test]
	public void ResolveSectionRoot_ScrollContent_ReturnsOuterSection() {
		var section = Own(new GameObject("3_BrushPresets", typeof(RectTransform), typeof(LayoutElement)));
		var content = new GameObject("Content", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
		content.transform.SetParent(section.transform, false);
		var scrollInner = new GameObject("ScrollContent", typeof(RectTransform));
		scrollInner.transform.SetParent(content.transform, false);
		var sr = content.GetComponent<ScrollRect>();
		sr.content = scrollInner.GetComponent<RectTransform>();
		sr.viewport = content.GetComponent<RectTransform>();

		var resolved = PaintTab_KritaLayout_UI.ResolveSectionRoot(scrollInner.GetComponent<RectTransform>());
		Assert.That(resolved, Is.SameAs(section.GetComponent<RectTransform>()));
	}

	[Test]
	public void ResolveSectionRoot_SectionWithContentChild_ReturnsSelf() {
		var section = Own(new GameObject("3_BrushPresets", typeof(RectTransform), typeof(LayoutElement)));
		var content = new GameObject("Content", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
		content.transform.SetParent(section.transform, false);

		var resolved = PaintTab_KritaLayout_UI.ResolveSectionRoot(section.GetComponent<RectTransform>());
		Assert.That(resolved, Is.SameAs(section.GetComponent<RectTransform>()));
	}

	[Test]
	public void ApplyDragDelta_TransfersHeight_RespectsMinHeight() {
		var aboveGo = Own(new GameObject("Above", typeof(RectTransform), typeof(LayoutElement)));
		var belowGo = Own(new GameObject("Below", typeof(RectTransform), typeof(LayoutElement)));
		var above = aboveGo.GetComponent<LayoutElement>();
		var below = belowGo.GetComponent<LayoutElement>();
		above.minHeight = 40f;
		below.minHeight = 40f;
		above.preferredHeight = 100f;
		below.preferredHeight = 100f;
		above.flexibleHeight = 0f;
		below.flexibleHeight = 0f;

		// Pointer up: above shrinks, below grows
		PaintTab_SectionSplitter_UI.ApplyDragDelta(above, below, deltaY: 20f);
		Assert.That(above.preferredHeight, Is.EqualTo(80f).Within(0.01f));
		Assert.That(below.preferredHeight, Is.EqualTo(120f).Within(0.01f));

		// Push above against min
		above.preferredHeight = 45f;
		below.preferredHeight = 100f;
		PaintTab_SectionSplitter_UI.ApplyDragDelta(above, below, deltaY: 20f);
		Assert.That(above.preferredHeight, Is.EqualTo(40f).Within(0.01f));
		Assert.That(below.preferredHeight, Is.EqualTo(105f).Within(0.01f));
	}

	[Test]
	public void EnsureSectionSplitters_CreatesThree_Idempotent() {
		var panel = Own(new GameObject("Panel_Paint", typeof(RectTransform), typeof(VerticalLayoutGroup)));
		var layout = panel.AddComponent<PaintTab_KritaLayout_UI>();
		layout.SetCreateSectionsIfMissing(true);

		Assert.That(panel.transform.Find(PaintTab_KritaLayout_UI.SplitLayersBrush), Is.Not.Null);
		Assert.That(panel.transform.Find(PaintTab_KritaLayout_UI.SplitBrushTool), Is.Not.Null);
		Assert.That(panel.transform.Find(PaintTab_KritaLayout_UI.SplitToolColor), Is.Not.Null);

		int before = panel.transform.childCount;
		layout.EnsureSectionSplitters();
		Assert.That(panel.transform.childCount, Is.EqualTo(before), "second Ensure must not duplicate splitters");
		Assert.That(CountNamed(panel.transform, PaintTab_KritaLayout_UI.SplitLayersBrush), Is.EqualTo(1));
		Assert.That(CountNamed(panel.transform, PaintTab_KritaLayout_UI.SplitBrushTool), Is.EqualTo(1));
		Assert.That(CountNamed(panel.transform, PaintTab_KritaLayout_UI.SplitToolColor), Is.EqualTo(1));
	}

	[Test]
	public void SectionWeights_SaveLoad_RoundTrip() {
		var panel = Own(new GameObject("Panel_Paint", typeof(RectTransform), typeof(VerticalLayoutGroup)));
		var layout = panel.AddComponent<PaintTab_KritaLayout_UI>();
		layout.SetCreateSectionsIfMissing(true);

		layout.SaveSectionWeights(2.2f, 0.8f, 0.4f, 0.6f);
		layout.ApplySavedSectionWeights();

		var layersLe = PaintTab_KritaLayout_UI.ResolveSectionRoot(layout.LayersSection).GetComponent<LayoutElement>();
		var brushLe = PaintTab_KritaLayout_UI.ResolveSectionRoot(layout.BrushPresetsSection).GetComponent<LayoutElement>();
		var toolLe = PaintTab_KritaLayout_UI.ResolveSectionRoot(layout.ToolOptionsSection).GetComponent<LayoutElement>();
		var colorLe = PaintTab_KritaLayout_UI.ResolveSectionRoot(layout.ColorPaletteSection).GetComponent<LayoutElement>();

		Assert.That(layersLe.flexibleHeight, Is.EqualTo(2.2f).Within(0.001f));
		Assert.That(brushLe.flexibleHeight, Is.EqualTo(0.8f).Within(0.001f));
		Assert.That(toolLe.flexibleHeight, Is.EqualTo(0.4f).Within(0.001f));
		Assert.That(colorLe.flexibleHeight, Is.EqualTo(0.6f).Within(0.001f));
		Assert.That(layersLe.preferredHeight, Is.EqualTo(-1f));
	}

	[Test]
	public void EnsureSectionSplitters_SkipsWeightApply_WhenDragLocked() {
		var panel = Own(new GameObject("Panel_Paint", typeof(RectTransform), typeof(VerticalLayoutGroup)));
		var layout = panel.AddComponent<PaintTab_KritaLayout_UI>();
		layout.SetCreateSectionsIfMissing(true);

		layout.SaveSectionWeights(2f, 1f, 0.5f, 0.5f);
		layout.ApplySavedSectionWeights();

		var brushLe = PaintTab_KritaLayout_UI.ResolveSectionRoot(layout.BrushPresetsSection).GetComponent<LayoutElement>();
		var toolLe = PaintTab_KritaLayout_UI.ResolveSectionRoot(layout.ToolOptionsSection).GetComponent<LayoutElement>();
		brushLe.preferredHeight = 180f;
		brushLe.flexibleHeight = 0f;
		toolLe.preferredHeight = 90f;
		toolLe.flexibleHeight = 0f;

		Assert.That(PaintTab_KritaLayout_UI.IsAnyFlexSectionDragLocked(
			PaintTab_KritaLayout_UI.ResolveSectionRoot(layout.LayersSection).GetComponent<LayoutElement>(),
			brushLe, toolLe,
			PaintTab_KritaLayout_UI.ResolveSectionRoot(layout.ColorPaletteSection).GetComponent<LayoutElement>()), Is.True);

		layout.EnsureSectionSplitters();

		Assert.That(brushLe.preferredHeight, Is.EqualTo(180f).Within(0.01f), "mid-drag preferred must survive Ensure");
		Assert.That(brushLe.flexibleHeight, Is.EqualTo(0f));
		Assert.That(toolLe.preferredHeight, Is.EqualTo(90f).Within(0.01f));
		Assert.That(toolLe.flexibleHeight, Is.EqualTo(0f));
	}

	[Test]
	public void ThemeOneSplitter_Source_DoesNotUseHairlineBorderToken() {
		string path = System.IO.Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_KritaLayout_UI.cs");
		Assert.That(System.IO.File.Exists(path), Is.True, path);
		string src = System.IO.File.ReadAllText(path);
		Assert.That(src, Does.Contain("t.controlBg"));
		Assert.That(src, Does.Contain("too faint for a 6px drag hit target"));
		Assert.That(src, Does.Not.Contain("ApplyBoundChromeGraphic(img, t.border)"));
	}

	[Test]
	public void CollectPaintUI_Source_CallsEnsureSectionSplitters() {
		string path = System.IO.Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_CollectPaintUI.cs");
		Assert.That(System.IO.File.Exists(path), Is.True, path);
		string src = System.IO.File.ReadAllText(path);
		Assert.That(src, Does.Contain("EnsureSectionSplitters()"));
	}

	static int CountNamed(Transform parent, string name) {
		int n = 0;
		for (int i = 0; i < parent.childCount; i++) {
			if (parent.GetChild(i).name == name) n++;
		}
		return n;
	}
}

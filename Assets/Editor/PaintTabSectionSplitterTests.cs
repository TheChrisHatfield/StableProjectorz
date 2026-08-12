using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
				UnityEngine.Object.DestroyImmediate(_owned[i]);
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

		// Simulate active splitter drag via BeginDrag path flag — preferred lock alone no longer blocks Ensure.
		Own(new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem)));
		var split = panel.transform.Find(PaintTab_KritaLayout_UI.SplitBrushTool).GetComponent<PaintTab_SectionSplitter_UI>();
		Assert.That(split, Is.Not.Null);
		split.OnBeginDrag(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) {
			button = UnityEngine.EventSystems.PointerEventData.InputButton.Left
		});
		Assert.That(split.IsDragging, Is.True);
		Assert.That(PaintTab_KritaLayout_UI.IsAnySplitterDragging(panel.transform), Is.True);

		layout.EnsureSectionSplitters();

		Assert.That(split.IsDragging, Is.True);
		Assert.That(brushLe.preferredHeight, Is.GreaterThan(0f), "mid-drag preferred must survive Ensure");
		Assert.That(brushLe.flexibleHeight, Is.EqualTo(0f));
		Assert.That(toolLe.flexibleHeight, Is.EqualTo(0f));
	}

	[Test]
	public void SectionSplitter_Source_SkipsRebuildWhenInactive() {
		string split = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_SectionSplitter_UI.cs"));
		string krita = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_KritaLayout_UI.cs"));
		Assert.That(split, Does.Contain("parent.gameObject.activeInHierarchy"));
		Assert.That(krita, Does.Contain("root.gameObject.activeInHierarchy"));
	}

	[Test]
	public void EnsureSectionSplitters_Source_GuardsSiblingParent() {
		string path = System.IO.Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_KritaLayout_UI.cs");
		string src = System.IO.File.ReadAllText(path);
		Assert.That(src, Does.Contain("SetSiblingUnder"));
		Assert.That(src, Does.Contain("child.parent != parent"));
		Assert.That(src, Does.Contain("_toolchestRow.parent == root"));
	}

	[Test]
	public void EnsureSectionSplitters_Source_EarlyReturnsWhileDragging() {
		string path = System.IO.Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_KritaLayout_UI.cs");
		string src = System.IO.File.ReadAllText(path);
		int ensure = src.IndexOf("public void EnsureSectionSplitters()", System.StringComparison.Ordinal);
		Assert.That(ensure, Is.GreaterThanOrEqualTo(0));
		string head = src.Substring(ensure, Math.Min(500, src.Length - ensure));
		Assert.That(head, Does.Contain("IsAnySplitterDragging(root)"));
		Assert.That(head, Does.Contain("return;"));
	}

	[Test]
	public void OnSplitterDragEnded_Source_AlwaysUnlocksOnFailurePaths() {
		string path = System.IO.Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_KritaLayout_UI.cs");
		string src = System.IO.File.ReadAllText(path);
		int ended = src.IndexOf("void OnSplitterDragEnded()", System.StringComparison.Ordinal);
		Assert.That(ended, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(ended, Math.Min(1200, src.Length - ended));
		Assert.That(body, Does.Contain("ApplySavedSectionWeights()"));
		Assert.That(body, Does.Contain("SanitizeFlexWeights(ref wL"));
		// Failure paths must not bare-return without unlocking.
		Assert.That(Regex.IsMatch(body,
			@"if \(layersLe == null[\s\S]*?ApplySavedSectionWeights\(\);\s*return;"), Is.True);
		Assert.That(Regex.IsMatch(body,
			@"sumH[\s\S]*?ApplySavedSectionWeights\(\);\s*return;"), Is.True);
	}

	[Test]
	public void SectionSplitter_Source_LocksAllSectionsOnDragBegan() {
		string krita = System.IO.Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_KritaLayout_UI.cs");
		string split = System.IO.Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_SectionSplitter_UI.cs");
		Assert.That(System.IO.File.ReadAllText(krita), Does.Contain("LockAllFlexSectionsFromRect"));
		Assert.That(System.IO.File.ReadAllText(split), Does.Contain("_onDragBegan?.Invoke()"));
	}

	[Test]
	public void SanitizeFlexWeights_RejectsCorruptValues() {
		float layers = float.NaN, brush = -1f, tool = 0f, color = float.PositiveInfinity;
		PaintTab_KritaLayout_UI.SanitizeFlexWeights(ref layers, ref brush, ref tool, ref color);
		Assert.That(layers, Is.EqualTo(PaintTab_KritaLayout_UI.DefaultFlexLayers));
		Assert.That(brush, Is.EqualTo(PaintTab_KritaLayout_UI.DefaultFlexBrush));
		Assert.That(tool, Is.EqualTo(PaintTab_KritaLayout_UI.DefaultFlexTool));
		Assert.That(color, Is.EqualTo(PaintTab_KritaLayout_UI.DefaultFlexColor));

		layers = 2f; brush = 1f; tool = 0.5f; color = 0.5f;
		PaintTab_KritaLayout_UI.SanitizeFlexWeights(ref layers, ref brush, ref tool, ref color);
		Assert.That(layers, Is.EqualTo(2f));
		Assert.That(brush, Is.EqualTo(1f));
	}

	[Test]
	public void SectionSplitter_HandleHeight_IsAtLeast8() {
		Assert.That(PaintTab_SectionSplitter_UI.HandleHeight, Is.GreaterThanOrEqualTo(8f));
	}

	[Test]
	public void SectionSplitter_OnDisable_FinishesActiveDrag() {
		string path = System.IO.Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_SectionSplitter_UI.cs");
		string src = System.IO.File.ReadAllText(path);
		Assert.That(src, Does.Contain("void OnDisable()"));
		Assert.That(src, Does.Contain("FinishDrag()"));
		Assert.That(src, Does.Contain("IsDragging"));
	}

	[Test]
	public void SectionSplitter_Source_ConvertsScreenDeltaByCanvasScale() {
		string path = System.IO.Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_SectionSplitter_UI.cs");
		Assert.That(System.IO.File.Exists(path), Is.True, path);
		string src = System.IO.File.ReadAllText(path);
		Assert.That(src, Does.Contain("ScreenDeltaToLayoutY"));
		Assert.That(src, Does.Contain("rootCanvas"));
		Assert.That(src, Does.Contain("scaleFactor"));
		Assert.That(src, Does.Contain("_dragActive"));
		Assert.That(src, Does.Contain("PointerEventData.InputButton.Left"));
	}

	[Test]
	public void SectionSplitter_Source_ForceRebuildsParentOnDrag() {
		string path = System.IO.Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_SectionSplitter_UI.cs");
		Assert.That(System.IO.File.Exists(path), Is.True, path);
		string src = System.IO.File.ReadAllText(path);
		Assert.That(src, Does.Contain("RebuildParentLayout"));
		Assert.That(src, Does.Contain("ForceRebuildLayoutImmediate(parent)"));
		Assert.That(src, Does.Contain("OnBeginDrag"));
		Assert.That(src, Does.Contain("OnDrag"));
		Assert.That(src, Does.Contain("OnEndDrag"));
	}

	[Test]
	public void EnsureSectionSplitters_AddsMissingLayoutElements() {
		var panel = Own(new GameObject("Panel_Paint", typeof(RectTransform), typeof(VerticalLayoutGroup)));
		var layout = panel.AddComponent<PaintTab_KritaLayout_UI>();
		layout.SetCreateSectionsIfMissing(true);

		var brushRoot = PaintTab_KritaLayout_UI.ResolveSectionRoot(layout.BrushPresetsSection);
		var old = brushRoot.GetComponent<LayoutElement>();
		UnityEngine.Object.DestroyImmediate(old);
		Assert.That(brushRoot.GetComponent<LayoutElement>(), Is.Null);

		layout.EnsureSectionSplitters();

		var added = brushRoot.GetComponent<LayoutElement>();
		Assert.That(added, Is.Not.Null);
		Assert.That(added.minHeight, Is.GreaterThanOrEqualTo(1f));
		Assert.That(panel.transform.Find(PaintTab_KritaLayout_UI.SplitBrushTool), Is.Not.Null);
	}

	[Test]
	public void ThemeOneSplitter_Source_DoesNotUseHairlineBorderToken() {
		string path = System.IO.Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_KritaLayout_UI.cs");
		Assert.That(System.IO.File.Exists(path), Is.True, path);
		string src = System.IO.File.ReadAllText(path);
		Assert.That(src, Does.Contain("t.controlBg"));
		Assert.That(src, Does.Contain("too faint for an 8px drag hit target"));
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

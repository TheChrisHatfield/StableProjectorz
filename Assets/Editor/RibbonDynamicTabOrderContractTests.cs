using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dynamic tab movement: CommandRibbon tabs (ControlNet, Art, Paint, add-ons) are draggable only after the
/// Settings unlock, and the user's order is stored in settings so it survives a restart.
/// </summary>
public sealed class RibbonDynamicTabOrderContractTests {

	bool _hadUnlockKey;
	int _prevUnlock;
	bool _hadOrderKey;
	string _prevOrder;

	[SetUp]
	public void SetUp() {
		_hadUnlockKey = PlayerPrefs.HasKey(RibbonTabOrder_Prefs.PREF_DYNAMIC_TAB_MOVEMENT);
		_prevUnlock = PlayerPrefs.GetInt(RibbonTabOrder_Prefs.PREF_DYNAMIC_TAB_MOVEMENT, 0);
		_hadOrderKey = PlayerPrefs.HasKey(RibbonTabOrder_Prefs.PREF_TAB_ORDER);
		_prevOrder = PlayerPrefs.GetString(RibbonTabOrder_Prefs.PREF_TAB_ORDER, "");
		PlayerPrefs.DeleteKey(RibbonTabOrder_Prefs.PREF_DYNAMIC_TAB_MOVEMENT);
		PlayerPrefs.DeleteKey(RibbonTabOrder_Prefs.PREF_TAB_ORDER);
	}

	[TearDown]
	public void TearDown() {
		if (_hadUnlockKey) PlayerPrefs.SetInt(RibbonTabOrder_Prefs.PREF_DYNAMIC_TAB_MOVEMENT, _prevUnlock);
		else PlayerPrefs.DeleteKey(RibbonTabOrder_Prefs.PREF_DYNAMIC_TAB_MOVEMENT);
		if (_hadOrderKey) PlayerPrefs.SetString(RibbonTabOrder_Prefs.PREF_TAB_ORDER, _prevOrder);
		else PlayerPrefs.DeleteKey(RibbonTabOrder_Prefs.PREF_TAB_ORDER);
		PlayerPrefs.Save();
	}

	static GameObject BuildRibbonStrip(out CommandRibbon_UI ribbon, out Transform strip, params string[] tabTitles) {
		var root = new GameObject("RibbonDynamicOrderRoot");
		root.SetActive(false);
		ribbon = root.AddComponent<CommandRibbon_UI>();
		var groupGo = new GameObject("TabsGroup", typeof(RectTransform));
		groupGo.transform.SetParent(root.transform, false);
		var group = groupGo.AddComponent<TabsGroup_UI>();
		var stripGo = new GameObject("Strip", typeof(RectTransform), typeof(HorizontalLayoutGroup));
		stripGo.transform.SetParent(groupGo.transform, false);
		strip = stripGo.transform;
		foreach (var title in tabTitles)
			AddTabCell(group, strip, title);
		typeof(CommandRibbon_UI)
			.GetField("_tabGroup", BindingFlags.Instance | BindingFlags.NonPublic)
			.SetValue(ribbon, group);
		return root;
	}

	static TabsGroupElem_UI AddTabCell(TabsGroup_UI group, Transform strip, string title) {
		var cell = new GameObject("Tab: " + title,
			typeof(RectTransform), typeof(Button), typeof(LayoutElement), typeof(TabsGroupElem_UI));
		cell.transform.SetParent(strip, false);
		var elem = cell.GetComponent<TabsGroupElem_UI>();
		elem.InitForRuntime(title, cell.GetComponent<Button>());
		if (group != null) group.AddTab(elem);
		return elem;
	}

	static List<string> TabKeys(Transform strip) {
		var keys = new List<string>();
		foreach (var cell in CommandRibbon_UI.CollectStripTabCells(strip))
			keys.Add(CommandRibbon_UI.StripTabOrderKey(cell));
		return keys;
	}

	[Test]
	public void DynamicTabMovement_IsLockedByDefault() {
		Assert.That(RibbonTabOrder_Prefs.IsDynamicTabMovementEnabled(), Is.False,
			"tabs must not be draggable until the user unlocks dynamic tab movement in Settings");
		Assert.That(RibbonTabOrder_Prefs.HasSavedOrder(), Is.False);
	}

	[Test]
	public void RefreshTabReorderHandles_AddsHandlesOnlyWhileUnlocked() {
		var root = BuildRibbonStrip(out var ribbon, out var strip, "art list", "mesh", "controlnet", "paint");
		try {
			ribbon.RefreshTabReorderHandles();
			foreach (var cell in CommandRibbon_UI.CollectStripTabCells(strip)) {
				Assert.That(cell.GetComponent<RibbonTabDragReorder_UI>(), Is.Null,
					"locked default must leave strip pointer handling untouched");
				Assert.That(RibbonTabDragReorder_UI.FindGrip(cell), Is.Null);
			}

			RibbonTabOrder_Prefs.SetDynamicTabMovementEnabled(true);
			ribbon.RefreshTabReorderHandles();
			foreach (var cell in CommandRibbon_UI.CollectStripTabCells(strip)) {
				Assert.That(cell.GetComponent<RibbonTabDragReorder_UI>(), Is.Not.Null,
					"every tab (incl. ControlNet) must be draggable when unlocked");
				Assert.That(RibbonTabDragReorder_UI.FindGrip(cell), Is.Null,
					"unlocked tabs stay text-only — no gold grip strip on the ribbon");
			}

			RibbonTabOrder_Prefs.SetDynamicTabMovementEnabled(false);
			ribbon.RefreshTabReorderHandles();
			foreach (var cell in CommandRibbon_UI.CollectStripTabCells(strip)) {
				Assert.That(cell.GetComponent<RibbonTabDragReorder_UI>(), Is.Null);
				Assert.That(RibbonTabDragReorder_UI.FindGrip(cell), Is.Null);
			}
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void MoveStripTabToSlot_MovesControlNetTabToFront() {
		var root = BuildRibbonStrip(out var ribbon, out var strip, "art list", "mesh", "controlnet", "paint");
		try {
			Transform ctrlNet = CommandRibbon_UI.CollectStripTabCells(strip)[2];
			Assert.That(CommandRibbon_UI.MoveStripTabToSlot(strip, ctrlNet, 0), Is.True);
			Assert.That(TabKeys(strip), Is.EqualTo(new List<string> { "controlnet", "art list", "mesh", "paint" }));

			Assert.That(CommandRibbon_UI.MoveStripTabToSlot(strip, ctrlNet, 0), Is.False,
				"already in that slot — no redundant hierarchy churn");
			Assert.That(CommandRibbon_UI.MoveStripTabToSlot(strip, ctrlNet, 3), Is.True);
			Assert.That(TabKeys(strip), Is.EqualTo(new List<string> { "art list", "mesh", "paint", "controlnet" }));
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void SavedOrder_SurvivesStripRebuild_LikeRestart() {
		var root = BuildRibbonStrip(out var ribbon, out var strip, "art list", "mesh", "controlnet", "paint");
		try {
			Transform ctrlNet = CommandRibbon_UI.CollectStripTabCells(strip)[2];
			CommandRibbon_UI.MoveStripTabToSlot(strip, ctrlNet, 0);
			Assert.That(ribbon.PersistCurrentTabOrder(), Is.True);
		}
		finally {
			Object.DestroyImmediate(root);
		}

		Assert.That(RibbonTabOrder_Prefs.LoadOrder(),
			Is.EqualTo(new List<string> { "controlnet", "art list", "mesh", "paint" }));

		// Fresh strip in authored order = next launch.
		var root2 = BuildRibbonStrip(out var ribbon2, out var strip2, "art list", "mesh", "controlnet", "paint");
		try {
			Assert.That(ribbon2.ApplySavedTabOrder(), Is.True);
			Assert.That(TabKeys(strip2), Is.EqualTo(new List<string> { "controlnet", "art list", "mesh", "paint" }));
		}
		finally {
			Object.DestroyImmediate(root2);
		}
	}

	[Test]
	public void ApplySavedTabOrder_KeepsTabsMissingFromTheSaveAtTheEnd() {
		RibbonTabOrder_Prefs.SaveOrder(new[] { "paint", "art list" });
		var root = BuildRibbonStrip(out var ribbon, out var strip, "art list", "mesh", "paint");
		try {
			// Add-on tab enabled after the order was saved.
			var group = (TabsGroup_UI)typeof(CommandRibbon_UI)
				.GetField("_tabGroup", BindingFlags.Instance | BindingFlags.NonPublic)
				.GetValue(ribbon);
			AddTabCell(group, strip, "addon_Demo");

			Assert.That(ribbon.ApplySavedTabOrder(), Is.True);
			Assert.That(TabKeys(strip),
				Is.EqualTo(new List<string> { "paint", "art list", "mesh", "addon_demo" }));
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void RestoreDefaultTabOrder_PutsAuthoredOrderBack_AndForgetsTheSave() {
		var root = BuildRibbonStrip(out var ribbon, out var strip, "art list", "mesh", "controlnet", "paint");
		// Layout reflow queues a coroutine, which EditMode cannot run — behavior under test is the ordering.
		UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
		try {
			// Awake captures the authored order in the app; do the same here before reordering.
			typeof(CommandRibbon_UI)
				.GetMethod("CaptureAuthoredTabOrderIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic)
				.Invoke(ribbon, null);
			Transform paint = CommandRibbon_UI.CollectStripTabCells(strip)[3];
			CommandRibbon_UI.MoveStripTabToSlot(strip, paint, 0);
			ribbon.PersistCurrentTabOrder();
			Assert.That(RibbonTabOrder_Prefs.HasSavedOrder(), Is.True);

			ribbon.RestoreDefaultTabOrder();

			Assert.That(TabKeys(strip), Is.EqualTo(new List<string> { "art list", "mesh", "controlnet", "paint" }));
			Assert.That(RibbonTabOrder_Prefs.HasSavedOrder(), Is.False,
				"Reset must forget the saved order, not just move cells");
		}
		finally {
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void NormalizeAddonStripDividers_KeepsDividerBeforeItsTab_AndHidesLeadingOne() {
		var root = BuildRibbonStrip(out var ribbon, out var strip, "art list", "controlnet");
		try {
			var group = (TabsGroup_UI)typeof(CommandRibbon_UI)
				.GetField("_tabGroup", BindingFlags.Instance | BindingFlags.NonPublic)
				.GetValue(ribbon);
			var divider = new GameObject("StripDivider_Demo", typeof(RectTransform), typeof(Image));
			divider.transform.SetParent(strip, false);
			var addonElem = AddTabCell(group, strip, "addon_Demo");

			var tabDict = (System.Collections.IDictionary)typeof(CommandRibbon_UI)
				.GetField("_addonTabById", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ribbon);
			var divDict = (System.Collections.IDictionary)typeof(CommandRibbon_UI)
				.GetField("_addonStripDividerById", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ribbon);
			tabDict["Demo"] = addonElem.gameObject;
			divDict["Demo"] = divider;

			// Add-on tab dragged to the front: its divider follows and hides (no stray leading bar).
			CommandRibbon_UI.MoveStripTabToSlot(strip, addonElem.transform, 0);
			ribbon.NormalizeAddonStripDividers();
			Assert.That(divider.transform.GetSiblingIndex(),
				Is.EqualTo(addonElem.transform.GetSiblingIndex() - 1));
			Assert.That(divider.activeSelf, Is.False, "leading tab must not show a divider on the strip edge");

			// Dragged back behind ControlNet: divider re-pairs directly before the tab and shows again.
			CommandRibbon_UI.MoveStripTabToSlot(strip, addonElem.transform, 2);
			ribbon.NormalizeAddonStripDividers();
			Assert.That(divider.transform.GetSiblingIndex(),
				Is.EqualTo(addonElem.transform.GetSiblingIndex() - 1));
			Assert.That(divider.activeSelf, Is.True);
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void ComputeTargetSlot_PicksCellUnderPointer_AndClampsOutsideTheRow() {
		var cells = new List<RectTransform>();
		var host = new GameObject("SlotMath");
		host.SetActive(false);
		try {
			for (int i = 0; i < 3; i++) {
				var go = new GameObject("Cell" + i, typeof(RectTransform));
				go.transform.SetParent(host.transform, false);
				var rt = (RectTransform)go.transform;
				rt.sizeDelta = new Vector2(100f, 30f);
				rt.localPosition = new Vector3(-100f + i * 100f, 0f, 0f);
				cells.Add(rt);
			}
			Assert.That(RibbonTabDragReorder_UI.ComputeTargetSlot(cells, -100f), Is.EqualTo(0));
			Assert.That(RibbonTabDragReorder_UI.ComputeTargetSlot(cells, 10f), Is.EqualTo(1));
			Assert.That(RibbonTabDragReorder_UI.ComputeTargetSlot(cells, 95f), Is.EqualTo(2));
			Assert.That(RibbonTabDragReorder_UI.ComputeTargetSlot(cells, -9999f), Is.EqualTo(0),
				"pointer left of the row drops at the front");
			Assert.That(RibbonTabDragReorder_UI.ComputeTargetSlot(cells, 9999f), Is.EqualTo(2),
				"pointer right of the row drops at the end");
			Assert.That(RibbonTabDragReorder_UI.ComputeTargetSlot(new List<RectTransform>(), 0f), Is.EqualTo(-1));
		}
		finally {
			Object.DestroyImmediate(host);
		}
	}

	[Test]
	public void DragLoop_UsesCallerBuffer_AndMovesBySlotWithoutRescanningStrip() {
		var root = BuildRibbonStrip(out var ribbon, out var strip, "art list", "mesh", "controlnet", "paint");
		try {
			var divider = new GameObject("StripDivider_Demo", typeof(RectTransform), typeof(Image));
			divider.transform.SetParent(strip, false);

			var buffer = new List<RectTransform> { null, null };
			CommandRibbon_UI.CollectStripTabCellRects(strip, buffer);
			Assert.That(buffer.Count, Is.EqualTo(4), "stale buffer entries must be cleared, dividers skipped");
			Assert.That(CommandRibbon_UI.StripTabOrderKey(buffer[2]), Is.EqualTo("controlnet"));

			Assert.That(CommandRibbon_UI.MoveStripTabToSlot(buffer[2], buffer, 0), Is.True);
			Assert.That(TabKeys(strip), Is.EqualTo(new List<string> { "controlnet", "art list", "mesh", "paint" }));
			Assert.That(CommandRibbon_UI.MoveStripTabToSlot(buffer[2], buffer, 42), Is.False,
				"out-of-range slot must be ignored, not clamped into a random move");

			var excepted = new List<RectTransform>();
			CommandRibbon_UI.CollectStripTabCellRects(strip, excepted, buffer[2]);
			Assert.That(excepted.Count, Is.EqualTo(3), "drag loop collects neighbors only — the floating cell stays out of slot math");
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void DragInProgress_SuppressesPerFrameStripReflow() {
		Assert.That(RibbonTabDragReorder_UI.IsDraggingAnyTab, Is.False, "no drag outside a pointer drag");

		string src = File.ReadAllText(Path.Combine(Application.dataPath, "_gm", "Layouts", "RightPanel", "CommandRibbon_UI.cs"));
		int update = src.IndexOf("void Update(){", System.StringComparison.Ordinal);
		Assert.That(update, Is.GreaterThan(0));
		string updateBody = src.Substring(update, 1400);
		Assert.That(updateBody, Does.Contain("RibbonTabDragReorder_UI.IsDraggingAnyTab"),
			"strip width poll must not reflow while a tab is mid-drag (frame jitter)");
		Assert.That(updateBody, Does.Contain("SyncStripTabSelectionChromeIfChanged();"),
			"Nomad selection re-tint stays inside the not-dragging branch");
		int chromeCall = updateBody.IndexOf("SyncStripTabSelectionChromeIfChanged();", System.StringComparison.Ordinal);
		int dragGuard = updateBody.IndexOf("!RibbonTabDragReorder_UI.IsDraggingAnyTab", System.StringComparison.Ordinal);
		Assert.That(dragGuard, Is.GreaterThan(0));
		Assert.That(chromeCall, Is.GreaterThan(dragGuard),
			"re-theming the strip on sibling-order change is the flicker — skip it while dragging");

		int refresh = src.IndexOf("void RefreshRibbonTabStripLayout", System.StringComparison.Ordinal);
		Assert.That(refresh, Is.GreaterThan(0));
		string refreshBody = src.Substring(refresh, 700);
		Assert.That(refreshBody, Does.Contain("RibbonTabDragReorder_UI.IsDraggingAnyTab"));
		Assert.That(refreshBody, Does.Not.Contain("LayoutRebuilder"),
			"mid-drag must not queue a strip rebuild — that snaps the floating cell");

		int drop = src.IndexOf("public void OnStripTabDropped()", System.StringComparison.Ordinal);
		int dropEnd = src.IndexOf("public void NormalizeAddonStripDividers()", drop + 1, System.StringComparison.Ordinal);
		string dropBody = src.Substring(drop, dropEnd - drop);
		Assert.That(dropBody, Does.Contain("RebuildStripLayoutImmediate"));
		Assert.That(dropBody, Does.Not.Contain("QueueTabStripRebuildNextFrame"),
			"next-frame parent walk + ForceUpdateCanvases flashes the ribbon window on drop");

		string drag = File.ReadAllText(Path.Combine(Application.dataPath, "_gm", "Layouts", "RightPanel", "RibbonTabDragReorder_UI.cs"));
		int onDrag = drag.IndexOf("public void OnDrag(", System.StringComparison.Ordinal);
		int onEnd = drag.IndexOf("public void OnEndDrag(", System.StringComparison.Ordinal);
		Assert.That(onDrag, Is.GreaterThan(0));
		Assert.That(onEnd, Is.GreaterThan(onDrag));
		string onDragBody = drag.Substring(onDrag, onEnd - onDrag);
		Assert.That(onDragBody, Does.Contain("_cellRt.localPosition"),
			"dragged cell must follow the pointer outside the layout group");
		Assert.That(onDragBody, Does.Contain("ClampLocalXToStrip"),
			"floating tab must stay inside the strip — otherwise it slides over the viewport");
		Assert.That(drag, Does.Contain("ApplyFloatingTabRect"),
			"stretch-anchored tabs must freeze to a fixed size while floating or they fill the panel L/R");
		Assert.That(drag, Does.Contain("GetStripLocalXClipRange"),
			"clamp must use the ScrollRect viewport when the strip content is wider than the frame");
		Assert.That(onDragBody, Does.Contain("kSlotSwapMarginPx"), "swap margin prevents slot ping-pong");
		Assert.That(onDragBody, Does.Contain("MovePlaceholderToSlot"),
			"neighbors shift via a placeholder gap, not SetSiblingIndex on the dragged cell");
		Assert.That(onDragBody, Does.Contain("RebuildStripLayoutImmediate"),
			"placeholder moves must snap neighbors this frame, not wait for a canvas-wide rebuild");
		Assert.That(onDragBody, Does.Not.Contain("MoveStripTabToSlot"),
			"committing sibling order every drag tick is what flickered the row");
		Assert.That(onDragBody, Does.Contain("_cellsBuffer"), "no per-event list allocation in the drag loop");
		Assert.That(drag, Does.Contain("ignoreLayout = true"));
		Assert.That(drag, Does.Not.Contain("AddComponent<CanvasGroup>"),
			"adding CanvasGroup on begin-drag rebuilds the canvas batch and flashes the strip");
	}

	[Test]
	public void InsertSlot_CountsNeighborsLeftOfPointer_AndPlaceholderMovesOnce() {
		var host = new GameObject("InsertSlotHost", typeof(RectTransform));
		try {
			var others = new List<RectTransform>();
			for (int i = 0; i < 3; i++) {
				var go = new GameObject("Cell" + i, typeof(RectTransform));
				go.transform.SetParent(host.transform, false);
				var rt = (RectTransform)go.transform;
				rt.sizeDelta = new Vector2(80f, 28f);
				rt.localPosition = new Vector3(-80f + i * 80f, 0f, 0f);
				others.Add(rt);
			}
			Assert.That(RibbonTabDragReorder_UI.ComputeInsertSlot(others, -200f), Is.EqualTo(0));
			Assert.That(RibbonTabDragReorder_UI.ComputeInsertSlot(others, -80f), Is.EqualTo(1),
				"on the first center counts as past it");
			Assert.That(RibbonTabDragReorder_UI.ComputeInsertSlot(others, 200f), Is.EqualTo(3));

			var gap = new GameObject(RibbonTabDragReorder_UI.PLACEHOLDER_NAME, typeof(RectTransform));
			gap.transform.SetParent(host.transform, false);
			gap.transform.SetSiblingIndex(1);
			Assert.That(RibbonTabDragReorder_UI.MovePlaceholderToSlot(gap.transform, others, 0), Is.True);
			Assert.That(gap.transform.GetSiblingIndex(), Is.EqualTo(0));
			Assert.That(RibbonTabDragReorder_UI.MovePlaceholderToSlot(gap.transform, others, 0), Is.False,
				"already in slot — do not SetSiblingIndex again (that rebuilds the row)");
			Assert.That(RibbonTabDragReorder_UI.MovePlaceholderToSlot(gap.transform, others, 3), Is.True);
			Assert.That(gap.transform.GetSiblingIndex(), Is.GreaterThan(others[2].GetSiblingIndex()));
		}
		finally {
			Object.DestroyImmediate(host);
		}
	}

	[Test]
	public void SaveOrder_NormalizesKeys_AndDropsDuplicates() {
		RibbonTabOrder_Prefs.SaveOrder(new[] { " ControlNet ", "PAINT", "controlnet", "", null });
		Assert.That(RibbonTabOrder_Prefs.LoadOrder(), Is.EqualTo(new List<string> { "controlnet", "paint" }));
		RibbonTabOrder_Prefs.ClearOrder();
		Assert.That(RibbonTabOrder_Prefs.HasSavedOrder(), Is.False);
	}

	[Test]
	public void CollectStripTabCells_SkipsDividersAndNonTabChildren() {
		var root = BuildRibbonStrip(out var ribbon, out var strip, "art list", "paint");
		try {
			var divider = new GameObject("StripDivider_Demo", typeof(RectTransform), typeof(Image));
			divider.transform.SetParent(strip, false);
			var spacer = new GameObject("Spacer", typeof(RectTransform));
			spacer.transform.SetParent(strip, false);

			Assert.That(TabKeys(strip), Is.EqualTo(new List<string> { "art list", "paint" }));
			Assert.That(CommandRibbon_UI.IsStripDividerChild(divider.transform), Is.True);
			Assert.That(CommandRibbon_UI.IsStripDividerChild(spacer.transform), Is.False);
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void DropCommit_Source_NormalizesDividers_SyncsGroup_AndPersists() {
		string path = Path.Combine(Application.dataPath, "_gm", "Layouts", "RightPanel", "CommandRibbon_UI.cs");
		string src = File.ReadAllText(path);
		int start = src.IndexOf("public void OnStripTabDropped()", System.StringComparison.Ordinal);
		Assert.That(start, Is.GreaterThan(0));
		int next = src.IndexOf("public void NormalizeAddonStripDividers()", start + 1, System.StringComparison.Ordinal);
		Assert.That(next, Is.GreaterThan(start));
		string body = src.Substring(start, next - start);
		Assert.That(body, Does.Contain("NormalizeAddonStripDividers()"));
		Assert.That(body, Does.Contain("SyncTabOrderFromStrip()"));
		Assert.That(body, Does.Contain("PersistCurrentTabOrder()"));
		Assert.That(body, Does.Contain("RebuildStripLayoutImmediate"));
		Assert.That(body, Does.Not.Contain("RefreshTabStripLayout()"),
			"drop must snap the strip this frame — full refresh themes + ForceUpdateCanvases and flashes the ribbon");
		Assert.That(body, Does.Not.Contain("Canvas.ForceUpdateCanvases"),
			"canvas-wide rebuild is the window flicker after a tab slide");

		int awake = src.IndexOf("void Awake(){", System.StringComparison.Ordinal);
		Assert.That(awake, Is.GreaterThan(0));
		int awakeEnd = src.IndexOf("void OnDestroy()", awake, System.StringComparison.Ordinal);
		string awakeBody = src.Substring(awake, awakeEnd - awake);
		Assert.That(awakeBody, Does.Contain("ApplySavedTabOrder()"),
			"saved tab order must be restored on launch");
		Assert.That(awakeBody, Does.Contain("RefreshTabReorderHandles()"));
	}

	[Test]
	public void ClampLocalXToStrip_KeepsTabInsideStripRect() {
		var stripGo = new GameObject("Strip", typeof(RectTransform));
		var cellGo = new GameObject("Cell", typeof(RectTransform));
		try {
			var strip = (RectTransform)stripGo.transform;
			strip.anchorMin = strip.anchorMax = new Vector2(0.5f, 0.5f);
			strip.pivot = new Vector2(0.5f, 0.5f);
			strip.sizeDelta = new Vector2(200f, 32f);
			var cell = (RectTransform)cellGo.transform;
			cell.SetParent(strip, false);
			cell.sizeDelta = new Vector2(40f, 28f);

			Assert.That(RibbonTabDragReorder_UI.ClampLocalXToStrip(strip, cell, -400f),
				Is.GreaterThanOrEqualTo(strip.rect.xMin + 19f));
			Assert.That(RibbonTabDragReorder_UI.ClampLocalXToStrip(strip, cell, 400f),
				Is.LessThanOrEqualTo(strip.rect.xMax - 19f));
			float mid = RibbonTabDragReorder_UI.ClampLocalXToStrip(strip, cell, 0f);
			Assert.That(mid, Is.EqualTo(0f).Within(0.01f));
		} finally {
			Object.DestroyImmediate(stripGo);
		}
	}

	[Test]
	public void ApplyFloatingTabRect_UnstretchesCellSoItCannotFillTheStrip() {
		var stripGo = new GameObject("Strip", typeof(RectTransform));
		var cellGo = new GameObject("StretchTab", typeof(RectTransform));
		try {
			var strip = (RectTransform)stripGo.transform;
			strip.anchorMin = strip.anchorMax = new Vector2(0.5f, 0.5f);
			strip.pivot = new Vector2(0.5f, 0.5f);
			strip.sizeDelta = new Vector2(300f, 40f);
			var cell = (RectTransform)cellGo.transform;
			cell.SetParent(strip, false);
			// Same stretch pattern as CommandRibbon strip tabs under the HLG.
			cell.anchorMin = Vector2.zero;
			cell.anchorMax = Vector2.one;
			cell.sizeDelta = Vector2.zero;
			cell.pivot = new Vector2(0.5f, 0.5f);
			LayoutRebuilder.ForceRebuildLayoutImmediate(strip);
			// Force a laid-out size before float (HLG would normally own this).
			cell.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 60f);
			cell.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 32f);

			RibbonTabDragReorder_UI.ApplyFloatingTabRect(strip, cell);

			Assert.That(cell.anchorMin, Is.EqualTo(new Vector2(0.5f, 0.5f)));
			Assert.That(cell.anchorMax, Is.EqualTo(new Vector2(0.5f, 0.5f)));
			Assert.That(cell.sizeDelta.x, Is.EqualTo(60f).Within(0.5f),
				"float must keep the tab's own width — stretch would make it ~panel-wide");
			Assert.That(cell.sizeDelta.x, Is.LessThan(strip.rect.width * 0.5f),
				"floating tab must stay narrower than half the strip (not panel-spanning)");
		} finally {
			Object.DestroyImmediate(stripGo);
		}
	}

	[Test]
	public void DropAndDrag_Source_Clamps_SnapsGap_PreservesScroll_AndPointerUpCommits() {
		string drag = File.ReadAllText(Path.Combine(Application.dataPath, "_gm", "Layouts", "RightPanel",
			"RibbonTabDragReorder_UI.cs"));
		Assert.That(drag, Does.Contain("IPointerUpHandler"),
			"releasing over the viewport must still snap the tab — EndDrag is often skipped");
		Assert.That(drag, Does.Contain("FinishDrag"));
		int endState = drag.IndexOf("void EndDragState(", System.StringComparison.Ordinal);
		Assert.That(endState, Is.GreaterThan(0));
		string endBody = drag.Substring(endState, System.Math.Min(900, drag.Length - endState));
		Assert.That(endBody, Does.Contain("gap.localPosition.x"),
			"drop must seat the tab on the placeholder before the gap is destroyed");

		string ribbon = File.ReadAllText(Path.Combine(Application.dataPath, "_gm", "Layouts", "RightPanel",
			"CommandRibbon_UI.cs"));
		int rebuild = ribbon.IndexOf("public static void RebuildStripLayoutImmediate", System.StringComparison.Ordinal);
		Assert.That(rebuild, Is.GreaterThan(0));
		int rebuildEnd = ribbon.IndexOf("public void NormalizeAddonStripDividers()", rebuild, System.StringComparison.Ordinal);
		string rebuildBody = ribbon.Substring(rebuild, rebuildEnd - rebuild);
		Assert.That(rebuildBody, Does.Contain("horizontalNormalizedPosition"),
			"strip rebuild must not jump the ScrollRect or neighbors slide out from under the tab");
		Assert.That(rebuildBody, Does.Not.Contain("horizontalNormalizedPosition = 1f"),
			"scroll-to-end is the add-tab path — not a reorder snap");
	}

	[Test]
	public void Settings_Source_WiresDynamicTabMovementToggleAndOrderButtons() {
		string mgr = File.ReadAllText(Path.Combine(Application.dataPath, "_gm", "Features", "Settings", "Settings_MGR.cs"));
		Assert.That(mgr, Does.Contain("StaticEvents.SubscribeUnique<bool>(\"Settings:set_ui_dynamicTabMovement\", set_ui_dynamicTabMovement)"));
		Assert.That(mgr, Does.Contain("StaticEvents.SubscribeUnique(\"Settings:OnButton_SaveRibbonTabOrder\", OnButton_SaveRibbonTabOrder)"));
		Assert.That(mgr, Does.Contain("StaticEvents.SubscribeUnique(\"Settings:OnButton_ResetRibbonTabOrder\", OnButton_ResetRibbonTabOrder)"));
		Assert.That(mgr, Does.Contain("StaticEvents.Unsubscribe<bool>(\"Settings:set_ui_dynamicTabMovement\", set_ui_dynamicTabMovement)"));
		Assert.That(mgr, Does.Contain("tryLoad_ui_dynamicTabMovement();"));
		Assert.That(mgr, Does.Contain("RibbonTabOrder_Prefs.SetDynamicTabMovementEnabled(unlocked)"));
		Assert.That(mgr, Does.Contain("CommandRibbon_UI.ApplyDynamicTabMovementSetting()"));
		Assert.That(mgr, Does.Contain("ribbon.PersistCurrentTabOrder()"));
		Assert.That(mgr, Does.Contain("ribbon.RestoreDefaultTabOrder()"));

		string ui = File.ReadAllText(Path.Combine(Application.dataPath, "_gm", "Features", "Settings", "Settings_UI.cs"));
		Assert.That(ui, Does.Contain("EnsureDynamicTabMovementRowsExist();"));
		Assert.That(ui, Does.Contain("Settings:set_ui_dynamicTabMovement"));
		Assert.That(ui, Does.Contain("Settings:OnButton_SaveRibbonTabOrder"));
		Assert.That(ui, Does.Contain("Settings:OnButton_ResetRibbonTabOrder"));
	}
}

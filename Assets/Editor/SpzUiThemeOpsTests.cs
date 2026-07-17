using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class SpzUiThemeOpsTests {

	[SetUp]
	public void SetUp() {
		SpzUiThemeOps.ResetTheme();
		Cleanup("p1-preset");
		Cleanup("p1-experiment");
		for (int i = 0; i < 33; i++)
			Cleanup($"p1-cap-{i:00}");
	}

	[TearDown]
	public void TearDown() {
		SetUp();
	}

	[Test]
	public void RegisterListAndApplyPresetById() {
		Assert.That(SpzUiThemeOps.TryRegisterTheme(
			" p1-preset ",
			" P1 Preset ",
			Tokens(("AcCeNt", " #112233 ")),
			" TestOwner ",
			out string error), Is.True, error);

		var list = SpzUiThemeOps.ListThemesResult();
		Assert.That((int)list["registered_count"], Is.EqualTo(1));
		Assert.That(list["themes"].ToString(), Does.Contain("\"id\": \"p1-preset\""));
		Assert.That(list["themes"].ToString(), Does.Contain("\"owner\": \"TestOwner\""));

		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-preset", null, "replace", out error), Is.True, error);
		var active = SpzUiThemeOps.GetThemeResult();
		Assert.That((string)active["tokens"]["accent"], Is.EqualTo("#112233FF"));
		Assert.That((string)active["theme_id"], Is.EqualTo("p1-preset"));
	}

	[Test]
	public void PatchKeepsActiveValuesAndReplaceRestoresDefaults() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			Tokens(("panel_bg", "#01020304"), ("accent", "#112233")),
			"replace",
			out string error), Is.True, error);

		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			Tokens(("accent", "#AABBCC")),
			"patch",
			out error), Is.True, error);
		var patched = SpzUiThemeOps.GetThemeResult();
		Assert.That((string)patched["tokens"]["panel_bg"], Is.EqualTo("#01020304"));
		Assert.That((string)patched["tokens"]["accent"], Is.EqualTo("#AABBCCFF"));

		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			Tokens(("accent", "#DDEEFF")),
			"replace",
			out error), Is.True, error);
		var replaced = SpzUiThemeOps.GetThemeResult();
		Assert.That((string)replaced["tokens"]["panel_bg"], Is.Not.EqualTo("#01020304"));
		Assert.That((string)replaced["tokens"]["accent"], Is.EqualTo("#DDEEFFFF"));
	}

	[Test]
	public void PresetWithOverridesUsesPresetAsBase() {
		Assert.That(SpzUiThemeOps.TryRegisterTheme(
			"p1-preset",
			null,
			Tokens(("panel_bg", "#102030"), ("accent", "#405060")),
			null,
			out string error), Is.True, error);

		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-preset",
			Tokens(("accent", "#ABCDEF")),
			"patch",
			out error), Is.True, error);
		var active = SpzUiThemeOps.GetThemeResult();
		Assert.That((string)active["tokens"]["panel_bg"], Is.EqualTo("#102030FF"));
		Assert.That((string)active["tokens"]["accent"], Is.EqualTo("#ABCDEFFF"));
	}

	[Test]
	public void UnknownTokensDoNotMutateActiveTheme() {
		var before = SpzUiThemeOps.GetThemeResult().ToString();
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			Tokens(("glow", "#FF0000")),
			"replace",
			out string error), Is.False);
		Assert.That(error, Does.Contain("Unknown"));
		Assert.That(SpzUiThemeOps.GetThemeResult().ToString(), Is.EqualTo(before));
	}

	[Test]
	public void PromotedRoleTokensApplyAtomically() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			Tokens(
				("success", "#22C55E"),
				("danger", "#EF4444"),
				("border", "#FFFFFF14"),
				("tab_active", "#545454"),
				("selection", "#3B82F6")),
			"replace",
			out string error), Is.True, error);
		var tokens = SpzUiThemeOps.GetThemeResult()["tokens"];
		Assert.That((string)tokens["success"], Is.EqualTo("#22C55EFF"));
		Assert.That((string)tokens["danger"], Is.EqualTo("#EF4444FF"));
		Assert.That((string)tokens["border"], Is.EqualTo("#FFFFFF14"));
		Assert.That((string)tokens["tab_active"], Is.EqualTo("#545454FF"));
		Assert.That((string)tokens["selection"], Is.EqualTo("#3B82F6FF"));
	}

	[Test]
	public void RegistrationCapRejectsThirtyThirdPresetWithoutMutation() {
		for (int i = 0; i < 32; i++) {
			Assert.That(SpzUiThemeOps.TryRegisterTheme(
				$"p1-cap-{i:00}", null, Tokens(("accent", "#112233")), null,
				out string error), Is.True, error);
		}

		Assert.That(SpzUiThemeOps.TryRegisterTheme(
			"p1-cap-32", null, Tokens(("accent", "#445566")), null,
			out string capError), Is.False);
		Assert.That(capError, Does.Contain("32"));
		Assert.That((int)SpzUiThemeOps.ListThemesResult()["registered_count"], Is.EqualTo(32));
	}

	[Test]
	public void UnregisterActivePresetLeavesOrphanPalette() {
		Assert.That(SpzUiThemeOps.TryRegisterTheme(
			"p1-preset", null, Tokens(("accent", "#112233")), null,
			out string error), Is.True, error);
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-preset", null, "replace", out error), Is.True, error);
		string activeTokens = SpzUiThemeOps.GetThemeResult()["tokens"].ToString();

		Assert.That(SpzUiThemeOps.TryUnregisterTheme("p1-preset", out error), Is.True, error);
		Assert.That(SpzUiThemeOps.GetThemeResult()["tokens"].ToString(), Is.EqualTo(activeTokens));
		Assert.That((bool)SpzUiThemeOps.ListThemesResult()["active_orphan"], Is.True);
	}

	[Test]
	public void MetadataReportsP2SchemaAndSurfaces() {
		var result = SpzUiThemeOps.GetThemeResult();
		Assert.That((string)result["addon_rpc_theme_version"], Is.EqualTo("1.12"));
		Assert.That(result["token_schema"].ToString(), Does.Contain("success"));
		Assert.That(result["token_schema"].ToString(), Does.Contain("tab_active"));
		Assert.That(result["reserved_token_names"].ToString(), Does.Not.Contain("danger"));
		var surfaces = (JArray)result["surfaces"];
		Assert.That(surfaces.Count, Is.GreaterThanOrEqualTo(7));
		foreach (var surface in surfaces)
			Assert.That((bool)surface["bound"], Is.True, surface["id"]?.ToString());
		Assert.That(result["composes_with"].ToString(), Does.Contain("spz.cmd.set_ui_scale"));
	}

	[Test]
	public void AddonManagerBuildsStichReferenceHierarchyWithWiredControls() {
		var host = new GameObject("AddonManagerUiTestHost");
		GameObject overlay = null;
		try {
			var manager = host.AddComponent<AddonManager_UI>();
			InvokePrivate(manager, "CreatePanelIfNeeded");
			var panel = (GameObject)GetPrivateField(manager, "_panel");

			Assert.That(panel, Is.Not.Null);
			overlay = panel.GetComponentInParent<Canvas>(true)?.gameObject;
			Assert.That(panel.transform.Find("StichAddonManager_v8"), Is.Not.Null);
			Assert.That(panel.transform.Find("Header/InstallButton")?.GetComponent<Button>(), Is.Not.Null);
			Assert.That(panel.transform.Find("Header/InstallButton/LineIcon")?.GetComponent<Image>()?.sprite, Is.Not.Null);
			Assert.That(panel.transform.Find("Header/RefreshButton")?.GetComponent<Button>(), Is.Not.Null);
			Assert.That(panel.transform.Find("Header/LoadAddonsNowButton")?.GetComponent<Button>(), Is.Not.Null);
			Assert.That(panel.transform.Find("Header/RunWithAddonsButton")?.GetComponent<Button>(), Is.Not.Null);
			Assert.That(panel.transform.parent?.GetComponent<Button>(), Is.Null);
			Assert.That(panel.transform.parent?.GetComponent<AddonManagerDimmerClose>(), Is.Not.Null);
			Assert.That(panel.transform.Find("FilterBar/FilterPills")?.GetComponent<ToggleGroup>(), Is.Not.Null);
			Assert.That(panel.transform.Find("RememberEnabledRow"), Is.Null);
			Assert.That(panel.transform.Find("ScrollView/ShortcutHints"), Is.Null);
			Assert.That(panel.transform.Find("StatusText")?.GetComponent<TextMeshProUGUI>(), Is.Not.Null);
			panel.transform.parent.gameObject.SetActive(true);
			panel.SetActive(true);
			Canvas.ForceUpdateCanvases();
			LayoutRebuilder.ForceRebuildLayoutImmediate(panel.GetComponent<RectTransform>());
			Canvas.ForceUpdateCanvases();
			var pillCorners = new Vector3[4];
			var scrollCorners = new Vector3[4];
			panel.transform.Find("FilterBar/FilterPills").GetComponent<RectTransform>().GetWorldCorners(pillCorners);
			panel.transform.Find("ScrollView").GetComponent<RectTransform>().GetWorldCorners(scrollCorners);
			Assert.That(pillCorners[0].y, Is.GreaterThanOrEqualTo(scrollCorners[1].y),
				"Filter pills must stay above the add-on list viewport.");

			var size = panel.GetComponent<RectTransform>().sizeDelta;
			Assert.That(size.x / size.y, Is.EqualTo(16f / 9f).Within(0.001f));
		} finally {
			if (overlay != null)
				UnityEngine.Object.DestroyImmediate(overlay);
			UnityEngine.Object.DestroyImmediate(host);
		}
	}

	static JObject Tokens(params (string name, string value)[] values) {
		var result = new JObject();
		foreach (var value in values)
			result[value.name] = value.value;
		return result;
	}

	static void Cleanup(string id) {
		SpzUiThemeOps.TryUnregisterTheme(id, out _);
	}

	static object GetPrivateField(object target, string fieldName) {
		return target.GetType()
			.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
			.GetValue(target);
	}

	static void InvokePrivate(object target, string methodName) {
		target.GetType()
			.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
			.Invoke(target, null);
	}
}

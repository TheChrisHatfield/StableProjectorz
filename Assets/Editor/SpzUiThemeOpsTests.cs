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
		SpzUiThemeOps.ClearPersistedTheme();
		Cleanup("p1-preset");
		Cleanup("p1-experiment");
		Cleanup("nomad-inspired");
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
	public void MetadataReportsP3TypedSchemaAndSurfaces() {
		var result = SpzUiThemeOps.GetThemeResult();
		Assert.That((string)result["addon_rpc_theme_version"], Is.EqualTo("1.18"));
		Assert.That((string)result["ui_scale_source"], Is.EqualTo("chrome"));
		Assert.That((string)result["persistence"], Is.EqualTo("player_prefs"));
		Assert.That(result["line_icons"].ToString(), Does.Contain("Brush"));
		var schema = (JArray)result["token_schema"];
		Assert.That(schema, Is.Not.Null);
		bool sawFont = false, sawSpacing = false, sawAccent = false, sawCorner = false, sawIconTint = false, sawPanelWidth = false, sawPanelAlpha = false, sawRibbonIconOnly = false;
		foreach (var entry in schema) {
			Assert.That(entry["name"], Is.Not.Null);
			Assert.That(entry["type"], Is.Not.Null);
			string name = (string)entry["name"];
			string type = (string)entry["type"];
			if (name == "accent") {
				sawAccent = true;
				Assert.That(type, Is.EqualTo("color"));
			}
			if (name == "icon_tint") {
				sawIconTint = true;
				Assert.That(type, Is.EqualTo("color"));
			}
			if (name == "font_scale") {
				sawFont = true;
				Assert.That(type, Is.EqualTo("float"));
				Assert.That((float)entry["min"], Is.EqualTo(0.75f));
				Assert.That((float)entry["max"], Is.EqualTo(1.5f));
			}
			if (name == "spacing_scale") {
				sawSpacing = true;
				Assert.That(type, Is.EqualTo("float"));
			}
			if (name == "corner_radius") {
				sawCorner = true;
				Assert.That(type, Is.EqualTo("float"));
				Assert.That((float)entry["min"], Is.EqualTo(0f));
				Assert.That((float)entry["max"], Is.EqualTo(12f));
			}
			if (name == "panel_width") {
				sawPanelWidth = true;
				Assert.That(type, Is.EqualTo("float"));
				Assert.That((float)entry["min"], Is.EqualTo(180f));
				Assert.That((float)entry["max"], Is.EqualTo(400f));
			}
			if (name == "panel_alpha") {
				sawPanelAlpha = true;
				Assert.That(type, Is.EqualTo("float"));
				Assert.That((float)entry["min"], Is.EqualTo(0.5f));
				Assert.That((float)entry["max"], Is.EqualTo(1f));
			}
			if (name == "ribbon_icon_only") {
				sawRibbonIconOnly = true;
				Assert.That(type, Is.EqualTo("float"));
				Assert.That((float)entry["min"], Is.EqualTo(0f));
				Assert.That((float)entry["max"], Is.EqualTo(1f));
			}
		}
		Assert.That(sawAccent && sawFont && sawSpacing && sawCorner && sawIconTint && sawPanelWidth && sawPanelAlpha && sawRibbonIconOnly, Is.True);
		Assert.That(result["reserved_token_names"].ToString(), Does.Not.Contain("danger"));
		var surfaces = (JArray)result["surfaces"];
		Assert.That(surfaces.Count, Is.GreaterThanOrEqualTo(16));
		bool sawLists = false, sawMultiview = false, sawWorkflowOpts = false, sawContextMenus = false, sawChromeTargets = false;
		foreach (var surface in surfaces) {
			Assert.That((bool)surface["bound"], Is.True, surface["id"]?.ToString());
			if ((string)surface["id"] == "right_panel_lists")
				sawLists = true;
			if ((string)surface["id"] == "multiview_pins")
				sawMultiview = true;
			if ((string)surface["id"] == "workflow_options")
				sawWorkflowOpts = true;
			if ((string)surface["id"] == "context_menus")
				sawContextMenus = true;
			if ((string)surface["id"] == "chrome_targets")
				sawChromeTargets = true;
		}
		Assert.That(sawLists && sawMultiview && sawWorkflowOpts && sawContextMenus && sawChromeTargets, Is.True);
		Assert.That(result["composes_with"].ToString(), Does.Contain("spz.cmd.set_ui_scale"));
		Assert.That(result["composes_with"].ToString(), Does.Contain("spz.cmd.set_skybox_color"));
	}

	[Test]
	public void ApplyTmpScaledCapturedTracksFontScaleWithoutCompounding() {
		var go = new GameObject("PhaseA_TmpScale");
		try {
			var tmp = go.AddComponent<TextMeshProUGUI>();
			tmp.fontSize = 20f;
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject { ["font_scale"] = 1.25 },
				"replace",
				out string error), Is.True, error);
			SpzUiThemeOps.ApplyTmpScaledCaptured(tmp, Color.white, 20f);
			Assert.That(tmp.fontSize, Is.EqualTo(25f).Within(0.05f));
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject { ["font_scale"] = 1.5 },
				"patch",
				out error), Is.True, error);
			SpzUiThemeOps.ApplyTmpScaledCaptured(tmp, Color.white, 20f);
			Assert.That(tmp.fontSize, Is.EqualTo(30f).Within(0.05f));
		} finally {
			UnityEngine.Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ApplyScaledLayoutGroupTracksSpacingScale() {
		var go = new GameObject("PhaseA_LayoutScale", typeof(RectTransform));
		try {
			var vlg = go.AddComponent<VerticalLayoutGroup>();
			vlg.spacing = 8f;
			vlg.padding = new RectOffset(4, 4, 4, 4);
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject { ["spacing_scale"] = 1.5 },
				"replace",
				out string error), Is.True, error);
			SpzUiThemeOps.ApplyScaledLayoutGroup(vlg);
			Assert.That(vlg.spacing, Is.EqualTo(12f).Within(0.05f));
			Assert.That(vlg.padding.left, Is.EqualTo(6));
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject { ["spacing_scale"] = 1.0 },
				"patch",
				out error), Is.True, error);
			SpzUiThemeOps.ApplyScaledLayoutGroup(vlg);
			Assert.That(vlg.spacing, Is.EqualTo(8f).Within(0.05f));
			Assert.That(vlg.padding.left, Is.EqualTo(4));
		} finally {
			UnityEngine.Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void PersistAndRestoreActiveThemeAcrossReset() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			Tokens(("accent", "#F2CA50"), ("font_scale", "1.2")),
			"replace",
			out string error), Is.True, error);
		Assert.That((string)SpzUiThemeOps.GetThemeResult()["persisted_theme_id"], Is.EqualTo("p1-experiment"));

		var tokensBefore = SpzUiThemeOps.GetThemeResult()["tokens"].DeepClone();
		string id = UnityEngine.PlayerPrefs.GetString("SpzUiTheme.ActiveThemeId", "");
		string json = UnityEngine.PlayerPrefs.GetString("SpzUiTheme.ActiveTokensJson", "");
		Assert.That(id, Is.EqualTo("p1-experiment"));
		Assert.That(json, Is.Not.Empty);

		// ResetTheme clears prefs; rewrite them to simulate cold start with saved prefs.
		SpzUiThemeOps.ResetTheme();
		UnityEngine.PlayerPrefs.SetString("SpzUiTheme.ActiveThemeId", id);
		UnityEngine.PlayerPrefs.SetString("SpzUiTheme.ActiveTokensJson", json);
		UnityEngine.PlayerPrefs.Save();

		Assert.That(SpzUiThemeOps.TryRestorePersistedTheme(out string detail), Is.True, detail);
		Assert.That(SpzUiThemeOps.ActiveThemeId, Is.EqualTo("p1-experiment"));
		Assert.That((string)SpzUiThemeOps.GetThemeResult()["tokens"]["accent"], Is.EqualTo("#F2CA50FF"));
		Assert.That((float)SpzUiThemeOps.GetThemeResult()["tokens"]["font_scale"], Is.EqualTo(1.2f).Within(0.001f));
		Assert.That(tokensBefore.ToString(), Is.EqualTo(SpzUiThemeOps.GetThemeResult()["tokens"].ToString()));
	}

	[Test]
	public void ResetThemeClearsPersistedTheme() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			Tokens(("accent", "#112233")),
			"replace",
			out string error), Is.True, error);
		SpzUiThemeOps.ResetTheme();
		Assert.That((string)SpzUiThemeOps.GetThemeResult()["persisted_theme_id"], Is.EqualTo(""));
		Assert.That(SpzUiThemeOps.TryRestorePersistedTheme(out _), Is.False);
	}

	[Test]
	public void CornerRadiusFailClosedAndApplyRoundedSprite() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["corner_radius"] = 20 },
			"replace",
			out string error), Is.False);
		Assert.That(error, Does.Contain("between").IgnoreCase);

		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["corner_radius"] = 8, ["icon_tint"] = "#AABBCC" },
			"replace",
			out error), Is.True, error);
		Assert.That((float)SpzUiThemeOps.GetThemeResult()["tokens"]["corner_radius"], Is.EqualTo(8f).Within(0.001f));
		Assert.That((string)SpzUiThemeOps.GetThemeResult()["tokens"]["icon_tint"], Is.EqualTo("#AABBCCFF"));

		var a = UiRuntimeSprites.GetRoundedRectSliced(4);
		var b = UiRuntimeSprites.GetRoundedRectSliced(8);
		Assert.That(a, Is.Not.Null);
		Assert.That(b, Is.Not.Null);
		Assert.That(ReferenceEquals(a, b), Is.False);
		Assert.That(UiRuntimeSprites.IsCachedRoundedRect(a), Is.True);

		var go = new GameObject("PhaseB4_RoundedBtn");
		try {
			var img = go.AddComponent<Image>();
			var authored = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
			img.sprite = authored;
			img.type = Image.Type.Simple;
			SpzUiThemeOps.ApplyRoundedControlSprite(img, markEligible: true);
			Assert.That(img.type, Is.EqualTo(Image.Type.Sliced));
			Assert.That(ReferenceEquals(img.sprite, b), Is.True);
			Assert.That(go.GetComponent<SpzUiThemeRoundedControl>(), Is.Not.Null);

			SpzUiThemeOps.RestoreRoundedControlSpritesUnder(go.transform);
			Assert.That(ReferenceEquals(img.sprite, authored), Is.True);
			Assert.That(img.type, Is.EqualTo(Image.Type.Simple));
			Assert.That(go.GetComponent<SpzUiThemeRoundedControl>(), Is.Null);

			var iconGo = new GameObject("LineIcon");
			iconGo.transform.SetParent(go.transform, false);
			var icon = iconGo.AddComponent<Image>();
			icon.color = Color.white;
			SpzUiThemeOps.ApplyToAddonUiRoot(go);
			Assert.That(icon.color, Is.EqualTo(SpzUiThemeOps.Active.iconTint));
		} finally {
			UnityEngine.Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void PanelWidthFailClosedAndAppliesLayoutElement() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["panel_width"] = 50 },
			"replace",
			out string error), Is.False);
		Assert.That(error, Does.Contain("between").IgnoreCase);

		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["panel_width"] = 280 },
			"replace",
			out error), Is.True, error);
		var go = new GameObject("PhaseB7_Width");
		try {
			var le = go.AddComponent<LayoutElement>();
			le.preferredWidth = 220f;
			le.minWidth = 100f;
			SpzUiThemeOps.ApplyPanelWidth(le);
			Assert.That(le.preferredWidth, Is.EqualTo(280f).Within(0.01f));
		} finally {
			UnityEngine.Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void PanelAlphaFailClosedAndResolveShellMultipliesAlpha() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["panel_alpha"] = 0.2 },
			"replace",
			out string error), Is.False);
		Assert.That(error, Does.Contain("between").IgnoreCase);

		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			Tokens(("panel_bg", "#112233FF"), ("panel_alpha", "0.5")),
			"replace",
			out error), Is.True, error);
		Color shell = SpzUiThemeOps.ResolvePanelShellColor();
		Assert.That(shell.a, Is.EqualTo(0.5f).Within(0.01f));
		Assert.That(SpzUiThemeOps.TryParseStudioLineIcon("Brush", out StudioLineIcon icon, out error), Is.True, error);
		Assert.That(icon, Is.EqualTo(StudioLineIcon.Brush));
		Assert.That(SpzUiThemeOps.TryParseStudioLineIcon("Expand", out icon, out error), Is.True, error);
		Assert.That(icon, Is.EqualTo(StudioLineIcon.Expand));
		Assert.That(SpzUiThemeOps.TryParseStudioLineIcon("Image", out icon, out error), Is.True, error);
		Assert.That(icon, Is.EqualTo(StudioLineIcon.Image));
		Assert.That(SpzUiThemeOps.TryParseStudioLineIcon("Layers", out icon, out error), Is.True, error);
		Assert.That(icon, Is.EqualTo(StudioLineIcon.Layers));
		Assert.That(SpzUiThemeOps.TryParseStudioLineIcon("ChevronLeft", out icon, out error), Is.True, error);
		Assert.That(icon, Is.EqualTo(StudioLineIcon.ChevronLeft));
		Assert.That(SpzUiThemeOps.TryParseStudioLineIcon("ChevronRight", out icon, out error), Is.True, error);
		Assert.That(icon, Is.EqualTo(StudioLineIcon.ChevronRight));
		Assert.That(SpzUiThemeOps.ListLineIconNames().ToString(), Does.Contain("Mesh"));
		Assert.That(SpzUiThemeOps.ListLineIconNames().ToString(), Does.Contain("Expand"));
		Assert.That(RibbonViewportFullViewOnScreen_Toggle_UI.ResolveFullViewDockIcon(), Is.EqualTo(StudioLineIcon.Expand));
		Assert.That(RibbonViewportFullViewOnScreen_Toggle_UI.ResolveOpenRightDockIcon(false), Is.EqualTo(StudioLineIcon.ChevronRight));
		Assert.That(RibbonViewportFullViewOnScreen_Toggle_UI.ResolveOpenRightDockIcon(true), Is.EqualTo(StudioLineIcon.ChevronLeft));
		Assert.That(SpzUiChromeOps.ListUiTargetIds(), Does.Contain("left_ribbon"));
		Assert.That(SpzUiChromeOps.ListUiTargetIds(), Does.Contain("workflow_options"));
	}

	[Test]
	public void StripLineIconResolveDefaultsDoNotRequireOverwriteOnRefresh() {
		// Regression: ApplyStudioTabChromeColors must keep compose/set_line_icon sprites
		// (only assign ResolveStripTabLineIcon when Image.sprite is null).
		Assert.That(SpzUiThemeOps.TryParseStudioLineIcon("Brush", out StudioLineIcon brush, out string error), Is.True, error);
		Assert.That(brush, Is.EqualTo(StudioLineIcon.Brush));
		Sprite a = UiRuntimeSprites.GetLineIcon(StudioLineIcon.Brush);
		Sprite b = UiRuntimeSprites.GetLineIcon(StudioLineIcon.Eye);
		Assert.That(a, Is.Not.Null);
		Assert.That(b, Is.Not.Null);
		Assert.That(ReferenceEquals(a, b), Is.False);
	}

	[Test]
	public void StripTabHaystackMapsPaintArtBgAndControlToDescriptiveIcons() {
		Assert.That(CommandRibbon_UI.ResolveStripTabLineIconFromHaystack("Tab: Paint paint Paint"),
			Is.EqualTo(StudioLineIcon.Brush));
		Assert.That(CommandRibbon_UI.ResolveStripTabLineIconFromHaystack("Tab: art list art list ART"),
			Is.EqualTo(StudioLineIcon.Image));
		Assert.That(CommandRibbon_UI.ResolveStripTabLineIconFromHaystack("Tab: art bg list ART (BG)"),
			Is.EqualTo(StudioLineIcon.Layers));
		Assert.That(CommandRibbon_UI.ResolveStripTabLineIconFromHaystack("controlnet ctrl"),
			Is.EqualTo(StudioLineIcon.Grid));
		Assert.That(CommandRibbon_UI.ResolveStripTabLineIconFromHaystack("mesh"),
			Is.EqualTo(StudioLineIcon.Mesh));
		Assert.That(CommandRibbon_UI.ResolveStripTabDisplayName(null), Is.EqualTo("Tab"));
		// Compose needles must hit identity beyond GameObject.name alone.
		Assert.That("Tab: 3d mesh mesh".IndexOf("Mesh", StringComparison.OrdinalIgnoreCase), Is.GreaterThanOrEqualTo(0));
		Assert.That("Tab: controlnet + anim controlnet ctrl".IndexOf("CTRL", StringComparison.OrdinalIgnoreCase),
			Is.GreaterThanOrEqualTo(0));
	}

	[Test]
	public void RibbonIconOnlyFailClosedAndActivatesAtHalf() {
		Assert.That(SpzUiThemeOps.RibbonIconOnlyActive, Is.False);
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["ribbon_icon_only"] = 1.5 },
			"replace",
			out string error), Is.False);
		Assert.That(error, Does.Contain("between").IgnoreCase);
		Assert.That(SpzUiThemeOps.RibbonIconOnlyActive, Is.False);

		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["ribbon_icon_only"] = true },
			"replace",
			out error), Is.True, error);
		Assert.That(SpzUiThemeOps.RibbonIconOnlyActive, Is.True);
		Assert.That(SpzUiThemeOps.Active.ribbonIconOnly, Is.EqualTo(1f).Within(0.001f));

		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["ribbon_icon_only"] = 0.5 },
			"replace",
			out error), Is.True, error);
		Assert.That(SpzUiThemeOps.RibbonIconOnlyActive, Is.True);

		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["ribbon_icon_only"] = 0.49 },
			"replace",
			out error), Is.True, error);
		Assert.That(SpzUiThemeOps.RibbonIconOnlyActive, Is.False);

		SpzUiThemeOps.ResetTheme();
		Assert.That(SpzUiThemeOps.RibbonIconOnlyActive, Is.False);
	}

	[Test]
	public void RibbonIconOnlyDoesNotOverrideBuiltinChromeBoundary() {
		// Token may be present, but bound chrome / icon-only strip layout stays off on builtin.
		Assert.That(SpzUiThemeOps.IsBuiltinDefaultActive, Is.True);
		Assert.That(SpzUiThemeOps.ShouldRecolorBoundChrome, Is.False);
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			SpzUiThemeOps.DefaultThemeId,
			new JObject { ["ribbon_icon_only"] = 1 },
			"patch",
			out string error), Is.True, error);
		Assert.That(SpzUiThemeOps.RibbonIconOnlyActive, Is.True);
		Assert.That(SpzUiThemeOps.ShouldRecolorBoundChrome, Is.False);
		Assert.That(SpzUiThemeOps.IsBuiltinDefaultActive, Is.True);
	}

	[Test]
	public void BuiltinDefaultIsActiveUntilNonDefaultApply() {
		Assert.That(SpzUiThemeOps.IsBuiltinDefaultActive, Is.True);
		Assert.That(SpzUiThemeOps.ShouldRecolorBoundChrome, Is.False);
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["accent"] = "#112233FF" },
			"replace",
			out string error), Is.True, error);
		Assert.That(SpzUiThemeOps.IsBuiltinDefaultActive, Is.False);
		Assert.That(SpzUiThemeOps.ShouldRecolorBoundChrome, Is.True);
		SpzUiThemeOps.ResetTheme();
		Assert.That(SpzUiThemeOps.IsBuiltinDefaultActive, Is.True);
		Assert.That(SpzUiThemeOps.ShouldRecolorBoundChrome, Is.False);
	}

	[Test]
	public void BoundChromeHelpersRestoreAuthoredOnBuiltin() {
		var go = new GameObject("theme-bound-chrome-test", typeof(RectTransform), typeof(Image));
		try {
			var img = go.GetComponent<Image>();
			Color authored = new Color(0.2f, 0.3f, 0.4f, 1f);
			img.color = authored;

			Assert.That(SpzUiThemeOps.ShouldRecolorBoundChrome, Is.False);
			SpzUiThemeOps.ApplyBoundChromeGraphic(img, Color.magenta);
			Assert.That(img.color, Is.EqualTo(authored));

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject { ["accent"] = "#112233FF" },
				"replace",
				out string error), Is.True, error);
			Assert.That(SpzUiThemeOps.ShouldRecolorBoundChrome, Is.True);
			SpzUiThemeOps.ApplyBoundChromeGraphic(img, Color.red);
			Assert.That(img.color, Is.EqualTo(Color.red));

			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.ApplyBoundChromeGraphic(img, Color.green);
			Assert.That(img.color, Is.EqualTo(authored));
		}
		finally {
			UnityEngine.Object.DestroyImmediate(go);
		}
	}

	[Test]
	public void ScaledLayoutGroupUnwindsWhenSpacingScaleReturnsToOne() {
		var go = new GameObject("theme-layout-scale-test", typeof(RectTransform), typeof(VerticalLayoutGroup));
		try {
			var vlg = go.GetComponent<VerticalLayoutGroup>();
			vlg.spacing = 8f;
			vlg.padding = new RectOffset(4, 4, 4, 4);

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject { ["spacing_scale"] = 2.0 },
				"replace",
				out string error), Is.True, error);
			SpzUiThemeOps.ApplyScaledLayoutGroup(vlg);
			Assert.That(vlg.spacing, Is.EqualTo(16f).Within(0.01f));

			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.RefreshScaledLayoutGroupsUnder(go.transform);
			Assert.That(vlg.spacing, Is.EqualTo(8f).Within(0.01f));
			Assert.That(vlg.padding.left, Is.EqualTo(4));
		}
		finally {
			UnityEngine.Object.DestroyImmediate(go);
		}
	}

	[Test]
	public void ThemeFloatTokensRejectNaNAndInfinity() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["ribbon_icon_only"] = "NaN" },
			"replace",
			out string error), Is.False);
		Assert.That(error, Does.Contain("finite").IgnoreCase);

		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["font_scale"] = "Infinity" },
			"replace",
			out error), Is.False);
		Assert.That(error, Does.Contain("finite").IgnoreCase);

		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["panel_alpha"] = float.NaN },
			"replace",
			out error), Is.False);
		Assert.That(error, Does.Contain("finite").IgnoreCase);
	}

	[Test]
	public void RibbonIconOnlyOffDoesNotStayLatchedAfterReplaceWithoutToken() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["ribbon_icon_only"] = 1, ["accent"] = "#112233FF" },
			"replace",
			out string error), Is.True, error);
		Assert.That(SpzUiThemeOps.RibbonIconOnlyActive, Is.True);

		// Form A replace from defaults without ribbon_icon_only must clear the gate (not orphan it).
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["accent"] = "#AABBCCFF" },
			"replace",
			out error), Is.True, error);
		Assert.That(SpzUiThemeOps.RibbonIconOnlyActive, Is.False);
		Assert.That(SpzUiThemeOps.Active.ribbonIconOnly, Is.EqualTo(0f).Within(0.001f));
	}

	[Test]
	public void NomadInspiredPresetRegistersScalesAndPatchesWithoutWipingAccent() {
		var tokens = new JObject {
			["panel_bg"] = "#1E1F23F2",
			["control_bg"] = "#292A2EFF",
			["field_bg"] = "#121317FF",
			["accent"] = "#F2CA50FF",
			["text_primary"] = "#E3E2E7FF",
			["text_muted"] = "#D0C5AFFF",
			["handle"] = "#C8C5CBFF",
			["success"] = "#7BC96FFF",
			["danger"] = "#FFB4ABFF",
			["border"] = "#99907C66",
			["tab_active"] = "#343539FF",
			["selection"] = "#F2CA5033",
			["font_scale"] = 1.05,
			["spacing_scale"] = 1.0,
		};
		Assert.That(SpzUiThemeOps.TryRegisterTheme(
			"nomad-inspired", "Nomad inspired", tokens, "NomadThemeSPZ", out string error), Is.True, error);
		Assert.That(SpzUiThemeOps.TryApplyTheme("nomad-inspired", null, "replace", out error), Is.True, error);
		var active = SpzUiThemeOps.GetThemeResult()["tokens"];
		Assert.That((string)active["accent"], Is.EqualTo("#F2CA50FF"));
		Assert.That((float)active["font_scale"], Is.EqualTo(1.05f).Within(0.001f));
		Assert.That((float)active["spacing_scale"], Is.EqualTo(1.0f).Within(0.001f));

		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"nomad-inspired",
			new JObject { ["font_scale"] = 1.2, ["spacing_scale"] = 1.1 },
			"patch",
			out error), Is.True, error);
		var patched = SpzUiThemeOps.GetThemeResult()["tokens"];
		Assert.That((string)patched["accent"], Is.EqualTo("#F2CA50FF"));
		Assert.That((float)patched["font_scale"], Is.EqualTo(1.2f).Within(0.001f));
		Assert.That((float)patched["spacing_scale"], Is.EqualTo(1.1f).Within(0.001f));
	}

	[Test]
	public void PatchWithRegisteredPresetPreservesPriorPatchedTokens() {
		var tokens = new JObject {
			["accent"] = "#F2CA50FF",
			["font_scale"] = 1.05,
			["ribbon_icon_only"] = 1,
		};
		Assert.That(SpzUiThemeOps.TryRegisterTheme(
			"nomad-inspired", "Nomad inspired", tokens, "NomadThemeSPZ", out string error), Is.True, error);
		Assert.That(SpzUiThemeOps.TryApplyTheme("nomad-inspired", null, "replace", out error), Is.True, error);
		Assert.That(SpzUiThemeOps.RibbonIconOnlyActive, Is.True);

		// Runtime override while preset stays registered.
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"nomad-inspired",
			new JObject { ["ribbon_icon_only"] = 0, ["accent"] = "#AABBCCFF" },
			"patch",
			out error), Is.True, error);
		Assert.That(SpzUiThemeOps.RibbonIconOnlyActive, Is.False);
		Assert.That((string)SpzUiThemeOps.GetThemeResult()["tokens"]["accent"], Is.EqualTo("#AABBCCFF"));

		// Scale patch must keep prior overrides (patch = active base), not rebuild from preset.
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"nomad-inspired",
			new JObject { ["font_scale"] = 1.2 },
			"patch",
			out error), Is.True, error);
		var after = SpzUiThemeOps.GetThemeResult()["tokens"];
		Assert.That((float)after["font_scale"], Is.EqualTo(1.2f).Within(0.001f));
		Assert.That((string)after["accent"], Is.EqualTo("#AABBCCFF"));
		Assert.That(SpzUiThemeOps.RibbonIconOnlyActive, Is.False);
	}

	[Test]
	public void ScaleTokensApplyAndScaledSpaceRespectsSpacing() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["font_scale"] = 1.25, ["spacing_scale"] = 1.5 },
			"patch",
			out string error), Is.True, error);
		var tokens = SpzUiThemeOps.GetThemeResult()["tokens"];
		Assert.That((float)tokens["font_scale"], Is.EqualTo(1.25f).Within(0.001f));
		Assert.That((float)tokens["spacing_scale"], Is.EqualTo(1.5f).Within(0.001f));
		Assert.That(SpzUiThemeOps.ScaledSpace(1), Is.EqualTo(ProjectUiScale.Space(1) * 1.5f).Within(0.01f));
		Assert.That(SpzUiThemeOps.Active.fontScale, Is.EqualTo(1.25f).Within(0.001f));
	}

	[Test]
	public void InvalidScaleFailsClosedWithoutMutatingColors() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			Tokens(("accent", "#112233")),
			"replace",
			out string error), Is.True, error);
		string accentBefore = (string)SpzUiThemeOps.GetThemeResult()["tokens"]["accent"];
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["font_scale"] = 2.0 },
			"patch",
			out error), Is.False);
		Assert.That(error, Does.Contain("between").IgnoreCase);
		Assert.That((string)SpzUiThemeOps.GetThemeResult()["tokens"]["accent"], Is.EqualTo(accentBefore));
		Assert.That((float)SpzUiThemeOps.GetThemeResult()["tokens"]["font_scale"], Is.EqualTo(1.0f).Within(0.001f));
	}

	[Test]
	public void ApplyToAddonUiRootScalesTmpWithFontScale() {
		var root = new GameObject("AddonPanel_test-scale");
		try {
			var tmpGo = new GameObject("Label");
			tmpGo.transform.SetParent(root.transform, false);
			var tmp = tmpGo.AddComponent<TextMeshProUGUI>();
			tmp.fontSize = 20f;
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject { ["font_scale"] = 1.25 },
				"replace",
				out string error), Is.True, error);
			SpzUiThemeOps.ApplyToAddonUiRoot(root);
			Assert.That(tmp.fontSize, Is.EqualTo(25f).Within(0.05f));
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject { ["font_scale"] = 1.5 },
				"patch",
				out error), Is.True, error);
			SpzUiThemeOps.ApplyToAddonUiRoot(root);
			Assert.That(tmp.fontSize, Is.EqualTo(30f).Within(0.05f));
		} finally {
			UnityEngine.Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
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

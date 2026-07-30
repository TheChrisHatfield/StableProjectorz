using System;
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
		Assert.That(surfaces.Count, Is.GreaterThanOrEqualTo(17));
		bool sawLists = false, sawMultiview = false, sawWorkflowOpts = false, sawContextMenus = false, sawChromeTargets = false;
		bool sawTopStripServ = false;
		string connectionNotes = null;
		foreach (var surface in surfaces) {
			Assert.That((bool)surface["bound"], Is.True, surface["id"]?.ToString());
			string id = (string)surface["id"];
			if (id == "right_panel_lists")
				sawLists = true;
			if (id == "multiview_pins")
				sawMultiview = true;
			if (id == "workflow_options")
				sawWorkflowOpts = true;
			if (id == "context_menus")
				sawContextMenus = true;
			if (id == "chrome_targets")
				sawChromeTargets = true;
			if (id == "top_strip_serv")
				sawTopStripServ = true;
			if (id == "connection_panels")
				connectionNotes = (string)surface["notes"];
		}
		Assert.That(sawLists && sawMultiview && sawWorkflowOpts && sawContextMenus && sawChromeTargets, Is.True);
		Assert.That(sawTopStripServ, Is.True, "SD SERV/3D SERV owned by RestartTheWebui, not ConnectionPanel_UI");
		Assert.That(connectionNotes, Does.Not.Contain("SD SERV").IgnoreCase);
		Assert.That(connectionNotes, Does.Contain("ConnectionPanel_UI"));
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
			Assert.That(img.type, Is.EqualTo(Image.Type.Simple), "Nomad litmus: solid square (no soft sliced whiskers)");
			Assert.That(UiRuntimeSprites.IsSolidRect(img.sprite), Is.True);
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
		Assert.That(SpzUiThemeOps.TryParseStudioLineIcon("Wireframe", out icon, out error), Is.True, error);
		Assert.That(icon, Is.EqualTo(StudioLineIcon.Wireframe));
		Assert.That(SpzUiThemeOps.TryParseStudioLineIcon("Cursor", out icon, out error), Is.True, error);
		Assert.That(icon, Is.EqualTo(StudioLineIcon.Cursor));
		Assert.That(SpzUiThemeOps.TryParseStudioLineIcon("Camera", out icon, out error), Is.True, error);
		Assert.That(icon, Is.EqualTo(StudioLineIcon.Camera));
		Assert.That(SpzUiThemeOps.TryParseStudioLineIcon("Bucket", out icon, out error), Is.True, error);
		Assert.That(icon, Is.EqualTo(StudioLineIcon.Bucket));
		Assert.That(SpzUiThemeOps.TryParseStudioLineIcon("Drop", out icon, out error), Is.True, error);
		Assert.That(icon, Is.EqualTo(StudioLineIcon.Drop));
		Assert.That(SpzUiThemeOps.TryParseStudioLineIcon("Eraser", out icon, out error), Is.True, error);
		Assert.That(icon, Is.EqualTo(StudioLineIcon.Eraser));
		Assert.That(SpzUiThemeOps.TryParseStudioLineIcon("Smudge", out icon, out error), Is.True, error);
		Assert.That(icon, Is.EqualTo(StudioLineIcon.Smudge));
		Assert.That(SpzUiThemeOps.TryParseStudioLineIcon("Bullseye", out icon, out error), Is.True, error);
		Assert.That(icon, Is.EqualTo(StudioLineIcon.Bullseye));
		Assert.That(SpzUiThemeOps.TryParseStudioLineIcon("Globe", out icon, out error), Is.True, error);
		Assert.That(icon, Is.EqualTo(StudioLineIcon.Globe));
		Assert.That(SpzUiThemeOps.ListLineIconNames().ToString(), Does.Contain("Mesh"));
		Assert.That(SpzUiThemeOps.ListLineIconNames().ToString(), Does.Contain("Bucket"));
		Assert.That(SpzUiThemeOps.ListLineIconNames().ToString(), Does.Contain("Cursor"));
		Assert.That(SpzUiThemeOps.ListLineIconNames().ToString(), Does.Contain("Bullseye"));
		Assert.That(SpzUiThemeOps.ListLineIconNames().ToString(), Does.Contain("Globe"));
		Assert.That(SpzUiThemeOps.ListLineIconNames().ToString(), Does.Contain("Expand"));
		Assert.That(SpzUiThemeOps.ListLineIconNames().ToString(), Does.Contain("Flatten"));
		Assert.That(SpzUiThemeOps.ListLineIconNames().ToString(), Does.Contain("Trash"));
		Assert.That(RibbonViewportFullViewOnScreen_Toggle_UI.ResolveFullViewDockIcon(), Is.EqualTo(StudioLineIcon.Expand));
		Assert.That(RibbonViewportFullViewOnScreen_Toggle_UI.ResolveOpenRightDockIcon(false), Is.EqualTo(StudioLineIcon.ChevronRight));
		Assert.That(RibbonViewportFullViewOnScreen_Toggle_UI.ResolveOpenRightDockIcon(true), Is.EqualTo(StudioLineIcon.ChevronLeft));
		Assert.That(SpzUiChromeOps.ListUiTargetIds(), Does.Contain("left_ribbon"));
		Assert.That(SpzUiChromeOps.ListUiTargetIds(), Does.Contain("workflow_options"));
	}

	[Test]
	public void SnapshotAuthoredColorBlockCapturesBeforeManualTint() {
		var go = new GameObject("CbSnap", typeof(RectTransform));
		var img = go.AddComponent<Image>();
		var btn = go.AddComponent<Button>();
		btn.targetGraphic = img;
		var authored = btn.colors;
		authored.highlightedColor = Color.magenta;
		btn.colors = authored;
		try {
			SpzUiThemeOps.SnapshotAuthoredColorBlock(btn);
			var tinted = btn.colors;
			tinted.highlightedColor = Color.cyan;
			btn.colors = tinted;
			SpzUiThemeOps.RestoreAuthoredColorBlock(btn);
			Assert.That(btn.colors.highlightedColor, Is.EqualTo(Color.magenta));
		} finally {
			UnityEngine.Object.DestroyImmediate(go);
		}
	}

	[Test]
	public void RestoreBoundChromeUnderUnwindsSelectableColorBlockAndHandleSize() {
		var root = new GameObject("RestoreChromeRoot", typeof(RectTransform));
		var btnGo = new GameObject("Btn", typeof(RectTransform));
		btnGo.transform.SetParent(root.transform, false);
		var img = btnGo.AddComponent<Image>();
		var btn = btnGo.AddComponent<Button>();
		btn.targetGraphic = img;
		var authoredBlock = btn.colors;
		authoredBlock.highlightedColor = Color.red;
		btn.colors = authoredBlock;

		var handleGo = new GameObject("Handle", typeof(RectTransform));
		handleGo.transform.SetParent(root.transform, false);
		var handleRt = handleGo.GetComponent<RectTransform>();
		handleRt.sizeDelta = new Vector2(40f, 40f);
		handleGo.AddComponent<Image>();

		try {
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				Tokens(("accent", "#F2CA50FF"), ("control_bg", "#292A2EFF")),
				"replace",
				out string error), Is.True, error);
			SpzUiThemeOps.ApplyBoundChromeSelectable(btn, SpzUiThemeOps.Active.controlBg, SpzUiThemeOps.Active.accent);
			Assert.That(btn.colors.highlightedColor, Is.Not.EqualTo(Color.red));

			var tag = handleGo.AddComponent<SpzUiThemeSliderHandleLayout>();
			tag.authoredSizeDelta = new Vector2(40f, 40f);
			tag.hasSnapshot = true;
			handleRt.sizeDelta = new Vector2(22f, 22f);

			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.RestoreBoundChromeUnder(root.transform);
			Assert.That(btn.colors.highlightedColor, Is.EqualTo(Color.red));
			Assert.That(handleRt.sizeDelta.x, Is.EqualTo(40f).Within(0.01f));
		} finally {
			UnityEngine.Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ApplyBoundChromeSelectableFlattensSlicedCornerAnchors() {
		var root = new GameObject("SlicedCornerFlatten");
		root.SetActive(false);
		try {
			var go = new GameObject("PovSlot", typeof(RectTransform), typeof(Image), typeof(Toggle));
			go.transform.SetParent(root.transform, false);
			var face = go.GetComponent<Image>();
			face.type = Image.Type.Sliced;
			face.color = Color.gray;
			var tickGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
			tickGo.transform.SetParent(go.transform, false);
			var tick = tickGo.GetComponent<Image>();
			tick.type = Image.Type.Sliced;
			tick.enabled = true;
			var authoredTickSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
			tick.sprite = authoredTickSprite;
			var toggle = go.GetComponent<Toggle>();
			toggle.targetGraphic = face;
			toggle.graphic = tick;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				Tokens(("control_bg", "#292A2EFF"), ("accent", "#F2CA50FF"), ("corner_radius", "5")),
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ApplyBoundChromeSelectable(toggle, SpzUiThemeOps.Active.controlBg, SpzUiThemeOps.Active.accent);

			Assert.That(face.type, Is.EqualTo(Image.Type.Simple), "Nomad litmus: BoundChrome faces are solid squares");
			Assert.That(UiRuntimeSprites.IsSolidRect(face.sprite), Is.True);
			// Checkmark glyph must survive BoundChromeSelectable (Settings ON state depends on it).
			Assert.That(tick.enabled, Is.True);
			Assert.That(ReferenceEquals(tick.sprite, authoredTickSprite), Is.True);
		}
		finally {
			UnityEngine.Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ApplyBoundChromeGraphicDoesNotFlattenToggleCheckmark() {
		var root = new GameObject("CheckmarkPreserve");
		root.SetActive(false);
		try {
			var go = new GameObject("Toggle", typeof(RectTransform), typeof(Image), typeof(Toggle));
			go.transform.SetParent(root.transform, false);
			var face = go.GetComponent<Image>();
			var tickGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
			tickGo.transform.SetParent(go.transform, false);
			var tick = tickGo.GetComponent<Image>();
			tick.type = Image.Type.Sliced;
			var authored = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
			tick.sprite = authored;
			var toggle = go.GetComponent<Toggle>();
			toggle.targetGraphic = face;
			toggle.graphic = tick;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				Tokens(("success", "#7BC96FFF"), ("corner_radius", "5")),
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ApplyBoundChromeGraphic(tick, SpzUiThemeOps.Active.success);
			Assert.That(SpzUiThemeOps.IsToggleCheckmarkGraphic(tick), Is.True);
			Assert.That(tick.type, Is.EqualTo(Image.Type.Sliced));
			Assert.That(ReferenceEquals(tick.sprite, authored), Is.True);
			Assert.That(tick.color, Is.EqualTo(SpzUiThemeOps.Active.success));
		}
		finally {
			UnityEngine.Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ApplyBoundChromeGraphicFlattensSlicedFaces() {
		var root = new GameObject("SlicedGraphicFlatten");
		root.SetActive(false);
		try {
			var go = new GameObject("PinFace", typeof(RectTransform), typeof(Image));
			go.transform.SetParent(root.transform, false);
			var face = go.GetComponent<Image>();
			face.type = Image.Type.Sliced;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				Tokens(("control_bg", "#292A2EFF"), ("corner_radius", "5")),
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ApplyBoundChromeGraphic(face, SpzUiThemeOps.Active.controlBg);
			Assert.That(face.type, Is.EqualTo(Image.Type.Simple), "Nomad litmus: sliced chrome flattens to solid square");
			Assert.That(UiRuntimeSprites.IsSolidRect(face.sprite), Is.True);
		}
		finally {
			UnityEngine.Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void FlattenToolFaceImageSnapshotsAndRestoreBoundChromeUnwindsLayout() {
		var root = new GameObject("ToolFaceLayout", typeof(RectTransform));
		root.SetActive(false);
		try {
			var parent = new GameObject("Cell", typeof(RectTransform));
			parent.transform.SetParent(root.transform, false);
			var faceGo = new GameObject("Face", typeof(RectTransform), typeof(Image));
			faceGo.transform.SetParent(parent.transform, false);
			var rt = faceGo.GetComponent<RectTransform>();
			rt.anchorMin = new Vector2(0.1f, 0.2f);
			rt.anchorMax = new Vector2(0.9f, 0.8f);
			rt.sizeDelta = new Vector2(12f, 8f);
			rt.anchoredPosition = new Vector2(3f, -2f);
			var face = faceGo.GetComponent<Image>();

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				Tokens(("control_bg", "#292A2EFF"), ("corner_radius", "5")),
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.FlattenToolFaceImage(face);
			Assert.That(rt.anchorMin, Is.EqualTo(Vector2.zero));
			Assert.That(rt.anchorMax, Is.EqualTo(Vector2.one));
			Assert.That(rt.sizeDelta, Is.EqualTo(Vector2.zero));

			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.RestoreBoundChromeUnder(root.transform);
			Assert.That(rt.anchorMin, Is.EqualTo(new Vector2(0.1f, 0.2f)));
			Assert.That(rt.anchorMax, Is.EqualTo(new Vector2(0.9f, 0.8f)));
			Assert.That(rt.sizeDelta, Is.EqualTo(new Vector2(12f, 8f)));
			Assert.That(rt.anchoredPosition, Is.EqualTo(new Vector2(3f, -2f)));
		}
		finally {
			UnityEngine.Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ThemeCheckboxToggleKeepsCheckmarkEnabledAndTinted() {
		var root = new GameObject("CheckboxTheme", typeof(RectTransform));
		root.SetActive(false);
		try {
			var go = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Toggle));
			go.transform.SetParent(root.transform, false);
			var face = go.GetComponent<Image>();
			var tickGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
			tickGo.transform.SetParent(go.transform, false);
			var tick = tickGo.GetComponent<Image>();
			tick.enabled = true;
			var authored = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
			tick.sprite = authored;
			var toggle = go.GetComponent<Toggle>();
			toggle.targetGraphic = face;
			toggle.graphic = tick;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				Tokens(("control_bg", "#292A2EFF"), ("accent", "#F2CA50FF"), ("success", "#7BC96FFF")),
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ThemeCheckboxToggle(
				toggle, SpzUiThemeOps.Active.controlBg, SpzUiThemeOps.Active.accent, SpzUiThemeOps.Active.success);

			Assert.That(tick.enabled, Is.True);
			Assert.That(ReferenceEquals(tick.sprite, authored), Is.True);
			Assert.That(tick.color, Is.EqualTo(SpzUiThemeOps.Active.success));
		}
		finally {
			UnityEngine.Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ApplyControlLineIconDoesNotHideToggleCheckmark() {
		var root = new GameObject("LineIconCheckPreserve", typeof(RectTransform));
		root.SetActive(false);
		try {
			var go = new GameObject("Toggle", typeof(RectTransform), typeof(Image), typeof(Toggle));
			go.transform.SetParent(root.transform, false);
			var face = go.GetComponent<Image>();
			var tickGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
			tickGo.transform.SetParent(go.transform, false);
			var tick = tickGo.GetComponent<Image>();
			tick.enabled = true;
			var toggle = go.GetComponent<Toggle>();
			toggle.targetGraphic = face;
			toggle.graphic = tick;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				Tokens(("icon_tint", "#D0C5AFFF")),
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ApplyControlLineIcon(go.transform, StudioLineIcon.Cursor, 18f);
			Assert.That(tick.enabled, Is.True, "line-icon path must not disable Toggle.graphic Checkmark");
		}
		finally {
			UnityEngine.Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
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
		Assert.That(CommandRibbon_UI.PrettifyStripTabTitle("controlnet"), Is.EqualTo("Control"));
		Assert.That(CommandRibbon_UI.PrettifyStripTabTitle("art list"), Is.EqualTo("Art"));
		Assert.That(CommandRibbon_UI.PrettifyStripTabLabel("ctrl"), Is.EqualTo("Control"));
		Assert.That(CommandRibbon_UI.PrettifyStripTabLabel("ART (BG)"), Is.EqualTo("Art BG"));
		Assert.That(CommandRibbon_UI.PrettifyStripTabLabel("ART"), Is.EqualTo("Art"));
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
	public void BoundChromeTmpRestoresDesignFontSizeWhenLeavingTheme() {
		var go = new GameObject("NomadFontScaleRestore");
		var tmp = go.AddComponent<TextMeshProUGUI>();
		tmp.fontSize = 20f;
		try {
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				Tokens(("text_primary", "#E3E2E7FF"), ("font_scale", "1.25")),
				"replace",
				out string error), Is.True, error);
			SpzUiThemeOps.ApplyBoundChromeTmp(tmp, SpzUiThemeOps.Active.textPrimary, 20f);
			Assert.That(tmp.fontSize, Is.EqualTo(25f).Within(0.05f));

			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.ApplyBoundChromeTmp(tmp, Color.white, 20f);
			Assert.That(tmp.fontSize, Is.EqualTo(20f).Within(0.05f));
			Assert.That(tmp.characterSpacing, Is.EqualTo(0f).Within(0.01f));
		} finally {
			UnityEngine.Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void BoundChromeTmpAppliesNomadTrackingAndControlLineIconRestores() {
		var go = new GameObject("NomadChromeTmp");
		var owner = new GameObject("ToolOwner", typeof(RectTransform));
		owner.transform.SetParent(go.transform, false);
		var authoredIcon = new GameObject("icon", typeof(RectTransform));
		authoredIcon.transform.SetParent(owner.transform, false);
		var authoredImg = authoredIcon.AddComponent<Image>();
		authoredImg.enabled = true;
		var tmpGo = new GameObject("Label", typeof(RectTransform));
		tmpGo.transform.SetParent(go.transform, false);
		var tmp = tmpGo.AddComponent<TextMeshProUGUI>();
		tmp.characterSpacing = 0f;
		tmp.fontSize = 14f;
		try {
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				Tokens(("text_primary", "#E3E2E7FF"), ("icon_tint", "#D0C5AFFF")),
				"replace",
				out string error), Is.True, error);
			SpzUiThemeOps.ApplyBoundChromeTmp(tmp, SpzUiThemeOps.Active.textPrimary);
			Assert.That(tmp.characterSpacing, Is.EqualTo(10f).Within(0.01f));
			SpzUiThemeOps.ApplyControlLineIcon(owner.transform, StudioLineIcon.Brush, 22f);
			Assert.That(authoredImg.enabled, Is.False);
			Transform line = owner.transform.Find("MonolithLineIcon");
			Assert.That(line, Is.Not.Null);
			Assert.That(line.gameObject.activeSelf, Is.True);
			Assert.That(line.GetComponent<Image>().sprite,
				Is.EqualTo(UiRuntimeSprites.GetLineIcon(StudioLineIcon.Brush)));

			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.ApplyBoundChromeTmp(tmp, Color.white);
			SpzUiThemeOps.ApplyControlLineIcon(owner.transform, StudioLineIcon.Brush, 22f);
			Assert.That(tmp.characterSpacing, Is.EqualTo(0f).Within(0.01f));
			Assert.That(line.gameObject.activeSelf, Is.False);
			Assert.That(authoredImg.enabled, Is.True);

			// Leave→re-Apply must reuse the inactive MonolithLineIcon (Transform.Find cannot see it).
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				Tokens(("text_primary", "#E3E2E7FF"), ("icon_tint", "#D0C5AFFF")),
				"replace",
				out error), Is.True, error);
			SpzUiThemeOps.ApplyControlLineIcon(owner.transform, StudioLineIcon.Eraser, 22f);
			int monolithCount = 0;
			for (int i = 0; i < owner.transform.childCount; i++) {
				if (owner.transform.GetChild(i).name == "MonolithLineIcon")
					monolithCount++;
			}
			Assert.That(monolithCount, Is.EqualTo(1));
			Assert.That(line.gameObject.activeSelf, Is.True);
		Assert.That(line.GetComponent<Image>().sprite,
			Is.EqualTo(UiRuntimeSprites.GetLineIcon(StudioLineIcon.Eraser)));
		} finally {
			UnityEngine.Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void EnsureCircleDialSquareLayout_PreservesTallHolderPreferredHeight() {
		var go = new GameObject("BrushSizeHolder", typeof(RectTransform), typeof(LayoutElement));
		try {
			var le = go.GetComponent<LayoutElement>();
			le.preferredWidth = -1f;
			le.minHeight = 85f;
			le.preferredHeight = 85f;
			le.flexibleWidth = 1f;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				Tokens(("accent", "#F2CA50FF"), ("control_bg", "#292A2EFF")),
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.EnsureCircleDialSquareLayout(le);
			Assert.That(le.preferredHeight, Is.EqualTo(85f).Within(0.01f),
				"Tall brush-size holder must not squash to a square (keeps 'size' caption band)");
			Assert.That(le.minHeight, Is.EqualTo(85f).Within(0.01f));
		} finally {
			UnityEngine.Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void BoundChromeDialValueTmpZeroesCharacterSpacingUnderNomad() {
		var go = new GameObject("NomadDialValue");
		var tmp = go.AddComponent<TextMeshProUGUI>();
		tmp.characterSpacing = 0f;
		tmp.fontSize = 16f;
		try {
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				Tokens(("text_primary", "#E3E2E7FF")),
				"replace",
				out string error), Is.True, error);
			SpzUiThemeOps.ApplyBoundChromeDialValueTmp(tmp, SpzUiThemeOps.Active.textPrimary, 16f);
			Assert.That(tmp.characterSpacing, Is.EqualTo(0f).Within(0.01f));
			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.ApplyBoundChromeDialValueTmp(tmp, Color.white, 16f);
			Assert.That(tmp.characterSpacing, Is.EqualTo(0f).Within(0.01f));
		} finally {
			UnityEngine.Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void BoundChromeReadableBodyTmpKeepsWrapAndZeroTrackingUnderNomad() {
		var go = new GameObject("NomadReadableBody");
		var tmp = go.AddComponent<TextMeshProUGUI>();
		tmp.text = "IP Adapter:\nsd1.5, sdxl (h94)";
		tmp.characterSpacing = 0f;
		tmp.lineSpacing = -20f;
		tmp.enableWordWrapping = true;
		tmp.overflowMode = TextOverflowModes.Overflow;
		tmp.fontStyle = FontStyles.Normal;
		tmp.fontSize = 14f;
		try {
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				Tokens(("text_primary", "#E3E2E7FF")),
				"replace",
				out string error), Is.True, error);
			SpzUiThemeOps.ApplyBoundChromeReadableBodyTmp(tmp, SpzUiThemeOps.Active.textPrimary, 14f);
			Assert.That(tmp.characterSpacing, Is.EqualTo(0f).Within(0.01f));
			Assert.That(tmp.enableWordWrapping, Is.True);
			Assert.That(tmp.overflowMode, Is.EqualTo(TextOverflowModes.Overflow));
			Assert.That(tmp.fontStyle & FontStyles.UpperCase, Is.EqualTo((FontStyles)0));
			Assert.That(tmp.lineSpacing, Is.GreaterThanOrEqualTo(-6.01f));
			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.ApplyBoundChromeReadableBodyTmp(tmp, Color.white, 14f);
			Assert.That(tmp.characterSpacing, Is.EqualTo(0f).Within(0.01f));
			Assert.That(tmp.lineSpacing, Is.EqualTo(-20f).Within(0.01f));
		} finally {
			UnityEngine.Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void FindDirectChildIncludingInactiveFindsDeactivatedMonolith() {
		var parent = new GameObject("StripCell", typeof(RectTransform));
		var child = new GameObject("MonolithLineIcon", typeof(RectTransform));
		child.transform.SetParent(parent.transform, false);
		child.SetActive(false);
		try {
			// Unity 6 Transform.Find may include inactive; helper must still resolve them.
			Transform found = SpzUiThemeOps.FindDirectChildIncludingInactive(parent.transform, "MonolithLineIcon");
			Assert.That(found, Is.Not.Null);
			Assert.That(found.gameObject.activeSelf, Is.False);
			Assert.That(found.name, Is.EqualTo("MonolithLineIcon"));
		} finally {
			UnityEngine.Object.DestroyImmediate(parent);
		}
	}

	[Test]
	public void NomadVerticalSliderChromeUsesSegmentTileAndBullseye() {
		var root = new GameObject("NomadSlider", typeof(RectTransform));
		var bgGo = new GameObject("Background", typeof(RectTransform));
		bgGo.transform.SetParent(root.transform, false);
		var bg = bgGo.AddComponent<Image>();
		var fillArea = new GameObject("Fill Area", typeof(RectTransform));
		fillArea.transform.SetParent(root.transform, false);
		var fillGo = new GameObject("Fill", typeof(RectTransform));
		fillGo.transform.SetParent(fillArea.transform, false);
		var fill = fillGo.AddComponent<Image>();
		var handleSlide = new GameObject("Handle Slide Area", typeof(RectTransform));
		handleSlide.transform.SetParent(root.transform, false);
		var handleGo = new GameObject("Handle", typeof(RectTransform));
		handleGo.transform.SetParent(handleSlide.transform, false);
		var handle = handleGo.AddComponent<Image>();
		var slider = root.AddComponent<Slider>();
		slider.targetGraphic = bg;
		slider.fillRect = fill.rectTransform;
		slider.handleRect = handle.rectTransform;
		slider.direction = Slider.Direction.BottomToTop;
		try {
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				Tokens(("field_bg", "#121317FF"), ("danger", "#FFB4ABFF"), ("icon_tint", "#D0C5AFFF")),
				"replace",
				out string error), Is.True, error);
			SpzUiThemeOps.ApplyNomadSliderChrome(slider);
			Assert.That(fill.sprite, Is.EqualTo(UiRuntimeSprites.NomadSliderSegmentTile));
			Assert.That(fill.type, Is.EqualTo(Image.Type.Tiled));
			Assert.That(handle.sprite, Is.EqualTo(UiRuntimeSprites.GetLineIcon(StudioLineIcon.Bullseye)));
			Assert.That(UiRuntimeSprites.IsNomadSliderSegmentTile(fill.sprite), Is.True);

			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.ApplyNomadSliderChrome(slider);
			Assert.That(UiRuntimeSprites.IsNomadSliderSegmentTile(fill.sprite), Is.False);
		} finally {
			UnityEngine.Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void NomadFillThumbSliderChrome_UsesAccentFillCameraOverlayAndRestores() {
		var root = new GameObject("NomadFillThumb", typeof(RectTransform));
		root.transform.localEulerAngles = new Vector3(0f, 0f, 90f);
		var bgGo = new GameObject("Background", typeof(RectTransform));
		bgGo.transform.SetParent(root.transform, false);
		var bg = bgGo.AddComponent<Image>();
		Color authoredBg = new Color(0.2f, 0.2f, 0.25f, 1f);
		bg.color = authoredBg;
		var fillArea = new GameObject("Fill Area", typeof(RectTransform));
		fillArea.transform.SetParent(root.transform, false);
		var fillGo = new GameObject("Fill", typeof(RectTransform));
		fillGo.transform.SetParent(fillArea.transform, false);
		var fill = fillGo.AddComponent<Image>();
		var handleSlide = new GameObject("Handle Slide Area", typeof(RectTransform));
		handleSlide.transform.SetParent(root.transform, false);
		var handleGo = new GameObject("Handle", typeof(RectTransform));
		handleGo.transform.SetParent(handleSlide.transform, false);
		var handle = handleGo.AddComponent<Image>();
		handle.color = Color.white;
		var slider = root.AddComponent<Slider>();
		slider.targetGraphic = handle; // FOV prefab wires handle as targetGraphic
		slider.fillRect = fill.rectTransform;
		slider.handleRect = handle.rectTransform;
		slider.direction = Slider.Direction.LeftToRight;
		var marker = root.AddComponent<SpzUiThemeNomadFillThumb>();
		marker.icon = StudioLineIcon.Camera;
		try {
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				Tokens(
					("field_bg", "#121317FF"),
					("accent", "#F2CA50FF"),
					("handle", "#CCCCCCFF"),
					("icon_tint", "#D0C5AFFF")),
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ApplyNomadSliderChrome(slider);

			Assert.That(bg.color, Is.EqualTo(SpzUiThemeOps.Active.fieldBg));
			Assert.That(fill.sprite, Is.EqualTo(UiRuntimeSprites.SolidRect));
			Assert.That(fill.color, Is.EqualTo(SpzUiThemeOps.Active.accent));
			Assert.That(UiRuntimeSprites.IsNomadSliderSegmentTile(fill.sprite), Is.False);
			Assert.That(handle.color.a, Is.EqualTo(0f).Within(0.01f));

			Transform overlay = SpzUiThemeOps.FindDirectChildIncludingInactive(fill.rectTransform, "NomadFillThumbOverlay");
			Assert.That(overlay, Is.Not.Null);
			Assert.That(overlay.gameObject.activeSelf, Is.True);
			var overlayImg = overlay.GetComponent<Image>();
			Assert.That(overlayImg, Is.Not.Null);
			Assert.That(overlayImg.sprite, Is.EqualTo(UiRuntimeSprites.CircleFilled));

			Transform icon = SpzUiThemeOps.FindDirectChildIncludingInactive(fill.rectTransform, "MonolithLineIcon");
			Assert.That(icon, Is.Not.Null);
			Assert.That(icon.gameObject.activeSelf, Is.True);
			Assert.That(icon.GetComponent<Image>().sprite,
				Is.EqualTo(UiRuntimeSprites.GetLineIcon(StudioLineIcon.Camera)));
			Assert.That(Mathf.Abs(Mathf.DeltaAngle(icon.localEulerAngles.z, -90f)), Is.LessThan(1f));

			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.ApplyNomadSliderChrome(slider);
			Assert.That(overlay.gameObject.activeSelf, Is.False);
			Assert.That(icon.gameObject.activeSelf, Is.False);
			Assert.That(bg.color, Is.EqualTo(authoredBg));
		} finally {
			UnityEngine.Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void RestoreBoundChromeUnder_HidesMonolithActiveBar() {
		var root = new GameObject("ActiveBarRoot", typeof(RectTransform));
		try {
			var bar = new GameObject("MonolithActiveBar", typeof(RectTransform), typeof(Image));
			bar.transform.SetParent(root.transform, false);
			bar.SetActive(true);
			SpzUiThemeOps.RestoreBoundChromeUnder(root.transform);
			Assert.That(bar.activeSelf, Is.False,
				"Leave litmus: MonolithActiveBar must deactivate with RestoreBoundChromeUnder");
		} finally {
			UnityEngine.Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void ThemePromptPresetSquareCell_OnBuiltin_DoesNotInjectHitFace() {
		var go = new GameObject("PresetLeaveNoEnsure", typeof(RectTransform), typeof(Toggle));
		try {
			var toggle = go.GetComponent<Toggle>();
			toggle.targetGraphic = null;
			SpzUiThemeOps.ResetTheme();
			Assert.That(SpzUiThemeOps.ShouldRecolorBoundChrome, Is.False);
			SpzUiThemeOps.ThemePromptPresetSquareCell(toggle, Color.black, Color.yellow);
			Assert.That(toggle.targetGraphic, Is.Null);
			Assert.That(go.transform.Find("BoundChromeHitFace"), Is.Null);
		} finally {
			UnityEngine.Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void RestoreBoundChromeUnder_RemovesSyntheticHitFace() {
		var root = new GameObject("SyntheticHitRoot", typeof(RectTransform), typeof(Button));
		try {
			var btn = root.GetComponent<Button>();
			btn.targetGraphic = null;
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject { ["accent"] = "#F2CA50FF", ["control_bg"] = "#292A2EFF" },
				"replace",
				out string error), Is.True, error);
			SpzUiThemeOps.ApplyBoundChromeSelectable(btn, SpzUiThemeOps.Active.controlBg, SpzUiThemeOps.Active.accent);
			Assert.That(btn.targetGraphic, Is.Not.Null);
			Assert.That(btn.targetGraphic.GetComponent<SpzUiThemeSyntheticHitFace>(), Is.Not.Null);

			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.RestoreBoundChromeUnder(root.transform);
			Assert.That(root.transform.Find("BoundChromeHitFace"), Is.Null);
			Assert.That(btn.targetGraphic, Is.Null);
		} finally {
			UnityEngine.Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ApplyBoundChromeSelectable_OnBuiltin_DoesNotInjectHitFace() {
		var go = new GameObject("LeaveNoEnsure", typeof(RectTransform), typeof(Button));
		try {
			var btn = go.GetComponent<Button>();
			btn.targetGraphic = null;
			SpzUiThemeOps.ResetTheme();
			Assert.That(SpzUiThemeOps.ShouldRecolorBoundChrome, Is.False);
			SpzUiThemeOps.ApplyBoundChromeSelectable(btn, Color.black, Color.yellow);
			Assert.That(btn.targetGraphic, Is.Null,
				"Leave/builtin must not call EnsureSelectableHitFace (sticky BoundChromeHitFace poisons Restore SPZ)");
			Assert.That(go.transform.Find("BoundChromeHitFace"), Is.Null);
		} finally {
			UnityEngine.Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
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
				new JObject { ["spacing_scale"] = 1.5 },
				"replace",
				out string error), Is.True, error);
			SpzUiThemeOps.ApplyScaledLayoutGroup(vlg);
			Assert.That(vlg.spacing, Is.EqualTo(12f).Within(0.01f));

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

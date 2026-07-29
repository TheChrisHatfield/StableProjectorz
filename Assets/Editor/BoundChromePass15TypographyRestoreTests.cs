using System.IO;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pass 15: Restore SPZ must unwind Nomad TMP font/outline without garbled glyphs.
/// Litmus: Apply Nomad → Restore SPZ → labels readable (font+material paired, tracking reset).
/// </summary>
public sealed class BoundChromePass15TypographyRestoreTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void RestoreNomadTypography_SourceRestoresFontBeforeOutline() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath, "_gm/Features/AddonSystem/SpzUiThemeOps.cs"));
		string src = File.ReadAllText(path);
		int restore = src.IndexOf("static void RestoreNomadTypography", System.StringComparison.Ordinal);
		Assert.That(restore, Is.GreaterThan(0));
		string body = src.Substring(restore, System.Math.Min(1200, src.Length - restore));
		int fontAssign = body.IndexOf("text.font = tag.authoredFont", System.StringComparison.Ordinal);
		int outline = body.IndexOf("TrySetNomadOutline", System.StringComparison.Ordinal);
		Assert.That(fontAssign, Is.GreaterThan(0));
		Assert.That(outline, Is.GreaterThan(fontAssign),
			"outlineWidth before font restore rebinds wrong atlas (garbled TMP)");
		Assert.That(body, Does.Contain("ForceMeshUpdate"));
		Assert.That(body, Does.Contain("UpdateMeshPadding"));
	}

	[Test]
	public void RestoreBoundChrome_UnwindsNomadTrackingAndFont() {
		var go = new GameObject("Pass15TmpRestore", typeof(RectTransform), typeof(TextMeshProUGUI));
		try {
			var tmp = go.GetComponent<TextMeshProUGUI>();
			tmp.text = "CFG Scale";
			tmp.characterSpacing = 0f;
			tmp.outlineWidth = 0f;
			var authoredFont = tmp.font;
			var authoredMat = tmp.fontSharedMaterial;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"pass15-typo",
				"{\"accent\":\"#F2CA50FF\",\"panel_bg\":\"#1E1F23F2\",\"control_bg\":\"#2A2B30FF\",\"text_primary\":\"#FFFFFFFF\",\"font_scale\":1.05}",
				out _), Is.True);
			Assert.That(SpzUiThemeOps.ShouldRecolorBoundChrome, Is.True);

			SpzUiThemeOps.ApplyBoundChromeTmp(tmp, SpzUiThemeOps.Active.textPrimary, 14f);
			Assert.That(tmp.characterSpacing, Is.GreaterThan(1f), "Nomad open tracking should apply");

			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.RestoreBoundChromeUnder(go.transform);

			Assert.That(tmp.characterSpacing, Is.EqualTo(0f).Within(0.01f));
			Assert.That(tmp.outlineWidth, Is.EqualTo(0f).Within(0.001f));
			if (authoredFont != null)
				Assert.That(tmp.font, Is.SameAs(authoredFont));
			if (authoredMat != null)
				Assert.That(tmp.fontSharedMaterial, Is.SameAs(authoredMat));
		} finally {
			Object.DestroyImmediate(go);
		}
	}

	[Test]
	public void TrySetNomadOutline_SourceUsesFontMaterialInstance() {
		string src = File.ReadAllText(Path.GetFullPath(Path.Combine(
			Application.dataPath, "_gm/Features/AddonSystem/SpzUiThemeOps.cs")));
		int fn = src.IndexOf("static void TrySetNomadOutline", System.StringComparison.Ordinal);
		Assert.That(fn, Is.GreaterThan(0));
		string body = src.Substring(fn, System.Math.Min(700, src.Length - fn));
		Assert.That(body, Does.Contain("fontMaterial"),
			"Outline must write a per-text instance, not pollute shared SDF materials");
	}
}

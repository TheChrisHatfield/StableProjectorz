using System.IO;
using NUnit.Framework;

/// <summary>
/// Mode click must not re-run full Nomad stack/font/layout apply — that made whole-ribbon aesthetics
/// jump after load when ThemeChanged already painted (or when first click finally completed the stack).
/// </summary>
public sealed class WorkflowRibbonModeClickSelectionOnlyContractTests {

	static string ReadSrc() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "WorkflowToolsRibbon SD", "WorkflowRibbon_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		return File.ReadAllText(path);
	}

	[Test]
	public void SetCurrentMode_RefreshesSelectionOnlyNotFullApplyThemeTokens() {
		string src = ReadSrc();
		int setMode = src.IndexOf("public void Set_CurrentMode(", System.StringComparison.Ordinal);
		Assert.That(setMode, Is.GreaterThan(0));
		int next = src.IndexOf("void OnToggle_ValueChanged(", setMode + 1, System.StringComparison.Ordinal);
		Assert.That(next, Is.GreaterThan(setMode));
		string body = src.Substring(setMode, next - setMode);
		Assert.That(body, Does.Contain("RefreshModeSelectionChromeOnly()"));
		Assert.That(body, Does.Not.Contain("ApplyThemeTokens()"),
			"Set_CurrentMode must not rebuild Roboto/stack aesthetics on every PROJ MASK/COLOR click");
	}

	[Test]
	public void OnToggle_DoesNotCallFullApplyThemeTokens() {
		string src = ReadSrc();
		int i = src.IndexOf("void OnToggle_ValueChanged(", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		int end = src.IndexOf("WorkflowRibbon_CurrMode GetMode_from_Toggle(", i + 1, System.StringComparison.Ordinal);
		string body = src.Substring(i, end - i);
		Assert.That(body, Does.Contain("if (_isSettingCurrentMode) return"));
		Assert.That(body, Does.Not.Contain("ApplyThemeTokens()"));
	}

	[Test]
	public void ThemeModeToggle_SupportsSelectionChromeOnlyEarlyReturn() {
		string src = ReadSrc();
		Assert.That(src, Does.Contain("bool selectionChromeOnly = false"));
		Assert.That(src, Does.Contain("if (selectionChromeOnly)"));
		Assert.That(src, Does.Contain("void OnEnable()"));
		Assert.That(src, Does.Contain("RefreshModeSelectionChromeOnly()"));
	}

	[Test]
	public void SelectionOnlyPath_SkipsStackedToolCell() {
		string src = ReadSrc();
		int i = src.IndexOf("static void ThemeModeToggle(", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i, System.Math.Min(3500, src.Length - i));
		int early = body.IndexOf("if (selectionChromeOnly)", System.StringComparison.Ordinal);
		int stack = body.IndexOf("ApplyNomadStackedToolCell(", System.StringComparison.Ordinal);
		Assert.That(early, Is.GreaterThan(0));
		Assert.That(stack, Is.GreaterThan(early),
			"selection-only must return before stacked cell rebuild so click does not restyle aesthetics");
	}
}

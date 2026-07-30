using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Theme-apply must not leave one-word Truncate labels until click retheme (PROJ vs PROJ MASK).
/// </summary>
public sealed class WorkflowRibbonThemeApplyOrderThemeTests {

	[Test]
	public void WorkflowRibbon_RebuildsHolderBeforeThemeModeToggles() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion",
			"WorkflowToolsRibbon SD", "WorkflowRibbon_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int applyIx = src.IndexOf("void ApplyThemeTokens()");
		Assert.That(applyIx, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(applyIx, System.Math.Min(2800, src.Length - applyIx));
		int rebuildIx = body.IndexOf("ForceRebuildLayoutImmediate(holderRt)");
		int firstModeIx = body.IndexOf("ThemeModeToggle(_projMasking");
		Assert.That(rebuildIx, Is.GreaterThanOrEqualTo(0));
		Assert.That(firstModeIx, Is.GreaterThanOrEqualTo(0));
		Assert.That(rebuildIx, Is.LessThan(firstModeIx),
			"Shell layout must rebuild before mode cells so wrap sees real cell height");
	}

	[Test]
	public void StackedWorkflowLabels_SkipStripLabelOutlinePath() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "SpzUiThemeOps.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int ix = src.IndexOf("public static void ApplyNomadStackedToolCell");
		Assert.That(ix, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(ix, System.Math.Min(3500, src.Length - ix));
		Assert.That(body, Does.Not.Contain("ApplyBoundChromeStripLabelTmp(tmp, labelColor"),
			"StripLabel outline 0.22 ghosted TOTAL OBJ / WHERE EMPTY after click");
		Assert.That(body, Does.Contain("TextOverflowModes.Overflow"));
	}
}

using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Path TextInputs alone must not count as a complete SPZ GO panel —
/// native fallback must still seed Import/Export action buttons.
/// </summary>
public sealed class SpzGoNativePanelSeedContractTests {

	[Test]
	public void EnsureNativeSpzGoPanel_RequiresActionButtons_NotJustTextInputs() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/AddonSystem/AddonUI_MGR.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int method = src.IndexOf("void EnsureNativeSpzGoPanel()", System.StringComparison.Ordinal);
		Assert.That(method, Is.GreaterThan(0));
		int next = src.IndexOf("void EnsureNativeNomadThemePanel()", method, System.StringComparison.Ordinal);
		Assert.That(next, Is.GreaterThan(method));
		string body = src.Substring(method, next - method);
		Assert.That(body, Does.Not.Contain("HasLiveAddonPanelWithWidgets(StableProjectorzGoAddonId)"),
			"TextInput-only panels must not bail via HasLiveAddonPanelWithWidgets.");
		Assert.That(body, Does.Contain("Button_Import"),
			"Live check / seed must require Import action button.");
		Assert.That(body, Does.Contain("Button_Export"),
			"Live check / seed must require Export action button.");
		Assert.That(body, Does.Contain("EnsureNativeSpzGoMissingWidgets"),
			"Incomplete panels must complete via EnsureNativeSpzGoMissingWidgets.");
	}
}

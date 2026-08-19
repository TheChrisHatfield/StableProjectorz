using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The native fallback must seed the whole multi-host shell, not a partial panel. A panel that
/// already carries one host section is still incomplete if the others are missing.
/// </summary>
public sealed class SpzGoNativePanelSeedContractTests {

	static string ReadAddonUiSource() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/AddonSystem/AddonUI_MGR.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		return File.ReadAllText(path);
	}

	static string ReadSeedBody(string src) {
		int method = src.IndexOf("void EnsureNativeSpzGoPanel()", System.StringComparison.Ordinal);
		Assert.That(method, Is.GreaterThan(0));
		int next = src.IndexOf("void EnsureNativeNomadThemePanel()", method, System.StringComparison.Ordinal);
		Assert.That(next, Is.GreaterThan(method));
		return src.Substring(method, next - method);
	}

	[Test]
	public void EnsureNativeSpzGoPanel_SeedsEveryRegisteredHostSection() {
		string body = ReadSeedBody(ReadAddonUiSource());
		Assert.That(body, Does.Not.Contain("HasLiveAddonPanelWithWidgets(StableProjectorzGoAddonId)"),
			"a partially built panel must not bail via HasLiveAddonPanelWithWidgets.");
		Assert.That(body, Does.Contain("foreach (var host in SpzGoHosts.All)"),
			"sections come from the host registry, so a new DCC needs no edit here.");
		Assert.That(body, Does.Contain("AddHostSection(StableProjectorzGoAddonId, panelId, host.Id)"));
		Assert.That(body, Does.Contain("SpzGoHostSection.SectionName(host.Id)"),
			"each host must be seeded only when its own section is missing.");
		Assert.That(body, Does.Contain("EnsureNativeSpzGoMissingWidgets"),
			"incomplete panels must complete via EnsureNativeSpzGoMissingWidgets.");
	}

	[Test]
	public void FlatPreSectionLayout_IsRetiredRatherThanLeftBesideTheSections() {
		string body = ReadSeedBody(ReadAddonUiSource());
		// A panel seeded by an older session keeps its loose rows unless they are pruned, and those
		// rows carry Import/Export buttons that transfer without any mode selected.
		Assert.That(body, Does.Contain("RetireFlatSpzGoLayout"));
		Assert.That(body, Does.Contain("RemovePanelRootNamedControls"),
			"only panel-root flat rows are pruned — tree-wide RemoveNamedControls would delete section widgets");
		Assert.That(body, Does.Contain("panel, \"Button_\")"));
		Assert.That(body, Does.Contain("panel, \"TextInput_\")"));
		Assert.That(body, Does.Contain("panel, \"Dropdown_\")"));
		Assert.That(body, Does.Contain("EnsureSpzGoPanelScroll"),
			"multi-host shell must scroll when Settings overflow the ribbon (R3e)");
	}

	[Test]
	public void ImportIsNotWiredToAGenericPathLoadOnTheFace() {
		string sections = File.ReadAllText(Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/AddonSystem/AddonUI_MGR.SpzGoSections.cs")));
		// R9: Import means the host hands SPZ its model. Nothing on the section face may route to
		// do_import_from_path, which loads whatever file sits at a typed path.
		Assert.That(sections, Does.Not.Contain("do_import_from_path"));
		Assert.That(sections, Does.Contain("SpzGoRequestImportFromHost"));
	}
}

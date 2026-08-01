using NUnit.Framework;
using spz;

/// <summary>
/// forge-neo-swap Phase B / R3: Neo has no /controlnet/settings — unit count must not stay 0.
/// </summary>
public sealed class ForgeNeoSwapCtrlNetUnitCountTests {

	[Test]
	public void Resolve_PrefersSettingsWhenPositive() {
		Assert.That(
			SD_ControlNetsList_UI.ResolveCtrlNetUnitCount(settingsOk: true, settingsUnits: 4, sysinfoUnits: 2, existingUnits: 1),
			Is.EqualTo(4));
	}

	[Test]
	public void Resolve_SettingsZeroFallsThroughToSysinfo() {
		Assert.That(
			SD_ControlNetsList_UI.ResolveCtrlNetUnitCount(settingsOk: true, settingsUnits: 0, sysinfoUnits: 5, existingUnits: 0),
			Is.EqualTo(5));
	}

	[Test]
	public void Resolve_SettingsMissingUsesSysinfo() {
		Assert.That(
			SD_ControlNetsList_UI.ResolveCtrlNetUnitCount(settingsOk: false, settingsUnits: 0, sysinfoUnits: 3, existingUnits: 0),
			Is.EqualTo(3));
	}

	[Test]
	public void Resolve_KeepsExistingWhenSysinfoStillZero() {
		Assert.That(
			SD_ControlNetsList_UI.ResolveCtrlNetUnitCount(settingsOk: false, settingsUnits: 0, sysinfoUnits: 0, existingUnits: 2),
			Is.EqualTo(2));
	}

	[Test]
	public void Resolve_UsesNeoDefaultWhenUnknown() {
		Assert.That(SD_ControlNetsList_UI.DefaultCtrlNetUnitCountWhenUnknown, Is.EqualTo(3));
		Assert.That(
			SD_ControlNetsList_UI.ResolveCtrlNetUnitCount(settingsOk: false, settingsUnits: 0, sysinfoUnits: 0, existingUnits: 0),
			Is.EqualTo(3),
			"Neo settings-404 + empty sysinfo must not leave Gen Art with zero CN units.");
	}

	[Test]
	public void Source_DocumentsNeoSettingsExpectedMiss() {
		string path = System.IO.Path.Combine(
			System.IO.Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Controlnet", "SD_ControlNetsList_UI.cs");
		Assert.That(System.IO.File.Exists(path), Is.True);
		string src = System.IO.File.ReadAllText(path);
		Assert.That(src, Does.Contain("ResolveCtrlNetUnitCount"));
		Assert.That(src, Does.Contain("DefaultCtrlNetUnitCountWhenUnknown"));
		Assert.That(src, Does.Contain("expected on Neo"));
	}
}

using System.IO;
using NUnit.Framework;

public sealed class SettingsCloseAndMultiPovNullSkipContractTests {

	[Test]
	public void SettingsClose_CommitsSoftIntegerFields_Source() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			""Assets"", ""_gm"", ""Features"", ""Settings"", ""Settings_MGR.cs"");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain(""CommitSoftSettingsIntegerFields""));
		Assert.That(src, Does.Contain(""CommitCurrentText()""));
		Assert.That(src, Does.Contain(""Settings:set_sdGpuDeviceId""));
	}

	[Test]
	public void MultiPOV_SetUvMasks_SkipsNullSlots_Source() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			""Assets"", ""_gm"", ""Features"", ""Camera"", ""Projections"", ""ProjectorCameras_RenderHelper.cs"");
		string src = File.ReadAllText(path);
		int i = src.IndexOf(""void MultiPOV_Set_UvMasks("", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i, System.Math.Min(900, src.Length - i));
		Assert.That(body, Does.Contain(""if (brush == null || vis == null) continue""));
		Assert.That(body, Does.Contain(""_ObjectUV_brushedMaskR8.Count""));
	}
}

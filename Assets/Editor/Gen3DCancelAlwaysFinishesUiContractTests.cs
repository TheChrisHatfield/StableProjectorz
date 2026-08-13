using System.IO;
using NUnit.Framework;

/// <summary>
/// Gen3D cancel must always finish GenerateButtons UI even if Gen3D_API.instance is null.
/// </summary>
public sealed class Gen3DCancelAlwaysFinishesUiContractTests {

	[Test]
	public void Gen_OnCancel_UsesFinallyForFinishedGenerate() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Generate", "Gen3D_MGR.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		int start = src.IndexOf("void Gen_OnCancel()", System.StringComparison.Ordinal);
		Assert.That(start, Is.GreaterThanOrEqualTo(0));
		int end = src.IndexOf("void Gen_OnProgress(", start, System.StringComparison.Ordinal);
		string body = src.Substring(start, end - start);
		Assert.That(body, Does.Contain("finally"));
		Assert.That(body, Does.Contain("OnConfirmed_FinishedGenerate"));
		Assert.That(body, Does.Contain("api.isBusy"));
		Assert.That(body, Does.Contain("dim_gen_3d"));
	}
}

using System.IO;
using NUnit.Framework;

/// <summary>
/// Gen3D cancel must stay armed through COMPLETE + final mesh download (isBusy / Gen_OnCancel wiring).
/// </summary>
public sealed class Gen3DBusyIncludesDownloadContractTests {

	[Test]
	public void IsBusy_Source_GatesOnLiveCoroutinesNotStatusAlone() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Generate", "Gen3D_API.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("public bool isBusy", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(idx, System.Math.Min(280, src.Length - idx));
		Assert.That(body, Does.Contain("_gen_or_resume_crtn != null"));
		Assert.That(body, Does.Contain("_download_crtn != null"));
		Assert.That(body, Does.Not.Contain("TaskStatus.COMPLETE"),
			"COMPLETE-before-download must not clear isBusy");
	}

	[Test]
	public void Gen_OnCancel_Source_FinishesUiWhenBusyOrApiNullIn3dMode() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Generate", "Gen3D_MGR.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		int start = src.IndexOf("void Gen_OnCancel()", System.StringComparison.Ordinal);
		Assert.That(start, Is.GreaterThanOrEqualTo(0));
		int end = src.IndexOf("void Gen_OnProgress(", start, System.StringComparison.Ordinal);
		string body = src.Substring(start, end - start);
		Assert.That(body, Does.Contain("api.isBusy"));
		Assert.That(body, Does.Contain("finally"));
		Assert.That(body, Does.Contain("OnConfirmed_FinishedGenerate"));
		Assert.That(body, Does.Contain("dim_gen_3d"),
			"null-API unwind must be scoped to 3D mode so SD cancel is not stolen");
	}

	[Test]
	public void Trigger3DGeneration_Source_RefusesWhileGenerateButtonsBusy() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Generate", "Gen3D_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public bool Trigger3DGeneration()", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(i, System.Math.Min(900, src.Length - i));
		Assert.That(body, Does.Contain("GenerateButtons_UI.isGenerating"));
		Assert.That(body, Does.Contain("return false"));
		Assert.That(body, Does.Contain("Gen3D_API.instance == null"),
			"Must refuse before StartedGenerate when API is missing.");
		Assert.That(body, Does.Contain("isBusy"),
			"Must return isBusy honesty — not always true after StartGeneration.");
	}
}

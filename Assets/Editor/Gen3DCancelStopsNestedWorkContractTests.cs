using System.IO;
using NUnit.Framework;

/// <summary>
/// Gen3D cancel must stop nested submit/poll/download so mesh is not imported after cancel.
/// </summary>
public sealed class Gen3DCancelStopsNestedWorkContractTests {

	[Test]
	public void CancelGeneration_StopsProgressSubmitDownloadAndGuardsCallbacks() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Generate", "Gen3D_API.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("_cancelRequested"));
		Assert.That(src, Does.Contain("_submit_crtn"));
		Assert.That(src, Does.Contain("_download_crtn"));
		int cancel = src.IndexOf("public void CancelGeneration()", System.StringComparison.Ordinal);
		int start = src.IndexOf("public void StartGeneration(", cancel, System.StringComparison.Ordinal);
		string body = src.Substring(cancel, start - cancel);
		Assert.That(body, Does.Contain("StopCoroutine(_progress_crtn)"));
		Assert.That(body, Does.Contain("StopCoroutine(_submit_crtn)"));
		Assert.That(body, Does.Contain("StopCoroutine(_download_crtn)"));
		Assert.That(body, Does.Contain("_generateStatus = TaskStatus.FAILED"));
		Assert.That(src, Does.Contain("if (_cancelRequested)"));
	}
}

using System.IO;
using NUnit.Framework;

/// <summary>
/// CtrlDetect used a bare StartCoroutine (not tracked) and cleared busy only on happy paths.
/// Parse throw left response==null &amp;&amp; !isError → wait forever; Cancel could not StopCoroutine it.
/// </summary>
public sealed class SdCtrlDetectBusyFinallyContractTests {

	static string ReadSrc() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_GenRequests_Helper.cs");
		Assert.That(File.Exists(path), Is.True, path);
		return File.ReadAllText(path);
	}

	[Test]
	public void CtrlDetect_TracksActiveCoroutineAndClearsBusyInFinally() {
		string src = ReadSrc();
		int submit = src.IndexOf("public void Submit_CtrlnetDetectRequest(", System.StringComparison.Ordinal);
		Assert.That(submit, Is.GreaterThan(0));
		string submitBody = src.Substring(submit, System.Math.Min(450, src.Length - submit));
		Assert.That(submitBody, Does.Contain("_activeRequestCrtn = StartCoroutine( Submit_CtrlDetect_crtn"));

		int crtn = src.IndexOf("IEnumerator Submit_CtrlDetect_crtn(", System.StringComparison.Ordinal);
		Assert.That(crtn, Is.GreaterThan(0));
		string body = src.Substring(crtn, System.Math.Min(3200, src.Length - crtn));
		Assert.That(body, Does.Contain("finally"));
		Assert.That(body, Does.Contain("_isGeneratingWhat = Generate_RequestingWhat.nothing"));
		Assert.That(body, Does.Contain("catch (Exception"));
		Assert.That(body, Does.Contain("_activeRequestCrtn = null"));
	}
}

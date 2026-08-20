using System.IO;
using NUnit.Framework;

/// <summary>
/// Periodic SD catalogue polls create UnityWebRequest every few seconds. Without Dispose/finally,
/// native handles accumulate across a long session (and early yield-break after error leaked too).
/// </summary>
public sealed class SdCataloguePollUnityWebRequestDisposeContractTests {

	static string Read(params string[] parts) {
		string path = Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts));
		Assert.That(File.Exists(path), Is.True, path);
		return File.ReadAllText(path);
	}

	static void AssertFetchDisposes(string src, string methodName) {
		int i = src.IndexOf(methodName, System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0), methodName);
		string window = src.Substring(i, System.Math.Min(1600, src.Length - i));
		Assert.That(window, Does.Contain("UnityWebRequest.Get"));
		Assert.That(window, Does.Contain("finally"), methodName + " must dispose even on yield break after error");
		Assert.That(window, Does.Contain("request.Dispose()"), methodName);
	}

	[Test]
	public void SamplersSchedulersSysInfo_DisposeRequests() {
		AssertFetchDisposes(
			Read("Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Samplers.cs"),
			"IEnumerator GetSamplers_crtn()");
		AssertFetchDisposes(
			Read("Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Scheduler.cs"),
			"IEnumerator GetSchedulers_crtn()");
		AssertFetchDisposes(
			Read("Assets", "_gm", "Features", "StableDiffusion", "SD_SysInfo_MGR.cs"),
			"IEnumerator FetchData_crtn(");
	}
}

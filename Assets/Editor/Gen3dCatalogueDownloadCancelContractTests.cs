using System.IO;
using NUnit.Framework;

/// <summary>
/// Catalogue row OnDisable used to StopCoroutine only. Download_MGR kept writing the zip, so
/// UpdateButtonStates treated a cancel as a successful install.
/// </summary>
public sealed class Gen3dCatalogueDownloadCancelContractTests {

	static string ReadSrc() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Generate", "Generators Catalogue UI",
			"Gen3D_CatalogueRow_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		return File.ReadAllText(path);
	}

	[Test]
	public void OnDisable_CancelsDownloadMgrAndDeletesPartialZip() {
		string src = ReadSrc();
		Assert.That(src, Does.Contain("_activeDownloadUrl"));
		Assert.That(src, Does.Contain("CancelActiveCatalogueDownload()"));
		Assert.That(src, Does.Contain("CancelDownload(url)"));
		Assert.That(src, Does.Contain("File.Delete(zipPath)"));

		int disable = src.IndexOf("void OnDisable()", System.StringComparison.Ordinal);
		Assert.That(disable, Is.GreaterThan(0));
		string body = src.Substring(disable, System.Math.Min(700, src.Length - disable));
		Assert.That(body, Does.Contain("CancelActiveCatalogueDownload()"),
			"disable must abort the shared Download_MGR request, not only the local coroutine");
	}
}

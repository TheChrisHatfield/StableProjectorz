using System.IO;
using NUnit.Framework;

public sealed class SpzGoLoadUpdatesLastSavePathTests {

	[Test]
	public void LoadProject_SetsLastSaveFilepath() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "ProjectSaveLoad_Helper.cs");
		string src = File.ReadAllText(path);
		int load = src.IndexOf("public void LoadProject(");
		int end = src.IndexOf("IEnumerator Save_FinalCompositeTexture_crtn", load);
		if (end < 0) end = src.Length;
		string body = src.Substring(load, end - load);
		Assert.That(body, Does.Contain("_last_saveFilepath = spzFilepath"),
			"Successful Load must update _last_saveFilepath for SPZ GO data_dir / exchange.");
	}
}

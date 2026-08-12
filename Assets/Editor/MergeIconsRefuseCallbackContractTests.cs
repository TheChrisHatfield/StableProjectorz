using System.IO;
using NUnit.Framework;

/// <summary>
/// MergeIcons refuse must invoke onHaveAlbedo(null) so GetTextures / retexture do not hang.
/// </summary>
public sealed class MergeIconsRefuseCallbackContractTests {

	[Test]
	public void MergeIcons_RefusePath_InvokesCallbackNull() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		int merge = src.IndexOf("public void MergeIcons(", System.StringComparison.Ordinal);
		int doSave = src.IndexOf("public void DoSaveProject()", merge, System.StringComparison.Ordinal);
		string body = src.Substring(merge, doSave - merge);
		Assert.That(body, Does.Contain("onHaveAlbedo?.Invoke(null)"));
	}

	[Test]
	public void GetTextures_NullAlbedo_ForwardsNull() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Icons", "IconUI_List_Art", "Art2D_IconsUI_List.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("albedoDict_withoutOwner == null"));
		Assert.That(src, Does.Contain("onReady_TexturesWithoutOwner?.Invoke(null)"));
	}
}

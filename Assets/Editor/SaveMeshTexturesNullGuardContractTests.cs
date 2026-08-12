using System.IO;
using NUnit.Framework;

public sealed class SaveMeshTexturesNullGuardContractTests {

	[Test]
	public void GetProjectionsDict_NullGuardsRendererAndDilation() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("void Get_ProjectionsDict(", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i, System.Math.Min(1600, src.Length - i));
		Assert.That(body, Does.Contain("Objects_Renderer_MGR.instance == null"));
		Assert.That(body, Does.Contain("TextureDilation_MGR.instance == null"));
	}

	[Test]
	public void SaveMeshTextures_FinallyInvokesOnComplete() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("void Save_Mesh_Textures(", System.StringComparison.Ordinal);
		string body = src.Substring(i, System.Math.Min(1200, src.Length - i));
		Assert.That(body, Does.Contain("finally"));
		Assert.That(body, Does.Contain("onComplete?.Invoke()"));
	}
}

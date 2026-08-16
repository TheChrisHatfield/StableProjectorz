using System;
using System.IO;
using NUnit.Framework;

/// <summary>
/// Guards the export texture path against missing managers and against leaving callers busy forever.
/// These anchor on Save_Mesh_Textures_crtn: the public Save_Mesh_Textures is only a coroutine launcher,
/// so matching on it would make every assertion below vacuous.
/// </summary>
public sealed class SaveMeshTexturesNullGuardContractTests {

	static string ReadSaveMgr() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		return File.ReadAllText(path);
	}

	[Test]
	public void TextureExport_NullGuardsRendererAndDilation() {
		string src = ReadSaveMgr();

		int albedo = src.IndexOf("RenderUdims TryGetAlbedoUdims()", StringComparison.Ordinal);
		Assert.That(albedo, Is.GreaterThan(0), "albedo lookup must live in a non-throwing helper");
		string albedoBody = src.Substring(albedo, Math.Min(700, src.Length - albedo));
		Assert.That(albedoBody, Does.Contain("Objects_Renderer_MGR.instance == null"));
		Assert.That(albedoBody, Does.Contain("albedo.texArray == null"),
			"a renderer with no accumulation texture must abort the export, not NRE");

		Assert.That(src, Does.Contain("isDilate && TextureDilation_MGR.instance != null"),
			"dilation must be skipped when the dilation manager is absent");
		Assert.That(src, Does.Contain("TextureDilation_MGR.instance == null"),
			"losing the dilation manager mid-wait must abort the wait");
	}

	[Test]
	public void SaveMeshTextures_FinallyInvokesOnComplete() {
		string src = ReadSaveMgr();
		int i = src.IndexOf("IEnumerator Save_Mesh_Textures_crtn(", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0), "the real work must live in the coroutine");
		string body = src.Substring(i);
		Assert.That(body, Does.Contain("finally"));
		Assert.That(body, Does.Contain("onComplete?.Invoke("),
			"every exit path (including yield break) must release the caller's busy flag");
		Assert.That(body, Does.Contain("onComplete?.Invoke(completedTextureStage)"),
			"completion must carry whether the texture stage actually ran, not just that it ended");
		int done = body.IndexOf("completedTextureStage = true;", StringComparison.Ordinal);
		int fin = body.IndexOf("} finally {", StringComparison.Ordinal);
		Assert.That(done, Is.GreaterThan(0).And.LessThan(fin),
			"the flag must be set at the end of the try body, so an early yield break reports false");
	}

	[Test]
	public void ExchangeExport_OnlyMarksItselfReadyWhenTexturesWereWritten() {
		// The .spz_go_ready sidecar is the other application's cue to auto-import "mesh + maps". The
		// texture stage can bail early (no accumulation textures) while the mesh write succeeded, and
		// stamping anyway hands the DCC an untextured mesh as a finished export.
		string src = ReadSaveMgr();
		int i = src.IndexOf("public bool Export3D_with_textures_ToPath(", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		int end = src.IndexOf("public static void TryDeleteSpzGoExchangeReadyStamp(", StringComparison.Ordinal);
		Assert.That(end, Is.GreaterThan(i));
		string body = src.Substring(i, end - i);

		Assert.That(body, Does.Contain("void OnComplete( bool texturesWritten )"),
			"the exchange completion must receive the texture-stage result");
		int guard = body.IndexOf("if( texturesWritten )", StringComparison.Ordinal);
		int stamp = body.IndexOf("TryWriteSpzGoExchangeReadyStamp(", StringComparison.Ordinal);
		Assert.That(guard, Is.GreaterThan(0), "the stamp must be conditional");
		Assert.That(stamp, Is.GreaterThan(guard), "the stamp must sit inside the success branch");
	}

	[Test]
	public void PublicEntry_IsOnlyACoroutineLauncher() {
		string src = ReadSaveMgr();
		int i = src.IndexOf("void Save_Mesh_Textures(", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		int crtn = src.IndexOf("IEnumerator Save_Mesh_Textures_crtn(", StringComparison.Ordinal);
		Assert.That(crtn, Is.GreaterThan(i),
			"if the wrapper ever regains a body, the window-based guards above must be re-pointed");
		string wrapper = src.Substring(i, crtn - i);
		Assert.That(wrapper, Does.Contain("StartCoroutine( Save_Mesh_Textures_crtn"));
	}
}

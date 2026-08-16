using System;
using System.IO;
using NUnit.Framework;

/// <summary>
/// TextureArray_to_Texture2DList binds a temporary RT as RenderTexture.active and reads slices back.
/// A throw inside that loop (allocation failure at 4K, unsupported format) used to leave active
/// pointing at a temp RT that never returned to the pool: later renders and readbacks then land on
/// the wrong target and export black or garbled textures. The budgeted sibling already documented
/// and guarded this, so the synchronous path must match.
/// </summary>
public sealed class TextureArrayReadbackActiveRtContractTests {

	static string ReadTools() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "TextureTools", "TextureTools_SPZ.cs");
		return File.ReadAllText(path);
	}

	static string SyncBlock(string src) {
		int i = src.IndexOf("TextureArray_to_Texture2DList(", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0), "the synchronous converter must exist");
		int end = src.IndexOf("TextureArray_to_Texture2DList_Budgeted", i, StringComparison.Ordinal);
		Assert.That(end, Is.GreaterThan(i), "anchor on the real block, not a fixed window");
		return src.Substring(i, end - i);
	}

	[Test]
	public void SyncReadbackRestoresActiveAndPoolsTempInFinally() {
		string body = SyncBlock(ReadTools());

		int active = body.IndexOf("RenderTexture.active = tempRT;", StringComparison.Ordinal);
		Assert.That(active, Is.GreaterThan(0), "the temp RT is bound as the active target");

		int tryIx = body.IndexOf("try {", active, StringComparison.Ordinal);
		Assert.That(tryIx, Is.GreaterThan(active),
			"everything after binding active must be guarded");

		int finallyIx = body.IndexOf("} finally {", tryIx, StringComparison.Ordinal);
		Assert.That(finallyIx, Is.GreaterThan(tryIx), "the guard needs a finally");

		string cleanup = body.Substring(finallyIx);
		Assert.That(cleanup, Does.Contain("RenderTexture.active = null;"),
			"active must be unbound even when a slice readback throws");
		Assert.That(cleanup, Does.Contain("RenderTexture.ReleaseTemporary(tempRT);"),
			"the temp RT must go back to the pool on every exit path");

		// The readback loop itself has to be inside the guard, not before it.
		int loop = body.IndexOf("for (int i=0; i<slices; i++)", StringComparison.Ordinal);
		Assert.That(loop, Is.GreaterThan(tryIx).And.LessThan(finallyIx),
			"the slice loop is the throwing part, so it must sit inside the try");
	}

	[Test]
	public void BudgetedSiblingStillGuardsToo() {
		// This fix mirrors the budgeted variant; if that regresses, the precedent is gone.
		string src = ReadTools();
		int i = src.IndexOf("TextureArray_to_Texture2DList_Budgeted", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i);
		int finallyIx = body.IndexOf("} finally {", StringComparison.Ordinal);
		Assert.That(finallyIx, Is.GreaterThan(0));
		string cleanup = body.Substring(finallyIx, Math.Min(220, body.Length - finallyIx));
		Assert.That(cleanup, Does.Contain("RenderTexture.active = null;"));
		Assert.That(cleanup, Does.Contain("RenderTexture.ReleaseTemporary(tempRT);"));
	}
}

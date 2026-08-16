using System;
using System.IO;
using NUnit.Framework;

/// <summary>
/// Batch of "busy forever / unreachable chrome" fixes:
/// - Gen3D Generate_crtn must clear _gen_or_resume_crtn in finally (isBusy keys off it).
/// - Addon_HttpServer must Close the listener if Start fails after bind.
/// - Paint Tool Options must create ToolOptionsRow when missing under a prefab section root,
///   not only when childCount &lt;= 1.
/// </summary>
public sealed class Gen3dHttpAndToolOptionsBusyContractTests {

	static string Read(params string[] parts) {
		string path = Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts));
		Assert.That(File.Exists(path), Is.True, $"missing source: {path}");
		return File.ReadAllText(path);
	}

	[Test]
	public void Gen3dGenerateClearsBusyHandleInFinally() {
		string src = Read("Assets", "_gm", "Features", "3D Generate", "Gen3D_API.cs");
		int i = src.IndexOf("IEnumerator Generate_crtn(", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		int end = src.IndexOf("IEnumerator GenerateSubmit_crtn(", i, StringComparison.Ordinal);
		Assert.That(end, Is.GreaterThan(i));
		string body = src.Substring(i, end - i);

		Assert.That(body, Does.Contain("try {"),
			"SerializeObject and the rest of the body must be guarded");
		int finallyIx = body.IndexOf("} finally {", StringComparison.Ordinal);
		Assert.That(finallyIx, Is.GreaterThan(0));
		string cleanup = body.Substring(finallyIx);
		Assert.That(cleanup, Does.Contain("_gen_or_resume_crtn = null;"),
			"isBusy must clear on every exit, including throws before the old bottom assignment");

		// Early yield breaks must not rely on a pre-finally null — the finally owns it.
		Assert.That(body, Does.Not.Contain("_gen_or_resume_crtn = null;\r\n\t            yield break;"));
		Assert.That(body, Does.Not.Contain("_gen_or_resume_crtn = null;\n\t            yield break;"));
	}

	[Test]
	public void HttpServerStartFailureClosesTheListener() {
		string src = Read("Assets", "_gm", "Features", "AddonSystem", "Addon_HttpServer.cs");
		int i = src.IndexOf("void StartServer()", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		int end = src.IndexOf("void StopServer()", i, StringComparison.Ordinal);
		Assert.That(end, Is.GreaterThan(i));
		string body = src.Substring(i, end - i);

		int katch = body.IndexOf("catch (Exception e)", StringComparison.Ordinal);
		Assert.That(katch, Is.GreaterThan(0));
		string failure = body.Substring(katch);
		Assert.That(failure, Does.Contain("_listener?.Close()").Or.Contain("_listener?.Stop()"),
			"an already-bound listener must be released or :5557 stays held");
		Assert.That(failure, Does.Contain("_isRunning = false;"));
		Assert.That(failure, Does.Contain("_listener = null;"));
	}

	[Test]
	public void ToolOptionsCreatesWhenRowMissingUnderPrefabSectionRoot() {
		string src = Read("Assets", "_gm", "Features", "Paint", "PaintTab", "PaintTab_CollectPaintUI.cs");

		Assert.That(src, Does.Contain("GetToolOptionsCreateParent("),
			"prefab section roots must resolve ScrollContent like Layers/Brush");
		Assert.That(src, Does.Contain("!HasRuntimeToolOptionsRow(_layout.ToolOptionsSection)"),
			"create must key off the missing row, not childCount alone");
		Assert.That(src, Does.Not.Contain("ToolOptionsSection.childCount <= 1"),
			"childCount<=1 refuses create when Header+Content already exist");

		int has = src.IndexOf("static bool HasRuntimeToolOptionsRow(", StringComparison.Ordinal);
		Assert.That(has, Is.GreaterThan(0));
		string body = src.Substring(has, Math.Min(1200, src.Length - has));
		Assert.That(body, Does.Contain("GetToolOptionsCreateParent("),
			"row detection must also look under Content/ScrollContent");
	}
}

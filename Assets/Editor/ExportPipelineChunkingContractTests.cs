using System;
using System.IO;
using NUnit.Framework;

// Source-contract for the export anti-freeze rework: the texture pipeline must run through a
// frame-budgeted coroutine (Thompson scheduler), dilate NON-instantly, read back per-slice under
// budget, encode off the main thread, and report progress to the viewport status bar.
public sealed class ExportPipelineChunkingContractTests {

	static string Read(params string[] parts) {
		string path = Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts));
		return File.ReadAllText(path);
	}

	[Test]
	public void SaveMgr_RunsTexturePipelineAsBudgetedCoroutine() {
		string src = Read("Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		Assert.That(src, Does.Contain("ExportFrameScheduler"), "must own an export frame-budget scheduler");
		Assert.That(src, Does.Contain("Save_Mesh_Textures_crtn"), "texture save must run as a coroutine");
		Assert.That(src, Does.Contain("StartCoroutine( Save_Mesh_Textures_crtn"),
			"the fire-and-forget entry must dispatch the coroutine");
	}

	[Test]
	public void SaveMgr_DilatesNonInstantly() {
		string src = Read("Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		Assert.That(src, Does.Contain("isRunInstantly = false"),
			"export dilation must be chunked across frames, not the old single-frame instant blit");
		Assert.That(src, Does.Not.Contain("dilationArg.isRunInstantly = true"),
			"the instant (freezing) dilation path must be gone from export");
	}

	[Test]
	public void SaveMgr_DilationWaitCannotHangExportForever() {
		string src = Read("Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		int loop = src.IndexOf("while (!dilateDone)", StringComparison.Ordinal);
		Assert.That(loop, Is.GreaterThan(0), "export must wait for the non-instant dilation callback");

		string body = src.Substring(loop, Math.Min(700, src.Length - loop));
		// Dillate completes on TextureDilation_MGR's own coroutine; if it throws or that manager is
		// disabled, the callback never fires. An unguarded wait strands _isSaving true forever.
		Assert.That(body, Does.Contain("break"),
			"the dilation wait must be escapable, or a missed callback locks out all future exports");
		Assert.That(body, Does.Contain("TextureDilation_MGR.instance == null"),
			"losing the dilation manager mid-wait must abort the wait");
		Assert.That(body, Does.Contain("dilateDeadline"),
			"the dilation wait needs a watchdog deadline");
		Assert.That(src, Does.Contain("DilationWatchdogSeconds"),
			"watchdog budget must be a named constant");
	}

	[Test]
	public void SaveMgr_ReadsBackBudgetedAndEncodesOffThread() {
		string src = Read("Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		Assert.That(src, Does.Contain("TextureArray_to_Texture2DList_Budgeted"),
			"UDIM readback must use the budgeted (per-frame) variant");
		Assert.That(src, Does.Contain("EncodeAndSaveTextures_crtn"), "encode must run as a coroutine");
		Assert.That(src, Does.Contain("RunEncodeJobToDisk"), "encode must be dispatched to the off-thread job runner");
		Assert.That(src, Does.Contain("Task.Run("), "encode+write must be offloaded to a worker thread");
	}

	[Test]
	public void SaveMgr_ReportsProgress() {
		string src = Read("Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		Assert.That(src, Does.Contain("ReportProgress"), "export must drive the progress bar");
		Assert.That(src, Does.Contain("Exporting textures"), "export must show a progress label");
		Assert.That(src, Does.Contain("SetProgressVisible(false)"), "progress bar must be hidden when export ends");
	}

	[Test]
	public void TextureTools_HasOffThreadEncodeAndBudgetedReadback() {
		string src = Read("Assets", "_gm", "Features", "TextureTools", "TextureTools_SPZ.cs");
		Assert.That(src, Does.Contain("CaptureEncodeJob"), "must snapshot raw pixels on the main thread");
		Assert.That(src, Does.Contain("RunEncodeJobToDisk"), "must expose a thread-safe encode+write");
		Assert.That(src, Does.Contain("EncodeArrayToPNG"), "off-thread encode must use raw-array ImageConversion");
		Assert.That(src, Does.Contain("TextureArray_to_Texture2DList_Budgeted"), "must expose budgeted UDIM readback");
	}

	[Test]
	public void StatusText_ExposesProgressToggle() {
		string src = Read("Assets", "_gm", "Features", "Viewport", "Main Viewport", "Viewport_StatusText.cs");
		Assert.That(src, Does.Contain("SetProgressVisible"), "status text must allow hiding just the progress bar");
	}

	[Test]
	public void FbxExport_CachesMeshArraysOnce() {
		string src = Read("Assets", "_gm", "Features", "3D Models", "ModelsHandler_SaveFBX_Helper.cs");
		Assert.That(src, Does.Contain("Vector3[] verts = mesh.Vertices"),
			"ExportMesh must cache Vertices once (property re-copies the full array every access)");
		Assert.That(src, Does.Contain("int[] tris = mesh.Triangles"),
			"ExportMesh must cache Triangles once");
		Assert.That(src, Does.Contain("Color32[] colors = mesh.VertexColors"),
			"ExportVertexColors must cache VertexColors once");
		Assert.That(src, Does.Contain("Vector2[] uvs = mesh.UV"),
			"ExportUVs must cache UV once");
	}
}

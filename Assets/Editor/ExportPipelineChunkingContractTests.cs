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
	public void BudgetedReadback_RebindsSharedMaterialEverySlice() {
		string src = Read("Assets", "_gm", "Features", "TextureTools", "TextureTools_SPZ.cs");
		int fn = src.IndexOf("void ReadBackSlice(int sliceIx)", StringComparison.Ordinal);
		Assert.That(fn, Is.GreaterThan(0), "budgeted readback must have a per-slice helper");
		int end = src.IndexOf("outList.Add(texture2D);", fn, StringComparison.Ordinal);
		Assert.That(end, Is.GreaterThan(fn));
		string body = src.Substring(fn, end - fn);

		// TextureArrayReadSlice_mat is shared global state and this loop yields between slices, so
		// any other texture-array read in between rebinds it and the rest of our slices would come
		// from the wrong source. Binding once before the loop is only correct for the single-frame
		// variant of this call.
		Assert.That(body, Does.Contain("mat.SetTexture(\"_MainTex\", textureArray)"),
			"each slice must re-bind the source array on the shared material");
		Assert.That(body, Does.Contain("RenderUdims.SetNumUdims"),
			"UDIM count must be re-applied per slice, not trusted across frames");
		Assert.That(body, Does.Contain("SAMPLER_POINT"),
			"sampler keywords must be re-applied per slice");

		int loop = src.IndexOf("IEnumerator TextureArray_to_Texture2DList_Budgeted", StringComparison.Ordinal);
		Assert.That(loop, Is.GreaterThan(0));
		string coroutine = src.Substring(loop, fn - loop);
		Assert.That(coroutine, Does.Contain("textureArray == null"),
			"a source array destroyed mid-export must stop the readback, not write garbage slices");
	}

	[Test]
	public void TexturePaths_DoNotMistakeAModelNameForAnImageFormat() {
		string src = Read("Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		int fn = src.IndexOf("static void SplitTexturePath(", StringComparison.Ordinal);
		Assert.That(fn, Is.GreaterThan(0),
			"both encoders must share one path split, or they drift apart again");
		int end = src.IndexOf("IEnumerator EncodeAndSaveTextures_crtn", fn, StringComparison.Ordinal);
		string body = src.Substring(fn, end - fn);

		// Export passes a base path whose MESH extension is already stripped, so "robot_v1.2.fbx"
		// arrives as "robot_v1.2". Reading ".2" as the image format makes every encoder refuse, and
		// the export still reports success and writes its ready stamp — with no textures on disk.
		Assert.That(body, Does.Contain(".png"));
		Assert.That(body, Does.Contain(".jpg"));
		Assert.That(body, Does.Contain(".tga"));
		Assert.That(body, Does.Contain("GetFileName(path)"),
			"an unrecognised extension must stay part of the name, not be cut off");
		Assert.That(body, Does.Contain("exten = \".png\""),
			"an unrecognised extension must fall back to a format we can actually encode");
		Assert.That(body, Does.Contain("string.IsNullOrEmpty(dir)"),
			"Path.Combine throws on a null directory; a bare filename must still work");

		// Neither caller may keep its own copy of the old logic.
		Assert.That(src, Does.Not.Contain("Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path))"),
			"callers must go through SplitTexturePath");
		Assert.That(src, Does.Contain("SplitTexturePath(path, out string pathBeforeExten, out string exten)"));
	}

	[Test]
	public void SyncEncoder_DoesNotThrowWhenStatusBarIsAbsent() {
		string src = Read("Assets", "_gm", "Features", "TextureTools", "TextureTools_SPZ.cs");
		int fn = src.IndexOf("public static void EncodeAndSaveTexture(", StringComparison.Ordinal);
		Assert.That(fn, Is.GreaterThan(0));
		int end = src.IndexOf("public class TextureEncodeJob", fn, StringComparison.Ordinal);
		string body = src.Substring(fn, end - fn);
		// Headless / RPC export runs with no viewport status bar.
		Assert.That(body, Does.Not.Contain("Viewport_StatusText.instance.ShowStatusText"),
			"the unsupported-format branch must not dereference the status bar singleton");
		Assert.That(body, Does.Contain("Viewport_StatusText.instance?.ShowStatusText"));
	}

	[Test]
	public void MeshExport_OverwritesItsTexturesInsteadOfStackingCopies() {
		string src = Read("Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");

		// The mesh write always overwrites its path. Uniquing the maps beside it desynchronises the
		// pair: a second export leaves a fresh from_spz.fbx next to the previous run's from_spz.png
		// plus a new "from_spz 2.png", and Blender then picks up whichever it likes.
		Assert.That(src, Does.Contain("string ComposeTexturePath(string basePath, string suffix)"),
			"there must be a non-uniquing destination for maps that accompany an overwritten mesh");
		Assert.That(src, Does.Contain("overwriteExisting ? ComposeTexturePath(save_to_basePath, \"\")"),
			"albedo must be able to overwrite");
		Assert.That(src, Does.Contain("overwriteExisting ? ComposeTexturePath(save_to_basePath, \"_AO\")"),
			"AO must be able to overwrite");

		// Both mesh-export flows opt in; the "save textures as…" dialog must not.
		int optIns = 0, idx = 0;
		while ((idx = src.IndexOf("overwriteExisting:true", idx, StringComparison.Ordinal)) >= 0) { optIns++; idx += 3; }
		Assert.That(optIns, Is.EqualTo(2),
			"exactly the dialog export and the exchange export write maps beside a rewritten mesh");
		Assert.That(src, Does.Contain("bool overwriteExisting = false"),
			"overwriting must be opt-in so dialog saves never clobber the user's own files");
	}

	[Test]
	public void UniquePathStillGuardsDialogSaves() {
		string src = Read("Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		// Regression guard: the shared composer must not have swallowed the uniquing behaviour.
		int fn = src.IndexOf("string MakeUniquePath(string basePath, string suffix)", StringComparison.Ordinal);
		Assert.That(fn, Is.GreaterThan(0));
		int end = src.IndexOf("IEnumerator WaitForRenderAll_crtn", fn, StringComparison.Ordinal);
		string body = src.Substring(fn, end - fn);
		Assert.That(body, Does.Contain("File.Exists(candidate)"));
		Assert.That(body, Does.Contain("for (int n = 2"));
		Assert.That(src, Does.Contain("MakeUniquePath(basePath, \"_Content\")"),
			"view-texture dialog saves must keep uniquing");
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
	public void SaveMgr_UdimPairingSurvivesMissingSectors() {
		string src = Read("Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		int pair = src.IndexOf("int pairCount", StringComparison.Ordinal);
		Assert.That(pair, Is.GreaterThan(0), "slices must be paired to UDIM sectors under a clamp");
		string body = src.Substring(pair, Math.Min(900, src.Length - pair));

		// A null sector list must yield zero pairs, never an index into null. Indexing it would abort
		// the coroutine after onHaveAlbedo: slices leak and onComplete still reports success.
		Assert.That(body, Does.Not.Contain("albedoUdims.udims_sectors[i]"),
			"must not index the sector list that was just null-checked");
		Assert.That(src, Does.Contain("int sectorCount = sectors != null ? sectors.Count : 0"),
			"a missing sector list must count as zero, not as slices.Count");
		Assert.That(body, Does.Contain("LogWarning"),
			"a slice/sector mismatch drops tiles and must not be silent");
		Assert.That(body, Does.Contain("DestroyImmediate"),
			"unpaired slices must be released rather than leaked");
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
	public void BudgetedReadback_ReleasesTempRtOnEveryExit() {
		string src = Read("Assets", "_gm", "Features", "TextureTools", "TextureTools_SPZ.cs");
		int i = src.IndexOf("TextureArray_to_Texture2DList_Budgeted", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		int end = src.IndexOf("Texture2DList_to_TextureArray", i, StringComparison.Ordinal);
		string body = src.Substring(i, (end > i ? end : src.Length) - i);

		// This variant spans frames, so it can be stopped midway; the single-frame original could not.
		Assert.That(body, Does.Contain("finally"),
			"the temp render target must be released even if the coroutine is stopped or throws");
		int release = body.IndexOf("ReleaseTemporary(tempRT)", StringComparison.Ordinal);
		int fin = body.IndexOf("} finally {", StringComparison.Ordinal);
		Assert.That(fin, Is.GreaterThan(0));
		Assert.That(release, Is.GreaterThan(fin), "release must live inside the finally block");

		// The material lookup must not sit between the allocation and its guard.
		int alloc = body.IndexOf("RenderTexture.GetTemporary", StringComparison.Ordinal);
		int matLookup = body.IndexOf("StaticShaders_MGR.instance", StringComparison.Ordinal);
		Assert.That(matLookup, Is.LessThan(alloc),
			"resolve the blit material before allocating, or a missing manager leaks the render target");
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

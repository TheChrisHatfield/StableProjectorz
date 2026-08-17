using System.IO;
using NUnit.Framework;

/// <summary>
/// SPZ GO bidirectional: Export must write .spz_go_ready after textures so Blender can auto-import.
/// </summary>
public sealed class SpzGoExchangeReadyStampTests {

	[Test]
	public void ExportToPath_WritesReadyStampOnTextureComplete() {
		string save = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		Assert.That(File.Exists(save), Is.True);
		string src = File.ReadAllText(save);
		Assert.That(src, Does.Contain("TryWriteSpzGoExchangeReadyStamp"),
			"Export must expose a ready-stamp helper for Blender auto-import.");
		Assert.That(src, Does.Contain(".spz_go_ready"),
			"Stamp filename must use .spz_go_ready sidecar next to the FBX.");
		// Anchor on the stamping callback itself. Several export paths declare their own OnComplete,
		// so keying off the first one in the file silently compares against an unrelated block.
		int stampCall = src.IndexOf("TryWriteSpzGoExchangeReadyStamp( meshPathForStamp )",
			System.StringComparison.Ordinal);
		Assert.That(stampCall, Is.GreaterThan(0));
		int owningOnComplete = src.LastIndexOf("void OnComplete( bool texturesWritten )", stampCall,
			System.StringComparison.Ordinal);
		Assert.That(owningOnComplete, Is.GreaterThan(0),
			"Ready stamp must be written from texture OnComplete, not before maps finish.");
		Assert.That(src.Substring(owningOnComplete, stampCall - owningOnComplete),
			Does.Contain("if( texturesWritten )"),
			"Stamp must only land when the texture stage actually ran");
		int clearSaving = src.IndexOf("_isSaving = false;", stampCall, System.StringComparison.Ordinal);
		Assert.That(clearSaving, Is.GreaterThan(stampCall),
			"Ready stamp must be written before _isSaving clears, or waiters race a missing sidecar.");
		int methodEnd = src.IndexOf("return true;", clearSaving, System.StringComparison.Ordinal);
		Assert.That(methodEnd, Is.GreaterThan(clearSaving));
		Assert.That(src.Substring(owningOnComplete, methodEnd - owningOnComplete),
			Does.Contain("_isSaving = false;"),
			"busy-clear must live in the same OnComplete as the stamp");
		Assert.That(src, Does.Contain("SpzGoExchangeReadyStampExists"),
			"HTTP/TCP waiters must share one stamp-exists check");
		Assert.That(src, Does.Contain("TryDeleteSpzGoExchangeReadyStamp"),
			"Export must clear a stale ready stamp before rewriting the exchange FBX.");
		int deleteAt = src.IndexOf("TryDeleteSpzGoExchangeReadyStamp( meshFilePath )");
		int exportModel = src.IndexOf("mh.ExportModelToPath( meshFilePath )");
		Assert.That(deleteAt, Is.GreaterThan(0));
		Assert.That(exportModel, Is.GreaterThan(deleteAt),
			"Stale stamp must be removed before ExportModelToPath rewrites the mesh.");
		Assert.That(src, Does.Contain("Viewport_StatusText.instance?.ShowStatusText"),
			"Save_Mesh_Textures must not NRE on null status UI before onComplete/stamp.");
	}
}

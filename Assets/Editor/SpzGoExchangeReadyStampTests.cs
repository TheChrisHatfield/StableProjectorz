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
		int onComplete = src.IndexOf("void OnComplete()");
		int stampCall = src.IndexOf("TryWriteSpzGoExchangeReadyStamp( meshPathForStamp )");
		int clearSaving = src.IndexOf("_isSaving = false;", onComplete >= 0 ? onComplete : 0);
		Assert.That(onComplete, Is.GreaterThan(0));
		Assert.That(stampCall, Is.GreaterThan(onComplete),
			"Ready stamp must be written from texture OnComplete, not before maps finish.");
		Assert.That(clearSaving, Is.GreaterThan(stampCall),
			"Ready stamp must be written before _isSaving clears, or waiters race a missing sidecar.");
	}
}

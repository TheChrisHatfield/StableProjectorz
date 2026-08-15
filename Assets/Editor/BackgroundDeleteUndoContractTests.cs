using System.IO;
using NUnit.Framework;

/// <summary>
/// Background Delete/Fill must schedule undo capture and wait for IsBusy like inpaint.
/// </summary>
public sealed class BackgroundDeleteUndoContractTests {

	[Test]
	public void BackgroundDelete_SchedulesUndoCapture() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Paint", "BG painter", "Background_Painter.cs");
		string src = File.ReadAllText(path);
		int deleteAt = src.IndexOf("protected override void OnDelete_button()", System.StringComparison.Ordinal);
		Assert.That(deleteAt, Is.GreaterThan(0));
		string body = src.Substring(deleteAt, System.Math.Min(500, src.Length - deleteAt));
		Assert.That(body, Does.Contain("BgFillOrDeleteWithUndo_Coroutine"),
			"Delete must not ClearRenderTexture without an undo capture.");
	}

	[Test]
	public void BackgroundFillDelete_WaitsForUndoBusy() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Paint", "BG painter", "Background_Painter.cs");
		string src = File.ReadAllText(path);
		int corAt = src.IndexOf("IEnumerator BgFillOrDeleteWithUndo_Coroutine", System.StringComparison.Ordinal);
		Assert.That(corAt, Is.GreaterThan(0));
		string body = src.Substring(corAt, System.Math.Min(1200, src.Length - corAt));
		Assert.That(body, Does.Contain("IsBusy"));
		Assert.That(body, Does.Contain("SchedulePreStrokeCapture"));
		Assert.That(body, Does.Contain("BackgroundGenMask"));
		int waitAt = body.IndexOf("IsBusy", System.StringComparison.Ordinal);
		int clearAt = body.IndexOf("ClearRenderTexture", System.StringComparison.Ordinal);
		Assert.That(clearAt, Is.GreaterThan(waitAt));
	}
}

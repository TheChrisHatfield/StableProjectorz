using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class FlattenAndHideGraphicLeaveTests {
	[Test]
	public void FlattenSliced_And_HideAuthored_LeaveUnwind_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "SpzUiThemeOps.cs");
		string src = File.ReadAllText(path);

		int flat = src.IndexOf("public static void FlattenSlicedChromeFace", System.StringComparison.Ordinal);
		string flatBody = src.Substring(flat, System.Math.Min(500, src.Length - flat));
		Assert.That(flatBody, Does.Contain("RestoreRoundedControlSpritesUnder(image.transform)"));

		int hide = src.IndexOf("public static void HideAuthoredGraphicForTheme", System.StringComparison.Ordinal);
		string hideBody = src.Substring(hide, System.Math.Min(900, src.Length - hide));
		Assert.That(hideBody, Does.Contain("SpzUiThemeHiddenGraphic"));
		Assert.That(hideBody, Does.Contain("leaveTag.wasEnabled"));
	}
}

using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>Role matrix must Ensure hit faces before ClearNonFace, including icon-as-face skips.</summary>
public sealed class RoleMatrixEnsureBeforeClearThemeTests {
	[Test]
	public void RoleMatrix_EnsuresBeforeClearNonFace() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "SpzUiThemeRoleMatrix.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("Compact/NarrowDock clear label raycasts", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(idx, System.Math.Min(900, src.Length - idx));
		Assert.That(body, Does.Contain("EnsureSelectableHitFace(btn)"));
		Assert.That(body.IndexOf("EnsureSelectableHitFace(btn)"),
			Is.LessThan(body.IndexOf("ClearNonFaceRaycastsForTheme(btn)")));
	}

	[Test]
	public void RoleMatrix_TintsAuthoredIconFacesInsteadOfSkippingHits() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "SpzUiThemeRoleMatrix.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("static void ThemeSelectablesUnder", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(idx, System.Math.Min(2200, src.Length - idx));
		Assert.That(body, Does.Contain("IsAuthoredIconFace(btn.targetGraphic)"));
		Assert.That(body, Does.Contain("ApplyBoundChromeGraphic(iconFace, t.iconTint)"));
	}
}

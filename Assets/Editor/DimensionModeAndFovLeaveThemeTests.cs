using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class DimensionModeAndFovLeaveThemeTests {
	[Test]
	public void DimensionMode_LeaveRestoresChoicesAndButtons() {
		string path = Path.Combine(Application.dataPath, ""_gm"", ""Layouts"", ""Viewport (MainView)"", ""DimensionMode_MGR.cs"");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain(""RestoreBoundChromeUnder(_choicesPanel_rectTransf)""));
		Assert.That(src, Does.Contain(""RestoreDimChoice(_sd_choice_button)""));
	}

	[Test]
	public void CamerasFov_LeaveDoesNotReapplyNomadSliderChrome() {
		string path = Path.Combine(Application.dataPath, ""_gm"", ""Features"", ""Camera"", ""Multi-View"", ""MultiView_CamerasFOV.cs"");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int leave = src.IndexOf(""if (!SpzUiThemeOps.ShouldRecolorBoundChrome)"");
		Assert.That(leave, Is.GreaterThanOrEqualTo(0));
		string leaveBody = src.Substring(leave, System.Math.Min(350, src.Length - leave));
		Assert.That(leaveBody, Does.Not.Contain(""ApplyNomadSliderChrome""),
			""Leave must Restore only — ApplyNomadSliderChrome with FillThumb marker used to re-Nomad FOV"");
	}

	[Test]
	public void ApplyNomadSliderChrome_GatesFillThumbBehindBoundChrome() {
		string path = Path.Combine(Application.dataPath, ""_gm"", ""Features"", ""AddonSystem"", ""SpzUiThemeOps.cs"");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int fn = src.IndexOf(""public static void ApplyNomadSliderChrome"");
		Assert.That(fn, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(fn, System.Math.Min(500, src.Length - fn));
		int gate = body.IndexOf(""!ShouldRecolorBoundChrome"");
		int fillThumb = body.IndexOf(""SpzUiThemeNomadFillThumb"");
		Assert.That(gate, Is.GreaterThanOrEqualTo(0));
		Assert.That(fillThumb, Is.GreaterThan(gate),
			""ShouldRecolor gate must run before FillThumb route so Leave SPZ restores FOV"");
	}
}

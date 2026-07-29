using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Pass 24: Multiview GRID/POV must Ensure (Refresh) before BoundChrome TMP clears label hits.
/// </summary>
public sealed class BoundChromePass24MultiviewEnsureBeforeTmpTests {

	[Test]
	public void Multiview_SourceRefreshesPovGridBeforeLabelTmp() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/Camera/Multi-View/MultiView_Ribbon_UI.cs"));
		string src = File.ReadAllText(path);
		int apply = src.IndexOf("void ApplyMultiviewChromeThemeTokens()", System.StringComparison.Ordinal);
		Assert.That(apply, Is.GreaterThan(0));
		string body = src.Substring(apply, System.Math.Min(2200, src.Length - apply));
		int refresh = body.IndexOf("RefreshPovAndGridChromeSelection()", System.StringComparison.Ordinal);
		int gridTmp = body.IndexOf("ApplyBoundChromeTmp(gLabel", System.StringComparison.Ordinal);
		int povStrip = body.IndexOf("ApplyBoundChromeStripLabelTmp(povLabel", System.StringComparison.Ordinal);
		Assert.That(refresh, Is.GreaterThan(0));
		Assert.That(gridTmp, Is.GreaterThan(refresh),
			"GRID label TMP must run after Ensure/Refresh so faces exist when labels lose raycasts");
		Assert.That(povStrip, Is.GreaterThan(refresh),
			"POV StripLabel must run after Ensure/Refresh");
	}
}

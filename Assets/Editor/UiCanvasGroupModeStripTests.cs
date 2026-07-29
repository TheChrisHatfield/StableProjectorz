using NUnit.Framework;
using spz;
using UnityEngine;

/// <summary>
/// 3D↔SD mode strips must not leave a semi-transparent twin under the active strip.
/// </summary>
public sealed class UiCanvasGroupModeStripTests {

	[Test]
	public void Tick_Hide_SnapsOffAndDisablesRaycasts() {
		var go = new GameObject("ModeStripHide", typeof(CanvasGroup));
		go.SetActive(false);
		try {
			var cg = go.GetComponent<CanvasGroup>();
			go.SetActive(true);
			cg.alpha = 0.7f;
			cg.blocksRaycasts = true;
			cg.interactable = true;

			UiCanvasGroupModeStrip.Tick(cg, show: false, fadeInSpeed: 5f);

			Assert.That(cg.alpha, Is.EqualTo(0f));
			Assert.That(cg.blocksRaycasts, Is.False);
			Assert.That(cg.interactable, Is.False);
			Assert.That(go.activeSelf, Is.False);
		}
		finally {
			Object.DestroyImmediate(go);
		}
	}

	[Test]
	public void Tick_Show_ActivatesWithoutLeavingZeroAlphaInactive() {
		var go = new GameObject("ModeStripShow", typeof(CanvasGroup));
		go.SetActive(false);
		try {
			var cg = go.GetComponent<CanvasGroup>();
			cg.alpha = 0f;

			UiCanvasGroupModeStrip.Tick(cg, show: true, fadeInSpeed: 1000f);

			Assert.That(go.activeSelf, Is.True);
			Assert.That(cg.alpha, Is.GreaterThan(0f));
			Assert.That(cg.blocksRaycasts, Is.True);
		}
		finally {
			Object.DestroyImmediate(go);
		}
	}

	[Test]
	public void LeftColumnSources_UseModeStripHelperAndOnlyPlaceWhenShown() {
		string sd = System.IO.File.ReadAllText(
			"Assets/_gm/Layouts/LeftPanel/Left_Column_SD_Placement_UI.cs");
		string gen3d = System.IO.File.ReadAllText(
			"Assets/_gm/Layouts/LeftPanel/Left_Column_3D_Placement_UI.cs");
		Assert.That(sd, Does.Contain("UiCanvasGroupModeStrip.Tick"));
		Assert.That(gen3d, Does.Contain("UiCanvasGroupModeStrip.Tick"));
		Assert.That(sd, Does.Contain("if (show)"));
		Assert.That(gen3d, Does.Contain("if (show)"));
		Assert.That(sd, Does.Not.Contain("canvGrp.gameObject.SetActive(true)"));
		Assert.That(gen3d, Does.Not.Contain("canvGrp.gameObject.SetActive(true)"));
	}

	[Test]
	public void WorkflowRibbonSources_UseModeStripHelper() {
		string sd = System.IO.File.ReadAllText(
			"Assets/_gm/Features/StableDiffusion/WorkflowToolsRibbon SD/SD_WorkflowOptionsRibbon_UI.cs");
		string gen3d = System.IO.File.ReadAllText(
			"Assets/_gm/Features/3D Generate/Gen3D_WorkflowOptionsRibbon_UI.cs");
		Assert.That(sd, Does.Contain("UiCanvasGroupModeStrip.Tick"));
		Assert.That(gen3d, Does.Contain("UiCanvasGroupModeStrip.Tick"));
		Assert.That(sd, Does.Not.Contain("SetActive(_wholePanel_canvGrp.alpha!=0)"));
		Assert.That(gen3d, Does.Not.Contain("SetActive(_wholePanel_canvGrp.alpha!=0)"));
	}
}

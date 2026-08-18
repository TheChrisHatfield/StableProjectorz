using System;
using System.IO;
using NUnit.Framework;

public sealed class Icon3dDestroyUnsubscribeContractTests {
	[Test]
	public void DestroySelf_UnsubscribesBeforeNullingBgIcon() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Generate", "Icon3D_UI.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public void DestroySelf()", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i, Math.Min(500, src.Length - i));
		Assert.That(body, Does.Contain("Unsubscribe_from_textureUpdates"));
		Assert.That(body.IndexOf("Unsubscribe_from_textureUpdates", StringComparison.Ordinal),
			Is.LessThan(body.IndexOf("_bgIcon_ref = null", StringComparison.Ordinal)),
			"unsubscribe must run while _genData still resolves from _bgIcon_ref");
	}
}

public sealed class PaintLayerAddDuringCollapseContractTests {
	[Test]
	public void AddLayerFromTextures_NullChecksAddLayerResult() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Paint", "Layers", "PaintLayerStack_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public bool AddLayerFromTextures(", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		int end = src.IndexOf("public void RemoveLayer(", i, StringComparison.Ordinal);
		string body = src.Substring(i, end - i);
		Assert.That(body, Does.Contain("layer == null || layer.Content == null"));
	}
}

public sealed class SinglePovFirstEnabledMaskContractTests {
	[Test]
	public void GenDataMasks_ExposesFirstEnabledPovLookup() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "GenData", "GenData_Masks.cs");
		Assert.That(File.ReadAllText(path), Does.Contain("TryGetFirstEnabledPovMasks"));
	}

	[Test]
	public void SingleViewPaintAndProjection_UseFirstEnabledNotSlotZero() {
		string paint = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Paint", "ApplyBrushStroke_ToUvMask.cs"));
		int i = paint.IndexOf("void Apply_intoMask_singleView(", StringComparison.Ordinal);
		string body = paint.Substring(i, Math.Min(1200, paint.Length - i));
		Assert.That(body, Does.Contain("TryGetFirstEnabledPovMasks"));
		Assert.That(body, Does.Not.Contain("_ObjectUV_brushedMaskR8[0]"));

		string proj = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Camera", "Projections", "ProjectorCameras_RenderHelper.cs"));
		int j = proj.IndexOf("void SinglePOV_Set_UvMasks(", StringComparison.Ordinal);
		string pbody = proj.Substring(j, Math.Min(400, proj.Length - j));
		Assert.That(pbody, Does.Contain("TryGetFirstEnabledPovMasks"));
		Assert.That(pbody, Does.Not.Contain("[0]"));
	}
}

public sealed class ProjectorCameraInitNullIconContractTests {
	[Test]
	public void Init_SkipsVisibilityWhenIconMissing() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Camera", "Projections", "ProjectorCamera.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public void Init(GenData2D genData)", StringComparison.Ordinal);
		string body = src.Substring(i, Math.Min(900, src.Length - i));
		Assert.That(body, Does.Contain("myIconUI == null"));
		Assert.That(body, Does.Contain("return;"));
	}
}

public sealed class WorkflowOptionsRibbonUpdateNullGuardTests {
	[Test]
	public void Update_NullChecksDimensionModeBeforeDeref() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "WorkflowToolsRibbon SD",
			"SD_WorkflowOptionsRibbon_UI.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("void Update()", StringComparison.Ordinal);
		string body = src.Substring(i, Math.Min(450, src.Length - i));
		Assert.That(body, Does.Contain("DimensionMode_MGR.instance == null) return"));
	}
}

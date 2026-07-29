using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Pass 16: ControlNet model/preprocessor dropdowns must Ensure hit face under Nomad
/// (caption TMP raycasts clear; without face Depth/Normals pick dies → Gen Art gated).
/// </summary>
public sealed class BoundChromePass16ControlNetDropdownHitFaceTests {

	[Test]
	public void ControlNetUnit_DropdownsUseApplyBoundChromeSelectable() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/StableDiffusion/Controlnet/ControlNetUnit_UI.cs"));
		string src = File.ReadAllText(path);
		int ddLoop = src.IndexOf(
			"foreach (var dd in GetComponentsInChildren<TMP_Dropdown>",
			System.StringComparison.Ordinal);
		Assert.That(ddLoop, Is.GreaterThan(0));
		string body = src.Substring(ddLoop, System.Math.Min(700, src.Length - ddLoop));
		Assert.That(body, Does.Contain("ApplyBoundChromeSelectable(dd"));
		Assert.That(body, Does.Not.Contain("ApplyBoundChromeGraphic(fieldImg"));
	}
}

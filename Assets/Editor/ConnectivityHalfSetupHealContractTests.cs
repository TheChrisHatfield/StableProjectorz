using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Connectivity half-setup: an existing piece must not skip creating its paired piece.
/// </summary>
public sealed class ConnectivityHalfSetupHealContractTests {

	static string Read(params string[] parts) {
		string path = Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts));
		Assert.That(File.Exists(path), Is.True, $"missing: {path}");
		return File.ReadAllText(path);
	}

	[Test]
	public void LayersCollapseButtonHealsOnExistingAddRow() {
		string src = Read("Assets", "_gm", "Features", "Paint", "PaintTab", "PaintTab_CollectPaintUI.cs");
		Assert.That(src, Does.Contain("static void EnsureLayersCollapseButton("));
		int ensure = src.IndexOf("static Button EnsureLayersAddButtonRow(", StringComparison.Ordinal);
		Assert.That(ensure, Is.GreaterThan(0));
		string body = src.Substring(ensure, Math.Min(900, src.Length - ensure));
		Assert.That(body, Does.Contain("EnsureLayersCollapseButton(existingRow)"),
			"finding AddLayerBtn alone must still ensure Collapse");
	}

	[Test]
	public void EnsureLayersCollapseButtonIsIdempotent() {
		var row = new GameObject("LayerButtonsRow", typeof(RectTransform));
		try {
			var m = typeof(PaintTab_CollectPaintUI).GetMethod("EnsureLayersCollapseButton",
				BindingFlags.Static | BindingFlags.NonPublic);
			Assert.That(m, Is.Not.Null);
			m.Invoke(null, new object[] { row.transform });
			m.Invoke(null, new object[] { row.transform });
			Assert.That(row.transform.Find("CollapseBtn"), Is.Not.Null);
			int count = 0;
			foreach (Transform c in row.transform)
				if (c.name == "CollapseBtn") count++;
			Assert.That(count, Is.EqualTo(1));
			Assert.That(row.transform.Find("CollapseBtn").GetComponent<Button>(), Is.Not.Null);
		}
		finally {
			UnityEngine.Object.DestroyImmediate(row);
		}
	}

	[Test]
	public void PaletteEmptyShellGetsButtons() {
		string src = Read("Assets", "_gm", "Features", "Paint", "PaintTab", "PaintTab_CollectPaintUI.cs");
		int i = src.IndexOf("static void EnsurePaletteLoadButtonExists(", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i, Math.Min(900, src.Length - i));
		Assert.That(body, Does.Contain("GetComponentInChildren<Button>(true)"),
			"an empty PaletteLoadButtonRow shell must not count as complete");
		Assert.That(body, Does.Not.Contain("if (section.GetChild(i).name == \"PaletteLoadButtonRow\")\r\n\t\t\t\t\treturn;")
			.And.Not.Contain("if (section.GetChild(i).name == \"PaletteLoadButtonRow\")\n\t\t\t\t\treturn;"));
	}

	[Test]
	public void DynamicTabMovementRequiresBothRows() {
		string src = Read("Assets", "_gm", "Features", "Settings", "Settings_UI.cs");
		Assert.That(src, Does.Contain("DynamicTabMovementRowsPresentUnder("));
		Assert.That(src, Does.Contain("DestroyDynamicTabMovementRowsUnder("));
		int i = src.IndexOf("void EnsureDynamicTabMovementRowsExist()", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i, Math.Min(800, src.Length - i));
		Assert.That(body, Does.Contain("DynamicTabMovementRowsPresentUnder(content)"),
			"toggle alone must not skip the Save/Reset Order row");
	}
}

using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;

/// <summary>
/// SD SERV chip keeps 2D and appends Cloud/Local so a green strip is not mistaken for local GPU Forge.
/// Spec cloud-inference R2.
/// </summary>
public sealed class ConnectionPanelCloudEmblemContractTests {

	[Test]
	public void PingJsonMarksCloudInference_ShimTrue() {
		Assert.That(ConnectionPanel_UI.PingJsonMarksCloudInference(
			"{\"status\":\"ok\",\"cloud_inference\":true}"), Is.True);
	}

	[Test]
	public void PingJsonMarksCloudInference_LocalForgeFalse() {
		Assert.That(ConnectionPanel_UI.PingJsonMarksCloudInference("{\"status\":\"ok\"}"), Is.False);
		Assert.That(ConnectionPanel_UI.PingJsonMarksCloudInference(
			"{\"status\":\"ok\",\"cloud_inference\":false}"), Is.False);
	}

	[Test]
	public void PingJsonMarksCloudInference_HtmlAndEmptyAreNotCloud() {
		Assert.That(ConnectionPanel_UI.PingJsonMarksCloudInference(null), Is.False);
		Assert.That(ConnectionPanel_UI.PingJsonMarksCloudInference(""), Is.False);
		Assert.That(ConnectionPanel_UI.PingJsonMarksCloudInference("<html>ok</html>"), Is.False);
	}

	[Test]
	public void ApplySdInferenceEmblem_CloudLocalAndDisconnectRestore2D() {
		var root = new GameObject("ConnCloudEmblem", typeof(RectTransform));
		root.SetActive(false);
		try {
			var dimGo = new GameObject("text (2d vs 3d)", typeof(RectTransform));
			dimGo.transform.SetParent(root.transform, false);
			var dim = dimGo.AddComponent<TextMeshProUGUI>();
			dim.text = "2D";

			var ui = root.AddComponent<ConnectionPanel_UI>();
			SetPanelKind(ui, 0);
			typeof(ConnectionPanel_UI).GetField(
				"_dim_text", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(ui, dim);

			ui.ApplySdInferenceEmblem(connected: true, cloudInference: true);
			Assert.That(dim.text, Is.EqualTo("2D \u00b7 Cloud"));
			Assert.That(ui.isCloudInferenceConnected, Is.True);

			ui.ApplySdInferenceEmblem(connected: true, cloudInference: false);
			Assert.That(dim.text, Is.EqualTo("2D \u00b7 Local"));
			Assert.That(ui.isCloudInferenceConnected, Is.False);

			ui.ApplySdInferenceEmblem(connected: false, cloudInference: false);
			Assert.That(dim.text, Is.EqualTo("2D"));
		}
		finally {
			UnityEngine.Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void ApplySdInferenceEmblem_TrellisLeavesAuthoredDimText() {
		var root = new GameObject("ConnTrellisEmblem", typeof(RectTransform));
		root.SetActive(false);
		try {
			var dimGo = new GameObject("text (2d vs 3d)", typeof(RectTransform));
			dimGo.transform.SetParent(root.transform, false);
			var dim = dimGo.AddComponent<TextMeshProUGUI>();
			dim.text = "3D";

			var ui = root.AddComponent<ConnectionPanel_UI>();
			SetPanelKind(ui, 1);
			typeof(ConnectionPanel_UI).GetField(
				"_dim_text", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(ui, dim);

			ui.ApplySdInferenceEmblem(connected: true, cloudInference: true);
			Assert.That(dim.text, Is.EqualTo("3D"));
			Assert.That(ui.isCloudInferenceConnected, Is.False);
		}
		finally {
			UnityEngine.Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void CheckConnection_SourceWiresPingJsonToEmblem() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Connection", "ConnectionPanel_UI.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("PingJsonMarksCloudInference"));
		Assert.That(src, Does.Contain("ApplySdInferenceEmblem"));
		Assert.That(src, Does.Contain("downloadHandler.text"));
	}

	[Test]
	public void NomadServCaption_DoesNotEllipsisClipLocalCloud() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Connection", "ConnectionPanel_UI.cs");
		string src = File.ReadAllText(path);
		int apply = src.IndexOf("void ApplyThemeTokens()", StringComparison.Ordinal);
		Assert.That(apply, Is.GreaterThan(0));
		int next = src.IndexOf("void OnOpenPanel_Button", apply, StringComparison.Ordinal);
		if (next < 0) next = src.Length;
		string body = src.Substring(apply, next - apply);
		Assert.That(body, Does.Not.Contain("TextOverflowModes.Ellipsis"),
			"Ellipsis clipped 2D · Local to 2D - Lo under the signal bars");
		Assert.That(body, Does.Contain("TextOverflowModes.Overflow"));
		Assert.That(body, Does.Contain("HideAuthoredGraphicForTheme(_connectionIcon)"));
		Assert.That(body, Does.Contain("ApplyBoundChromeReadableBodyTmp(_dim_text, status, 9f)"));
	}

	static void SetPanelKind(ConnectionPanel_UI ui, int kind) {
		var kindType = typeof(ConnectionPanel_UI).GetNestedType(
			"ConnectionPanel_Kind", BindingFlags.NonPublic);
		Assert.That(kindType, Is.Not.Null);
		typeof(ConnectionPanel_UI).GetField(
			"_panelKind", BindingFlags.Instance | BindingFlags.NonPublic)
			.SetValue(ui, Enum.ToObject(kindType, kind));
	}
}

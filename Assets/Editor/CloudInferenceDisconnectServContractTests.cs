using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Reflection;

/// <summary>
/// Cloud Disconnect must clear SERV immediately via mark_sd_disconnected wiring.
/// </summary>
public sealed class CloudInferenceDisconnectServContractTests {

	[Test]
	public void ForceMarkDisconnected_ClearsCloudEmblemAndConnected() {
		var go = new GameObject("CloudDisconnectServTest");
		try {
			var ui = go.AddComponent<spz.ConnectionPanel_UI>();
			var dimGo = new GameObject("dim");
			dimGo.transform.SetParent(go.transform);
			var dim = dimGo.AddComponent<TextMeshProUGUI>();
			dim.text = "2D";
			typeof(spz.ConnectionPanel_UI).GetField(
				"_dim_text", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(ui, dim);
			SetPanelKind(ui, 0);
			ui.ApplySdInferenceEmblem(connected: true, cloudInference: true);
			Assert.That(ui.isCloudInferenceConnected, Is.True);
			ui.ForceMarkDisconnected();
			Assert.That(ui.isConnected, Is.False);
			Assert.That(ui.isCloudInferenceConnected, Is.False);
			Assert.That(dim.text, Is.EqualTo("2D"));
		} finally {
			Object.DestroyImmediate(go);
		}
	}

	[Test]
	public void DisconnectCloud_SourceCallsMarkSdDisconnected() {
		string initPath = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "StreamingAssets", "Addons", "CloudInferenceSPZ", "__init__.py");
		string sockPath = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_SocketServer.cs");
		Assert.That(File.Exists(initPath), Is.True);
		Assert.That(File.Exists(sockPath), Is.True);
		string init = File.ReadAllText(initPath);
		string sock = File.ReadAllText(sockPath);
		Assert.That(init, Does.Contain("mark_sd_disconnected"));
		Assert.That(init, Does.Contain("disconnect_cloud"));
		Assert.That(sock, Does.Contain("spz.cmd.mark_sd_disconnected"));
		Assert.That(sock, Does.Contain("MarkSdDisconnected"));
	}

	static void SetPanelKind(spz.ConnectionPanel_UI ui, int kind) {
		var kindType = typeof(spz.ConnectionPanel_UI).GetNestedType(
			"ConnectionPanel_Kind", BindingFlags.NonPublic);
		Assert.That(kindType, Is.Not.Null);
		typeof(spz.ConnectionPanel_UI).GetField(
			"_panelKind", BindingFlags.Instance | BindingFlags.NonPublic)
			.SetValue(ui, System.Enum.ToObject(kindType, kind));
	}
}

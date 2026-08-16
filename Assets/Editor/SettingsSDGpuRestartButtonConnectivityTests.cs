using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The SD GPU row is a two-part feature: the device-id field and the "Restart WebUI (apply GPU)"
/// button that applies it. EnsureSDGpuRowExists early-returned on the [SerializeField] input alone,
/// so a prefab-assigned field skipped the runtime row and the button was never created by any code
/// path — the id could be typed but never applied. Same class of bug as the documented
/// "Paint panel exists but no tab" regression in .cursor/rules/connectivity-and-setup.mdc.
/// </summary>
public sealed class SettingsSDGpuRestartButtonConnectivityTests {

	static void EnsureButton(Settings_UI ui, Transform row) {
		var m = typeof(Settings_UI).GetMethod("EnsureSDGpuRestartButton",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(m, Is.Not.Null, "EnsureSDGpuRestartButton must exist as a reusable helper");
		m.Invoke(ui, new object[] { row });
	}

	static Button FindApplyButton(Transform row) {
		foreach (Transform child in row) {
			if (child != null && child.name == Settings_UI.SDGpuRestartButtonName)
				return child.GetComponent<Button>();
		}
		return null;
	}

	[Test]
	public void CreatesApplyButtonUnderAnExistingRow() {
		var host = new GameObject("SettingsHost");
		host.SetActive(false);
		var rowGo = new GameObject("Row_SD_GPU", typeof(RectTransform));
		try {
			var ui = host.AddComponent<Settings_UI>();
			Assert.That(FindApplyButton(rowGo.transform), Is.Null, "precondition: row has no apply button");

			EnsureButton(ui, rowGo.transform);

			var btn = FindApplyButton(rowGo.transform);
			Assert.That(btn, Is.Not.Null,
				"a prefab-assigned GPU field must still get its apply button");
			Assert.That(btn.onClick.GetPersistentEventCount() >= 0, Is.True);
			Assert.That(btn.targetGraphic, Is.Not.Null, "the button needs a graphic to be clickable");
			Assert.That(btn.targetGraphic.raycastTarget, Is.True);
		}
		finally {
			UnityEngine.Object.DestroyImmediate(rowGo);
			UnityEngine.Object.DestroyImmediate(host);
		}
	}

	[Test]
	public void DoesNotDuplicateTheApplyButton() {
		var host = new GameObject("SettingsHost");
		host.SetActive(false);
		var rowGo = new GameObject("Row_SD_GPU", typeof(RectTransform));
		try {
			var ui = host.AddComponent<Settings_UI>();
			EnsureButton(ui, rowGo.transform);
			EnsureButton(ui, rowGo.transform);
			EnsureButton(ui, rowGo.transform);

			int count = 0;
			foreach (Transform child in rowGo.transform) {
				if (child != null && child.name == Settings_UI.SDGpuRestartButtonName) count++;
			}
			Assert.That(count, Is.EqualTo(1), "repeat ensures must not stack duplicate buttons");
		}
		finally {
			UnityEngine.Object.DestroyImmediate(rowGo);
			UnityEngine.Object.DestroyImmediate(host);
		}
	}

	[Test]
	public void NullRowIsIgnored() {
		var host = new GameObject("SettingsHost");
		host.SetActive(false);
		try {
			var ui = host.AddComponent<Settings_UI>();
			Assert.DoesNotThrow(() => EnsureButton(ui, null));
		}
		finally {
			UnityEngine.Object.DestroyImmediate(host);
		}
	}

	[Test]
	public void EarlyReturnPathStillEnsuresTheButtonAndPublishesRefLast() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Settings", "Settings_UI.cs");
		string src = File.ReadAllText(path);

		int ensure = src.IndexOf("void EnsureSDGpuRowExists()", StringComparison.Ordinal);
		Assert.That(ensure, Is.GreaterThan(0));
		int helper = src.IndexOf("void EnsureSDGpuRestartButton(", StringComparison.Ordinal);
		Assert.That(helper, Is.GreaterThan(ensure), "the helper must follow the row builder");
		string body = src.Substring(ensure, helper - ensure);

		int guard = body.IndexOf("if (_sdGpuDeviceId_input != null)", StringComparison.Ordinal);
		int guardEnsure = body.IndexOf("EnsureSDGpuRestartButton(", StringComparison.Ordinal);
		Assert.That(guard, Is.GreaterThan(0));
		Assert.That(guardEnsure, Is.GreaterThan(guard),
			"the 'already have the field' branch must still ensure the paired button");

		int assign = body.IndexOf("_sdGpuDeviceId_input = intInput;", StringComparison.Ordinal);
		int rowEnsure = body.LastIndexOf("EnsureSDGpuRestartButton(", StringComparison.Ordinal);
		Assert.That(assign, Is.GreaterThan(0));
		Assert.That(rowEnsure, Is.LessThan(assign),
			"the success ref must be published only after the button exists, or a half-built row sticks");
	}
}

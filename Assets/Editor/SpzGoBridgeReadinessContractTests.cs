using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace spz.EditorTests {

	/// <summary>
	/// spz-go-multi-dcc Phase 2/3: a stub host (ZBrush / Painter) must stay honestly not-ready until its
	/// file-exchange bridge is actually installed, and then light up without a code change. These cover
	/// the readiness probe + install-marker contract; the live-DCC round-trip is a separate litmus.
	/// </summary>
	public sealed class SpzGoBridgeReadinessContractTests {
		string _tmpRoot;
		System.Func<string, bool> _savedProbe;

		[SetUp]
		public void SetUp() {
			_savedProbe = SpzGoHosts.BridgeInstalledProbe;
			_tmpRoot = Path.Combine(Path.GetTempPath(), "spz_go_bridge_test_" + System.Guid.NewGuid().ToString("N"));
			SpzGoBridgeInstall.InstallRootOverride = _tmpRoot;
			SpzGoHosts.BridgeInstalledProbe = SpzGoBridgeInstall.IsInstalled;
		}

		[TearDown]
		public void TearDown() {
			SpzGoHosts.BridgeInstalledProbe = _savedProbe;
			SpzGoBridgeInstall.InstallRootOverride = null;
			try { if (Directory.Exists(_tmpRoot)) Directory.Delete(_tmpRoot, true); } catch { }
		}

		[Test]
		public void Blender_IsAlwaysReady_WithoutAnyMarker() {
			Assert.That(SpzGoHosts.IsBridgeReady(SpzGoHosts.BlenderId), Is.True,
				"the Blender bridge ships working — no install marker required");
		}

		[Test]
		public void StubHosts_StartNotReady() {
			Assert.That(SpzGoHosts.IsBridgeReady(SpzGoHosts.ZBrushId), Is.False);
			Assert.That(SpzGoHosts.IsBridgeReady(SpzGoHosts.PainterId), Is.False);
		}

		[Test]
		public void InstallMarker_FlipsReadinessOnlyWhilePluginPresent() {
			string pluginDir = Path.Combine(_tmpRoot, "installed_zbrush");
			Directory.CreateDirectory(pluginDir);
			File.WriteAllText(Path.Combine(pluginDir, "spz_zbrush_bridge.py"), "# stub");

			Assert.That(SpzGoBridgeInstall.MarkInstalled(SpzGoHosts.ZBrushId, pluginDir), Is.True);
			Assert.That(SpzGoHosts.IsBridgeReady(SpzGoHosts.ZBrushId), Is.True,
				"a recorded, present install must read as ready");
			Assert.That(SpzGoHosts.IsBridgeReady(SpzGoHosts.PainterId), Is.False,
				"installing ZBrush must not light Painter");

			// A manually deleted plugin drops back to not-ready rather than a logo that would fail.
			Directory.Delete(pluginDir, true);
			Assert.That(SpzGoHosts.IsBridgeReady(SpzGoHosts.ZBrushId), Is.False,
				"marker without the plugin file is not ready");
		}

		[Test]
		public void ClearInstalled_TakesTheHostBackToNotReady() {
			string pluginDir = Path.Combine(_tmpRoot, "installed_painter");
			Directory.CreateDirectory(pluginDir);
			File.WriteAllText(Path.Combine(pluginDir, "spz_painter_plugin.py"), "# stub");
			SpzGoBridgeInstall.MarkInstalled(SpzGoHosts.PainterId, pluginDir);
			Assert.That(SpzGoHosts.IsBridgeReady(SpzGoHosts.PainterId), Is.True);

			SpzGoBridgeInstall.ClearInstalled(SpzGoHosts.PainterId);
			Assert.That(SpzGoHosts.IsBridgeReady(SpzGoHosts.PainterId), Is.False);
		}

		[Test]
		public void CopyInstall_DoesNotClaimSuccessWhenTheMarkerCannotBeWritten() {
			string shipDir = Path.Combine(_tmpRoot, "ship");
			string destDir = Path.Combine(_tmpRoot, "dest");
			Directory.CreateDirectory(shipDir);
			File.WriteAllText(Path.Combine(shipDir, "spz_zbrush_bridge.py"), "# stub");

			// Readiness is read back from the marker, so a copy whose marker never lands is not an
			// install. Point the marker root at a path blocked by a file so writing it fails.
			string blocker = Path.Combine(_tmpRoot, "blocker");
			File.WriteAllText(blocker, "not a directory");
			SpzGoBridgeInstall.InstallRootOverride = Path.Combine(blocker, "markers");

			bool ok = FastPath_API.TryInstallSpzGoBridgeByCopy(
				SpzGoHosts.ZBrushId, shipDir, destDir, new[] { "spz_zbrush_bridge.py" }, "SpzGoBridge",
				out string message);

			Assert.That(ok, Is.False, "an install the UI cannot read back must not report success");
			Assert.That(message, Does.Contain("marker"), "the failure has to say what actually broke");
			Assert.That(SpzGoHosts.IsBridgeReady(SpzGoHosts.ZBrushId), Is.False,
				"reported state and effective readiness must agree");
		}

		[Test]
		public void ZBrushDataFolder_BeatsAnUnrelatedFolderThatMerelySaysZBrush() {
			string root = Path.Combine(_tmpRoot, "docs");
			string real = Path.Combine(root, "ZBrushData2026");
			string scratch = Path.Combine(root, "ZBrush Projects");
			Directory.CreateDirectory(real);
			Directory.CreateDirectory(scratch);
			// Make the scratch folder the most recently touched, which is what a working artist's
			// machine looks like — the real data folder is written rarely.
			Directory.SetLastWriteTimeUtc(real, new System.DateTime(2020, 1, 1));
			Directory.SetLastWriteTimeUtc(scratch, System.DateTime.UtcNow);

			Assert.That(FastPath_API.PickZBrushDataDir(new[] { root }), Is.EqualTo(real),
				"the folder ZBrush actually creates must win over any recently touched 'zbrush' folder");
		}

		[Test]
		public void WithNoZBrushDataFolder_AZBrushishFolderIsStillBetterThanNothing() {
			string root = Path.Combine(_tmpRoot, "docs2");
			string scratch = Path.Combine(root, "zbrush_stuff");
			Directory.CreateDirectory(scratch);
			Assert.That(FastPath_API.PickZBrushDataDir(new[] { root }), Is.EqualTo(scratch));
			Assert.That(FastPath_API.PickZBrushDataDir(new[] { Path.Combine(_tmpRoot, "missing") }), Is.Empty);
		}

		[Test]
		public void UnsetProbe_LeavesStubsNotReady() {
			SpzGoHosts.BridgeInstalledProbe = null;
			Assert.That(SpzGoHosts.IsBridgeReady(SpzGoHosts.ZBrushId), Is.False,
				"headless / no-probe must never claim a stub is ready");
			Assert.That(SpzGoHosts.IsBridgeReady(SpzGoHosts.BlenderId), Is.True);
		}
	}
}

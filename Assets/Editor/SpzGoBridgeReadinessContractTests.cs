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
		public void ZBrushData_HigherYearBeatsNewerButOlderYearFolder() {
			string root = Path.Combine(_tmpRoot, "docs_years");
			string y2025 = Path.Combine(root, "ZBrushData2025");
			string y2026 = Path.Combine(root, "ZBrushData2026");
			Directory.CreateDirectory(y2025);
			Directory.CreateDirectory(y2026);
			Directory.SetLastWriteTimeUtc(y2025, System.DateTime.UtcNow);
			Directory.SetLastWriteTimeUtc(y2026, new System.DateTime(2020, 1, 1));
			Assert.That(FastPath_API.PickZBrushDataDir(new[] { root }), Is.EqualTo(y2026),
				"install into the newest ZBrush year, not whichever data folder was touched last");
			Assert.That(FastPath_API.ParseZBrushDataYear(y2026), Is.EqualTo(2026));
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
		public void PainterPluginsResolver_DoesNotInventAFolderWhenPainterIsMissing() {
			string emptyDocs = Path.Combine(_tmpRoot, "empty_docs");
			Directory.CreateDirectory(emptyDocs);
			Assert.That(FastPath_API.PickPainterPluginsDir(new[] { emptyDocs }), Is.Empty,
				"must not invent Documents/Adobe/... when Painter never created its user tree");
		}

		[Test]
		public void PainterPluginsResolver_PicksHighestVersionAmongExistingTrees() {
			string docs = Path.Combine(_tmpRoot, "painter_docs");
			string legacy = Path.Combine(docs, "Adobe", "Adobe Substance 3D Painter");
			string v10 = Path.Combine(docs, "Adobe", "Adobe Substance 3D Painter 10.1");
			Directory.CreateDirectory(legacy);
			Directory.CreateDirectory(v10);
			Directory.SetLastWriteTimeUtc(legacy, System.DateTime.UtcNow);
			Directory.SetLastWriteTimeUtc(v10, new System.DateTime(2020, 1, 1));

			string picked = FastPath_API.PickPainterPluginsDir(new[] { docs });
			Assert.That(picked, Is.EqualTo(Path.Combine(v10, "python", "plugins")),
				"versioned Painter user tree must beat an unversioned sibling even if older on disk");
			Assert.That(FastPath_API.ParsePainterVersionFromPath(v10).Major, Is.EqualTo(10));
			Assert.That(FastPath_API.ParsePainterVersionFromPath(v10).Minor, Is.EqualTo(1));
		}

		[Test]
		public void BlenderExecutable_PicksHighestMajorMinorInPath() {
			string low = @"C:\Program Files\Blender Foundation\Blender 3.6\blender.exe";
			string high = @"C:\Program Files\Blender Foundation\Blender 4.2\blender.exe";
			Assert.That(FastPath_API.PickBlenderExecutable(new[] { low, high }), Is.EqualTo(high));
			Assert.That(FastPath_API.ParseBlenderVersionFromPath(high), Is.EqualTo((4, 2)));
			Assert.That(FastPath_API.ParseBlenderVersionFromPath(low), Is.EqualTo((3, 6)));
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

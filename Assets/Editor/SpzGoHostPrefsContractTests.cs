using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace spz.EditorTests {

	/// <summary>
	/// spz-go-multi-dcc R15: every host section shows the same controls, but the values behind them are
	/// that host's own. These cover the storage half of that contract — the panel half lives in the
	/// section builder tests.
	/// </summary>
	public sealed class SpzGoHostPrefsContractTests {
		readonly Dictionary<string, int> _savedInts = new Dictionary<string, int>();
		readonly Dictionary<string, string> _savedStrings = new Dictionary<string, string>();
		readonly HashSet<string> _absent = new HashSet<string>();

		[SetUp]
		public void ClearHostPrefs() {
			foreach (var host in SpzGoHosts.All) {
				StashInt(SpzGoHostPrefs.AxisOrderKey(host.Id));
				StashInt(SpzGoHostPrefs.FlipKey(host.Id));
				StashInt(SpzGoHostPrefs.ModeKey(host.Id));
				StashInt(SpzGoHostPrefs.SettingsOpenKey(host.Id));
				StashString(SpzGoHostPrefs.ImportPathKey(host.Id));
				StashString(SpzGoHostPrefs.ExportPathKey(host.Id));
			}
			StashInt(ExportAxisSettings.AxisOrderPrefKey);
			StashInt(ExportAxisSettings.FlipXPrefKey);
			StashInt(ExportAxisSettings.FlipYPrefKey);
			StashInt(ExportAxisSettings.FlipZPrefKey);
		}

		[TearDown]
		public void RestoreHostPrefs() {
			foreach (var kv in _savedInts) PlayerPrefs.SetInt(kv.Key, kv.Value);
			foreach (var kv in _savedStrings) PlayerPrefs.SetString(kv.Key, kv.Value);
			foreach (string key in _absent) PlayerPrefs.DeleteKey(key);
			_savedInts.Clear();
			_savedStrings.Clear();
			_absent.Clear();
			PlayerPrefs.Save();
		}

		void StashInt(string key) {
			if (PlayerPrefs.HasKey(key)) _savedInts[key] = PlayerPrefs.GetInt(key);
			else _absent.Add(key);
			PlayerPrefs.DeleteKey(key);
		}

		void StashString(string key) {
			if (PlayerPrefs.HasKey(key)) _savedStrings[key] = PlayerPrefs.GetString(key);
			else _absent.Add(key);
			PlayerPrefs.DeleteKey(key);
		}

		[Test]
		public void Registry_CoversTheThreeSpecifiedHostsWithDistinctKeys() {
			Assert.That(SpzGoHosts.Get(SpzGoHosts.BlenderId), Is.Not.Null);
			Assert.That(SpzGoHosts.Get(SpzGoHosts.ZBrushId), Is.Not.Null);
			Assert.That(SpzGoHosts.Get(SpzGoHosts.PainterId), Is.Not.Null);

			var ids = new HashSet<string>();
			foreach (var host in SpzGoHosts.All) {
				Assert.That(ids.Add(host.Id), Is.True, "duplicate host id " + host.Id);
				Assert.That(host.DisplayName, Is.Not.Null.And.Not.Empty);
				// A section whose icon fails to resolve must still render a header (R: host header).
				Assert.That(host.Glyph, Is.Not.Null.And.Not.Empty, host.Id + " needs a placeholder glyph");
			}
		}

		[Test]
		public void StubbedHosts_CarryAReasonInsteadOfSilentFailure() {
			foreach (var host in SpzGoHosts.All) {
				if (host.BridgeReady) continue;
				Assert.That(host.NotReadyReason, Is.Not.Null.And.Not.Empty,
					host.Id + " must say why activate cannot run");
			}
			Assert.That(SpzGoHosts.Blender.BridgeReady, Is.True, "the Blender bridge ships working");
		}

		[Test]
		public void MandatorySet_IsTheSameShapeForEveryHost() {
			// The list is shared by construction; assert it still names the controls the spec requires,
			// so dropping one from the template is a test failure rather than a missing row in the panel.
			CollectionAssert.Contains(SpzGoHostSection.MandatorySettingsLabels, ExportAxisSettings.AxisOrderLabel);
			CollectionAssert.Contains(SpzGoHostSection.MandatorySettingsLabels, ExportAxisSettings.FlipLabel);
			CollectionAssert.Contains(SpzGoHostSection.MandatorySettingsLabels, SpzGoHostSection.AutofillLabel);
			CollectionAssert.Contains(SpzGoHostSection.MandatorySettingsLabels, SpzGoHostSection.ImportPathLabel);
			CollectionAssert.Contains(SpzGoHostSection.MandatorySettingsLabels, SpzGoHostSection.ExportPathLabel);
		}

		[Test]
		public void WidgetNames_AreHostQualifiedSoSectionsDoNotCollide() {
			var names = new HashSet<string>();
			foreach (var host in SpzGoHosts.All) {
				Assert.That(names.Add(SpzGoHostSection.SectionName(host.Id)), Is.True);
				Assert.That(names.Add(SpzGoHostSection.LogoName(host.Id)), Is.True);
				Assert.That(names.Add(SpzGoHostSection.ModeToggleName(host.Id, SpzGoMode.Import)), Is.True);
				Assert.That(names.Add(SpzGoHostSection.ModeToggleName(host.Id, SpzGoMode.Export)), Is.True);
			}
		}

		[Test]
		public void AxisAndFlip_AreStoredPerHost() {
			SpzGoHostPrefs.SetAxisOrderIndex(SpzGoHosts.BlenderId, (int)ExportAxisSettings.AxisOrder.ZXY);
			SpzGoHostPrefs.SetFlipIndex(SpzGoHosts.BlenderId, 2);

			Assert.That(SpzGoHostPrefs.GetAxisOrderIndex(SpzGoHosts.ZBrushId), Is.EqualTo(0),
				"ZBrush must not inherit Blender's axis order");
			Assert.That(SpzGoHostPrefs.GetFlipIndex(SpzGoHosts.ZBrushId), Is.EqualTo(0),
				"ZBrush must not inherit Blender's flip");

			SpzGoHostPrefs.SetAxisOrderIndex(SpzGoHosts.ZBrushId, (int)ExportAxisSettings.AxisOrder.YXZ);
			Assert.That(SpzGoHostPrefs.GetAxisOrderIndex(SpzGoHosts.BlenderId),
				Is.EqualTo((int)ExportAxisSettings.AxisOrder.ZXY),
				"writing ZBrush must not disturb Blender");
		}

		[Test]
		public void Mode_DefaultsToExportAndRoundTripsPerHost() {
			foreach (var host in SpzGoHosts.All)
				Assert.That(SpzGoHostPrefs.GetMode(host.Id), Is.EqualTo(SpzGoMode.Export), host.Id);

			SpzGoHostPrefs.SetMode(SpzGoHosts.PainterId, SpzGoMode.Import);
			Assert.That(SpzGoHostPrefs.GetMode(SpzGoHosts.PainterId), Is.EqualTo(SpzGoMode.Import));
			Assert.That(SpzGoHostPrefs.GetMode(SpzGoHosts.BlenderId), Is.EqualTo(SpzGoMode.Export),
				"one host switching direction must not move another");
		}

		[Test]
		public void SettingsDropTab_DefaultsCollapsedPerHost() {
			foreach (var host in SpzGoHosts.All)
				Assert.That(SpzGoHostPrefs.GetSettingsOpen(host.Id), Is.False, host.Id);

			SpzGoHostPrefs.SetSettingsOpen(SpzGoHosts.ZBrushId, true);
			Assert.That(SpzGoHostPrefs.GetSettingsOpen(SpzGoHosts.ZBrushId), Is.True);
			Assert.That(SpzGoHostPrefs.GetSettingsOpen(SpzGoHosts.BlenderId), Is.False,
				"opening one drop-tab must not open another (R5)");
		}

		[Test]
		public void Paths_AreStoredPerHostAndDirection() {
			SpzGoHostPrefs.SetPath(SpzGoHosts.BlenderId, import: true, value: "A");
			SpzGoHostPrefs.SetPath(SpzGoHosts.BlenderId, import: false, value: "B");
			SpzGoHostPrefs.SetPath(SpzGoHosts.PainterId, import: true, value: "C");

			Assert.That(SpzGoHostPrefs.GetPath(SpzGoHosts.BlenderId, true), Is.EqualTo("A"));
			Assert.That(SpzGoHostPrefs.GetPath(SpzGoHosts.BlenderId, false), Is.EqualTo("B"));
			Assert.That(SpzGoHostPrefs.GetPath(SpzGoHosts.PainterId, true), Is.EqualTo("C"));
			Assert.That(SpzGoHostPrefs.GetPath(SpzGoHosts.PainterId, false), Is.EqualTo(""));
		}

		[Test]
		public void HostBasis_MatchesTheSharedBasisForEverySelection() {
			for (int order = 0; order < ExportAxisSettings.AxisOrderNames.Length; order++) {
				for (int flip = 0; flip < ExportAxisSettings.FlipNames.Length; flip++) {
					SpzGoHostPrefs.SetAxisOrderIndex(SpzGoHosts.ZBrushId, order);
					SpzGoHostPrefs.SetFlipIndex(SpzGoHosts.ZBrushId, flip);

					ExportAxisSettings.SetAxisOrderIndex(order);
					ExportAxisSettings.SetFlipIndex(flip);

					var hostBasis = SpzGoHostPrefs.GetExportBasis(SpzGoHosts.ZBrushId);
					var shared = ExportAxisSettings.Snapshot();
					Assert.That(hostBasis.Order, Is.EqualTo(shared.Order), $"order {order} flip {flip}");
					Assert.That(hostBasis.FlipX, Is.EqualTo(shared.FlipX), $"order {order} flip {flip}");
					Assert.That(hostBasis.FlipY, Is.EqualTo(shared.FlipY), $"order {order} flip {flip}");
					Assert.That(hostBasis.FlipZ, Is.EqualTo(shared.FlipZ), $"order {order} flip {flip}");
				}
			}
		}

		[Test]
		public void ActivatingAHost_PushesItsOwnBasisIntoTheExportPipeline() {
			// The writers snapshot the shared basis, so a host transfer that skipped this step would
			// silently reuse whichever host ran last.
			SpzGoHostPrefs.SetAxisOrderIndex(SpzGoHosts.BlenderId, (int)ExportAxisSettings.AxisOrder.XZY);
			SpzGoHostPrefs.SetFlipIndex(SpzGoHosts.BlenderId, 1);
			SpzGoHostPrefs.SetAxisOrderIndex(SpzGoHosts.PainterId, (int)ExportAxisSettings.AxisOrder.YZX);
			SpzGoHostPrefs.SetFlipIndex(SpzGoHosts.PainterId, 3);

			SpzGoHostPrefs.ApplyExportBasisToShared(SpzGoHosts.BlenderId);
			Assert.That(ExportAxisSettings.Snapshot().Order, Is.EqualTo(ExportAxisSettings.AxisOrder.XZY));
			Assert.That(ExportAxisSettings.FlipIndex, Is.EqualTo(1));

			SpzGoHostPrefs.ApplyExportBasisToShared(SpzGoHosts.PainterId);
			Assert.That(ExportAxisSettings.Snapshot().Order, Is.EqualTo(ExportAxisSettings.AxisOrder.YZX));
			Assert.That(ExportAxisSettings.FlipIndex, Is.EqualTo(3));

			Assert.That(SpzGoHostPrefs.GetAxisOrderIndex(SpzGoHosts.BlenderId),
				Is.EqualTo((int)ExportAxisSettings.AxisOrder.XZY),
				"pushing Painter's basis must not rewrite Blender's stored one");
		}

		[Test]
		public void ExchangePath_ResolvesHostIdAndAppliesThatBasisOnDccExport() {
			// Logo Export already called ApplyExportBasisToShared; DCC HTTP only had a path — without
			// path/host resolution a ZBrush pull would reuse Blender's last shared basis.
			SpzGoHostPrefs.SetAxisOrderIndex(SpzGoHosts.BlenderId, (int)ExportAxisSettings.AxisOrder.XYZ);
			SpzGoHostPrefs.SetFlipIndex(SpzGoHosts.BlenderId, 0);
			SpzGoHostPrefs.SetAxisOrderIndex(SpzGoHosts.ZBrushId, (int)ExportAxisSettings.AxisOrder.YZX);
			SpzGoHostPrefs.SetFlipIndex(SpzGoHosts.ZBrushId, 4);
			SpzGoHostPrefs.ApplyExportBasisToShared(SpzGoHosts.BlenderId);

			string zbPath = System.IO.Path.Combine(
				"C:", "tmp", "StableProjectorzGO_exchange", SpzGoHosts.ZBrushId, "from_spz.fbx");
			Assert.That(SpzGoHostPrefs.TryResolveHostIdFromExchangePath(zbPath),
				Is.EqualTo(SpzGoHosts.ZBrushId));
			Assert.That(SpzGoHostPrefs.TryApplyExportBasisForPath(zbPath, null), Is.True);
			Assert.That(ExportAxisSettings.Snapshot().Order, Is.EqualTo(ExportAxisSettings.AxisOrder.YZX));
			Assert.That(ExportAxisSettings.FlipIndex, Is.EqualTo(4));

			string flatBlender = System.IO.Path.Combine(
				"C:", "tmp", "StableProjectorzGO_exchange", "from_spz.fbx");
			Assert.That(SpzGoHostPrefs.TryResolveHostIdFromExchangePath(flatBlender),
				Is.EqualTo(SpzGoHosts.BlenderId));

			SpzGoHostPrefs.SetAxisOrderIndex(SpzGoHosts.PainterId, (int)ExportAxisSettings.AxisOrder.ZYX);
			Assert.That(SpzGoHostPrefs.TryApplyExportBasisForPath(@"D:\elsewhere\custom.fbx", SpzGoHosts.PainterId),
				Is.True);
			Assert.That(ExportAxisSettings.Snapshot().Order, Is.EqualTo(ExportAxisSettings.AxisOrder.ZYX));
			Assert.That(SpzGoHostPrefs.TryApplyExportBasisForPath(@"D:\elsewhere\custom.fbx", null), Is.False);
		}

		[Test]
		public void FastPathExportToPath_WiresHostBasisApplyBeforeSave() {
			string src = System.IO.File.ReadAllText(
				System.IO.Path.GetFullPath("Assets/_gm/Features/AddonSystem/FastPath_API.cs"));
			int idx = src.IndexOf("public bool Export3DWithTexturesToPath", System.StringComparison.Ordinal);
			Assert.That(idx, Is.GreaterThanOrEqualTo(0));
			string body = src.Substring(idx, System.Math.Min(1800, src.Length - idx));
			Assert.That(body, Does.Contain("TryApplyExportBasisForPath"),
				"DCC export must push host axis prefs before Save_MGR writes the mesh");
			Assert.That(body, Does.Contain("hostId"),
				"optional hostId from HTTP/TCP must reach the basis apply");
		}

		[Test]
		public void Blender_AdoptsTheAxisSettingUsersAlreadyHad() {
			ExportAxisSettings.SetAxisOrderIndex((int)ExportAxisSettings.AxisOrder.ZYX);
			ExportAxisSettings.SetFlipIndex(2);

			Assert.That(SpzGoHostPrefs.GetAxisOrderIndex(SpzGoHosts.BlenderId),
				Is.EqualTo((int)ExportAxisSettings.AxisOrder.ZYX));
			Assert.That(SpzGoHostPrefs.GetFlipIndex(SpzGoHosts.BlenderId), Is.EqualTo(2));
			Assert.That(SpzGoHostPrefs.GetAxisOrderIndex(SpzGoHosts.ZBrushId), Is.EqualTo(0),
				"only Blender wrote the pre-host keys");
		}

		[Test]
		public void Migration_DoesNotOverwriteAnExplicitHostChoice() {
			SpzGoHostPrefs.SetAxisOrderIndex(SpzGoHosts.BlenderId, (int)ExportAxisSettings.AxisOrder.YXZ);
			ExportAxisSettings.SetAxisOrderIndex((int)ExportAxisSettings.AxisOrder.ZYX);

			Assert.That(SpzGoHostPrefs.GetAxisOrderIndex(SpzGoHosts.BlenderId),
				Is.EqualTo((int)ExportAxisSettings.AxisOrder.YXZ));
		}

		[Test]
		public void OutOfRangeSelections_Clamp() {
			SpzGoHostPrefs.SetAxisOrderIndex(SpzGoHosts.ZBrushId, 999);
			SpzGoHostPrefs.SetFlipIndex(SpzGoHosts.ZBrushId, -4);
			Assert.That(SpzGoHostPrefs.GetAxisOrderIndex(SpzGoHosts.ZBrushId),
				Is.EqualTo(ExportAxisSettings.AxisOrderNames.Length - 1));
			Assert.That(SpzGoHostPrefs.GetFlipIndex(SpzGoHosts.ZBrushId), Is.EqualTo(0));
		}

		[Test]
		public void WidgetsResolveTheirHostByWalkingUpToTheSection() {
			var panel = new GameObject("AddonPanel_StableProjectorzGO_SPZ GO");
			try {
				var section = new GameObject(SpzGoHostSection.SectionName(SpzGoHosts.ZBrushId));
				section.transform.SetParent(panel.transform, false);
				var settings = new GameObject("SectionContent_Settings");
				settings.transform.SetParent(section.transform, false);
				var dropdown = new GameObject("Dropdown_" + ExportAxisSettings.AxisOrderLabel);
				dropdown.transform.SetParent(settings.transform, false);

				Assert.That(SpzGoHostSection.HostIdForWidget(dropdown.transform),
					Is.EqualTo(SpzGoHosts.ZBrushId),
					"a settings widget must resolve to its own host, not the first section in the panel");
				Assert.That(SpzGoHostSection.HostIdForWidget(panel.transform), Is.Null,
					"a widget outside every section has no host scope");
			} finally {
				Object.DestroyImmediate(panel);
			}
		}
	}
}

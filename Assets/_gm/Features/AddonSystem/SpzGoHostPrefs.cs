using System;
using System.IO;
using UnityEngine;

namespace spz {

	/// <summary>
	/// Per-host storage for the settings every SPZ GO section shares (spz-go-multi-dcc R15). The control
	/// shape is agnostic; the values are not — Blender's axis basis must not become ZBrush's the moment
	/// the user opens the other section.
	///
	/// The export pipeline still reads one shared basis (<see cref="ExportAxisSettings"/>), because a
	/// mesh is written with a single orientation. <see cref="ApplyExportBasisToShared"/> is what makes
	/// the two agree: the activated host's basis is pushed into the shared one right before its transfer
	/// runs, so the pipeline never has to know which host asked.
	/// </summary>
	public static class SpzGoHostPrefs {
		public const string KeyRoot = "spz.spzgo.";

		public static string AxisOrderKey(string hostId) => KeyRoot + hostId + ".axis_order";
		public static string FlipKey(string hostId) => KeyRoot + hostId + ".flip";
		public static string ModeKey(string hostId) => KeyRoot + hostId + ".mode";
		public static string SettingsOpenKey(string hostId) => KeyRoot + hostId + ".settings_open";
		public static string ImportPathKey(string hostId) => KeyRoot + hostId + ".import_path";
		public static string ExportPathKey(string hostId) => KeyRoot + hostId + ".export_path";

		public static int GetAxisOrderIndex(string hostId) {
			EnsureMigrated(hostId);
			return Mathf.Clamp(PlayerPrefs.GetInt(AxisOrderKey(hostId), 0),
				0, ExportAxisSettings.AxisOrderNames.Length - 1);
		}

		public static void SetAxisOrderIndex(string hostId, int index) {
			PlayerPrefs.SetInt(AxisOrderKey(hostId),
				Mathf.Clamp(index, 0, ExportAxisSettings.AxisOrderNames.Length - 1));
			PlayerPrefs.Save();
		}

		public static int GetFlipIndex(string hostId) {
			EnsureMigrated(hostId);
			return Mathf.Clamp(PlayerPrefs.GetInt(FlipKey(hostId), 0),
				0, ExportAxisSettings.FlipNames.Length - 1);
		}

		public static void SetFlipIndex(string hostId, int index) {
			PlayerPrefs.SetInt(FlipKey(hostId),
				Mathf.Clamp(index, 0, ExportAxisSettings.FlipNames.Length - 1));
			PlayerPrefs.Save();
		}

		/// <summary>Export by default: a section always has exactly one mode selected (R3d).</summary>
		public static SpzGoMode GetMode(string hostId) =>
			PlayerPrefs.GetInt(ModeKey(hostId), (int)SpzGoMode.Export) == (int)SpzGoMode.Import
				? SpzGoMode.Import
				: SpzGoMode.Export;

		public static void SetMode(string hostId, SpzGoMode mode) {
			PlayerPrefs.SetInt(ModeKey(hostId), (int)mode);
			PlayerPrefs.Save();
		}

		/// <summary>Settings drop-tabs start collapsed so three host sections still fit the ribbon (R4).</summary>
		public static bool GetSettingsOpen(string hostId) =>
			PlayerPrefs.GetInt(SettingsOpenKey(hostId), 0) != 0;

		public static void SetSettingsOpen(string hostId, bool open) {
			PlayerPrefs.SetInt(SettingsOpenKey(hostId), open ? 1 : 0);
			PlayerPrefs.Save();
		}

		public static string GetPath(string hostId, bool import) =>
			PlayerPrefs.GetString(import ? ImportPathKey(hostId) : ExportPathKey(hostId), "");

		public static void SetPath(string hostId, bool import, string value) {
			PlayerPrefs.SetString(import ? ImportPathKey(hostId) : ExportPathKey(hostId), value ?? "");
			PlayerPrefs.Save();
		}

		/// <summary>The orientation this host's transfers use, without touching the shared basis.</summary>
		public static ExportAxisSettings.Basis GetExportBasis(string hostId) {
			int mask = ExportAxisSettings.FlipMaskForIndex(GetFlipIndex(hostId));
			return new ExportAxisSettings.Basis(
				(ExportAxisSettings.AxisOrder)GetAxisOrderIndex(hostId),
				(mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0);
		}

		/// <summary>
		/// Hand this host's basis to the shared export settings. Call immediately before running a
		/// transfer for <paramref name="hostId"/> — the FBX writer and the mesh stream both snapshot the
		/// shared basis, so without this a ZBrush export would silently reuse Blender's orientation.
		/// </summary>
		public static void ApplyExportBasisToShared(string hostId) {
			if (SpzGoHosts.Get(hostId) == null) return;
			ExportAxisSettings.SetAxisOrderIndex(GetAxisOrderIndex(hostId));
			ExportAxisSettings.SetFlipIndex(GetFlipIndex(hostId));
		}

		/// <summary>
		/// Prefer an explicit host id from the DCC request; otherwise infer from the exchange folder
		/// (<c>.../StableProjectorzGO_exchange/&lt;hostId&gt;/from_spz.fbx</c>). Flat Blender exchange
		/// (no host segment) maps to Blender. Returns null when the path is outside the exchange tree
		/// and no hint was given — callers leave the shared basis alone in that case.
		/// </summary>
		public static string ResolveHostIdForExport(string meshFilePath, string hostIdHint) {
			if (!string.IsNullOrEmpty(hostIdHint) && SpzGoHosts.Get(hostIdHint) != null)
				return SpzGoHosts.Get(hostIdHint).Id;
			return TryResolveHostIdFromExchangePath(meshFilePath);
		}

		/// <summary>
		/// Apply the host basis that belongs to this export, using <paramref name="hostIdHint"/> when
		/// present, else the exchange path. No-op when neither identifies a registered host.
		/// </summary>
		public static bool TryApplyExportBasisForPath(string meshFilePath, string hostIdHint = null) {
			string hostId = ResolveHostIdForExport(meshFilePath, hostIdHint);
			if (hostId == null) return false;
			ApplyExportBasisToShared(hostId);
			return true;
		}

		public static string TryResolveHostIdFromExchangePath(string meshFilePath) {
			if (string.IsNullOrEmpty(meshFilePath)) return null;
			string norm;
			try {
				norm = Path.GetFullPath(meshFilePath);
			} catch {
				return null;
			}
			string[] parts = norm.Split(
				new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
				StringSplitOptions.RemoveEmptyEntries);
			const string exchangeFolder = "StableProjectorzGO_exchange";
			for (int i = 0; i < parts.Length; i++) {
				if (!string.Equals(parts[i], exchangeFolder, StringComparison.OrdinalIgnoreCase))
					continue;
				if (i + 1 >= parts.Length)
					return SpzGoHosts.BlenderId;
				string next = parts[i + 1];
				var host = SpzGoHosts.Get(next);
				if (host != null)
					return host.Id;
				// Flat legacy Blender layout: exchange/from_spz.fbx (next segment is the file name).
				return SpzGoHosts.BlenderId;
			}
			return null;
		}

		/// <summary>
		/// Blender's axis basis lived in the global export keys before hosts existed. Adopt it once, so a
		/// user who set XZY / flip Y last week does not silently get identity back on the next export.
		/// Only Blender inherits: the other hosts never wrote those keys.
		/// </summary>
		static void EnsureMigrated(string hostId) {
			if (!string.Equals(hostId, SpzGoHosts.BlenderId, System.StringComparison.OrdinalIgnoreCase))
				return;
			if (PlayerPrefs.HasKey(AxisOrderKey(hostId)) || PlayerPrefs.HasKey(FlipKey(hostId)))
				return;
			bool hasLegacy = PlayerPrefs.HasKey(ExportAxisSettings.AxisOrderPrefKey)
				|| PlayerPrefs.HasKey(ExportAxisSettings.FlipXPrefKey)
				|| PlayerPrefs.HasKey(ExportAxisSettings.FlipYPrefKey)
				|| PlayerPrefs.HasKey(ExportAxisSettings.FlipZPrefKey);
			if (!hasLegacy)
				return;
			PlayerPrefs.SetInt(AxisOrderKey(hostId), ExportAxisSettings.AxisOrderIndex);
			PlayerPrefs.SetInt(FlipKey(hostId), ExportAxisSettings.FlipIndex);
			PlayerPrefs.Save();
		}
	}
}

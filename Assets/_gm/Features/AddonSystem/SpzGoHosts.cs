using System;
using System.Collections.Generic;

namespace spz {

	/// <summary>
	/// Direction of the next handoff, chosen per host. Import is host → SPZ, Export is SPZ → host
	/// (spz-go-multi-dcc R8). The mode only selects a direction; the host logo runs it.
	/// </summary>
	public enum SpzGoMode {
		Import = 0,
		Export = 1,
	}

	/// <summary>
	/// One DCC the SPZ GO panel can hand meshes to. Sections are built by walking this registry, so a
	/// later host is an entry here plus a transport — not another hand-written slab of panel code.
	/// </summary>
	public sealed class SpzGoHost {
		public readonly string Id;
		public readonly string DisplayName;
		/// <summary>Neutral placeholder for the activate face until logo art / licensing is settled.</summary>
		public readonly string Glyph;
		/// <summary>
		/// False while the host has no bridge behind it. Activate must report <see cref="NotReadyReason"/>
		/// instead of a success line the transfer never earned (spz-go-multi-dcc R13).
		/// </summary>
		public readonly bool BridgeReady;
		public readonly string NotReadyReason;
		/// <summary>
		/// True when the host bridge watches the exchange folder and answers pull requests on its own.
		/// False for hosts that only answer after an explicit in-DCC button (ZBrush), so Import status
		/// must not claim "it pushes when open".
		/// </summary>
		public readonly bool AnswersPullAutomatically;

		public SpzGoHost(string id, string displayName, string glyph, bool bridgeReady, string notReadyReason,
			bool answersPullAutomatically = true) {
			Id = id;
			DisplayName = displayName;
			Glyph = glyph;
			BridgeReady = bridgeReady;
			NotReadyReason = notReadyReason;
			AnswersPullAutomatically = answersPullAutomatically;
		}
	}

	public static class SpzGoHosts {
		public const string BlenderId = "blender";
		public const string ZBrushId = "zbrush";
		public const string PainterId = "painter";

		public static readonly SpzGoHost Blender = new SpzGoHost(
			BlenderId, "Blender", "BL", true, null, answersPullAutomatically: true);
		public static readonly SpzGoHost ZBrush = new SpzGoHost(
			ZBrushId, "ZBrush", "ZB", false,
			"ZBrush bridge not installed yet — open Settings → Install into ZBrush",
			answersPullAutomatically: false);
		public static readonly SpzGoHost Painter = new SpzGoHost(
			PainterId, "Substance Painter", "SP", false,
			"Substance Painter bridge not installed yet — open Settings → Install into Substance Painter",
			answersPullAutomatically: true);

		static readonly SpzGoHost[] _all = { Blender, ZBrush, Painter };

		public static IReadOnlyList<SpzGoHost> All => _all;

		/// <summary>
		/// Runtime probe: true when a host whose <see cref="SpzGoHost.BridgeReady"/> is compile-time false
		/// has since had its file-exchange bridge installed on this machine. The app points this at the
		/// install-marker check; when unset (headless / contract tests) stubs stay honestly not-ready so no
		/// unearned success line can appear (spz-go-multi-dcc R13).
		/// </summary>
		public static Func<string, bool> BridgeInstalledProbe = null;

		/// <summary>
		/// Effective readiness: a host that ships working is always ready; a stub host is ready only once
		/// its bridge is installed and the probe confirms it. Callers gate activate on this, not the raw
		/// compile-time flag, so installing a ZBrush/Painter bridge lights its logo without a code change.
		/// </summary>
		public static bool IsBridgeReady(string id) {
			var h = Get(id);
			if (h == null) return false;
			if (h.BridgeReady) return true;
			var probe = BridgeInstalledProbe;
			return probe != null && probe(id);
		}

		public static SpzGoHost Get(string id) {
			if (string.IsNullOrEmpty(id)) return null;
			for (int i = 0; i < _all.Length; i++) {
				if (string.Equals(_all[i].Id, id, StringComparison.OrdinalIgnoreCase))
					return _all[i];
			}
			return null;
		}
	}

	/// <summary>
	/// The shape every host section shares. Widget names are host-qualified because all three sections
	/// live in one panel and would otherwise collide on identical labels; the labels the user reads stay
	/// unqualified. Names are resolved here rather than spelled out at each call site so the builder,
	/// the change handlers and the contract tests cannot drift apart.
	/// </summary>
	public static class SpzGoHostSection {
		public const string ImportModeLabel = "Import";
		public const string ExportModeLabel = "Export";
		public const string SettingsLabel = "Settings";
		public const string AutofillLabel = "Autofill paths";
		public const string ImportPathLabel = "Import path";
		public const string ExportPathLabel = "Export path";

		/// <summary>
		/// Controls no host may omit (spz-go-multi-dcc R15). Values are host-scoped, the shape is not.
		/// </summary>
		public static readonly string[] MandatorySettingsLabels = {
			ExportAxisSettings.AxisOrderLabel,
			ExportAxisSettings.FlipLabel,
			AutofillLabel,
			ImportPathLabel,
			ExportPathLabel,
		};

		public const string SectionNamePrefix = "HostSection_";
		public const string LogoNamePrefix = "HostLogo_";
		public const string ModeToggleNamePrefix = "ModeToggle_";

		public static string SectionName(string hostId) => SectionNamePrefix + hostId;
		public static string LogoName(string hostId) => LogoNamePrefix + hostId;

		public static string ModeToggleName(string hostId, SpzGoMode mode) =>
			ModeToggleNamePrefix + hostId + "_" + (mode == SpzGoMode.Import ? ImportModeLabel : ExportModeLabel);

		/// <summary>
		/// Callback names are registered in one flat per-add-on table, so three sections that each own an
		/// "Autofill paths" button need the host in the name or only the last one registered would run.
		/// </summary>
		public const string CallbackHostSeparator = "__";

		public static string QualifyCallback(string baseName, string hostId) =>
			string.IsNullOrEmpty(hostId) ? baseName : baseName + CallbackHostSeparator + hostId;

		public static string BaseCallbackName(string qualified) {
			if (string.IsNullOrEmpty(qualified)) return qualified;
			int at = qualified.IndexOf(CallbackHostSeparator, StringComparison.Ordinal);
			return at > 0 ? qualified.Substring(0, at) : qualified;
		}

		public static string HostIdFromCallback(string qualified) {
			if (string.IsNullOrEmpty(qualified)) return null;
			int at = qualified.IndexOf(CallbackHostSeparator, StringComparison.Ordinal);
			return at > 0 ? qualified.Substring(at + CallbackHostSeparator.Length) : null;
		}

		/// <summary>
		/// The host a widget belongs to, found by walking up to its section container. Change handlers
		/// resolve the host this way instead of taking a host argument, so widgets added by Python land
		/// in the same host scope as natively seeded ones with no extra RPC surface.
		/// </summary>
		public static string HostIdForWidget(UnityEngine.Transform widget) {
			for (var t = widget; t != null; t = t.parent) {
				string name = t.name;
				if (name != null && name.StartsWith(SectionNamePrefix, StringComparison.Ordinal))
					return name.Substring(SectionNamePrefix.Length);
			}
			return null;
		}
	}
}

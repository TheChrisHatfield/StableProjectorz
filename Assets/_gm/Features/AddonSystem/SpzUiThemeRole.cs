using UnityEngine;

namespace spz {

	/// <summary>
	/// Traditional control kind → Nomad BoundChrome treatment.
	/// Traditional / authored SPZ UI remains source of truth; roles keep Nomad aligned.
	/// </summary>
	public enum SpzUiThemeRole {
		/// <summary>Resolve from hierarchy heuristics.</summary>
		Auto = 0,
		/// <summary>Domain art / skip chrome (RawImage, swatches).</summary>
		Skip = 1,
		/// <summary>Circle dial or numeric overlay — zero tracking.</summary>
		DialValue = 2,
		/// <summary>Short Button/Toggle chrome caption.</summary>
		CompactTool = 3,
		/// <summary>Multi-line list / long dropdown caption.</summary>
		ReadableBody = 4,
		/// <summary>Workflow stacked cell — caller must invoke stacked apply with glyph.</summary>
		StripStack = 5,
		/// <summary>Narrow Gen Art docks (FULL/SRN).</summary>
		NarrowDock = 6,
		/// <summary>PROMPT row header.</summary>
		PromptHeader = 7,
		/// <summary>Prompt +/- polarity glyph.</summary>
		PromptSign = 8,
		/// <summary>Button/Toggle/Dropdown shell.</summary>
		SelectableFace = 9,
		/// <summary>Download-more SlideOut list chrome.</summary>
		DownloadMoreSlide = 10,
		/// <summary>TMP_InputField text / placeholder.</summary>
		FieldText = 11,
		/// <summary>Generic BoundChrome TMP (color/scale; caller may zero spacing).</summary>
		BoundChromeTmp = 12,
	}

	/// <summary>
	/// Optional override when traditional hierarchy heuristics are ambiguous.
	/// Place on the TMP/Graphic (or an ancestor) under an ownership root.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class SpzUiThemeRoleTag : MonoBehaviour {
		public SpzUiThemeRole role = SpzUiThemeRole.Auto;
	}

	/// <summary>
	/// Options for <see cref="SpzUiThemeOps.ApplyBoundChromeRolesUnder"/>.
	/// Default (zero-init) enables all standard passes.
	/// </summary>
	public struct SpzUiThemeRoleMatrixOptions {
		/// <summary>When true, skip SelectableFace / dial / TMP / slide / field passes that would touch this component.</summary>
		public System.Func<Component, bool> Exclude;
		/// <summary>Detect PROMPT headers and +/- polarity signs by text/name heuristics.</summary>
		public bool DetectPromptLabels;
		/// <summary>Use <see cref="SpzUiThemeOps.ThemeFlatToolToggle"/> for Toggles (Soft/Tileable style) instead of checkbox silo.</summary>
		public bool PreferFlatToolToggles;
		/// <summary>When true, unclassified loose TMP uses CompactTool (ControlNet field labels) instead of BoundChromeTmp.</summary>
		public bool CompactLooseLabels;
		/// <summary>When set, skip the matching pass (default false = run pass).</summary>
		public bool SkipSelectables;
		public bool SkipTmp;
		public bool SkipDials;
		public bool SkipDownloadSlides;
		public bool SkipInputFields;
		public bool SkipLayoutScale;
	}

}

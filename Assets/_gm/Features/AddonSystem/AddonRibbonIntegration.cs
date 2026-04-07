using UnityEngine;

namespace spz {

	/// <summary>
	/// <para><b>Add-on ribbon paradigm (single contract)</b></para>
	/// <list type="number">
	/// <item><description>Each installed add-on folder name is the canonical <c>addonId</c> (e.g. <c>MeshTools</c>).</description></item>
	/// <item><description>Python <c>register()</c> calls <c>api.ui.create_panel(addon_id, title)</c> — <c>title</c> is the tab label; <c>addon_id</c> must match the folder id.</description></item>
	/// <item><description>Unity handles <c>spz.ui.create_panel</c> in <see cref="Addon_SocketServer"/> → <see cref="AddonUI_MGR.CreatePanel"/>.</description></item>
	/// <item><description><see cref="AddonUI_MGR.CreatePanel"/> routes the shell through <see cref="CommandRibbon_UI.GetOrCreatePanelForAddon"/> when <see cref="CommandRibbon_UI"/> exists: one runtime tab + one content panel per <c>addonId</c>. Add-ons are not part of the paint feature; they only share the command ribbon tab strip and body stack with Art, ControlNet, Paint, etc. The shell <see cref="UnityEngine.RectTransform"/> matches built-in tab bodies when possible. Strip tab cells use the same hierarchy as the runtime Paint tab button (TabBg + active slice + label, no root Image) for layout only.</description></item>
	/// <item><description>Optional prefab hook: assign the CommandRibbon tab-bodies override field to a dedicated stack rect, or call <see cref="CommandRibbon_UI.GetRibbonTabBodiesParentRect"/> / <see cref="CommandRibbon_UI.TrySelectAddonRibbonTab"/> from C#.</description></item>
	/// <item><description>Tab identity in <see cref="TabsGroup_UI"/> is <see cref="TabIdForAddon"/> (prefix <see cref="TabIdPrefix"/>). Keyboard: Shift+6..9 map to loaded add-ons by folder id sorted ordinal case-insensitive (see <see cref="CommandRibbon_UI"/>).</description></item>
	/// <item><description>Teardown: <see cref="Addon_MGR.UnloadAddon"/> → <see cref="AddonUI_MGR.DestroyAddonUI"/> + <see cref="CommandRibbon_UI.RemoveAddonPanel"/>.</description></item>
	/// </list>
	/// <para>New add-ons should only implement Python <c>register()</c> using the spz UI API; do not create ribbon tabs manually in Unity.</para>
	/// </summary>
	public static class AddonRibbonIntegration {

		/// <summary>Prefix for <see cref="TabsGroupElem_UI"/> runtime titles / <see cref="TabsGroup_UI.SwitchTab"/> arguments.</summary>
		public const string TabIdPrefix = "addon_";

		/// <summary>Stable tab id for an add-on (must stay in sync with <see cref="CommandRibbon_UI"/>).</summary>
		public static string TabIdForAddon (string addonId) {
			if (string.IsNullOrEmpty(addonId)) return TabIdPrefix;
			return TabIdPrefix + addonId;
		}

		/// <summary>True if <paramref name="tabId"/> was produced by <see cref="TabIdForAddon"/>.</summary>
		public static bool IsAddonTabId (string tabId) {
			return !string.IsNullOrEmpty(tabId)
			       && tabId.StartsWith(TabIdPrefix, System.StringComparison.Ordinal);
		}

		/// <summary>Returns the <c>addonId</c> for an add-on tab id, or null if not an add-on tab.</summary>
		public static string AddonIdFromTabId (string tabId) {
			if (!IsAddonTabId(tabId)) return null;
			return tabId.Substring(TabIdPrefix.Length);
		}

		/// <summary>Resolves the command ribbon the same way as add-on UI creation (instance, then inactive search).</summary>
		public static CommandRibbon_UI ResolveCommandRibbon () {
			var r = CommandRibbon_UI.instance;
			if (r != null) return r;
			return Object.FindObjectOfType<CommandRibbon_UI>(true);
		}
	}
}

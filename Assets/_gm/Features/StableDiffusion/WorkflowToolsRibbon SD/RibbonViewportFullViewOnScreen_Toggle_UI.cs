using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace spz {

	/// <summary>
	/// Docks FULL/SCREEN directly <b>above</b> the GEN ART control by inserting a sibling wrapper under the <see cref="VerticalLayoutGroup"/> ancestor of GEN ART (prefab <c>GenerateButtons_Main_UI (vertGroup)</c>) and letting the VLG + <see cref="LayoutElement"/> own sizing. GEN ART's immediate parent (<c>stretch me (mask)</c>) uses anchor-based placement, so a sibling there overflows the parent rect and never shows—hence the VLG-ancestor insertion. Matches GEN ART's sliced sprite, fill, two-line bold black label and optional corner triangle. Suppresses the column-wide cream <c>frame</c> while docked and draws a face-sized adaptive border on FULL/SRN and OPEN RIGHT instead. Not a right-panel command-ribbon tab.
	/// Uses <see cref="SD_WorkflowOptionsRibbon_UI"/> or <see cref="GenerateButtons_Main_UI"/> to host this behaviour + JSON-RPC <c>spz.ui.attach_viewport_fullview_toggle</c> / <see cref="RibbonDock_ButtonSpec"/>. Build coroutines are hosted from <see cref="Addon_MGR"/> / <see cref="MainViewport_UI"/> (not the right panel tab strip). Enable the StreamingAssets add-on <c>RibbonOnlyFullscreen</c> via <see cref="Addon_MGR"/> to attach the dock.
	/// </summary>
	public class RibbonViewportFullViewOnScreen_Toggle_UI : MonoBehaviour {

		const string RowName = "RibbonRow_FullViewOnScreen";
		const string SpacerName = "RibbonRow_FullViewOnScreen_Spacer";
		const string MenuRowName = "RibbonRow_FullViewOnScreen_Menu";
		const string FaceBorderName = "DockFaceBorder";
		const string GenButtonsColumnFrameName = "frame";
		const int MaxWaitFrames = 240;
		/// <summary>Spacer row height under FULL SRN in the VLG; pushes Gen Art and re-do down without stretching the button face.</summary>
		const float ExtraBottomGapPx = 152f;
		/// <summary>Floor when adaptive clearance shrinks the spacer so FULL stays below DimensionMode SD circle.</summary>
		const float MinAdaptiveBottomGapPx = 24f;
		/// <summary>Extra local-px gap below the DimensionMode disc after overlap is resolved.</summary>
		const float DimModeClearancePx = 10f;
		/// <summary>FULL/SRN + OPEN RIGHT label design size (Nomad BoundChrome seed).</summary>
		const float DockLabelBasePt = 13f;

		static readonly Color FallbackFill = new Color(217f / 255f, 144f / 255f, 88f / 255f, 1f);

		static readonly Dictionary<int, RibbonDock_ButtonSpec> PendingDockSpecs = new Dictionary<int, RibbonDock_ButtonSpec>();
		static readonly List<RibbonViewportFullViewOnScreen_Toggle_UI> RegisteredInstances = new List<RibbonViewportFullViewOnScreen_Toggle_UI>();

		SD_WorkflowOptionsRibbon_UI _host;
		RibbonDock_ButtonSpec _spec;
		Image _bgImage;
		Button _dockButton;
		Color _fillBase = Color.white;
		Color _authoredFillBase = FallbackFill;
		bool _built;
		Coroutine _buildRoutine;
		MonoBehaviour _buildCoroutineOwner;
		RectTransform _fullViewMenuRt;
		CanvasGroup _fullViewMenuCg;
		Coroutine _fullViewMenuAnimRoutine;
		bool _fullViewMenuOpen;
		float _fullViewMenuOpenedAtUnscaledTime;
		/// <summary>Label on the secondary "open/hide right dock" control (OPEN RIGHT vs HIDE RIGHT).</summary>
		TextMeshProUGUI _openRightDockLabel;
		Image _fullSrnLineIcon;
		Image _openRightLineIcon;
		/// <summary>GenerateButtons root cream <c>frame</c> — stretch-fills the whole column; suppressed while dock is up.</summary>
		GameObject _suppressedGenButtonsColumnFrame;
		bool _ownsColumnFrameSuppress;
		static int s_columnFrameSuppressCount;
		static GameObject s_columnFrameGo;
		static bool s_columnFrameWasActive;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		static void ResetColumnFrameSuppressStatics() {
			// Enter Play Mode Options can disable domain reload — static suppress count would stick
			// and leave GenerateButtons cream frame off after the next Play.
			s_columnFrameSuppressCount = 0;
			s_columnFrameGo = null;
			s_columnFrameWasActive = false;
			// Stale instance IDs in PendingDockSpecs can re-apply the wrong dock spec after Play.
			RegisteredInstances.Clear();
			PendingDockSpecs.Clear();
		}

		Sprite _cachedFaceBorderSprite;
		Color _cachedFaceBorderColor = new Color(1f, 1f, 1f, 0.95f);
		float _cachedFaceBorderPpu = 6f;

		RectTransform _genArtAnchorRestoreTarget;
		Vector2 _genArtSavedAnchorMin;
		Vector2 _genArtSavedAnchorMax;
		Vector2 _genArtSavedOffsetMin;
		Vector2 _genArtSavedOffsetMax;
		bool _savedGenArtAnchors;
		RectTransform _builtRowRt;
		RectTransform _spacerRowRt;
		float _appliedBottomGapPx = -1f;
		int _adaptClearanceFrame = -1;
		bool _lastDimChoicesFanOpen;

		static bool SpecsEqual(in RibbonDock_ButtonSpec a, in RibbonDock_ButtonSpec b) {
			return string.Equals(a.CommandId, b.CommandId, StringComparison.Ordinal)
				&& string.Equals(a.Label ?? string.Empty, b.Label ?? string.Empty, StringComparison.Ordinal);
		}

		static bool SpecsSameCommand(in RibbonDock_ButtonSpec a, in RibbonDock_ButtonSpec b) {
			return string.Equals(a.CommandId, b.CommandId, StringComparison.Ordinal);
		}

		public static void EnsureCreated(SD_WorkflowOptionsRibbon_UI host, RibbonDock_ButtonSpec spec) {
			if (host == null) {
				return;
			}
			_ = ApplyComponentToGameObject(host.gameObject, spec);
		}

		/// <summary>When <see cref="SD_WorkflowOptionsRibbon_UI"/> is not in the scene yet, still mount the dock on the same GameObject as <see cref="GenerateButtons_Main_UI"/> so the strip above GEN ART can build (add-on enable / HTTP-off path).</summary>
		public static bool TryEnsureOnGenerateButtonsStrip(RibbonDock_ButtonSpec spec) {
			var gbm = ResolveGenerateButtonsMain();
			if (gbm == null) {
				return false;
			}
			// CoEnsure used to AddComponent on GBM while a dock already lived on the workflow host → dual instances.
			DestroyDockComponentsNotOn(gbm.gameObject);
			return ApplyComponentToGameObject(gbm.gameObject, spec);
		}

		/// <summary>True if any dock MonoBehaviour exists (including inactive hosts / unfinished builds).</summary>
		public static bool HasAnyDockComponent() {
			PruneRegisteredInstances();
			for (int i = 0; i < RegisteredInstances.Count; i++) {
				if (RegisteredInstances[i] != null)
					return true;
			}
			return false;
		}

		static void PruneRegisteredInstances() {
			for (int i = RegisteredInstances.Count - 1; i >= 0; i--) {
				if (RegisteredInstances[i] == null)
					RegisteredInstances.RemoveAt(i);
			}
		}

		/// <summary>Remove stray dock behaviours so only one host owns FULL/SRN (prefer Gen Art strip).</summary>
		static void DestroyDockComponentsNotOn(GameObject keepHost) {
			PruneRegisteredInstances();
			for (int i = RegisteredInstances.Count - 1; i >= 0; i--) {
				var c = RegisteredInstances[i];
				if (c == null) {
					RegisteredInstances.RemoveAt(i);
					continue;
				}
				if (keepHost != null && c.gameObject == keepHost)
					continue;
				c.TeardownForAddonDisabled();
				RegisteredInstances.RemoveAt(i);
				UnityEngine.Object.Destroy(c);
			}
			// Also strip orphans that never registered (edge cases).
			var all = UnityEngine.Object.FindObjectsByType<RibbonViewportFullViewOnScreen_Toggle_UI>(
				FindObjectsInactive.Include, FindObjectsSortMode.None);
			for (int i = 0; i < all.Length; i++) {
				var c = all[i];
				if (c == null) continue;
				if (keepHost != null && c.gameObject == keepHost) continue;
				c.TeardownForAddonDisabled();
				UnityEngine.Object.Destroy(c);
			}
		}

		/// <returns>False if the dock component could not be added (e.g. <see cref="GameObject.AddComponent"/> failed).</returns>
		static bool ApplyComponentToGameObject(GameObject go, RibbonDock_ButtonSpec spec) {
			if (go == null) {
				return false;
			}
			var c = go.GetComponent<RibbonViewportFullViewOnScreen_Toggle_UI>();
			bool createdNow = false;
			bool commandChanged = false;
			if (c == null) {
				createdNow = true;
				int gid = go.GetInstanceID();
				PendingDockSpecs[gid] = spec;
				try {
					c = go.AddComponent<RibbonViewportFullViewOnScreen_Toggle_UI>();
				}
				finally {
					PendingDockSpecs.Remove(gid);
				}
			} else {
				commandChanged = !SpecsSameCommand(c._spec, spec);
				c.ApplySpec(spec);
			}
			// AddComponent can throw (e.g. game object being destroyed); do not dereference a still-null c.
			if (c == null) {
				return false;
			}
			bool rowDestroyed = c._builtRowRt == null || c._builtRowRt.gameObject == null;
			bool spacerDestroyed = c._spacerRowRt == null || c._spacerRowRt.gameObject == null;
			// Do NOT treat !activeInHierarchy as missing — Gen Art / cancel / layout churn hides the row
			// briefly; NotifyAttachRequested would TearDownBuiltDock and the button stays gone.
			if (createdNow) {
				// Awake already started CoBuildWhenGenArtReady. NotifyAttachRequested would TearDown → flash.
				if (!c._built && c._buildRoutine == null)
					c.NudgeOrRebuildWithoutTear();
			} else if (commandChanged) {
				// ApplySpec already tore; ensure a build starts.
				if (!c._built && c._buildRoutine == null)
					c.NudgeOrRebuildWithoutTear();
			} else if (!c._built) {
				// Addon_MGR polls attach every frame while waiting for Gen Art. Do not TearDownBuiltDock
				// while CoBuildWhenGenArtReady is already running — that aborted the wait forever.
				if (c._buildRoutine == null)
					c.NudgeOrRebuildWithoutTear();
			} else if (rowDestroyed || spacerDestroyed) {
				c.NotifyAttachRequested();
			} else if (!c._builtRowRt.gameObject.activeInHierarchy
			           || (c._spacerRowRt != null && !c._spacerRowRt.gameObject.activeInHierarchy)) {
				c.NudgeOrRebuildWithoutTear();
			}
			return true;
		}

		/// <summary>True when a dock row exists (active or not) or a build is still running — stop re-attach thrash.</summary>
		public static bool IsAnyDockBuiltOrBuilding() {
			PruneRegisteredInstances();
			for (int i = 0; i < RegisteredInstances.Count; i++) {
				var c = RegisteredInstances[i];
				if (c == null) continue;
				if (c._buildRoutine != null) return true;
				if (c._built && c._builtRowRt != null && c._builtRowRt.gameObject != null) return true;
			}
			return false;
		}

		/// <summary>True if any instance finished layout with an active row (for <see cref="Addon_MGR"/>; RPC can return before async build completes).</summary>
		public static bool IsAnyVisibleBuiltDock() {
			for (int i = 0; i < RegisteredInstances.Count; i++) {
				var c = RegisteredInstances[i];
				if (c == null) {
					continue;
				}
				if (c._built && c._builtRowRt != null && c._builtRowRt && c._builtRowRt.gameObject.activeInHierarchy) {
					return true;
				}
			}
			return false;
		}

		/// <summary>True when a dock build coroutine is still waiting on Gen Art / layout (do not re-tear).</summary>
		public static bool IsAnyDockBuildInFlight() {
			for (int i = 0; i < RegisteredInstances.Count; i++) {
				var c = RegisteredInstances[i];
				if (c != null && c._buildRoutine != null)
					return true;
			}
			return false;
		}

		/// <summary>Nudge all dock instances without full tear (used by paint toolchest layout; avoids wiping the button when switching to Paint / other tabs).</summary>
		public static void NotifyAllAttachRequested() {
			for (int i = RegisteredInstances.Count - 1; i >= 0; i--) {
				var c = RegisteredInstances[i];
				if (c == null) {
					RegisteredInstances.RemoveAt(i);
					continue;
				}
				c.NudgeOrRebuildWithoutTear();
			}
		}

		/// <summary>Removes all dock <see cref="MonoBehaviour"/> instances (e.g. when <see cref="Addon_MGR.RibbonOnlyFullscreenAddonId"/> is disabled). Tears down rows/coroutines first, then strips orphans by name, then removes behaviours.</summary>
		public static void TeardownAllDocksForAddonDisabled() {
			var all = UnityEngine.Object.FindObjectsByType<RibbonViewportFullViewOnScreen_Toggle_UI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			for (int i = 0; i < all.Length; i++) {
				if (all[i] != null) {
					all[i].TeardownForAddonDisabled();
				}
			}
			DestroyAllInjectedFullViewRowsInOpenScenes();
			for (int i = 0; i < all.Length; i++) {
				var c = all[i];
				if (c != null) {
					UnityEngine.Object.Destroy(c);
				}
			}
		}

		/// <summary>Stops build coroutine, removes injected row(s) + spacers, restores Gen Art layout — safe to call while add-on is disabled.</summary>
		public void TeardownForAddonDisabled() {
			TearDownBuiltDock();
		}

		static void DestroyAllInjectedFullViewRowsInOpenScenes() {
			var toDestroy = new List<GameObject>(8);
			for (int si = 0; si < SceneManager.sceneCount; si++) {
				var sc = SceneManager.GetSceneAt(si);
				if (!sc.isLoaded) {
					continue;
				}
				var roots = sc.GetRootGameObjects();
				for (int ri = 0; ri < roots.Length; ri++) {
					CollectNamedFullViewRowRoots(roots[ri].transform, toDestroy);
				}
			}
			for (int i = 0; i < toDestroy.Count; i++) {
				if (toDestroy[i] != null) {
					UnityEngine.Object.DestroyImmediate(toDestroy[i]);
				}
			}
		}

		static void CollectNamedFullViewRowRoots(Transform t, List<GameObject> outList) {
			if (t == null) {
				return;
			}
			// Must include MenuRowName — otherwise DestroyAllInjectedFullViewRowsInOpenScenes leaves
			// orphan OPEN RIGHT rows after TearDown / addon-disable sweeps (DestroyNamedRowsUnderTransform does include it).
			if (string.Equals(t.name, RowName, StringComparison.Ordinal)
			    || string.Equals(t.name, SpacerName, StringComparison.Ordinal)
			    || string.Equals(t.name, MenuRowName, StringComparison.Ordinal)) {
				outList.Add(t.gameObject);
			}
			for (int c = 0; c < t.childCount; c++) {
				CollectNamedFullViewRowRoots(t.GetChild(c), outList);
			}
		}

		public void ApplySpec(RibbonDock_ButtonSpec spec) {
			bool commandChanged = !string.Equals(_spec.CommandId, spec.CommandId, StringComparison.Ordinal);
			bool labelChanged = !string.Equals(_spec.Label ?? string.Empty, spec.Label ?? string.Empty, StringComparison.Ordinal);
			// Label text on the face is hardcoded FULL/SRN; tearing on label-only (Unity FULL\nSCREEN vs Python FULL\nSRN)
			// caused the dock to appear then flash away when enable + HTTP register both attached.
			if (commandChanged) {
				TearDownBuiltDock();
			}
			_spec = spec;
			if (!commandChanged && labelChanged && _built) {
				TryRefreshBuiltLabelText();
			}
		}

		void TryRefreshBuiltLabelText() {
			if (_builtRowRt == null) return;
			var face = SpzUiThemeOps.FindDirectChildIncludingInactive(_builtRowRt, "DockButtonFace") as RectTransform;
			var tmp = face != null ? face.GetComponentInChildren<TextMeshProUGUI>(true) : null;
			if (tmp == null) return;
			ApplyFullSrnLabelStyle(tmp, null, tmp.rectTransform);
		}

		/// <summary>Stops wait/build, removes dock row(s), restores Gen Art anchors. Called on every <see cref="NotifyAttachRequested"/>, <see cref="ApplySpec"/> when spec changes, and <see cref="OnDestroy"/>.</summary>
		void TearDownBuiltDock() {
			ForceHideFullViewMenuInstant();
			StopBuildCoroutineIfAny();
			DestroyStaleFullViewRowsUnderHost();
			_builtRowRt = null;
			_spacerRowRt = null;
			_bgImage = null;
			_dockButton = null;
			_fullViewMenuRt = null;
			_fullViewMenuCg = null;
			_openRightDockLabel = null;
			_fullSrnLineIcon = null;
			_openRightLineIcon = null;
			_built = false;
			_appliedBottomGapPx = -1f;
			_adaptClearanceFrame = -1;
			_lastDimChoicesFanOpen = false;
			RestoreGenerateButtonsColumnFrame();
			RestoreGenArtAnchorsIfSaved();
		}

		void DestroyStaleFullViewRowsUnderHost() {
			DestroyNamedRowsUnderTransform(_host != null ? _host.transform : null);
			// Row is often parented in the viewport inner-left (Gen Art column), not under the SD host transform.
			DestroyNamedRowsUnderTransform(ViewportInnerLeftRibbonRoot());
			var gbm = GenerateButtons_Main_UI.instance;
			if (gbm != null) {
				DestroyNamedRowsUnderTransform(gbm.transform);
				var gar = gbm.GenArtButtonRectTransform;
				if (gar != null && gar.parent != null) {
					DestroyNamedRowsUnderTransform(gar.parent);
				}
			}
		}

		static void DestroyNamedRowsUnderTransform(Transform hostRoot) {
			if (hostRoot == null) {
				return;
			}
			var all = hostRoot.GetComponentsInChildren<Transform>(true);
			for (int i = 0; i < all.Length; i++) {
				var t = all[i];
				// Unity fake-null after Destroy — still skip; use Immediate so same-frame rebuild cannot reuse doomed rows.
				if (t == null || t == hostRoot) {
					continue;
				}
				if (string.Equals(t.name, RowName, StringComparison.Ordinal)
				    || string.Equals(t.name, SpacerName, StringComparison.Ordinal)
				    || string.Equals(t.name, MenuRowName, StringComparison.Ordinal)) {
					// Deferred Destroy leaves the GO in the VLG until EOF; TryBuildOnce then reclaims it and
					// end-of-frame destroy wipes FULL/SRN + OPEN RIGHT after NotifyAttachRequested.
					DestroyImmediate(t.gameObject);
				}
			}
		}

		void RestoreGenArtAnchorsIfSaved() {
			if (!_savedGenArtAnchors || _genArtAnchorRestoreTarget == null) {
				return;
			}
			_genArtAnchorRestoreTarget.anchorMin = _genArtSavedAnchorMin;
			_genArtAnchorRestoreTarget.anchorMax = _genArtSavedAnchorMax;
			_genArtAnchorRestoreTarget.offsetMin = _genArtSavedOffsetMin;
			_genArtAnchorRestoreTarget.offsetMax = _genArtSavedOffsetMax;
			_savedGenArtAnchors = false;
		}

		/// <summary>Full reset + async build. Used for attach, spec change, and explicit reattach — not for <see cref="OnEnable"/> (that would remove the button every time another ribbon tab hid this host).</summary>
		public void NotifyAttachRequested() {
			if (_built && (_builtRowRt == null || _builtRowRt.gameObject == null)) {
				_built = false;
				_builtRowRt = null;
			}
			TearDownBuiltDock();
			TryBeginBuildRoutine();
		}

		/// <summary>Refresh or complete build without tearing an existing row (tab switches / paint layout nudges). Does not call <see cref="TearDownBuiltDock"/>.</summary>
		void NudgeOrRebuildWithoutTear() {
			if (_built && _builtRowRt != null && _builtRowRt.gameObject != null) {
				RefreshActiveFill();
			}
			TryBeginBuildRoutine();
		}

		void Awake() {
			_host = GetComponent<SD_WorkflowOptionsRibbon_UI>();
			int id = gameObject.GetInstanceID();
			if (PendingDockSpecs.TryGetValue(id, out var pending)) {
				_spec = pending;
			}
			RegisteredInstances.Add(this);
			TryBeginBuildRoutine();
		}

		void Start() {
			TryBeginBuildRoutine();
		}

		void OnEnable() {
			ViewportFullViewOnScreen_Driver.ActiveChanged += OnDriverActiveChanged;
			SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
			// Do not use NotifyAttachRequested() here: it always TearDownBuiltDock(), so the button vanishes
			// whenever the host re-enables after Art / Paint / other command-ribbon tab changes.
			NudgeOrRebuildWithoutTear();
			ApplyThemeTokens();
		}

		void OnDisable() {
			ForceHideFullViewMenuInstant();
			ViewportFullViewOnScreen_Driver.ActiveChanged -= OnDriverActiveChanged;
			SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
			// Only stop a build coroutine hosted on *this* behaviour. External runners (Addon_MGR /
			// MainViewport) must keep going — otherwise enable-from-Add-on-Manager after Generate
			// dies when the workflow-ribbon host disables under the modal.
			if (_buildCoroutineOwner == (MonoBehaviour)this)
				StopBuildCoroutineIfAny();
			// Host (e.g. generate strip) is often disabled when switching other ribbon tabs—do not destroy the dock
			// then, or the button is gone when returning. Only remove rows when the add-on is off in Add-on Manager.
			if (Addon_MGR.ShouldTearDownViewportFullViewDockOnHostDisabled()) {
				TearDownBuiltDock();
			}
		}

		void OnDestroy() {
			SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
			ViewportFullViewOnScreen_Driver.ActiveChanged -= OnDriverActiveChanged;
			RegisteredInstances.Remove(this);
			// TearDown restores GenerateButtons cream frame + destroys injected rows. Skipping it left
			// the column frame suppressed after host destroy / addon disable races.
			TearDownBuiltDock();
		}

		static MonoBehaviour ResolveCoroutineRunner() {
			// Prefer add-on / viewport hosts — do not depend on the right command-ribbon tab strip.
			var addons = Addon_MGR.instance;
			if (addons != null && addons.isActiveAndEnabled) {
				return addons;
			}
			var mv = MainViewport_UI.instance;
			if (mv != null && mv.isActiveAndEnabled) {
				return mv;
			}
			var es = UnityEngine.Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Exclude);
			if (es != null && es.isActiveAndEnabled) {
				return es;
			}
			return RibbonViewportDockRoutineHost.Get();
		}

		void StopBuildCoroutineIfAny() {
			if (_buildRoutine != null) {
				if (_buildCoroutineOwner != null) {
					_buildCoroutineOwner.StopCoroutine(_buildRoutine);
				}
				_buildRoutine = null;
			}
			_buildCoroutineOwner = null;
		}

		void TryBeginBuildRoutine() {
			if (_built && (_builtRowRt == null || _builtRowRt.gameObject == null)) {
				_built = false;
				_builtRowRt = null;
			}
			if (_built) {
				return;
			}
			if (_buildRoutine != null) {
				return;
			}
			var runner = isActiveAndEnabled ? (MonoBehaviour)this : ResolveCoroutineRunner();
			if (runner == null) {
				return;
			}
			_buildCoroutineOwner = runner;
			_buildRoutine = runner.StartCoroutine(CoBuildWhenGenArtReady());
		}

		IEnumerator CoBuildWhenGenArtReady() {
			try {
				for (int f = 0; f < MaxWaitFrames && !_built; f++) {
					if (this == null) {
						yield break;
					}
					if (TryBuildOnce()) {
						yield break;
					}
					yield return null;
				}
				if (this != null) {
					TryBuildOnce();
				}
			}
			finally {
				_buildRoutine = null;
				_buildCoroutineOwner = null;
			}
		}

		static bool IsUnderSubtree(RectTransform root, RectTransform candidate) {
			if (root == null || candidate == null) {
				return false;
			}
			Transform t = candidate;
			while (t != null) {
				if (t == root) {
					return true;
				}
				t = t.parent;
			}
			return false;
		}

		static bool NameLooksLikeGenArtRow(string n) {
			if (string.IsNullOrEmpty(n)) {
				return false;
			}
			bool hasButton = n.IndexOf("button", StringComparison.OrdinalIgnoreCase) >= 0;
			if (n.IndexOf("Generate Art", StringComparison.OrdinalIgnoreCase) >= 0 && hasButton) {
				return true;
			}
			if (n.IndexOf("Gen Art", StringComparison.OrdinalIgnoreCase) >= 0 && hasButton) {
				return true;
			}
			if (n.IndexOf("GEN", StringComparison.OrdinalIgnoreCase) >= 0
			    && n.IndexOf("ART", StringComparison.OrdinalIgnoreCase) >= 0) {
				return true;
			}
			return false;
		}

		static RectTransform FindGenArtRectRecursive(RectTransform rt) {
			if (rt == null) {
				return null;
			}
			if (NameLooksLikeGenArtRow(rt.name)) {
				return rt;
			}
			for (int i = 0; i < rt.childCount; i++) {
				var ch = rt.GetChild(i) as RectTransform;
				var hit = FindGenArtRectRecursive(ch);
				if (hit != null) {
					return hit;
				}
			}
			return null;
		}

		/// <summary>When object names are generic, find a <see cref="Button"/> under the same label tree as a TMP that looks like the Gen Art affordance. Fails on icon-only or fully localized strings with no "gen/art" tokens.</summary>
		static bool LabelLooksLikeGenArtButton(string t) {
			if (string.IsNullOrEmpty(t)) {
				return false;
			}
			if (t.Length > 200) {
				return false; // very long: likely a log / tooltip, not a row label
			}
			// "Generate Art", "Générer" won't match "generat" — cover common phrasing + compact UI.
			if (t.IndexOf("gen art", StringComparison.OrdinalIgnoreCase) >= 0) {
				return true;
			}
			if (t.IndexOf("genart", StringComparison.OrdinalIgnoreCase) >= 0) {
				return true;
			}
			if (t.IndexOf("generat", StringComparison.OrdinalIgnoreCase) >= 0
			    && t.IndexOf("art", StringComparison.OrdinalIgnoreCase) >= 0) {
				return true;
			}
			return t.IndexOf("GEN", StringComparison.OrdinalIgnoreCase) >= 0
				&& t.IndexOf("ART", StringComparison.OrdinalIgnoreCase) >= 0
				&& t.Length <= 64;
		}

		/// <summary>Gen Art row inside the SD workflow panel often has no "Generate Art" object name; find a Button whose label matches <see cref="LabelLooksLikeGenArtButton"/>.</summary>
		static RectTransform FindGenArtRectByGenerateLabel(RectTransform root) {
			if (root == null) {
				return null;
			}
			var tmps = root.GetComponentsInChildren<TextMeshProUGUI>(true);
			for (int i = 0; i < tmps.Length; i++) {
				var tmp = tmps[i];
				if (!LabelLooksLikeGenArtButton(tmp.text)) {
					continue;
				}
				Transform tr = tmp.transform;
				for (int g = 0; g < 12 && tr != null; g++) {
					if (tr.GetComponent<Button>() != null && tr is RectTransform rr) {
						return rr;
					}
					tr = tr.parent;
				}
			}
			return null;
		}

		static RectTransform ViewportInnerLeftRibbonRoot() {
			var mv = MainViewport_UI.instance;
			return mv != null ? mv.innerLeftRibbonRect : null;
		}

		/// <summary>Viewport inner-left strip (GEN ART / GEN BG). Dock row is parented to <see cref="SD_WorkflowOptionsRibbon_UI.WholePanelRoot"/> when Gen Art resolves here so we do not stack over those buttons.</summary>
		static bool IsUnderViewportInnerLeftRibbon(RectTransform rt) {
			var leftR = ViewportInnerLeftRibbonRoot();
			return rt != null && leftR != null && rt.IsChildOf(leftR);
		}

		/// <summary>Singleton or scene search (Awake order can leave <see cref="GenerateButtons_Main_UI.instance"/> null for a few frames).</summary>
		static GenerateButtons_Main_UI ResolveGenerateButtonsMain() {
			if (GenerateButtons_Main_UI.instance != null) {
				return GenerateButtons_Main_UI.instance;
			}
			return UnityEngine.Object.FindFirstObjectByType<GenerateButtons_Main_UI>(FindObjectsInactive.Include);
		}

		static RectTransform ResolveGenArtRect(SD_WorkflowOptionsRibbon_UI host, RectTransform wholePanelRoot) {
			// Must run first: works when the dock is on the Gen strip only (_host is null) and is not blocked by a null host.
			var gbm = ResolveGenerateButtonsMain();
			if (gbm != null) {
				var fromMain = gbm.GenArtButtonRectTransform;
				if (fromMain != null) {
					return fromMain;
				}
			}
			RectTransform hostRt = host != null ? host.transform as RectTransform : null;
			RectTransform pick = null;
			if (wholePanelRoot != null) {
				pick = FindGenArtRectRecursive(wholePanelRoot);
				if (pick == null) {
					pick = FindGenArtRectByGenerateLabel(wholePanelRoot);
				}
			}
			if (pick == null && hostRt != null) {
				pick = FindGenArtRectRecursive(hostRt);
				if (pick == null) {
					pick = FindGenArtRectByGenerateLabel(hostRt);
				}
			}
			if (host != null) {
				var fromProperty = host.GenArtButtonRect;
				if (pick == null && fromProperty) {
					if (IsUnderSubtree(wholePanelRoot, fromProperty) || IsUnderSubtree(hostRt, fromProperty)) {
						pick = fromProperty;
					}
				}
				if (pick == null && fromProperty) {
					pick = fromProperty;
				}
			}
			if (pick == null && host != null) {
				var rootRt = host.transform.root as RectTransform;
				if (rootRt != null) {
					pick = FindGenArtRectRecursive(rootRt);
					if (pick == null) {
						pick = FindGenArtRectByGenerateLabel(rootRt);
					}
				}
			}
			return pick;
		}

		static TextMeshProUGUI FindPrimaryTmp(RectTransform genRoot) {
			if (genRoot == null) {
				return null;
			}
			var tmps = genRoot.GetComponentsInChildren<TextMeshProUGUI>(true);
			for (int i = 0; i < tmps.Length; i++) {
				if (tmps[i].transform.parent == genRoot) {
					return tmps[i];
				}
			}
			return tmps.Length > 0 ? tmps[0] : null;
		}

		/// <summary>GEN ART is often a <c>Button</c> with the target graphic on a child; match that sprite, not a missing <c>Image</c> on the root.</summary>
		static Image FindGenArtReferenceImage(RectTransform genRoot) {
			if (genRoot == null) {
				return null;
			}
			var btn = genRoot.GetComponent<Button>();
			if (btn != null && btn.targetGraphic is Image btnImg) {
				return btnImg;
			}
			var onRoot = genRoot.GetComponent<Image>();
			if (onRoot != null) {
				return onRoot;
			}
			return genRoot.GetComponentInChildren<Image>(true);
		}

		static float GetClampedGenArtButtonHeightPx(RectTransform genArt) {
			if (genArt == null) {
				return 56f;
			}
			var srcLe = genArt.GetComponent<LayoutElement>();
			float genArtH = srcLe != null && srcLe.preferredHeight > 1f
				? srcLe.preferredHeight
				: (genArt.rect.height > 1f ? genArt.rect.height : 56f);
			return Mathf.Clamp(genArtH, 40f, 96f);
		}

		/// <summary>
		/// Peach fill for leave/build — never bake live Nomad grey from Gen Art into <see cref="_authoredFillBase"/>.
		/// Prefer BoundChrome first-write snapshot on Gen Art, else live color when builtin, else <see cref="FallbackFill"/>.
		/// </summary>
		static Color ResolveAuthoredGenArtFill(Image genRefImg) {
			if (genRefImg != null
			    && SpzUiThemeOps.TryGetAuthoredGraphicColor(genRefImg, out Color authored)
			    && authored.a > 0.01f)
				return authored;
			if (genRefImg != null && !SpzUiThemeOps.ShouldRecolorBoundChrome)
				return genRefImg.color;
			return FallbackFill;
		}

		/// <summary>Prefer actual GEN ART target-graphic rect height (visual face), then fall back to clamped root height.</summary>
		static float GetVisualGenArtFaceHeightPx(RectTransform genArt, Image genRefImg) {
			if (genRefImg != null && genRefImg.rectTransform != null) {
				float h = genRefImg.rectTransform.rect.height;
				if (h > 1f) {
					return Mathf.Clamp(h, 32f, 96f);
				}
			}
			return GetClampedGenArtButtonHeightPx(genArt);
		}

		/// <summary>
		/// Walk up from GEN ART until we find an ancestor with <see cref="VerticalLayoutGroup"/>. The **direct child** of that VLG on the GEN ART branch is the wrapper slot we must insert **above** for natural top-down stacking. In the GenerateButtons_Main_UI (vertGroup) prefab this is the <c>generate holder</c> wrapper.
		/// </summary>
		static bool TryResolveVerticalStackInsertion(RectTransform genArt, out RectTransform vlgRoot, out RectTransform genArtWrapper) {
			vlgRoot = null;
			genArtWrapper = null;
			if (genArt == null) {
				return false;
			}
			Transform prev = genArt;
			Transform t = genArt.parent;
			while (t != null) {
				if (t is RectTransform trt && trt.GetComponent<VerticalLayoutGroup>() != null) {
					vlgRoot = trt;
					genArtWrapper = prev as RectTransform;
					return genArtWrapper != null;
				}
				prev = t;
				t = t.parent;
			}
			return false;
		}

		static void CopyLittleTriangleIfPresent(RectTransform genArtRt, RectTransform targetButtonRt) {
			if (genArtRt == null || targetButtonRt == null) {
				return;
			}
			for (int i = 0; i < genArtRt.childCount; i++) {
				var c = genArtRt.GetChild(i);
				if (c.name.IndexOf("triangle", StringComparison.OrdinalIgnoreCase) < 0) {
					continue;
				}
				if (!(c is RectTransform srcRt)) {
					continue;
				}
				var srcImg = c.GetComponent<Image>();
				var triGo = new GameObject(c.name);
				triGo.layer = targetButtonRt.gameObject.layer;
				var triRt = triGo.AddComponent<RectTransform>();
				triRt.SetParent(targetButtonRt, false);
				triRt.localRotation = srcRt.localRotation;
				triRt.localScale = srcRt.localScale;
				triRt.anchorMin = srcRt.anchorMin;
				triRt.anchorMax = srcRt.anchorMax;
				triRt.pivot = srcRt.pivot;
				triRt.anchoredPosition = srcRt.anchoredPosition;
				triRt.sizeDelta = srcRt.sizeDelta;
				if (srcImg != null) {
					var img = triGo.AddComponent<Image>();
					img.sprite = srcImg.sprite;
					img.color = srcImg.color;
					img.preserveAspect = srcImg.preserveAspect;
					img.raycastTarget = false;
				}
				return;
			}
		}

		/// <summary>Match GEN ART: sliced sprite, fill, two-line black label, optional corner triangle; border look comes from the shared sprite.</summary>
		void ApplyGenArtStyleDockButton(GameObject row, RectTransform rowRt, RectTransform genArt, Image genRefImg, TextMeshProUGUI genRefTmp) {
			// Use an inner face so width follows Gen Art's horizontal insets (e.g. -4 sizeDelta in prefab), not the full wrapper width.
			var faceGo = new GameObject("DockButtonFace");
			faceGo.layer = row.layer;
			var faceRt = faceGo.AddComponent<RectTransform>();
			faceRt.SetParent(rowRt, false);
			ApplyFaceRectLayout(faceRt, genArt, genRefImg);

			_bgImage = faceGo.AddComponent<Image>();
			if (genRefImg != null && genRefImg.sprite != null) {
				_bgImage.sprite = genRefImg.sprite;
				_bgImage.type = genRefImg.type;
				_bgImage.pixelsPerUnitMultiplier = genRefImg.pixelsPerUnitMultiplier;
			}
			_authoredFillBase = ResolveAuthoredGenArtFill(genRefImg);
			_fillBase = _authoredFillBase;
			_bgImage.color = _fillBase;
			SpzUiThemeOps.ResnapshotAuthoredGraphicColor(_bgImage);

			var button = faceGo.AddComponent<Button>();
			button.targetGraphic = _bgImage;
			var genBtn = genArt != null ? genArt.GetComponent<Button>() : null;
			if (genBtn != null) {
				button.transition = genBtn.transition;
				button.colors = genBtn.colors;
				button.spriteState = genBtn.spriteState;
				button.animationTriggers = genBtn.animationTriggers;
			} else {
				button.transition = Selectable.Transition.ColorTint;
				var cb = button.colors;
				cb.normalColor = Color.white;
				cb.highlightedColor = new Color(0.96f, 0.96f, 0.96f, 1f);
				cb.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
				cb.selectedColor = cb.highlightedColor;
				cb.disabledColor = new Color(0.78f, 0.78f, 0.78f, 0.5f);
				cb.colorMultiplier = 1f;
				cb.fadeDuration = 0.1f;
				button.colors = cb;
			}
			button.onClick.AddListener(OnDockedButtonClicked);
			_dockButton = button;

			CopyLittleTriangleIfPresent(genArt, faceRt);

			var textGo = new GameObject("Text (TMP)");
			textGo.layer = faceGo.layer;
			var textRt = textGo.AddComponent<RectTransform>();
			textRt.SetParent(faceRt, false);
			var tmp = textGo.AddComponent<TextMeshProUGUI>();
			ApplyFullSrnLabelStyle(tmp, genRefTmp, textRt);
			EnsureDockLineIcon(faceRt, ResolveFullViewDockIcon(), out _fullSrnLineIcon);
			EnsureAdaptiveFaceBorder(faceRt);
			EnsureFullViewMenu(faceRt, genRefImg, genRefTmp);
			SuppressGenerateButtonsColumnFrame();
			ApplyThemeTokens();
		}

		void EnsureFullViewMenu(RectTransform faceRt, Image genRefImg, TextMeshProUGUI genRefTmp) {
			if (faceRt == null) {
				return;
			}
			RectTransform rowRt = faceRt.parent as RectTransform;
			RectTransform vlgRoot = rowRt != null ? rowRt.parent as RectTransform : null;
			// ForceHide leaves the menu inactive — Transform.Find misses it and spawned duplicate menus.
			if (_fullViewMenuRt == null || _fullViewMenuRt.gameObject == null) {
				var found = vlgRoot != null
					? SpzUiThemeOps.FindDirectChildIncludingInactive(vlgRoot, MenuRowName)
					: null;
				_fullViewMenuRt = found as RectTransform;
			}
			if (_fullViewMenuRt == null) {
				if (vlgRoot == null) {
					return;
				}
				var menuGo = new GameObject(MenuRowName);
				menuGo.layer = faceRt.gameObject.layer;
				_fullViewMenuRt = menuGo.AddComponent<RectTransform>();
				_fullViewMenuRt.SetParent(vlgRoot, false);
				_fullViewMenuRt.anchorMin = Vector2.zero;
				_fullViewMenuRt.anchorMax = Vector2.zero;
				_fullViewMenuRt.pivot = new Vector2(0.5f, 0.5f);
				float menuSlotH = GetVisualGenArtFaceHeightPx(
					ResolveGenerateButtonsMain()?.GenArtButtonRectTransform, genRefImg);
				_fullViewMenuRt.sizeDelta = new Vector2(0f, menuSlotH);
				var menuLe = menuGo.AddComponent<LayoutElement>();
				menuLe.preferredHeight = menuSlotH;
				menuLe.minHeight = menuSlotH;
				menuLe.flexibleHeight = 0f;
				menuLe.flexibleWidth = 0f;
				var vlg = menuGo.AddComponent<VerticalLayoutGroup>();
				vlg.childAlignment = TextAnchor.UpperCenter;
				vlg.childControlWidth = true;
				vlg.childControlHeight = false;
				vlg.childForceExpandWidth = true;
				vlg.childForceExpandHeight = false;
				vlg.spacing = 0f;
				vlg.padding = new RectOffset(0, 0, 0, 0);
				CreateFullViewMenuButton(menuGo.transform, "OpenRightDock", "OPEN\nRIGHT", OnFullViewMenuOpenRightDockClicked, genRefImg, genRefTmp);
			}
			if (rowRt != null && _fullViewMenuRt.parent == rowRt.parent) {
				_fullViewMenuRt.SetSiblingIndex(rowRt.GetSiblingIndex() + 1);
			}
			var openRt = SpzUiThemeOps.FindDirectChildIncludingInactive(_fullViewMenuRt, "OpenRightDock") as RectTransform;
			if (openRt != null) {
				SyncOpenRightDockLayout(openRt, genRefImg);
			}
			_fullViewMenuCg = _fullViewMenuRt.GetComponent<CanvasGroup>();
			if (_fullViewMenuCg == null) {
				_fullViewMenuCg = _fullViewMenuRt.gameObject.AddComponent<CanvasGroup>();
			}
			ForceHideFullViewMenuInstant();
		}

		/// <summary>
		/// Existing menus skip <see cref="CreateFullViewMenuButton"/> — re-apply Gen Art face insets + slot height
		/// so OPEN RIGHT stays aligned with FULL/SRN after Gen Art layout changes.
		/// </summary>
		void SyncOpenRightDockLayout(RectTransform openRt, Image genRefImg) {
			if (openRt == null)
				return;
			var genArt = ResolveGenerateButtonsMain()?.GenArtButtonRectTransform;
			float slotH = GetVisualGenArtFaceHeightPx(genArt, genRefImg);
			openRt.sizeDelta = new Vector2(0f, slotH);
			var openLe = openRt.GetComponent<LayoutElement>();
			if (openLe != null) {
				openLe.preferredHeight = slotH;
				openLe.minHeight = slotH;
			}
			if (_fullViewMenuRt != null) {
				_fullViewMenuRt.sizeDelta = new Vector2(0f, slotH);
				var menuLe = _fullViewMenuRt.GetComponent<LayoutElement>();
				if (menuLe != null) {
					menuLe.preferredHeight = slotH;
					menuLe.minHeight = slotH;
				}
			}
			var openFace = SpzUiThemeOps.FindDirectChildIncludingInactive(openRt, "DockButtonFace") as RectTransform;
			if (openFace != null)
				ApplyFaceRectLayout(openFace, genArt, genRefImg);
			else
				openFace = openRt;
			EnsureAdaptiveFaceBorder(openFace);
		}

		void SetSecondaryButtonVisible(bool show) {
			if (_fullViewMenuRt == null || _fullViewMenuCg == null) {
				return;
			}
			if (_fullViewMenuAnimRoutine != null) {
				StopCoroutine(_fullViewMenuAnimRoutine);
				_fullViewMenuAnimRoutine = null;
			}
			// Do not set _fullViewMenuOpen here: that flag is for legacy popup/click-away; the secondary
			// control is a layout row and should stay up while in on-screen full-view session.
			bool wasShown = _fullViewMenuRt.gameObject.activeSelf;
			_fullViewMenuRt.gameObject.SetActive(show);
			_fullViewMenuCg.alpha = show ? 1f : 0f;
			_fullViewMenuCg.interactable = show;
			_fullViewMenuCg.blocksRaycasts = show;
			_fullViewMenuRt.localScale = Vector3.one;
			if (show && _builtRowRt != null && _fullViewMenuRt.parent == _builtRowRt.parent) {
				_fullViewMenuRt.SetSiblingIndex(_builtRowRt.GetSiblingIndex() + 1);
			}
			// OPEN RIGHT adds/removes Gen Art face-height slot — only re-fit when visibility actually changes.
			if (wasShown != show)
				ApplyAdaptiveBottomGap(force: true);
		}

		void EnsureFullViewMenuWiringIfMissing() {
			if (_fullViewMenuRt != null && _fullViewMenuCg != null) {
				return;
			}
			var faceRt = _dockButton != null ? (_dockButton.transform as RectTransform) : null;
			if (faceRt == null) {
				faceRt = _bgImage != null ? (_bgImage.transform as RectTransform) : null;
			}
			if (faceRt == null) {
				return;
			}
			var tmp = faceRt.GetComponentInChildren<TextMeshProUGUI>(true);
			EnsureFullViewMenu(faceRt, _bgImage, tmp);
		}

		void CreateFullViewMenuButton(Transform parent, string name, string label, UnityEngine.Events.UnityAction onClick, Image genRefImg, TextMeshProUGUI genRefTmp) {
			var rowGo = new GameObject(name);
			rowGo.layer = parent.gameObject.layer;
			var rowRt = rowGo.AddComponent<RectTransform>();
			rowRt.SetParent(parent, false);
			var genArt = ResolveGenerateButtonsMain()?.GenArtButtonRectTransform;
			float slotH = GetVisualGenArtFaceHeightPx(genArt, genRefImg);
			rowRt.sizeDelta = new Vector2(0f, slotH);
			var le = rowGo.AddComponent<LayoutElement>();
			le.preferredHeight = slotH;
			le.minHeight = slotH;
			le.flexibleHeight = 0f;
			// Same inset face as FULL/SRN / GEN ART — do not paint the full VLG child width.
			var faceGo = new GameObject("DockButtonFace");
			faceGo.layer = rowGo.layer;
			var faceRt = faceGo.AddComponent<RectTransform>();
			faceRt.SetParent(rowRt, false);
			ApplyFaceRectLayout(faceRt, genArt, genRefImg);
			var img = faceGo.AddComponent<Image>();
			if (genRefImg != null && genRefImg.sprite != null) {
				img.sprite = genRefImg.sprite;
				img.type = genRefImg.type;
				img.pixelsPerUnitMultiplier = genRefImg.pixelsPerUnitMultiplier;
			}
			img.color = ResolveAuthoredGenArtFill(genRefImg);
			SpzUiThemeOps.ResnapshotAuthoredGraphicColor(img);
			var btn = faceGo.AddComponent<Button>();
			btn.targetGraphic = img;
			var genBtn = genArt != null ? genArt.GetComponent<Button>() : null;
			if (genBtn != null) {
				btn.transition = genBtn.transition;
				btn.colors = genBtn.colors;
				btn.spriteState = genBtn.spriteState;
				btn.animationTriggers = genBtn.animationTriggers;
			}
			btn.onClick.AddListener(onClick);
			var txtGo = new GameObject("Text (TMP)");
			txtGo.layer = faceGo.layer;
			var txtRt = txtGo.AddComponent<RectTransform>();
			txtRt.SetParent(faceRt, false);
			var txt = txtGo.AddComponent<TextMeshProUGUI>();
			// One point smaller than FULL/SRN; seed designPt so BoundChrome does not snap back to 13.
			const float openRightLabelPt = DockLabelBasePt - 1f;
			ApplyFullSrnLabelStyle(txt, genRefTmp, txtRt);
			txt.text = label;
			SpzUiThemeOps.EnsureDesignFontPt(txt, openRightLabelPt);
			if (SpzUiThemeOps.ShouldRecolorBoundChrome)
				SpzUiThemeOps.ApplyBoundChromeNarrowDockLabelTmp(txt, SpzUiThemeOps.Active.textPrimary, openRightLabelPt);
			else
				txt.fontSize = openRightLabelPt;
			EnsureAdaptiveFaceBorder(faceRt);
			if (string.Equals(name, "OpenRightDock", StringComparison.Ordinal)) {
				_openRightDockLabel = txt;
				EnsureDockLineIcon(faceRt, ResolveOpenRightDockIcon(rightPanelOpen: false), out _openRightLineIcon);
			}
		}

		/// <summary>Nomad sculpt: Expand glyph for FULL/SRN dock face.</summary>
		public static StudioLineIcon ResolveFullViewDockIcon() => StudioLineIcon.Expand;

		/// <summary>OPEN RIGHT → ChevronRight; HIDE RIGHT (right open) → ChevronLeft.</summary>
		public static StudioLineIcon ResolveOpenRightDockIcon(bool rightPanelOpen) =>
			rightPanelOpen ? StudioLineIcon.ChevronLeft : StudioLineIcon.ChevronRight;

		static void EnsureDockLineIcon(RectTransform parent, StudioLineIcon glyph, out Image iconImg) {
			iconImg = null;
			if (parent == null) return;
			// Same inactive-child pitfall as DockFaceBorder — OPEN RIGHT is often inactive.
			Transform existing = SpzUiThemeOps.FindDirectChildIncludingInactive(parent, "LineIcon");
			RectTransform iconRt;
			if (existing == null) {
				var go = new GameObject("LineIcon", typeof(RectTransform));
				go.layer = parent.gameObject.layer;
				iconRt = go.GetComponent<RectTransform>();
				iconRt.SetParent(parent, false);
				iconImg = go.AddComponent<Image>();
				iconImg.raycastTarget = false;
				iconImg.preserveAspect = true;
			} else {
				iconRt = existing as RectTransform;
				iconImg = existing.GetComponent<Image>();
				if (iconImg == null)
					iconImg = existing.gameObject.AddComponent<Image>();
			}
			if (iconRt != null) {
				// Match Gen Art face center so FULL / OPEN RIGHT glyphs share one vertical column.
				iconRt.anchorMin = new Vector2(0.5f, 0.5f);
				iconRt.anchorMax = new Vector2(0.5f, 0.5f);
				iconRt.pivot = new Vector2(0.5f, 0.5f);
				iconRt.anchoredPosition = Vector2.zero;
				iconRt.sizeDelta = new Vector2(22f, 22f);
			}
			if (iconImg != null) {
				iconImg.sprite = UiRuntimeSprites.GetLineIcon(glyph);
				// Only hide newly created icons; rebinding an existing glyph must not flash/hide sculpt chrome.
				if (existing == null)
					iconImg.gameObject.SetActive(false);
			}
		}

		void ApplyThemeTokens() {
			if (!_built && _builtRowRt == null && _dockButton == null)
				return;
			bool sculpt = SpzUiThemeOps.ShouldRecolorBoundChrome;
			var t = SpzUiThemeOps.Active;

			if (!sculpt) {
				if (_builtRowRt != null)
					SpzUiThemeOps.RestoreBoundChromeUnder(_builtRowRt);
				if (_fullViewMenuRt != null)
					SpzUiThemeOps.RestoreBoundChromeUnder(_fullViewMenuRt);
				// Dock face can sit outside built row — restore so leave does not leave Nomad ColorBlock sticky.
				if (_dockButton != null)
					SpzUiThemeOps.RestoreBoundChromeUnder(_dockButton.transform);
				if (_spacerRowRt != null)
					SpzUiThemeOps.RestoreBoundChromeUnder(_spacerRowRt);
			}

			if (_bgImage != null) {
				bool on = IsInOnScreenFullViewSession();
				if (sculpt) {
					_fillBase = t.controlBg;
					Color fill = on ? Color.Lerp(t.controlBg, t.accent, 0.14f) : t.controlBg;
					if (_dockButton != null) {
						SpzUiThemeOps.EnsureSelectableHitFace(_dockButton);
						SpzUiThemeOps.ApplyBoundChromeSelectable(_dockButton, fill, t.accent);
						if (_dockButton.targetGraphic is Image face) {
							SpzUiThemeOps.ApplyRoundedControlSprite(face, markEligible: true);
							face.preserveAspect = false;
						}
						SpzUiThemeOps.ClearNonFaceRaycastsForTheme(_dockButton);
					} else {
						SpzUiThemeOps.ApplyBoundChromeGraphic(_bgImage, fill);
						SpzUiThemeOps.ApplyRoundedControlSprite(_bgImage, markEligible: true);
						_bgImage.preserveAspect = false;
					}
				} else {
					_authoredFillBase = ResolveAuthoredGenArtFill(FindGenArtReferenceImage(
						ResolveGenerateButtonsMain()?.GenArtButtonRectTransform));
					_fillBase = _authoredFillBase;
					_bgImage.color = on ? Color.Lerp(_fillBase, Color.black, 0.14f) : _fillBase;
					SpzUiThemeOps.ResnapshotAuthoredGraphicColor(_bgImage);
				}
			}

			ApplyDockFaceChrome(_dockButton != null ? _dockButton.transform as RectTransform : null,
				ref _fullSrnLineIcon, ResolveFullViewDockIcon(), sculpt, t, forceFullSrnLabel: true);

			bool rightOpen = false;
			var sk = Global_Skeleton_UI.instance;
			if (sk != null && sk.TryGetSidePanelVisibility(out bool left, out bool right))
				rightOpen = !left && right;
			StudioLineIcon openGlyph = ResolveOpenRightDockIcon(rightOpen);

			if (_fullViewMenuRt != null) {
				var openRt = SpzUiThemeOps.FindDirectChildIncludingInactive(_fullViewMenuRt, "OpenRightDock") as RectTransform;
				if (openRt != null) {
					var openFace = SpzUiThemeOps.FindDirectChildIncludingInactive(openRt, "DockButtonFace") as RectTransform ?? openRt;
					var openBtn = openFace.GetComponent<Button>() ?? openRt.GetComponentInChildren<Button>(true);
					var openImg = openBtn != null && openBtn.targetGraphic is Image tg
						? tg
						: openFace.GetComponent<Image>();
					if (sculpt) {
						if (openBtn != null) {
							SpzUiThemeOps.EnsureSelectableHitFace(openBtn);
							SpzUiThemeOps.ApplyBoundChromeSelectable(openBtn, t.controlBg, t.accent);
							if (openBtn.targetGraphic is Image of) {
								SpzUiThemeOps.ApplyRoundedControlSprite(of, markEligible: true);
								of.preserveAspect = false;
							}
							SpzUiThemeOps.ClearNonFaceRaycastsForTheme(openBtn);
						} else if (openImg != null) {
							SpzUiThemeOps.ApplyBoundChromeGraphic(openImg, t.controlBg);
							SpzUiThemeOps.ApplyRoundedControlSprite(openImg, markEligible: true);
						}
					} else if (openImg != null) {
						SpzUiThemeOps.RestoreAuthoredGraphic(openImg);
						openImg.color = _authoredFillBase;
						SpzUiThemeOps.ResnapshotAuthoredGraphicColor(openImg);
					}
					ApplyDockFaceChrome(openFace, ref _openRightLineIcon, openGlyph, sculpt, t, forceFullSrnLabel: false);
					if (_openRightDockLabel == null)
						_openRightDockLabel = openFace.GetComponentInChildren<TextMeshProUGUI>(true);
				}
			}

			RefreshOpenRightSecondaryLabel();
		}

		static void ApplyDockFaceChrome(RectTransform face, ref Image iconImg, StudioLineIcon glyph, bool sculpt, SpzUiThemeOps.ThemeTokens t, bool forceFullSrnLabel) {
			if (face == null) return;
			if (iconImg == null)
				EnsureDockLineIcon(face, glyph, out iconImg);
			var label = face.GetComponentInChildren<TextMeshProUGUI>(true);
			HideCornerTrianglesUnder(face, hide: sculpt);
			if (sculpt) {
				// Flat grey + text (not icon-only, not beveled peach brick).
				if (label != null) {
					label.maxVisibleCharacters = int.MaxValue;
					if (forceFullSrnLabel
					    && (string.IsNullOrWhiteSpace(label.text)
					        || label.text.IndexOf("FULL", System.StringComparison.OrdinalIgnoreCase) < 0))
						label.text = "FULL\nSRN";
					// Narrow Gen Art column — FULL uses 13pt; OPEN/HIDE RIGHT one point smaller.
					float labelPt = forceFullSrnLabel ? DockLabelBasePt : (DockLabelBasePt - 1f);
					SpzUiThemeOps.ApplyBoundChromeNarrowDockLabelTmp(label, t.textPrimary, labelPt);
				}
				if (iconImg != null)
					iconImg.gameObject.SetActive(false);
			} else {
				if (label != null) {
					label.maxVisibleCharacters = int.MaxValue;
					// Restore authored TMP — then force Gen-Art-column black so leave/build never keep Nomad white.
					SpzUiThemeOps.RestoreAuthoredGraphic(label);
					ApplyGenArtColumnLabelColor(label);
				}
				if (iconImg != null)
					iconImg.gameObject.SetActive(false);
			}
		}

		/// <summary>FULL/SRN sits on the peach Gen Art strip — labels must read black like GEN ART / GEN BG.</summary>
		static void ApplyGenArtColumnLabelColor(TMP_Text label) {
			if (label == null)
				return;
			Color col = Color.black;
			var gbm = ResolveGenerateButtonsMain();
			if (gbm != null && gbm.GenArtButtonRectTransform != null) {
				var genTmp = gbm.GenArtButtonRectTransform.GetComponentInChildren<TextMeshProUGUI>(true);
				if (genTmp != null)
					col = genTmp.color;
			}
			label.color = col;
			// Nomad may have first-write snapshotted light text — overwrite so later RestoreBoundChromeUnder keeps Gen Art black.
			SpzUiThemeOps.ResnapshotAuthoredGraphicColor(label);
		}

		static void HideCornerTrianglesUnder(Transform root, bool hide) {
			if (root == null) return;
			foreach (var img in root.GetComponentsInChildren<Image>(true)) {
				if (img == null) continue;
				string n = img.gameObject.name ?? "";
				if (n.IndexOf("triangle", System.StringComparison.OrdinalIgnoreCase) < 0)
					continue;
				if (hide)
					SpzUiThemeOps.HideAuthoredGraphicForTheme(img);
				else {
					// RestoreAuthoredGraphic alone leaves enabled=false when SpzUiThemeHiddenGraphic remains
					// (ApplyDockFaceChrome after a path that skipped RestoreBoundChromeUnder).
					var tag = img.GetComponent<SpzUiThemeHiddenGraphic>();
					if (tag != null) {
						if (tag.hasSnapshot)
							img.enabled = tag.wasEnabled;
						if (Application.isPlaying)
							UnityEngine.Object.Destroy(tag);
						else
							UnityEngine.Object.DestroyImmediate(tag);
					}
					SpzUiThemeOps.RestoreAuthoredGraphic(img);
				}
			}
		}

		/// <summary>
		/// FULL/SRN on the narrow Gen Art column: modest insets + capped point size.
		/// Do not copy Gen Art's fontSize/strip tracking after BoundChrome — fresh TMP defaults (~36)
		/// and strip tracking (18) were overflowing the face (clipped F/L and S/N).
		/// </summary>
		static void ApplyFullSrnLabelStyle(TextMeshProUGUI tmp, TextMeshProUGUI genRefTmp, RectTransform textRt) {
			if (tmp == null) {
				return;
			}
			// Modest insets — 12px/side left almost no room for "FULL" on the Gen Art column.
			const float padH = 4f;
			const float padV = 2.5f;
			if (textRt != null) {
				if (SpzUiThemeOps.ShouldRecolorBoundChrome) {
					SpzUiThemeOps.SnapshotToolFaceLayout(textRt);
					textRt.anchorMin = Vector2.zero;
					textRt.anchorMax = Vector2.one;
					textRt.offsetMin = new Vector2(padH, padV);
					textRt.offsetMax = new Vector2(-padH, -padV);
				} else {
					SpzUiThemeOps.RestoreBoundChromeUnder(textRt);
				}
			}
			string raw = "FULL\nSRN";
			tmp.text = raw.ToUpperInvariant();
			// Never hardcode black before BoundChrome snapshot — that sticks after Restore SPZ.
			if (SpzUiThemeOps.ShouldRecolorBoundChrome) {
				SpzUiThemeOps.ApplyBoundChromeNarrowDockLabelTmp(tmp, SpzUiThemeOps.Active.textPrimary, DockLabelBasePt);
				// Preserve strip UpperCase; Bold alone would clear it.
				tmp.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
				tmp.alignment = TextAlignmentOptions.Center;
				tmp.horizontalAlignment = HorizontalAlignmentOptions.Center;
				tmp.verticalAlignment = VerticalAlignmentOptions.Middle;
				tmp.raycastTarget = false;
				tmp.textWrappingMode = TextWrappingModes.NoWrap;
			} else {
				// Leave: full BoundChrome unwind — do not re-force Bold/outline/tracking after Restore.
				SpzUiThemeOps.RestoreBoundChromeUnder(tmp.transform);
				ApplyGenArtColumnLabelColor(tmp);
				tmp.raycastTarget = false;
			}
		}

		/// <summary>Keep frame/fill/text under one RectTransform so vertical movement never splits the button visuals.</summary>
		static void ApplyFaceRectLayout(RectTransform faceRt, RectTransform genArt, Image genRefImg) {
			if (faceRt == null) {
				return;
			}
			// Anchor face to the TOP of the wrapper so the visible button sits at the top of its VLG slot.
			faceRt.anchorMin = new Vector2(0f, 1f);
			faceRt.anchorMax = new Vector2(1f, 1f);
			faceRt.pivot = new Vector2(0.5f, 1f);
			float insetL = 0f;
			float insetR = 0f;
			if (genArt != null) {
				insetL = genArt.offsetMin.x;
				insetR = genArt.offsetMax.x;
			}
			float faceH = GetVisualGenArtFaceHeightPx(genArt, genRefImg);
			faceRt.anchoredPosition = Vector2.zero;
			faceRt.offsetMin = new Vector2(insetL, -faceH);
			faceRt.offsetMax = new Vector2(insetR, 0f);
		}

		/// <summary>Remove legacy visuals outside DockButtonFace so frame/fill cannot split onto different rects.</summary>
		static void NormalizeReusableRow(RectTransform rowRt, RectTransform faceRt) {
			if (rowRt == null || faceRt == null) {
				return;
			}
			var rowImg = rowRt.GetComponent<Image>();
			if (rowImg != null) {
				Destroy(rowImg);
			}
			var rowBtn = rowRt.GetComponent<Button>();
			if (rowBtn != null) {
				Destroy(rowBtn);
			}
			for (int i = rowRt.childCount - 1; i >= 0; i--) {
				var ch = rowRt.GetChild(i);
				if (ch != faceRt) {
					Destroy(ch.gameObject);
				}
			}
		}

		static RectTransform FindChildRectByName(RectTransform parent, string name) {
			if (parent == null) {
				return null;
			}
			for (int i = 0; i < parent.childCount; i++) {
				var ch = parent.GetChild(i);
				if (ch.name == name && ch is RectTransform rr) {
					return rr;
				}
			}
			return null;
		}

		static RectTransform EnsureSpacerRow(RectTransform vlgRoot, int siblingIndex, float gapPx, int layer) {
			var spacerRt = FindChildRectByName(vlgRoot, SpacerName);
			if (spacerRt == null) {
				var spacer = new GameObject(SpacerName);
				spacer.layer = layer;
				spacerRt = spacer.AddComponent<RectTransform>();
				spacerRt.SetParent(vlgRoot, false);
			}
			// If max < min, Unity's Mathf.Clamp is not reliable; guard childCount==0 the same way.
			int lastSibling = Mathf.Max(0, vlgRoot.childCount - 1);
			spacerRt.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, lastSibling));
			spacerRt.anchorMin = Vector2.zero;
			spacerRt.anchorMax = Vector2.zero;
			spacerRt.pivot = new Vector2(0.5f, 0.5f);
			spacerRt.sizeDelta = new Vector2(0f, gapPx);
			var le = spacerRt.GetComponent<LayoutElement>();
			if (le == null) {
				le = spacerRt.gameObject.AddComponent<LayoutElement>();
			}
			le.ignoreLayout = false;
			le.preferredHeight = gapPx;
			le.minHeight = gapPx;
			le.flexibleHeight = 0f;
			le.flexibleWidth = 0f;
			return spacerRt;
		}

		int ResolveSpacerSiblingIndex(RectTransform vlgRoot) {
			if (_builtRowRt == null || vlgRoot == null)
				return 0;
			int afterDock = _builtRowRt.GetSiblingIndex() + 1;
			if (_fullViewMenuRt != null
			    && _fullViewMenuRt.gameObject.activeSelf
			    && _fullViewMenuRt.parent == vlgRoot)
				return _fullViewMenuRt.GetSiblingIndex() + 1;
			return afterDock;
		}

		/// <summary>
		/// GenerateButtons VLG is bottom-anchored: a tall ExtraBottomGap climbs FULL/SRN under the
		/// DimensionMode SD circle (and further when OPEN RIGHT is shown). Shrink the spacer until
		/// the dock face clears the disc. Do not reset to max every tick — that caused visible thrash.
		/// </summary>
		void ApplyAdaptiveBottomGap(bool force = false) {
			if (!_built || _builtRowRt == null || _builtRowRt.gameObject == null)
				return;
			var vlgRoot = _builtRowRt.parent as RectTransform;
			if (vlgRoot == null)
				return;
			int frame = Time.frameCount;
			if (!force && _adaptClearanceFrame == frame)
				return;
			_adaptClearanceFrame = frame;

			// Only the first fit (or after TearDown resets _appliedBottomGapPx) starts from max.
			// force:true used to reset to ExtraBottomGap every RefreshActiveFill → one-frame climb under SD.
			float gap = _appliedBottomGapPx < 0f ? ExtraBottomGapPx : _appliedBottomGapPx;
			int spacerIndex = ResolveSpacerSiblingIndex(vlgRoot);
			_spacerRowRt = EnsureSpacerRow(vlgRoot, spacerIndex, gap, _builtRowRt.gameObject.layer);
			LayoutRebuilder.ForceRebuildLayoutImmediate(vlgRoot);

			float deficit = MeasureDimModeOverlapDeficitLocalPx();
			if (deficit > 0.5f) {
				// Up to three shrink passes — menu/spacer sibling order can change mid-pass.
				for (int pass = 0; pass < 3; pass++) {
					deficit = MeasureDimModeOverlapDeficitLocalPx();
					if (deficit <= 0.5f)
						break;
					float floor = pass < 2 ? MinAdaptiveBottomGapPx : 0f;
					gap = Mathf.Max(floor, gap - deficit);
					spacerIndex = ResolveSpacerSiblingIndex(vlgRoot);
					_spacerRowRt = EnsureSpacerRow(vlgRoot, spacerIndex, gap, _builtRowRt.gameObject.layer);
					LayoutRebuilder.ForceRebuildLayoutImmediate(vlgRoot);
				}
			} else if (force && gap < ExtraBottomGapPx - 0.5f) {
				// Grow only on force (rebuild / OPEN RIGHT). Periodic Update trying ExtraBottomGap
				// every 8 frames caused climb-under-SD → shrink thrash when clearance was tight.
				float tryGap = ExtraBottomGapPx;
				spacerIndex = ResolveSpacerSiblingIndex(vlgRoot);
				_spacerRowRt = EnsureSpacerRow(vlgRoot, spacerIndex, tryGap, _builtRowRt.gameObject.layer);
				LayoutRebuilder.ForceRebuildLayoutImmediate(vlgRoot);
				deficit = MeasureDimModeOverlapDeficitLocalPx();
				if (deficit > 0.5f) {
					gap = Mathf.Max(MinAdaptiveBottomGapPx, tryGap - deficit);
					_spacerRowRt = EnsureSpacerRow(vlgRoot, spacerIndex, gap, _builtRowRt.gameObject.layer);
					LayoutRebuilder.ForceRebuildLayoutImmediate(vlgRoot);
				} else {
					gap = tryGap;
				}
			}
			_appliedBottomGapPx = gap;
		}

		float MeasureDimModeOverlapDeficitLocalPx() {
			var face = _dockButton != null
				? _dockButton.transform as RectTransform
				: (_builtRowRt != null
					? SpzUiThemeOps.FindDirectChildIncludingInactive(_builtRowRt, "DockButtonFace") as RectTransform
					: null);
			if (face == null)
				face = _builtRowRt;
			var dimMgr = DimensionMode_MGR.instance;
			var dim = dimMgr != null ? dimMgr.MainChoiceVisualRect : null;
			if (face == null || dim == null || !dim.gameObject.activeInHierarchy)
				return 0f;
			var vlgRoot = _builtRowRt != null ? _builtRowRt.parent as RectTransform : null;
			if (vlgRoot == null)
				return 0f;

			var faceCorners = new Vector3[4];
			var dimCorners = new Vector3[4];
			face.GetWorldCorners(faceCorners);
			dim.GetWorldCorners(dimCorners);
			// Corners: 0=bottom-left, 1=top-left — convert to VLG local so gap is in layout px.
			float faceTopLocal = vlgRoot.InverseTransformPoint(faceCorners[1]).y;
			float dimBottomLocal = vlgRoot.InverseTransformPoint(dimCorners[0]).y;
			// When the SD/3D/UV fan is open (and mirrored toward Gen Art in fullscreen), use the
			// choice panel footprint so FULL/SRN does not climb under the satellites.
			if (dimMgr.TryGetOpenChoicesPanelVisualRect(out var choicesRt) && choicesRt.gameObject.activeInHierarchy) {
				var choiceCorners = new Vector3[4];
				choicesRt.GetWorldCorners(choiceCorners);
				float choicesBottomLocal = vlgRoot.InverseTransformPoint(choiceCorners[0]).y;
				dimBottomLocal = Mathf.Min(dimBottomLocal, choicesBottomLocal);
			}
			return Mathf.Max(0f, faceTopLocal - dimBottomLocal + DimModeClearancePx);
		}

		/// <summary>
		/// GenerateButtons root <c>frame</c> stretch-fills the CSF-grown column (FULL + re-do + DEL LAST).
		/// Hide it while the dock is up; each dock face gets its own adaptive stroke instead.
		/// Ref-counted across dock instances so one TearDown cannot re-enable while another still owns suppress.
		/// </summary>
		void SuppressGenerateButtonsColumnFrame() {
			if (_ownsColumnFrameSuppress)
				return;
			var gbm = ResolveGenerateButtonsMain();
			if (gbm == null)
				return;
			Transform frameT = null;
			for (int i = 0; i < gbm.transform.childCount; i++) {
				var ch = gbm.transform.GetChild(i);
				if (string.Equals(ch.name, GenButtonsColumnFrameName, StringComparison.Ordinal)) {
					frameT = ch;
					break;
				}
			}
			if (frameT == null)
				return;
			// Face stroke must match GEN ART group white outline — do not overwrite cache with cream column frame.
			if (_cachedFaceBorderSprite == null)
				CacheFaceBorderSpriteFromColumnIfNeeded();
			if (s_columnFrameSuppressCount == 0) {
				s_columnFrameGo = frameT.gameObject;
				s_columnFrameWasActive = frameT.gameObject.activeSelf;
				frameT.gameObject.SetActive(false);
			}
			s_columnFrameSuppressCount++;
			_ownsColumnFrameSuppress = true;
			_suppressedGenButtonsColumnFrame = frameT.gameObject;
		}

		void RestoreGenerateButtonsColumnFrame() {
			if (!_ownsColumnFrameSuppress)
				return;
			_ownsColumnFrameSuppress = false;
			_suppressedGenButtonsColumnFrame = null;
			if (s_columnFrameSuppressCount > 0)
				s_columnFrameSuppressCount--;
			if (s_columnFrameSuppressCount == 0 && s_columnFrameGo != null) {
				s_columnFrameGo.SetActive(s_columnFrameWasActive);
				s_columnFrameGo = null;
			}
		}

		/// <summary>
		/// Cream 9-slice stroke sized to the face only (stretch anchors). New buttons (OPEN RIGHT)
		/// get their own border that follows that face's LayoutElement height/width.
		/// </summary>
		void EnsureAdaptiveFaceBorder(RectTransform face) {
			if (face == null)
				return;
			CacheFaceBorderSpriteFromColumnIfNeeded();
			// Transform.Find skips inactive children — OPEN RIGHT is often inactive while menu is hidden,
			// which previously spawned duplicate DockFaceBorder every EnsureFullViewMenu pass.
			Transform existing = SpzUiThemeOps.FindDirectChildIncludingInactive(face, FaceBorderName);
			RectTransform borderRt;
			Image borderImg;
			if (existing == null) {
				var go = new GameObject(FaceBorderName, typeof(RectTransform));
				go.layer = face.gameObject.layer;
				borderRt = go.GetComponent<RectTransform>();
				borderRt.SetParent(face, false);
				borderImg = go.AddComponent<Image>();
			} else {
				borderRt = existing as RectTransform;
				borderImg = existing.GetComponent<Image>();
				if (borderImg == null)
					borderImg = existing.gameObject.AddComponent<Image>();
			}
			if (borderRt != null) {
				borderRt.anchorMin = Vector2.zero;
				borderRt.anchorMax = Vector2.one;
				borderRt.offsetMin = Vector2.zero;
				borderRt.offsetMax = Vector2.zero;
				borderRt.pivot = new Vector2(0.5f, 0.5f);
				borderRt.SetAsLastSibling();
			}
			if (borderImg != null) {
				borderImg.raycastTarget = false;
				borderImg.preserveAspect = false;
				borderImg.type = Image.Type.Sliced;
				// Outline only — fillCenter on the dark ribbon sampled atlas padding as green/cyan corners.
				borderImg.fillCenter = false;
				// Prefer Gen Art group white frame color (not cream column frame / brown bevel).
				borderImg.color = _cachedFaceBorderColor;
				borderImg.pixelsPerUnitMultiplier = Mathf.Max(6f, _cachedFaceBorderPpu);
				if (_cachedFaceBorderSprite != null)
					borderImg.sprite = _cachedFaceBorderSprite;
				borderImg.enabled = _cachedFaceBorderSprite != null;
			}
		}

		void CacheFaceBorderSpriteFromColumnIfNeeded() {
			if (_cachedFaceBorderSprite != null)
				return;
			var gbm = ResolveGenerateButtonsMain();
			if (gbm == null)
				return;
			// Prefer the white group frame beside GEN ART / GEN BG (generate holder), not the cream column frame.
			var genArt = gbm.GenArtButtonRectTransform;
			if (genArt != null && genArt.parent != null
			    && TryCacheFrameImageUnder(genArt.parent))
				return;
			TryCacheFrameImageUnder(gbm.transform);
		}

		bool TryCacheFrameImageUnder(Transform parent) {
			if (parent == null)
				return false;
			for (int i = 0; i < parent.childCount; i++) {
				var ch = parent.GetChild(i);
				if (!string.Equals(ch.name, GenButtonsColumnFrameName, StringComparison.Ordinal))
					continue;
				var img = ch.GetComponent<Image>();
				if (img == null || img.sprite == null)
					return false;
				_cachedFaceBorderSprite = img.sprite;
				_cachedFaceBorderColor = img.color.a > 0.01f
					? img.color
					: new Color(1f, 1f, 1f, 0.95f);
				_cachedFaceBorderPpu = img.pixelsPerUnitMultiplier > 0.01f
					? img.pixelsPerUnitMultiplier
					: 6f;
				return true;
			}
			return false;
		}

		void RefreshActiveFill() {
			if (_bgImage == null) {
				return;
			}
			if (!IsViewportFullviewCommand()) {
				return;
			}
			// "On" while the left column is hidden: center-only fullscreen, or right-only (paint) — same session.
			bool on = IsInOnScreenFullViewSession();
			// Show secondary before theming — ApplyThemeTokens must see an active OpenRightDock
			// (and FindDirectChild works either way, but ColorBlock/sprite apply while inactive still
			// left OPEN RIGHT unstyled until the next ThemeChanged if Find previously returned null).
			SetSecondaryButtonVisible(on);
			if (SpzUiThemeOps.ShouldRecolorBoundChrome) {
				ApplyThemeTokens();
			} else {
				_bgImage.color = on ? Color.Lerp(_fillBase, Color.black, 0.14f) : _fillBase;
			}
			RefreshOpenRightSecondaryLabel();
		}

		void RefreshOpenRightSecondaryLabel() {
			if (_openRightDockLabel == null && _fullViewMenuRt != null) {
				var t = SpzUiThemeOps.FindDirectChildIncludingInactive(_fullViewMenuRt, "OpenRightDock");
				if (t != null) {
					var face = SpzUiThemeOps.FindDirectChildIncludingInactive(t, "DockButtonFace") ?? t;
					_openRightDockLabel = face.GetComponentInChildren<TextMeshProUGUI>(true);
					if (_openRightLineIcon == null)
						EnsureDockLineIcon(face as RectTransform, ResolveOpenRightDockIcon(false), out _openRightLineIcon);
				}
			}
			bool rightOpen = false;
			string label = "OPEN\nRIGHT";
			var sk = Global_Skeleton_UI.instance;
			if (sk != null && sk.TryGetSidePanelVisibility(out bool left, out bool right)) {
				rightOpen = !left && right;
				if (rightOpen)
					label = "HIDE\nRIGHT";
			}
			if (_openRightDockLabel != null)
				_openRightDockLabel.text = label;
			// Under Nomad, ApplyDockFaceChrome keeps the line glyph hidden (text-only OPEN/HIDE RIGHT).
			// Do not re-activate the icon here — that undoes flat dock chrome.
			if (_openRightLineIcon != null && SpzUiThemeOps.ShouldRecolorBoundChrome)
				_openRightLineIcon.gameObject.SetActive(false);
		}

		bool IsViewportFullviewCommand() {
			// This dock is dedicated to fullscreen toggle; tolerate empty spec during attach/rebind races.
			return string.IsNullOrEmpty(_spec.CommandId)
			       || string.Equals(_spec.CommandId, "viewport_fullview_toggle", StringComparison.Ordinal);
		}

		/// <summary>True in center-only full view, or in right-dock sub-state (left still collapsed).</summary>
		static bool IsInOnScreenFullViewSession() {
			if (ViewportFullViewOnScreen_Driver.IsActive) {
				return true;
			}
			var sk = Global_Skeleton_UI.instance;
			if (sk != null && sk.TryGetSidePanelVisibility(out bool left, out _)) {
				return !left;
			}
			return false;
		}

		void Update() {
			if (_built && _builtRowRt != null) {
				bool fanOpen = DimensionMode_MGR.instance != null
				               && DimensionMode_MGR.instance.TryGetOpenChoicesPanelVisualRect(out _);
				if (fanOpen != _lastDimChoicesFanOpen) {
					_lastDimChoicesFanOpen = fanOpen;
					// Fan open/close changes footprint immediately — don't wait for 8-frame poll.
					ApplyAdaptiveBottomGap(force: true);
				} else if ((Time.frameCount & 7) == 0) {
					// Layout can settle a frame after Gen Art / DimensionMode hover scale; keep clear of SD disc.
					ApplyAdaptiveBottomGap(force: false);
				}
			}
			if (!_fullViewMenuOpen || _fullViewMenuRt == null) {
				return;
			}
			if (Time.unscaledTime - _fullViewMenuOpenedAtUnscaledTime < 0.06f) {
				return;
			}
			bool click = KeyMousePenInput.isLMBpressedThisFrame()
			             || KeyMousePenInput.isRMBpressedThisFrame()
			             || KeyMousePenInput.isMMBpressedThisFrame();
			if (!click) {
				return;
			}
			Vector2 p = KeyMousePenInput.cursorScreenPos();
			Camera uiCam = ResolveUiCameraForRect(_fullViewMenuRt);
			if (RectTransformUtility.RectangleContainsScreenPoint(_fullViewMenuRt, p, uiCam)) {
				return;
			}
			if (_dockButton != null && RectTransformUtility.RectangleContainsScreenPoint(
				    _dockButton.transform as RectTransform, p, uiCam)) {
				return;
			}
			ToggleFullViewMenu(false);
		}

		static Camera ResolveUiCameraForRect(RectTransform rt) {
			if (rt == null)
				return null;
			var canvas = rt.GetComponentInParent<Canvas>();
			if (canvas == null)
				return null;
			var root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
			if (root.renderMode == RenderMode.ScreenSpaceOverlay)
				return null;
			if (root.worldCamera != null)
				return root.worldCamera;
			return Camera.main;
		}

		static bool HasNamedChild(Transform parent, string objectName) {
			if (parent == null) {
				return false;
			}
			for (int i = 0; i < parent.childCount; i++) {
				var ch = parent.GetChild(i);
				if (ch == null) {
					continue;
				}
				if (ch.name == objectName) {
					return true;
				}
			}
			return false;
		}

		/// <returns>True if done (created, already present, or cannot parent).</returns>
		bool TryBuildOnce() {
			if (this == null || _built) {
				return true;
			}
			if (string.IsNullOrEmpty(_spec.CommandId)) {
				_spec = RibbonDock_ButtonSpec.FromRpc(null);
			}
			RectTransform wholePanel = _host != null ? _host.WholePanelRoot : null;
			RectTransform genArt = ResolveGenArtRect(_host, wholePanel);
			if (genArt == null) {
				return false;
			}
			// "Above Gen Art" is only meaningful in the VerticalLayoutGroup ancestor (prefab: GenerateButtons_Main_UI (vertGroup)); Gen Art's immediate parent uses anchor-based placement so a sibling there overflows outside the parent rect (this is the reason the button never showed).
			if (!TryResolveVerticalStackInsertion(genArt, out RectTransform vlgRoot, out _)) {
				return false;
			}
			Image genRefImg = FindGenArtReferenceImage(genArt);
			TextMeshProUGUI genRefTmp = FindPrimaryTmp(genArt);
			float btnPx = GetVisualGenArtFaceHeightPx(genArt, genRefImg);
			if (HasNamedChild(vlgRoot, RowName)) {
				_builtRowRt = null;
				for (int ci = 0; ci < vlgRoot.childCount; ci++) {
					var ch = vlgRoot.GetChild(ci);
					if (ch == null || ch.name != RowName || !(ch is RectTransform rr)) {
						continue;
					}
					// Ignore stale legacy rows that don't contain the current canonical face node.
					if (SpzUiThemeOps.FindDirectChildIncludingInactive(rr, "DockButtonFace") is RectTransform) {
						_builtRowRt = rr;
						break;
					}
					DestroyImmediate(rr.gameObject);
				}
				if (_builtRowRt == null) {
					return false;
				}
				var reuseFace = SpzUiThemeOps.FindDirectChildIncludingInactive(_builtRowRt, "DockButtonFace") as RectTransform;
				var reuseBtn = reuseFace != null ? reuseFace.GetComponent<Button>() : null;
				var reuseImg = reuseFace != null ? reuseFace.GetComponent<Image>() : null;
				var reuseTmp = reuseFace != null ? reuseFace.GetComponentInChildren<TextMeshProUGUI>(true) : null;
				if (reuseFace != null && reuseBtn != null && reuseImg != null && reuseTmp != null) {
					// Keep this control at the top of the left-ribbon stack (above re-do / generate holder rows).
					_builtRowRt.SetSiblingIndex(0);
					var reuseLe = _builtRowRt.GetComponent<LayoutElement>();
					if (reuseLe != null) {
						reuseLe.preferredHeight = btnPx;
						reuseLe.minHeight = btnPx;
					}
					_builtRowRt.sizeDelta = new Vector2(0f, btnPx);
					NormalizeReusableRow(_builtRowRt, reuseFace);
					ApplyFaceRectLayout(reuseFace, genArt, genRefImg);
					ApplyFullSrnLabelStyle(reuseTmp, genRefTmp, reuseTmp.rectTransform);
					_spacerRowRt = EnsureSpacerRow(vlgRoot, _builtRowRt.GetSiblingIndex() + 1, ExtraBottomGapPx, _builtRowRt.gameObject.layer);
					reuseBtn.onClick.RemoveAllListeners();
					reuseBtn.onClick.AddListener(OnDockedButtonClicked);
					reuseBtn.targetGraphic = reuseImg;
					_dockButton = reuseBtn;
					_bgImage = reuseImg;
					// Prefer Gen Art authored peach — live genRefImg.color may still be Nomad grey.
					_authoredFillBase = ResolveAuthoredGenArtFill(genRefImg);
					_fillBase = _authoredFillBase;
					if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
						reuseImg.color = _authoredFillBase;
						SpzUiThemeOps.ResnapshotAuthoredGraphicColor(reuseImg);
					}
					EnsureDockLineIcon(reuseFace, ResolveFullViewDockIcon(), out _fullSrnLineIcon);
					EnsureAdaptiveFaceBorder(reuseFace);
					EnsureFullViewMenu(reuseFace, genRefImg, genRefTmp);
					SuppressGenerateButtonsColumnFrame();
					_built = true;
					LayoutRebuilder.ForceRebuildLayoutImmediate(vlgRoot);
					ApplyThemeTokens();
					RefreshActiveFill();
					ApplyAdaptiveBottomGap(force: true);
					return true;
				}
				DestroyImmediate(_builtRowRt.gameObject);
				_builtRowRt = null;
				return false;
			}

			// Wrapper slot under the VLG root. LayoutElement.preferredHeight replicates how "generate holder" (containing Gen Art / Gen BG) claims vertical space in the same VLG; ignoreLayout = false so VLG controls its height.
			var wrapper = new GameObject(RowName);
			wrapper.layer = vlgRoot.gameObject.layer;
			var wrapperRt = wrapper.AddComponent<RectTransform>();
			wrapperRt.SetParent(vlgRoot, false);
			// Keep this control at the top of the left-ribbon stack (above re-do / generate holder rows).
			wrapperRt.SetSiblingIndex(0);
			// Match existing wrapper anchors so VLG's SetChildAlongAxis sizes the rect predictably (ChildControlWidth/Height/ForceExpandWidth all on in prefab).
			wrapperRt.anchorMin = Vector2.zero;
			wrapperRt.anchorMax = Vector2.zero;
			wrapperRt.pivot = new Vector2(0.5f, 0.5f);
			float slotH = btnPx;
			wrapperRt.sizeDelta = new Vector2(0f, slotH);

			var wrapperLe = wrapper.AddComponent<LayoutElement>();
			wrapperLe.ignoreLayout = false;
			wrapperLe.preferredHeight = slotH;
			wrapperLe.minHeight = slotH;
			wrapperLe.flexibleWidth = 0f;
			wrapperLe.flexibleHeight = 0f;
			_spacerRowRt = EnsureSpacerRow(vlgRoot, wrapperRt.GetSiblingIndex() + 1, ExtraBottomGapPx, wrapper.layer);

			ApplyGenArtStyleDockButton(wrapper, wrapperRt, genArt, genRefImg, genRefTmp);

			LayoutRebuilder.ForceRebuildLayoutImmediate(vlgRoot);
			ViewportFullViewOnScreen_Driver.SyncFromCurrentSkeleton();
			RefreshActiveFill();
			_builtRowRt = wrapperRt;
			_built = true;
			ApplyAdaptiveBottomGap(force: true);
			return true;
		}

		/// <summary>Intentionally disabled: never spawn this control in non-ribbon fallback locations.</summary>
		bool TryBuildFallbackTopBar() {
			return false;
		}

		void OnDriverActiveChanged(bool _) {
			RefreshActiveFill();
			ApplyAdaptiveBottomGap(force: true);
		}

		void OnDockedButtonClicked() {
			// Do not call ForceHideFullViewMenuInstant here: it would hide the secondary row before the bridge runs.
			string commandId = string.IsNullOrEmpty(_spec.CommandId) ? "viewport_fullview_toggle" : _spec.CommandId;
			if (!RibbonDock_CommandBridge.TryInvoke(commandId)) {
				return;
			}
			if (string.Equals(commandId, "viewport_fullview_toggle", StringComparison.Ordinal)) {
				ViewportFullViewOnScreen_Driver.SyncFromCurrentSkeleton();
				EnsureFullViewMenuWiringIfMissing();
				RefreshActiveFill();
			}
		}

		void OnFullViewMenuOpenRightDockClicked() {
			var sk = Global_Skeleton_UI.instance;
			if (sk == null || !sk.TryGetSidePanelVisibility(out bool left, out bool right)) {
				return;
			}
			bool changed = false;
			if (!left && !right) {
				changed = sk.SetSidePanelVisibility(false, true);
			} else if (!left && right) {
				// Return to center-only on-screen full view (both outer columns from skeleton: no left, close right).
				changed = sk.SetSidePanelVisibility(false, false);
			} else {
				return;
			}
			if (!changed) {
				return;
			}
			FullView_OuterPanel_Chrome_Binder.SyncChromeToDriver();
			sk.ForceLayoutRefreshAfterPanelResize();
			ViewportFullViewOnScreen_Driver.SyncFromCurrentSkeleton();
			// Single adaptive lane: resolve from current side state after toggle, then run a deferred settle pass.
			ViewportFullViewOnScreen_Driver.ApplyAdaptiveResolutionToSdInputsForCurrentSideState();
			ViewportFullViewOnScreen_Driver.ScheduleAdaptiveResolutionToSdInputsNextFrame();
			RefreshActiveFill();
		}

		void ToggleFullViewMenu(bool show) {
			if (_fullViewMenuRt == null || _fullViewMenuCg == null) {
				return;
			}
			if (_fullViewMenuAnimRoutine != null) {
				StopCoroutine(_fullViewMenuAnimRoutine);
				_fullViewMenuAnimRoutine = null;
			}
			if (show) {
				if (_builtRowRt != null && _fullViewMenuRt.parent == _builtRowRt.parent) {
					_fullViewMenuRt.SetSiblingIndex(_builtRowRt.GetSiblingIndex() + 1);
				}
				_fullViewMenuRt.gameObject.SetActive(true);
				_fullViewMenuOpen = true;
				_fullViewMenuOpenedAtUnscaledTime = Time.unscaledTime;
			}
			_fullViewMenuAnimRoutine = StartCoroutine(CoAnimateFullViewMenu(show));
		}

		void ForceHideFullViewMenuInstant() {
			if (_fullViewMenuAnimRoutine != null) {
				StopCoroutine(_fullViewMenuAnimRoutine);
				_fullViewMenuAnimRoutine = null;
			}
			_fullViewMenuOpen = false;
			if (_fullViewMenuCg != null) {
				_fullViewMenuCg.alpha = 0f;
				_fullViewMenuCg.interactable = false;
				_fullViewMenuCg.blocksRaycasts = false;
			}
			if (_fullViewMenuRt != null) {
				_fullViewMenuRt.localScale = new Vector3(0.92f, 0.92f, 1f);
				_fullViewMenuRt.gameObject.SetActive(false);
			}
		}

		IEnumerator CoAnimateFullViewMenu(bool show) {
			if (_fullViewMenuRt == null || _fullViewMenuCg == null) {
				yield break;
			}
			float startA = _fullViewMenuCg.alpha;
			float endA = show ? 1f : 0f;
			Vector3 startS = _fullViewMenuRt.localScale;
			Vector3 endS = show ? Vector3.one : new Vector3(0.92f, 0.92f, 1f);
			const float dur = 0.14f;
			float t = 0f;
			_fullViewMenuCg.blocksRaycasts = show;
			_fullViewMenuCg.interactable = show;
			while (t < dur) {
				t += Time.unscaledDeltaTime;
				float k = Mathf.Clamp01(t / dur);
				k = 1f - Mathf.Pow(1f - k, 3f);
				_fullViewMenuCg.alpha = Mathf.Lerp(startA, endA, k);
				_fullViewMenuRt.localScale = Vector3.Lerp(startS, endS, k);
				yield return null;
			}
			_fullViewMenuCg.alpha = endA;
			_fullViewMenuRt.localScale = endS;
			if (!show) {
				_fullViewMenuOpen = false;
				_fullViewMenuCg.blocksRaycasts = false;
				_fullViewMenuCg.interactable = false;
				_fullViewMenuRt.gameObject.SetActive(false);
			}
			_fullViewMenuAnimRoutine = null;
		}
	}

	/// <summary>Last-resort coroutine host when ribbon/viewport/add-on managers are not yet enabled (attach RPC during early load).</summary>
	sealed class RibbonViewportDockRoutineHost : MonoBehaviour {

		static RibbonViewportDockRoutineHost s_inst;

		internal static RibbonViewportDockRoutineHost Get() {
			if (s_inst == null) {
				var go = new GameObject("[spz] RibbonDockCoroutineHost");
				DontDestroyOnLoad(go);
				s_inst = go.AddComponent<RibbonViewportDockRoutineHost>();
			}
			return s_inst;
		}
	}
}

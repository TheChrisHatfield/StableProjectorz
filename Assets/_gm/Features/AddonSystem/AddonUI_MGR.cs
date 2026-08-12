using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace spz {

	/// <summary>
	/// Manages dynamic UI creation for add-ons.
	/// Creates panels, buttons, and other UI elements requested by Python add-ons.
	/// </summary>
	public class AddonUI_MGR : MonoBehaviour {
		public static AddonUI_MGR instance { get; private set; }
		
		[SerializeField] RectTransform _addonPanelsParent; // Where to place add-on panels
		[SerializeField] GameObject _panelPrefab; // Generic panel prefab
		[SerializeField] GameObject _buttonPrefab; // Generic button prefab
		
		// Registry of UI elements by add-on ID
		private Dictionary<string, List<GameObject>> _addonUIElements = new Dictionary<string, List<GameObject>>();
		
		// Callback registry for button clicks
		private Dictionary<string, Action> _buttonCallbacks = new Dictionary<string, Action>();
		
		const string StableProjectorzGoAddonId = "StableProjectorzGO";
		const string NomadThemeAddonId = "NomadThemeSPZ";
		const string NomadThemeId = "nomad-inspired";
		const string NomadThemeLabel = "Nomad inspired";
		const float NomadDefaultFontScale = 0.84f;
		const float NomadDefaultSpacingScale = 0.94f;
		/// <summary>Charcoal skybox RGB from Nomad panel_bg / field_bg.</summary>
		static readonly Color NomadSkyboxTop = new Color(0x1E / 255f, 0x1F / 255f, 0x23 / 255f, 1f);
		static readonly Color NomadSkyboxBottom = new Color(0x12 / 255f, 0x13 / 255f, 0x17 / 255f, 1f);

		string _nomadFontScaleSliderId;
		string _nomadSpacingScaleSliderId;
		bool _nomadSkyboxCaptured;
		Color _nomadSkyboxTopBefore = Color.clear;
		Color _nomadSkyboxBottomBefore = Color.clear;

		/// <summary>Pro-Studio Monolith palette from the supplied Nomad UI replication design (rpc 1.13 scales).</summary>
		static JObject BuildNomadThemeTokens(float fontScale = NomadDefaultFontScale, float spacingScale = NomadDefaultSpacingScale) {
			return new JObject {
				["panel_bg"] = "#1E1F23F2",
				["control_bg"] = "#292A2EFF",
				["field_bg"] = "#121317FF",
				["accent"] = "#F2CA50FF",
				["text_primary"] = "#E3E2E7FF",
				["text_muted"] = "#D0C5AFFF",
				["handle"] = "#C8C5CBFF",
				["success"] = "#7BC96FFF",
				["danger"] = "#FFB4ABFF",
				["border"] = "#99907C66",
				["tab_active"] = "#343539FF",
				["selection"] = "#F2CA5033",
				["icon_tint"] = "#D0C5AFFF",
				["font_scale"] = fontScale,
				["spacing_scale"] = spacingScale,
				["corner_radius"] = 0,
				["panel_width"] = 220,
				["panel_alpha"] = 0.92,
				["ribbon_icon_only"] = 1,
			};
		}
		
		// Registry of UI element values by element ID
		private Dictionary<string, object> _uiElementValues = new Dictionary<string, object>();
		
		// Registry of UI element references by element ID
		private Dictionary<string, Component> _uiElementComponents = new Dictionary<string, Component>();

		/// <summary>
		/// Panels created before <see cref="CommandRibbon_UI"/> existed. Parked off-screen (never mid-viewport)
		/// until the ribbon can host them as proper tabs.
		/// </summary>
		sealed class ParkedPanel {
			public string addonId;
			public string title;
			public GameObject panel;
		}
		readonly List<ParkedPanel> _parkedForRibbon = new List<ParkedPanel>();
		bool _ribbonMigrateRunning;
		/// <summary>Completed 30s migrate passes while panels remain parked; capped to avoid infinite retry.</summary>
		int _ribbonMigrateRounds;
		const int kRibbonMigrateMaxRounds = 6;
		
		void Awake() {
			if (instance != null) { DestroyImmediate(gameObject); return; }
			instance = this;
			SpzUiThemeOps.ThemeChanged += ApplyThemeToAllAddonUi;
		}

		void Start() {
			// Legacy mid-screen AddonPanelsRoot (center anchors) must never stay visible.
			QuarantineLegacyMidScreenFallbackRoot();
			EnsureRibbonMigrateCoroutine();
			StartCoroutine(CoRestorePersistedThemeNextFrame());
		}

		System.Collections.IEnumerator CoRestorePersistedThemeNextFrame() {
			// Let other ThemeChanged subscribers finish Awake/Start first.
			yield return null;
			if (!SpzUiThemeOps.TryRestorePersistedTheme(out string detail)) {
				if (!string.IsNullOrEmpty(detail) && detail.IndexOf("no persisted", StringComparison.OrdinalIgnoreCase) < 0)
					UnityEngine.Debug.LogWarning($"[AddonUI_MGR] Theme restore skipped: {detail}");
				yield break;
			}
			UnityEngine.Debug.Log($"[AddonUI_MGR] Theme restore: {detail}");
			if (SpzUiThemeOps.ShouldRecolorBoundChrome) {
				// Do not compose charcoal skybox — SPZ background stays with BoundChrome.
				ComposeNomadStripIconsNative();
			}
		}

		void OnDestroy() {
			SpzUiThemeOps.ThemeChanged -= ApplyThemeToAllAddonUi;
			if (instance == this)
				instance = null;
		}

		void ApplyThemeToAllAddonUi() {
			foreach (var elements in _addonUIElements.Values) {
				if (elements == null)
					continue;
				foreach (var element in elements) {
					// Only panel roots: child widgets are covered by GetComponentsInChildren,
					// and re-applying on every registered control can retint hit-target images.
					if (element != null && element.name.StartsWith("AddonPanel_", StringComparison.Ordinal))
						SpzUiThemeOps.ApplyToAddonUiRoot(element);
				}
			}
		}
		
		/// <summary>
		/// Creates add-on UI under the ribbon tab shell for <paramref name="addonId"/> (see <see cref="AddonRibbonIntegration"/>).
		/// The shell shares the same stacked body rect as Art/ControlNet/Paint; widgets are parented as children of that shell.
		/// </summary>
		public string CreatePanel(string addonId, string title) {
			UnityEngine.Debug.Log($"[AddonUI_MGR] CreatePanel requested for addon: {addonId}, title: {title}");
			if (string.Equals(addonId, Addon_MGR.RibbonOnlyFullscreenAddonId, StringComparison.Ordinal)) {
				UnityEngine.Debug.Log("[AddonUI_MGR] Skipping command-ribbon tab/panel for RibbonOnlyFullscreen (viewport Gen Art strip only; enable in Add-on Manager).");
				return null;
			}
			// Hard gate: disabled add-ons must not create ribbon tabs (blocks stale Python register / prefs restore).
			if (!Addon_MGR.IsAddonEnabledStatic(addonId)) {
				UnityEngine.Debug.LogWarning(
					$"[AddonUI_MGR] Refusing CreatePanel for disabled add-on '{addonId}'. Enable it in Add-on Manager.");
				return null;
			}
			RectTransform parentForThisAddon = null;
			var commandRibbon = AddonRibbonIntegration.ResolveCommandRibbon();
			if (CommandRibbon_UI.instance == null && commandRibbon != null)
				UnityEngine.Debug.Log("[AddonUI_MGR] CommandRibbon_UI.instance was null; resolved ribbon via FindObjectOfType(including inactive).");
			bool forceParkHiddenRibbon = !Addon_MGR.ShouldShowInCommandRibbonStatic(addonId);
			bool ribbonResolved = commandRibbon != null && !forceParkHiddenRibbon;
			if (forceParkHiddenRibbon && commandRibbon != null) {
				// Prefer a single parking host — drop any leftover ribbon tab/shell without destroying panel content.
				commandRibbon.RemoveAddonPanelPreservingContent(addonId);
			}
			if (ribbonResolved) {
				parentForThisAddon = commandRibbon.GetOrCreatePanelForAddon(addonId, title);
				if (parentForThisAddon != null)
					UnityEngine.Debug.Log($"[AddonUI_MGR] Got ribbon panel parent for: {title}");
				else
					UnityEngine.Debug.LogWarning($"[AddonUI_MGR] GetOrCreatePanelForAddon returned null for: {addonId}. Parking until ribbon shell is ready.");
			} else if (forceParkHiddenRibbon) {
				UnityEngine.Debug.Log($"[AddonUI_MGR] Host pref hide ribbon for '{addonId}' — parking panel (add-on stays active).");
			} else {
				UnityEngine.Debug.LogWarning("[AddonUI_MGR] CommandRibbon_UI not found yet. Parking panel off-screen until ribbon is ready (will not overlay the viewport).");
			}
			// Temporary parking — ribbon missing OR ribbon present but shell not creatable yet OR host ribbon pref off.
			bool parkedPendingRibbon = parentForThisAddon == null;
			if (parkedPendingRibbon)
				parentForThisAddon = EnsureHiddenAddonPanelsParking();
			if (parentForThisAddon == null) {
				UnityEngine.Debug.LogError("[AddonUI_MGR] No parent found for add-on panels (ribbon and parking failed). Returning null.");
				return null;
			}
			// Reuse an existing AddonPanel_* under the resolved parent (ribbon shell OR parking).
			// Must run when ribbon is missing too, or repeated CreatePanel stacks duplicate parked panels.
			string expectedPanelName = $"AddonPanel_{addonId}_{title}";
			for (int ch = 0; ch < parentForThisAddon.childCount; ch++) {
				var t = parentForThisAddon.GetChild(ch);
				if (t == null) {
					continue;
				}
				if (!IsExactAddonPanelChild(t.name, addonId, title, expectedPanelName))
					continue;
				var go = t.gameObject;
				if (!_addonUIElements.ContainsKey(addonId)) {
					_addonUIElements[addonId] = new List<GameObject>();
				}
				if (!_addonUIElements[addonId].Contains(go)) {
					_addonUIElements[addonId].Add(go);
				}
				if (Addon_MGR.instance != null) {
					Addon_MGR.instance.RegisterAddonUI(addonId, go);
				}
				// Reload / second create_panel: clear widgets so add_button does not stack duplicates.
				ClearAddonPanelChildren(go.transform);
				var reuseTitle = go.GetComponentInChildren<TextMeshProUGUI>(true);
				if (reuseTitle != null) {
					reuseTitle.text = title;
					if (reuseTitle.GetComponent<LayoutElement>() == null) {
						var titleLe = reuseTitle.gameObject.AddComponent<LayoutElement>();
						titleLe.preferredHeight = ProjectUiScale.Space(3);
						titleLe.minHeight = ProjectUiScale.Space(2);
						titleLe.flexibleWidth = 1f;
					}
				} else {
					var titleObj = new GameObject("Title");
					titleObj.transform.SetParent(go.transform, false);
					var titleLe = titleObj.AddComponent<LayoutElement>();
					titleLe.preferredHeight = ProjectUiScale.Space(3);
					titleLe.minHeight = ProjectUiScale.Space(2);
					titleLe.flexibleWidth = 1f;
					reuseTitle = titleObj.AddComponent<TextMeshProUGUI>();
					reuseTitle.text = title;
					reuseTitle.fontSize = 18;
					reuseTitle.overflowMode = TextOverflowModes.Ellipsis;
					ApplyRuntimeTmpFont(reuseTitle);
				}
				SpzUiThemeOps.ApplyToAddonUiRoot(go);
				if (!parkedPendingRibbon) {
					PurgeParkedForAddon(addonId, go);
					ClearAddonShellWaitingPlaceholder(parentForThisAddon);
				} else {
					EnsureParkedEntry(addonId, title, go);
				}
				UnityEngine.Debug.Log($"[AddonUI_MGR] Reusing existing panel for {addonId} under {parentForThisAddon.name} (parked={parkedPendingRibbon})");
				return go.GetInstanceID().ToString();
			}
			UnityEngine.Debug.Log($"[AddonUI_MGR] Creating panel content under: {parentForThisAddon.name}");

			// Create panel GameObject
			GameObject panelObj;
			if (_panelPrefab != null) {
				panelObj = Instantiate(_panelPrefab, parentForThisAddon);
				// Prefab instances keep the prefab asset name; theme apply and reuse both key off AddonPanel_*.
				panelObj.name = $"AddonPanel_{addonId}_{title}";
			} else {
				// Create basic panel if no prefab
				panelObj = new GameObject($"AddonPanel_{addonId}_{title}");
				panelObj.transform.SetParent(parentForThisAddon, false);
				
				var rectTransform = panelObj.AddComponent<RectTransform>();
				rectTransform.anchorMin = new Vector2(0, 0);
				rectTransform.anchorMax = new Vector2(1, 1);
				rectTransform.sizeDelta = Vector2.zero;
				
				var image = panelObj.AddComponent<Image>();
				image.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
				
				var verticalLayout = panelObj.AddComponent<VerticalLayoutGroup>();
				int pad = Mathf.RoundToInt(ProjectUiScale.Space(1)); // 8px grid
				verticalLayout.spacing = ProjectUiScale.Space(1);
				verticalLayout.padding = new RectOffset(pad, pad, pad, pad); // left, right, top, bottom
				verticalLayout.childControlHeight = false;
				verticalLayout.childControlWidth = true;
				verticalLayout.childForceExpandHeight = false;
				verticalLayout.childForceExpandWidth = true;
			}

			// Shell LayoutElement so Nomad panel_width applies here — not to every AddButton face.
			var shellLe = panelObj.GetComponent<LayoutElement>();
			if (shellLe == null)
				shellLe = panelObj.AddComponent<LayoutElement>();
			SpzUiThemeOps.ApplyPanelWidth(shellLe);
			shellLe.flexibleWidth = 1f;
			var titleText = panelObj.GetComponentInChildren<TextMeshProUGUI>();
			if (titleText == null) {
				// Try to find or create title
				var titleObj = new GameObject("Title");
				titleObj.transform.SetParent(panelObj.transform, false);
				var titleLe = titleObj.AddComponent<LayoutElement>();
				titleLe.preferredHeight = ProjectUiScale.Space(3); // 24
				titleLe.minHeight = ProjectUiScale.Space(2);
				titleLe.flexibleWidth = 1f;
				titleText = titleObj.AddComponent<TextMeshProUGUI>();
				titleText.text = title;
				titleText.fontSize = 18;
				titleText.overflowMode = TextOverflowModes.Ellipsis;
				ApplyRuntimeTmpFont(titleText);
			} else {
				titleText.text = title;
				if (titleText.GetComponent<LayoutElement>() == null) {
					var titleLe = titleText.gameObject.AddComponent<LayoutElement>();
					titleLe.preferredHeight = ProjectUiScale.Space(3);
					titleLe.minHeight = ProjectUiScale.Space(2);
					titleLe.flexibleWidth = 1f;
				}
			}
			
			// Register with add-on
			if (!_addonUIElements.ContainsKey(addonId)) {
				_addonUIElements[addonId] = new List<GameObject>();
			}
			_addonUIElements[addonId].Add(panelObj);
			
			// Register with Addon_MGR
			if (Addon_MGR.instance != null) {
				Addon_MGR.instance.RegisterAddonUI(addonId, panelObj);
			}
			SpzUiThemeOps.ApplyToAddonUiRoot(panelObj);

			if (parkedPendingRibbon) {
				_parkedForRibbon.Add(new ParkedPanel {
					addonId = addonId,
					title = title,
					panel = panelObj,
				});
				// Only retry migrate when a ribbon tab is expected; host-hidden stays parked until pref flips.
				if (Addon_MGR.ShouldShowInCommandRibbonStatic(addonId)) {
					_ribbonMigrateRounds = 0;
					EnsureRibbonMigrateCoroutine();
				}
			} else {
				// Live under ribbon — purge any earlier parked duplicates for this addon.
				PurgeParkedForAddon(addonId, panelObj);
				ClearAddonShellWaitingPlaceholder(parentForThisAddon);
			}
			
			// Return panel ID (use GameObject instance ID)
			return panelObj.GetInstanceID().ToString();
		}

		/// <summary>
		/// Removes parking-lot entries for <paramref name="addonId"/>. Destroys parked GOs that are not
		/// <paramref name="keepAlive"/> so a later migrate cannot stack a second panel under the ribbon.
		/// </summary>
		void PurgeParkedForAddon(string addonId, GameObject keepAlive) {
			if (string.IsNullOrEmpty(addonId)) return;
			for (int i = _parkedForRibbon.Count - 1; i >= 0; i--) {
				ParkedPanel parked = _parkedForRibbon[i];
				if (parked == null || parked.panel == null) {
					_parkedForRibbon.RemoveAt(i);
					continue;
				}
				if (!string.Equals(parked.addonId, addonId, StringComparison.Ordinal))
					continue;
				if (keepAlive != null && parked.panel == keepAlive) {
					_parkedForRibbon.RemoveAt(i);
					continue;
				}
				UnityEngine.Debug.Log($"[AddonUI_MGR] Purging parked duplicate for {addonId}: {parked.panel.name}");
				UnityEngine.Object.Destroy(parked.panel);
				_parkedForRibbon.RemoveAt(i);
			}
		}

		/// <summary>Ensures <paramref name="panel"/> is tracked for ribbon migrate (idempotent).</summary>
		void EnsureParkedEntry(string addonId, string title, GameObject panel) {
			if (panel == null || string.IsNullOrEmpty(addonId)) return;
			for (int p = 0; p < _parkedForRibbon.Count; p++) {
				if (_parkedForRibbon[p] != null && _parkedForRibbon[p].panel == panel)
					return;
			}
			_parkedForRibbon.Add(new ParkedPanel {
				addonId = addonId,
				title = title,
				panel = panel,
			});
			if (!Addon_MGR.ShouldShowInCommandRibbonStatic(addonId))
				return;
			_ribbonMigrateRounds = 0;
			EnsureRibbonMigrateCoroutine();
		}

		/// <summary>
		/// Reparents salvaged AddonPanel_* content into off-screen parking after a failed ribbon shell recreate.
		/// Keeps GameObject instance IDs alive for AddonUI_MGR / Python.
		/// </summary>
		public void ReparkSalvagedAddonContent(string addonId, string title, List<Transform> content) {
			if (content == null || content.Count == 0) return;
			var parking = EnsureHiddenAddonPanelsParking();
			if (parking == null) {
				UnityEngine.Debug.LogError("[AddonUI_MGR] ReparkSalvagedAddonContent: parking unavailable; leaving salvaged roots unparented.");
				return;
			}
			for (int i = 0; i < content.Count; i++) {
				Transform child = content[i];
				if (child == null) continue;
				child.SetParent(parking, false);
				var rt = child as RectTransform;
				if (rt != null) {
					rt.anchorMin = Vector2.zero;
					rt.anchorMax = Vector2.one;
					rt.sizeDelta = Vector2.zero;
					rt.anchoredPosition = Vector2.zero;
				}
				string id = addonId;
				string panelTitle = title;
				if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(panelTitle))
					TryParseAddonPanelName(child.name, out id, out panelTitle);
				EnsureParkedEntry(id ?? addonId, panelTitle ?? title ?? child.name, child.gameObject);
			}
			UnityEngine.Debug.Log($"[AddonUI_MGR] Reparked {content.Count} salvaged panel(s) for {addonId} after failed ribbon recreate.");
		}

		/// <summary>
		/// Hidden off-screen parking for panels created before the command ribbon exists.
		/// Must never use center anchors (legacy AddonPanelsRoot put Camera Tools mid-viewport).
		/// </summary>
		RectTransform EnsureHiddenAddonPanelsParking() {
			if (_addonPanelsParent != null
			    && _addonPanelsParent
			    && string.Equals(_addonPanelsParent.name, "AddonPanelsParking", StringComparison.Ordinal)) {
				_addonPanelsParent.gameObject.SetActive(false);
				return _addonPanelsParent;
			}
			var existing = GameObject.Find("AddonPanelsParking");
			if (existing != null) {
				var rt = existing.transform as RectTransform;
				if (rt != null) {
					// Legacy parking used center anchors and painted mid-viewport when briefly active.
					rt.anchorMin = new Vector2(0f, 0f);
					rt.anchorMax = new Vector2(0f, 0f);
					rt.pivot = new Vector2(0f, 0f);
					rt.sizeDelta = new Vector2(320f, 800f);
					rt.anchoredPosition = new Vector2(-4000f, -4000f);
					existing.SetActive(false);
					var ray = existing.GetComponentInParent<GraphicRaycaster>();
					if (ray != null && existing.transform.parent != null
					    && string.Equals(existing.transform.parent.name, "AddonPanelsParking_Canvas", StringComparison.Ordinal))
						ray.enabled = false;
					_addonPanelsParent = rt;
					return rt;
				}
			}
			Transform canvasParent = null;
			var rightPanel = GameObject.Find("UI_Global_Right_Panel");
			if (rightPanel != null) {
				var canvas = rightPanel.GetComponentInChildren<Canvas>();
				if (canvas != null) canvasParent = canvas.transform;
			}
			if (canvasParent == null) {
				var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
				if (canvas != null) canvasParent = canvas.transform;
			}
			if (canvasParent == null) {
				var canvasObj = new GameObject("AddonPanelsParking_Canvas");
				canvasObj.layer = 5;
				var c = canvasObj.AddComponent<Canvas>();
				c.renderMode = RenderMode.ScreenSpaceOverlay;
				c.sortingOrder = -100; // behind app chrome while parking
				canvasObj.AddComponent<CanvasScaler>();
				// No GraphicRaycaster — parking must never steal viewport / ribbon hits.
				canvasParent = canvasObj.transform;
			}
			var root = new GameObject("AddonPanelsParking");
			root.transform.SetParent(canvasParent, false);
			var parking = root.AddComponent<RectTransform>();
			// Park far off-screen; keep inactive so children cannot raycast or paint the viewport.
			parking.anchorMin = new Vector2(0f, 0f);
			parking.anchorMax = new Vector2(0f, 0f);
			parking.pivot = new Vector2(0f, 0f);
			parking.sizeDelta = new Vector2(320f, 800f);
			parking.anchoredPosition = new Vector2(-4000f, -4000f);
			root.SetActive(false);
			_addonPanelsParent = parking;
			return parking;
		}

		void QuarantineLegacyMidScreenFallbackRoot() {
			var legacy = GameObject.Find("AddonPanelsRoot");
			if (legacy == null) return;
			UnityEngine.Debug.LogWarning("[AddonUI_MGR] Quarantining legacy mid-screen AddonPanelsRoot (Camera Tools overlay).");
			var parking = EnsureHiddenAddonPanelsParking();
			for (int i = legacy.transform.childCount - 1; i >= 0; i--) {
				var child = legacy.transform.GetChild(i);
				if (child == null) continue;
				if (!TryParseAddonPanelName(child.name, out string addonId, out string title)) {
					child.SetParent(parking, false);
					continue;
				}
				child.SetParent(parking, false);
				bool already = false;
				for (int p = 0; p < _parkedForRibbon.Count; p++) {
					if (_parkedForRibbon[p].panel == child.gameObject) { already = true; break; }
				}
				if (!already) {
					if (!Addon_MGR.IsAddonEnabledStatic(addonId)) {
						UnityEngine.Debug.Log(
							$"[AddonUI_MGR] Discarding legacy panel for disabled add-on '{addonId}' (no park/migrate).");
						Destroy(child.gameObject);
						continue;
					}
					_parkedForRibbon.Add(new ParkedPanel {
						addonId = addonId,
						title = title,
						panel = child.gameObject,
					});
					_ribbonMigrateRounds = 0;
				}
			}
			legacy.SetActive(false);
			Destroy(legacy);
		}

		/// <summary>
		/// Parses <c>AddonPanel_{addonId}_{title}</c>. Uses longest known addon-id prefix so ids that
		/// contain underscores (e.g. <c>My_Cool_Addon</c>) are not truncated at the first underscore.
		/// </summary>
		bool TryParseAddonPanelName(string goName, out string addonId, out string title) {
			addonId = null;
			title = null;
			const string prefix = "AddonPanel_";
			if (string.IsNullOrEmpty(goName) || !goName.StartsWith(prefix, StringComparison.Ordinal))
				return false;
			string rest = goName.Substring(prefix.Length);
			string bestId = null;
			void Consider(string id) {
				if (string.IsNullOrEmpty(id) || rest.Length <= id.Length) return;
				if (!rest.StartsWith(id, StringComparison.Ordinal)) return;
				if (rest[id.Length] != '_') return;
				if (bestId == null || id.Length > bestId.Length)
					bestId = id;
			}
			if (Addon_MGR.instance != null) {
				var registered = Addon_MGR.instance.GetAddons();
				if (registered != null) {
					foreach (var id in registered.Keys)
						Consider(id);
				}
			}
			foreach (var id in _addonUIElements.Keys)
				Consider(id);
			for (int p = 0; p < _parkedForRibbon.Count; p++) {
				if (_parkedForRibbon[p] != null)
					Consider(_parkedForRibbon[p].addonId);
			}
			if (bestId != null) {
				addonId = bestId;
				title = rest.Substring(bestId.Length + 1);
				return !string.IsNullOrEmpty(addonId) && !string.IsNullOrEmpty(title);
			}
			// Fallback for unknown addons: first underscore (legacy ids without underscores).
			int sep = rest.IndexOf('_');
			if (sep <= 0 || sep >= rest.Length - 1)
				return false;
			addonId = rest.Substring(0, sep);
			title = rest.Substring(sep + 1);
			return !string.IsNullOrEmpty(addonId);
		}

		void EnsureRibbonMigrateCoroutine() {
			if (_ribbonMigrateRunning || !isActiveAndEnabled)
				return;
			if (_ribbonMigrateRounds >= kRibbonMigrateMaxRounds) {
				// Late ribbon after give-up: allow another migrate cycle when shell exists and panels await show.
				if (AddonRibbonIntegration.ResolveCommandRibbon() == null || CountParkedAwaitingRibbonShow() == 0)
					return;
				UnityEngine.Debug.Log(
					"[AddonUI_MGR] Ribbon became available after migrate give-up — resetting round counter.");
				_ribbonMigrateRounds = 0;
			}
			_ribbonMigrateRunning = true;
			StartCoroutine(MigrateParkedPanelsToRibbon_crtn());
		}

		IEnumerator MigrateParkedPanelsToRibbon_crtn() {
			const float maxWait = 30f;
			float elapsed = 0f;
			try {
				while (elapsed < maxWait) {
					QuarantineLegacyMidScreenFallbackRoot();
					TryMigrateParkedPanelsNow();
					if (CountParkedAwaitingRibbonShow() == 0)
						yield break;
					elapsed += 0.25f;
					yield return new WaitForSeconds(0.25f);
				}
				int pending = CountParkedAwaitingRibbonShow();
				if (pending > 0)
					UnityEngine.Debug.LogWarning($"[AddonUI_MGR] {pending} add-on panel(s) still parked after {maxWait}s (ribbon shell not ready).");
			}
			finally {
				_ribbonMigrateRunning = false;
				if (CountParkedAwaitingRibbonShow() == 0) {
					_ribbonMigrateRounds = 0;
				} else {
					_ribbonMigrateRounds++;
					if (_ribbonMigrateRounds < kRibbonMigrateMaxRounds && isActiveAndEnabled) {
						// Back off between passes so we do not spin forever at 0.25s.
						float backoff = Mathf.Min(8f, 0.5f * _ribbonMigrateRounds);
						StartCoroutine(RestartRibbonMigrateAfterBackoff_crtn(backoff));
					} else if (CountParkedAwaitingRibbonShow() > 0) {
						UnityEngine.Debug.LogWarning(
							$"[AddonUI_MGR] Giving up ribbon migrate after {kRibbonMigrateMaxRounds} passes; {CountParkedAwaitingRibbonShow()} panel(s) remain parked.");
					}
				}
			}
		}

		IEnumerator RestartRibbonMigrateAfterBackoff_crtn(float delaySeconds) {
			if (delaySeconds > 0f)
				yield return new WaitForSeconds(delaySeconds);
			if (CountParkedAwaitingRibbonShow() > 0 && isActiveAndEnabled && !_ribbonMigrateRunning)
				EnsureRibbonMigrateCoroutine();
		}

		/// <summary>Parked panels that should move onto the ribbon when the shell is ready (excludes host ribbon-hidden).</summary>
		int CountParkedAwaitingRibbonShow() {
			int n = 0;
			for (int i = 0; i < _parkedForRibbon.Count; i++) {
				ParkedPanel parked = _parkedForRibbon[i];
				if (parked == null || parked.panel == null)
					continue;
				if (!Addon_MGR.IsAddonEnabledStatic(parked.addonId))
					continue;
				if (!Addon_MGR.ShouldShowInCommandRibbonStatic(parked.addonId))
					continue;
				n++;
			}
			return n;
		}

		/// <summary>Re-run park→ribbon migrate (e.g. after host pref Show in Command Ribbon turns on).</summary>
		public void RequestMigrateParkedPanelsNow() {
			TryMigrateParkedPanelsNow();
			if (CountParkedAwaitingRibbonShow() == 0)
				return;
			// Pref flip / late ribbon must not be blocked by a prior give-up counter.
			_ribbonMigrateRounds = 0;
			EnsureRibbonMigrateCoroutine();
		}

		/// <summary>
		/// True when the panel lives under the off-screen parking host (ribbon missing or Show-in-Ribbon off).
		/// Python create_panel success must not be read as "visible tab".
		/// </summary>
		public bool IsPanelParkedOffRibbon(string panelId) {
			GameObject go = FindUIElement(panelId);
			if (go == null) return false;
			for (int i = 0; i < _parkedForRibbon.Count; i++) {
				var p = _parkedForRibbon[i];
				if (p != null && p.panel == go)
					return true;
			}
			Transform t = go.transform;
			while (t != null) {
				if (string.Equals(t.name, "AddonPanelsParking", StringComparison.Ordinal)
				    || string.Equals(t.name, "HiddenAddonPanelsParking", StringComparison.Ordinal))
					return true;
				t = t.parent;
			}
			return false;
		}

		void TryMigrateParkedPanelsNow() {
			var ribbon = AddonRibbonIntegration.ResolveCommandRibbon();
			if (ribbon == null) return;
			for (int i = _parkedForRibbon.Count - 1; i >= 0; i--) {
				ParkedPanel parked = _parkedForRibbon[i];
				if (parked == null || parked.panel == null) {
					_parkedForRibbon.RemoveAt(i);
					continue;
				}
				// Disabled add-ons must not get a ribbon tab via park migration (CreatePanel is gated; migrate was not).
				if (!Addon_MGR.IsAddonEnabledStatic(parked.addonId)) {
					UnityEngine.Debug.Log(
						$"[AddonUI_MGR] Discarding parked panel for disabled add-on '{parked.addonId}'.");
					Destroy(parked.panel);
					_parkedForRibbon.RemoveAt(i);
					continue;
				}
				if (!Addon_MGR.ShouldShowInCommandRibbonStatic(parked.addonId)) {
					// Host pref: keep under parking; leave on migrate list until ribbon is shown again.
					continue;
				}
				RectTransform shell = ribbon.GetOrCreatePanelForAddon(parked.addonId, parked.title);
				if (shell == null) {
					UnityEngine.Debug.LogWarning($"[AddonUI_MGR] Ribbon shell still null for parked {parked.addonId}; will retry.");
					continue;
				}
				parked.panel.transform.SetParent(shell, false);
				var rt = parked.panel.transform as RectTransform;
				if (rt != null) {
					rt.anchorMin = Vector2.zero;
					rt.anchorMax = Vector2.one;
					rt.sizeDelta = Vector2.zero;
					rt.anchoredPosition = Vector2.zero;
				}
				SpzUiThemeOps.ApplyToAddonUiRoot(parked.panel);
				ClearAddonShellWaitingPlaceholder(shell);
				UnityEngine.Debug.Log($"[AddonUI_MGR] Migrated parked panel '{parked.panel.name}' onto ribbon shell {shell.name}");
				_parkedForRibbon.RemoveAt(i);
			}
		}

		/// <summary>Removes the temporary “loading…” label CommandRibbon shows before Python create_panel.</summary>
		static void ClearAddonShellWaitingPlaceholder(Transform shell) {
			if (shell == null) return;
			Transform ph = shell.Find("AddonShell_WaitingPlaceholder");
			if (ph != null)
				UnityEngine.Object.Destroy(ph.gameObject);
		}
		
		/// <summary>
		/// When Python HTTP never runs create_panel, seed a minimal in-process panel for known add-ons so the ribbon tab is not blank.
		/// </summary>
		/// <summary>
		/// When Python HTTP never runs create_panel, seed a minimal in-process panel for known add-ons so the ribbon tab is not blank.
		/// </summary>
		/// <param name="force">True from MarkAddonLoadFailed — seed even while the launcher PID is still alive.</param>
		public void EnsureNativeFallbackUiWhenPythonMissing(string addonId, bool force = false) {
			if (string.IsNullOrEmpty(addonId) || !Addon_MGR.IsAddonEnabledStatic(addonId))
				return;
			if (!force && !Addon_MGR.ShouldSeedNativeAddonFallbackStatic())
				return;
			if (string.Equals(addonId, StableProjectorzGoAddonId, StringComparison.Ordinal)) {
				EnsureNativeSpzGoPanel();
				return;
			}
			if (string.Equals(addonId, NomadThemeAddonId, StringComparison.Ordinal)) {
				EnsureNativeNomadThemePanel();
			}
		}

		void EnsureNativeSpzGoPanel() {
			// Path TextInputs alone must not skip seeding — Import/Export buttons are the other half.
			if (TryGetLiveAddonPanel(StableProjectorzGoAddonId, out GameObject existingPanel)
			    && PanelHasNamedControlPrefix(existingPanel, "Button_Import")
			    && PanelHasNamedControlPrefix(existingPanel, "Button_Export"))
				return;

			string panelId;
			if (existingPanel != null) {
				panelId = existingPanel.GetInstanceID().ToString();
				if (!_addonUIElements.ContainsKey(StableProjectorzGoAddonId))
					_addonUIElements[StableProjectorzGoAddonId] = new List<GameObject>();
				if (!_addonUIElements[StableProjectorzGoAddonId].Contains(existingPanel))
					_addonUIElements[StableProjectorzGoAddonId].Add(existingPanel);
				UnityEngine.Debug.Log("[AddonUI_MGR] Completing native SPZ GO panel (path fields present, action buttons missing).");
				EnsureNativeSpzGoMissingWidgets(panelId, existingPanel, seedPathsIfMissing: true);
				return;
			}

			panelId = CreatePanel(StableProjectorzGoAddonId, "SPZ GO");
			if (string.IsNullOrEmpty(panelId)) {
				UnityEngine.Debug.LogWarning("[AddonUI_MGR] Native SPZ GO fallback: CreatePanel failed.");
				return;
			}
			UnityEngine.Debug.Log("[AddonUI_MGR] Seeding native SPZ GO panel (Python create_panel missing / HTTP :5557 down).");
			string importDefault = "";
			string exportDefault = "";
			var fp = FastPath_API.instance;
			string dataDir = fp != null ? fp.GetProjectDataDirOrSession() : null;
			if (!string.IsNullOrEmpty(dataDir)) {
				string exchange = Path.Combine(dataDir, "StableProjectorzGO_exchange");
				importDefault = Path.Combine(exchange, "from_blender.fbx");
				exportDefault = Path.Combine(exchange, "from_spz.fbx");
			}
			// Order: primary paths first, optional Blender last (matches Python register()).
			AddTextInput(StableProjectorzGoAddonId, panelId, "Import path", importDefault);
			AddTextInput(StableProjectorzGoAddonId, panelId, "Export path", exportDefault);
			AddTextInput(StableProjectorzGoAddonId, panelId, "Blender.exe (optional)", "");
			AddButton(StableProjectorzGoAddonId, panelId, "Import", "do_import_from_path");
			AddButton(StableProjectorzGoAddonId, panelId, "Export", "do_export_to_path");
			AddButton(StableProjectorzGoAddonId, panelId, "Install into Blender", "do_install_blender_addon_force");
		}

		void EnsureNativeSpzGoMissingWidgets(string panelId, GameObject panel, bool seedPathsIfMissing) {
			if (panel == null || string.IsNullOrEmpty(panelId)) return;
			if (seedPathsIfMissing) {
				string importDefault = "";
				string exportDefault = "";
				var fp = FastPath_API.instance;
				string dataDir = fp != null ? fp.GetProjectDataDirOrSession() : null;
				if (!string.IsNullOrEmpty(dataDir)) {
					string exchange = Path.Combine(dataDir, "StableProjectorzGO_exchange");
					importDefault = Path.Combine(exchange, "from_blender.fbx");
					exportDefault = Path.Combine(exchange, "from_spz.fbx");
				}
				if (!PanelHasNamedControlPrefix(panel, "TextInput_Import path"))
					AddTextInput(StableProjectorzGoAddonId, panelId, "Import path", importDefault);
				if (!PanelHasNamedControlPrefix(panel, "TextInput_Export path"))
					AddTextInput(StableProjectorzGoAddonId, panelId, "Export path", exportDefault);
				if (!PanelHasNamedControlPrefix(panel, "TextInput_Blender.exe"))
					AddTextInput(StableProjectorzGoAddonId, panelId, "Blender.exe (optional)", "");
			}
			if (!PanelHasNamedControlPrefix(panel, "Button_Import"))
				AddButton(StableProjectorzGoAddonId, panelId, "Import", "do_import_from_path");
			if (!PanelHasNamedControlPrefix(panel, "Button_Export"))
				AddButton(StableProjectorzGoAddonId, panelId, "Export", "do_export_to_path");
			if (!PanelHasNamedControlPrefix(panel, "Button_Install into Blender"))
				AddButton(StableProjectorzGoAddonId, panelId, "Install into Blender", "do_install_blender_addon_force");
		}

		bool TryGetLiveAddonPanel(string addonId, out GameObject panel) {
			panel = null;
			if (!_addonUIElements.TryGetValue(addonId, out var list) || list == null)
				return false;
			for (int i = 0; i < list.Count; i++) {
				var go = list[i];
				if (go == null) continue;
				if (!go.name.StartsWith("AddonPanel_", StringComparison.Ordinal)) continue;
				panel = go;
				return true;
			}
			return false;
		}

		static bool PanelHasNamedControlPrefix(GameObject panel, string namePrefix) {
			if (panel == null || string.IsNullOrEmpty(namePrefix)) return false;
			var transforms = panel.GetComponentsInChildren<Transform>(true);
			for (int c = 0; c < transforms.Length; c++) {
				var ch = transforms[c];
				if (ch == null || ch == panel.transform) continue;
				if (ch.name.StartsWith(namePrefix, StringComparison.Ordinal))
					return true;
			}
			return false;
		}

		void EnsureNativeNomadThemePanel() {
			if (HasLiveAddonPanelWithWidgets(NomadThemeAddonId))
				return;
			string panelId = CreatePanel(NomadThemeAddonId, "Nomad Theme");
			if (string.IsNullOrEmpty(panelId)) {
				UnityEngine.Debug.LogWarning("[AddonUI_MGR] Native Nomad fallback: CreatePanel failed.");
				return;
			}
			UnityEngine.Debug.Log("[AddonUI_MGR] Seeding native Nomad Theme panel (Python create_panel missing / HTTP :5557 down).");
			AddButton(NomadThemeAddonId, panelId, "Apply Pro-Studio Nomad palette", "apply_nomad_palette");
			AddButton(NomadThemeAddonId, panelId, "Restore StableProjectorz palette", "restore_stableprojectorz_palette");
			_nomadFontScaleSliderId = AddSlider(NomadThemeAddonId, panelId, "Font scale", 0.75f, 1.5f, NomadDefaultFontScale);
			_nomadSpacingScaleSliderId = AddSlider(NomadThemeAddonId, panelId, "Spacing scale", 0.75f, 1.5f, NomadDefaultSpacingScale);
			AddButton(NomadThemeAddonId, panelId, "Apply Scales", "apply_nomad_scales");
			AddButton(NomadThemeAddonId, panelId, "Refresh Theme Status", "refresh_nomad_theme_status");
		}

		bool HasLiveAddonPanelWithWidgets(string addonId) {
			if (!_addonUIElements.TryGetValue(addonId, out var list) || list == null)
				return false;
			for (int i = 0; i < list.Count; i++) {
				var go = list[i];
				if (go == null) continue;
				if (!go.name.StartsWith("AddonPanel_", StringComparison.Ordinal)) continue;
				// Title only = not enough; need at least one control descendant beyond Title
				// (widgets may sit under Content/ wrappers from Python layouts).
				var transforms = go.GetComponentsInChildren<Transform>(true);
				int controls = 0;
				for (int c = 0; c < transforms.Length; c++) {
					var ch = transforms[c];
					if (ch == null || ch == go.transform) continue;
					if (string.Equals(ch.name, "Title", StringComparison.Ordinal)) continue;
					if (ch.name.StartsWith("Button_", StringComparison.Ordinal)
					    || ch.name.StartsWith("TextInput_", StringComparison.Ordinal)
					    || ch.name.StartsWith("Slider_", StringComparison.Ordinal)
					    || ch.name.StartsWith("Dropdown_", StringComparison.Ordinal)
					    || ch.name.StartsWith("Toggle_", StringComparison.Ordinal))
						controls++;
				}
				if (controls > 0) return true;
			}
			return false;
		}

		static void ApplyRuntimeTmpFont(TextMeshProUGUI tmp) {
			if (tmp == null || tmp.font != null) return;
			TextMeshProUGUI src = null;
			if (CommandRibbon_UI.instance != null)
				src = CommandRibbon_UI.instance.GetComponentInChildren<TextMeshProUGUI>(true);
			if (src == null)
				src = UnityEngine.Object.FindObjectOfType<TextMeshProUGUI>();
			if (src != null && src.font != null) {
				tmp.font = src.font;
				tmp.fontSharedMaterial = src.fontSharedMaterial;
			}
		}

		/// <summary>
		/// Adds a button to a panel
		/// </summary>
		public string AddButton(string addonId, string panelId, string label, string callbackName) {
			GameObject panelObj = FindUIElement(panelId);
			if (panelObj == null) {
				UnityEngine.Debug.LogError($"[AddonUI_MGR] Panel {panelId} not found");
				return null;
			}
			
			// Create button
			GameObject buttonObj;
			if (_buttonPrefab != null) {
				buttonObj = Instantiate(_buttonPrefab, panelObj.transform);
				// Widget probes and theme roles key off Button_*; prefab asset names break empty-shell detection.
				buttonObj.name = $"Button_{label}";
				var prefabLabel = buttonObj.GetComponentInChildren<TextMeshProUGUI>(true);
				if (prefabLabel != null)
					prefabLabel.text = label;
			} else {
				// Create basic button if no prefab
				buttonObj = new GameObject($"Button_{label}");
				buttonObj.transform.SetParent(panelObj.transform, false);
				
				var rectTransform = buttonObj.AddComponent<RectTransform>();
				// Do not use panel_width for button preferredWidth — Nomad 400px blows up ribbon rows.
				const float btnW = 280f;
				rectTransform.sizeDelta = new Vector2(btnW, ProjectUiScale.Space(4) + 4f); // ~36
				var layoutElement = buttonObj.AddComponent<LayoutElement>();
				layoutElement.preferredHeight = ProjectUiScale.Space(4) + 4f;
				layoutElement.minHeight = ProjectUiScale.Space(4);
				layoutElement.preferredWidth = btnW;
				layoutElement.flexibleWidth = 1f;
				
				var image = buttonObj.AddComponent<Image>();
				if (image.sprite == null) {
					image.sprite = UiRuntimeSprites.SolidRect;
					image.type = Image.Type.Simple;
				}
				SpzUiThemeOps.ApplyRoundedControlSprite(image, markEligible: true);
				image.color = new Color(0.3f, 0.3f, 0.3f, 1f);
				image.raycastTarget = true;
				
				var button = buttonObj.AddComponent<Button>();
				button.targetGraphic = image;
				
				// Add text label
				var textObj = new GameObject("Text");
				textObj.transform.SetParent(buttonObj.transform, false);
				var textRect = textObj.AddComponent<RectTransform>();
				textRect.anchorMin = Vector2.zero;
				textRect.anchorMax = Vector2.one;
				textRect.sizeDelta = Vector2.zero;
				
				var text = textObj.AddComponent<TextMeshProUGUI>();
				text.text = label;
				text.fontSize = 14;
				text.alignment = TextAlignmentOptions.Center;
				text.color = Color.white;
				text.raycastTarget = false;
				ApplyRuntimeTmpFont(text);
			}
			
			// Set up button click handler
			var buttonComponent = buttonObj.GetComponent<Button>();
			if (buttonComponent != null) {
				string callbackId = $"{addonId}_{callbackName}";
				buttonComponent.onClick.AddListener(() => {
					if (_buttonCallbacks.ContainsKey(callbackId)) {
						_buttonCallbacks[callbackId]?.Invoke();
					} else {
						// Send callback to Python server
						SendCallbackToPython(addonId, callbackName);
					}
				});
				RegisterSpzGoNativeButtonCallbackIfNeeded(addonId, panelId, callbackName);
				RegisterNomadThemeNativeButtonCallbackIfNeeded(addonId, callbackName);
			}
			
			// Register with add-on
			if (_addonUIElements.ContainsKey(addonId)) {
				_addonUIElements[addonId].Add(buttonObj);
			}
			SpzUiThemeOps.ApplyToAddonUiRoot(buttonObj);
			
			return buttonObj.GetInstanceID().ToString();
		}

		/// <summary>
		/// Adds a checkbox-style toggle to a panel (rpc 1.14+). Value is bool via get/set_value.
		/// Optional <paramref name="callbackName"/> is invoked when the user toggles (same channel as buttons).
		/// </summary>
		public string AddToggle(string addonId, string panelId, string label, bool defaultOn, string callbackName = null) {
			GameObject panelObj = FindUIElement(panelId);
			if (panelObj == null) {
				UnityEngine.Debug.LogError($"[AddonUI_MGR] Panel {panelId} not found");
				return null;
			}

			GameObject toggleObj = new GameObject($"Toggle_{label}");
			toggleObj.transform.SetParent(panelObj.transform, false);
			var toggleRect = toggleObj.AddComponent<RectTransform>();
			const float rowW = 280f;
			toggleRect.sizeDelta = new Vector2(rowW, 32);
			var layoutElement = toggleObj.AddComponent<LayoutElement>();
			layoutElement.preferredHeight = 32f;
			layoutElement.minHeight = 28f;
			layoutElement.preferredWidth = rowW;
			layoutElement.flexibleWidth = 1f;

			var bg = toggleObj.AddComponent<Image>();
			if (bg.sprite == null) {
				bg.sprite = UiRuntimeSprites.SolidRect;
				bg.type = Image.Type.Simple;
			}
			SpzUiThemeOps.ApplyRoundedControlSprite(bg, markEligible: true);
			bg.color = new Color(0.3f, 0.3f, 0.3f, 1f);
			bg.raycastTarget = true;

			var toggle = toggleObj.AddComponent<Toggle>();
			toggle.targetGraphic = bg;
			toggle.isOn = defaultOn;

			var checkGo = new GameObject("Checkmark");
			checkGo.transform.SetParent(toggleObj.transform, false);
			var checkRt = checkGo.AddComponent<RectTransform>();
			checkRt.anchorMin = new Vector2(0f, 0.2f);
			checkRt.anchorMax = new Vector2(0.18f, 0.8f);
			checkRt.offsetMin = Vector2.zero;
			checkRt.offsetMax = Vector2.zero;
			var checkImg = checkGo.AddComponent<Image>();
			// Null sprite = invisible ON; match Manager dial/ribbon checkmarks.
			checkImg.sprite = UiRuntimeSprites.CircleFilled;
			checkImg.type = Image.Type.Simple;
			checkImg.preserveAspect = true;
			checkImg.color = new Color(0.3f, 0.6f, 1f, 1f);
			checkImg.raycastTarget = false;
			// Assign graphic before BoundChrome; never solid-square the ON glyph (IsToggleCheckmarkGraphic).
			toggle.graphic = checkImg;

			var labelGo = new GameObject("Label");
			labelGo.transform.SetParent(toggleObj.transform, false);
			var labelRt = labelGo.AddComponent<RectTransform>();
			labelRt.anchorMin = new Vector2(0.2f, 0f);
			labelRt.anchorMax = new Vector2(1f, 1f);
			labelRt.offsetMin = Vector2.zero;
			labelRt.offsetMax = Vector2.zero;
			var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
			labelTmp.text = label ?? "Toggle";
			labelTmp.fontSize = 14;
			labelTmp.alignment = TextAlignmentOptions.Left;
			labelTmp.color = Color.white;
			labelTmp.raycastTarget = false;
			ApplyRuntimeTmpFont(labelTmp);

			string elementId = toggleObj.GetInstanceID().ToString();
			_uiElementValues[elementId] = defaultOn;
			_uiElementComponents[elementId] = toggle;
			toggle.onValueChanged.AddListener(isOn => {
				_uiElementValues[elementId] = isOn;
				SendValueChangeToPython(addonId, elementId, "toggle", isOn);
				if (!string.IsNullOrEmpty(callbackName)) {
					string callbackId = $"{addonId}_{callbackName}";
					if (_buttonCallbacks.ContainsKey(callbackId))
						_buttonCallbacks[callbackId]?.Invoke();
					else
						SendCallbackToPython(addonId, callbackName);
				}
				// BoundChrome selection fill is ThemeChanged-only without this retint.
				SpzUiThemeOps.ApplyToAddonUiRoot(toggleObj);
			});

			if (_addonUIElements.ContainsKey(addonId))
				_addonUIElements[addonId].Add(toggleObj);
			SpzUiThemeOps.ApplyToAddonUiRoot(toggleObj);
			return elementId;
		}
		
		/// <summary>
		/// SPZ GO Import/Export must work when FastAPI is up but the add-on is registered in-Unity; HTTP /invoke_callback can fail
		/// (Python module key mismatch) or be unreachable. In-process handlers read the same TMP fields the Python path uses.
		/// </summary>
		void RegisterSpzGoNativeButtonCallbackIfNeeded(string addonId, string panelId, string callbackName) {
			if (!string.Equals(addonId, StableProjectorzGoAddonId, StringComparison.Ordinal))
				return;
			if (string.Equals(callbackName, "do_import_from_path", StringComparison.Ordinal)) {
				RegisterButtonCallback(addonId, callbackName, () => SpzGoRunHeadlessImportOrExportFromPanel(addonId, panelId, callbackName, isImport: true));
			} else if (string.Equals(callbackName, "do_export_to_path", StringComparison.Ordinal)) {
				RegisterButtonCallback(addonId, callbackName, () => SpzGoRunHeadlessImportOrExportFromPanel(addonId, panelId, callbackName, isImport: false));
			} else if (string.Equals(callbackName, "do_install_blender_addon_force", StringComparison.Ordinal)) {
				RegisterButtonCallback(addonId, callbackName, () => SpzGoNativeInstallBlenderBridge(panelId));
			}
		}

		void SpzGoNativeInstallBlenderBridge(string panelId) {
			if (_spzGoBlenderInstallBusy) {
				SpzGoStatusLine("Blender install already running…", true);
				return;
			}
			StartCoroutine(CoSpzGoNativeInstallBlenderBridge(panelId));
		}

		bool _spzGoBlenderInstallBusy;

		IEnumerator CoSpzGoNativeInstallBlenderBridge(string panelId) {
			if (_spzGoBlenderInstallBusy)
				yield break;
			_spzGoBlenderInstallBusy = true;
			try {
				string blender = SpzGoReadBlenderExeFromPanel(panelId);
				if (string.IsNullOrEmpty(blender))
					blender = FastPath_API.FindBlenderExecutable();
				var fp = FastPath_API.instance;
				if (fp == null) {
					SpzGoStatusLine("3D / API not ready", false);
					yield break;
				}
				SpzGoStatusLine("Installing Blender add-on…", true);
				string message = null;
				bool ok = false;
				string blenderCopy = blender;
				var worker = new System.Threading.Thread(() => {
					try {
						ok = fp.TryInstallSpzGoBlenderBridge(blenderCopy, force: true, out message);
					} catch (Exception ex) {
						ok = false;
						message = ex.Message;
					}
				});
				worker.IsBackground = true;
				worker.Name = "SpzGoBlenderInstall";
				worker.Start();
				while (worker.IsAlive)
					yield return null;
				UnityEngine.Debug.Log($"[AddonUI_MGR] SPZ GO Blender install: ok={ok} {message}");
				if (ok && message != null && message.StartsWith("SPZ_GO_INSTALL_SKIP", StringComparison.Ordinal))
					SpzGoStatusLine("Blender add-on up to date", true);
				else if (ok)
					SpzGoStatusLine("Blender add-on installed", true);
				else
					SpzGoStatusLine(string.IsNullOrEmpty(message) ? "Blender install failed" : TruncateStatus(message), false);
			} finally {
				_spzGoBlenderInstallBusy = false;
			}
		}

		static string TruncateStatus(string message) {
			if (string.IsNullOrEmpty(message)) return message;
			const int max = 120;
			return message.Length <= max ? message : message.Substring(0, max - 1) + "…";
		}

		string SpzGoReadBlenderExeFromPanel(string panelId) {
			var panel = FindUIElement(panelId);
			if (panel == null) return "";
			var allT = panel.transform.GetComponentsInChildren<Transform>(true);
			for (int i = 0; i < allT.Length; i++) {
				var t = allT[i];
				if (t == null || t.name == null) continue;
				if (!t.name.StartsWith("TextInput_", StringComparison.Ordinal)) continue;
				if (t.name.IndexOf("Blender", StringComparison.OrdinalIgnoreCase) < 0) continue;
				var inf = t.GetComponentInChildren<TMP_InputField>(true);
				if (inf == null) continue;
				string path = (inf.text ?? "").Trim().Trim('"');
				if (string.IsNullOrEmpty(path))
					break; // empty Blender field — fall back to FindBlenderExecutable
				try { return Path.GetFullPath(path); } catch { return path; }
			}
			return "";
		}

		/// <summary>
		/// Nomad Theme Apply/Restore/scales must work even when Python HTTP /invoke_callback is down or the module is not loaded.
		/// Mirrors the SPZ GO in-process button pattern so ribbon clicks are not dead.
		/// </summary>
		void RegisterNomadThemeNativeButtonCallbackIfNeeded(string addonId, string callbackName) {
			if (!string.Equals(addonId, NomadThemeAddonId, StringComparison.Ordinal))
				return;
			if (string.Equals(callbackName, "apply_nomad_palette", StringComparison.Ordinal)) {
				RegisterButtonCallback(addonId, callbackName, ApplyNomadThemeNative);
			} else if (string.Equals(callbackName, "restore_stableprojectorz_palette", StringComparison.Ordinal)) {
				RegisterButtonCallback(addonId, callbackName, RestoreSpzThemeNative);
			} else if (string.Equals(callbackName, "apply_nomad_scales", StringComparison.Ordinal)) {
				RegisterButtonCallback(addonId, callbackName, ApplyNomadScalesNative);
			} else if (string.Equals(callbackName, "refresh_nomad_theme_status", StringComparison.Ordinal)) {
				RegisterButtonCallback(addonId, callbackName, RefreshNomadThemeStatusNative);
			}
		}

		float ReadNomadSliderOrDefault(string elementId, float fallback) {
			if (string.IsNullOrEmpty(elementId))
				return fallback;
			object raw = GetUIElementValue(elementId);
			if (raw is float f)
				return f;
			if (raw is double d)
				return (float)d;
			if (raw != null && float.TryParse(raw.ToString(), out float parsed))
				return parsed;
			return fallback;
		}

		void CaptureNomadSkyboxIfNeeded() {
			if (_nomadSkyboxCaptured)
				return;
			var skybox = SkyboxBackground_MGR.instance;
			if (skybox == null) {
				// Do not mark captured with Color.clear — Restore would wipe the live skybox once mgr exists.
				return;
			}
			_nomadSkyboxTopBefore = skybox.GetTopColor();
			_nomadSkyboxBottomBefore = skybox.GetBottomColor();
			_nomadSkyboxCaptured = true;
		}

		void ComposeNomadSkyboxNative() {
			CaptureNomadSkyboxIfNeeded();
			var fastPath = FastPath_API.instance;
			if (fastPath == null) {
				UnityEngine.Debug.LogWarning("[AddonUI_MGR] Nomad skybox compose skipped — FastPath_API missing.");
				return;
			}
			bool okTop = fastPath.SetSkyboxColor(true, NomadSkyboxTop.r, NomadSkyboxTop.g, NomadSkyboxTop.b, NomadSkyboxTop.a);
			bool okBot = fastPath.SetSkyboxColor(false, NomadSkyboxBottom.r, NomadSkyboxBottom.g, NomadSkyboxBottom.b, NomadSkyboxBottom.a);
			if (!okTop || !okBot)
				UnityEngine.Debug.LogWarning($"[AddonUI_MGR] Nomad skybox compose partial failure (top={okTop}, bottom={okBot}).");
		}

		void RestoreNomadSkyboxNative() {
			if (!_nomadSkyboxCaptured) {
				// Never captured — do not wipe the live skybox to Color.clear.
				return;
			}
			var fastPath = FastPath_API.instance;
			Color top = _nomadSkyboxTopBefore;
			Color bottom = _nomadSkyboxBottomBefore;
			_nomadSkyboxCaptured = false;
			if (fastPath == null) {
				UnityEngine.Debug.LogWarning("[AddonUI_MGR] Nomad skybox restore skipped — FastPath_API missing.");
				return;
			}
			fastPath.SetSkyboxColor(true, top.r, top.g, top.b, top.a);
			fastPath.SetSkyboxColor(false, bottom.r, bottom.g, bottom.b, bottom.a);
		}

		void ApplyNomadThemeNative() {
			MaybeBindNomadSlidersFromExistingPanel();
			float fontScale = ReadNomadSliderOrDefault(_nomadFontScaleSliderId, NomadDefaultFontScale);
			float spacingScale = ReadNomadSliderOrDefault(_nomadSpacingScaleSliderId, NomadDefaultSpacingScale);
			var tokens = BuildNomadThemeTokens(fontScale, spacingScale);
			if (!SpzUiThemeOps.TryRegisterTheme(NomadThemeId, NomadThemeLabel, tokens, NomadThemeAddonId, out string error)) {
				UnityEngine.Debug.LogWarning($"[AddonUI_MGR] Nomad register_theme failed: {error}");
				ShowAddonButtonStatus($"Nomad theme register failed: {error}", false);
				return;
			}
			if (!SpzUiThemeOps.TryApplyTheme(NomadThemeId, null, "replace", out error)) {
				UnityEngine.Debug.LogWarning($"[AddonUI_MGR] Nomad apply_theme failed: {error}");
				ShowAddonButtonStatus($"Nomad theme apply failed: {error}", false);
				return;
			}
			// Keep SPZ viewport/skybox gradient — Nomad is chrome only (do not paint charcoal over SPZ BG).
			ComposeNomadStripIconsNative();
			UnityEngine.Debug.Log($"[AddonUI_MGR] Applied native Nomad theme '{NomadThemeId}' (font={fontScale:F2}, spacing={spacingScale:F2}) + strip icons (SPZ skybox kept)");
			ShowAddonButtonStatus("Pro-Studio Nomad palette applied", true);
		}

		void ComposeNomadStripIconsNative() {
			// Specific needles first — bare "Art" would also hit "art bg list".
			var pairs = new (string tab, string icon)[] {
				("Paint", "Brush"),
				("art bg", "Layers"),
				("Art BG", "Layers"),
				("art list", "Image"),
				("Control", "Grid"),
				("CTRL", "Grid"),
				("controlnet", "Grid"),
				("Mesh", "Mesh"),
				("3D", "Mesh"),
				("Obj", "Mesh"),
				("Nomad", "Settings"),
			};
			int ok = 0;
			foreach (var pair in pairs) {
				if (SpzUiThemeOps.TrySetStripTabLineIcon(pair.tab, pair.icon, out _))
					ok++;
			}
			UnityEngine.Debug.Log($"[AddonUI_MGR] Nomad strip line icons applied ({ok}/{pairs.Length} matches)");
		}

		void RestoreSpzThemeNative() {
			SpzUiThemeOps.ResetTheme();
			RestoreNomadSkyboxNative();
			UnityEngine.Debug.Log("[AddonUI_MGR] Restored StableProjectorz default palette + skybox (native)");
			ShowAddonButtonStatus("StableProjectorz palette restored", true);
		}

		void ApplyNomadScalesNative() {
			if (!string.Equals(SpzUiThemeOps.ActiveThemeId, NomadThemeId, StringComparison.Ordinal)) {
				UnityEngine.Debug.LogWarning("[AddonUI_MGR] apply_nomad_scales refused — Nomad theme not active.");
				ShowAddonButtonStatus("Apply Nomad Palette first", false);
				return;
			}
			MaybeBindNomadSlidersFromExistingPanel();
			float fontScale = ReadNomadSliderOrDefault(_nomadFontScaleSliderId, NomadDefaultFontScale);
			float spacingScale = ReadNomadSliderOrDefault(_nomadSpacingScaleSliderId, NomadDefaultSpacingScale);
			var patch = new JObject {
				["font_scale"] = fontScale,
				["spacing_scale"] = spacingScale,
			};
			if (!SpzUiThemeOps.TryApplyTheme(NomadThemeId, patch, "patch", out string error)) {
				UnityEngine.Debug.LogWarning($"[AddonUI_MGR] Nomad scale patch failed: {error}");
				ShowAddonButtonStatus($"Nomad scale patch failed: {error}", false);
				return;
			}
			UnityEngine.Debug.Log($"[AddonUI_MGR] Patched Nomad scales font={fontScale:F3} spacing={spacingScale:F3}");
			ShowAddonButtonStatus($"Scales applied ({fontScale:F2}/{spacingScale:F2})", true);
		}

		void RefreshNomadThemeStatusNative() {
			var theme = SpzUiThemeOps.GetThemeResult();
			var catalog = SpzUiThemeOps.ListThemesResult();
			var surfaces = theme["surfaces"] as JArray;
			int bound = 0;
			int total = surfaces != null ? surfaces.Count : 0;
			if (surfaces != null) {
				foreach (var s in surfaces) {
					if (s is JObject jo && jo["bound"] != null && jo["bound"].Type == JTokenType.Boolean && (bool)jo["bound"])
						bound++;
				}
			}
			string msg =
				$"theme={theme["theme_id"]} bound={bound}/{total} registered={catalog["registered_count"]}";
			UnityEngine.Debug.Log($"[AddonUI_MGR] Nomad theme status: {msg}");
			ShowAddonButtonStatus(msg, true);
		}

		/// <summary>
		/// When Python created the panel first, native apply still needs slider instance ids.
		/// </summary>
		void MaybeBindNomadSlidersFromExistingPanel() {
			if (IsLiveNomadSliderId(_nomadFontScaleSliderId) && IsLiveNomadSliderId(_nomadSpacingScaleSliderId))
				return;
			if (!IsLiveNomadSliderId(_nomadFontScaleSliderId))
				_nomadFontScaleSliderId = null;
			if (!IsLiveNomadSliderId(_nomadSpacingScaleSliderId))
				_nomadSpacingScaleSliderId = null;
			if (!_addonUIElements.TryGetValue(NomadThemeAddonId, out var list) || list == null)
				return;
			foreach (var go in list) {
				if (go == null) continue;
				// Sliders are panel children (Slider_*), not the panel root — scan descendants too.
				var transforms = go.GetComponentsInChildren<Transform>(true);
				for (int i = 0; i < transforms.Length; i++) {
					var t = transforms[i];
					if (t == null || t.name == null) continue;
					if (string.IsNullOrEmpty(_nomadFontScaleSliderId)
					    && t.name.StartsWith("Slider_Font scale", StringComparison.Ordinal)
					    && t.GetComponentInChildren<Slider>(true) != null)
						_nomadFontScaleSliderId = t.gameObject.GetInstanceID().ToString();
					else if (string.IsNullOrEmpty(_nomadSpacingScaleSliderId)
					         && t.name.StartsWith("Slider_Spacing scale", StringComparison.Ordinal)
					         && t.GetComponentInChildren<Slider>(true) != null)
						_nomadSpacingScaleSliderId = t.gameObject.GetInstanceID().ToString();
				}
			}
		}

		bool IsLiveNomadSliderId(string elementId) {
			if (string.IsNullOrEmpty(elementId))
				return false;
			if (!_uiElementComponents.TryGetValue(elementId, out var c) || !IsLiveUiComponent(c))
				return false;
			return c is Slider;
		}

		static void ShowAddonButtonStatus(string message, bool ok) {
			if (Viewport_StatusText.instance != null)
				Viewport_StatusText.instance.ShowStatusText(message, false, ok ? 3.5f : 5f, false);
		}
		
		/// <summary>
		/// Resolve Import/Export path fields by label name (not sibling index) so demoting Blender.exe
		/// or reordering rows cannot wire the wrong TMP field.
		/// </summary>
		static TMP_InputField FindSpzGoPathField(List<Transform> textRowRoots, bool isImport) {
			if (textRowRoots == null || textRowRoots.Count == 0)
				return null;
			string needle = isImport ? "Import" : "Export";
			for (int i = 0; i < textRowRoots.Count; i++) {
				var t = textRowRoots[i];
				if (t == null || t.name == null) continue;
				// Skip optional Blender row even if it somehow matched.
				if (t.name.IndexOf("Blender", StringComparison.OrdinalIgnoreCase) >= 0)
					continue;
				if (t.name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
					return t.GetComponentInChildren<TMP_InputField>(true);
			}
			// Legacy fallback: Blender, Import, Export (old register order) OR Import, Export, Blender.
			var fields = new List<TMP_InputField>(3);
			for (int i = 0; i < textRowRoots.Count; i++) {
				var inf = textRowRoots[i].GetComponentInChildren<TMP_InputField>(true);
				if (inf != null) fields.Add(inf);
			}
			if (fields.Count >= 3) {
				// Prefer indices 0/1 when first row is Import; else 1/2 for Blender-first.
				string n0 = textRowRoots[0] != null ? textRowRoots[0].name : "";
				if (!string.IsNullOrEmpty(n0) && n0.IndexOf("Import", StringComparison.OrdinalIgnoreCase) >= 0)
					return fields[isImport ? 0 : 1];
				return fields[isImport ? 1 : 2];
			}
			if (fields.Count >= 2)
				return fields[isImport ? 0 : 1];
			return null;
		}

		/// <summary>Path fields are resolved by name (Import / Export); Blender.exe is optional and demoted.</summary>
		void SpzGoRunHeadlessImportOrExportFromPanel(string addonId, string panelId, string callbackName, bool isImport) {
			bool okNative = TrySpzGoRunHeadlessImportOrExportFromPanel(panelId, isImport);
			if (okNative) {
				return;
			}
			UnityEngine.Debug.LogWarning($"[AddonUI_MGR] SPZ GO native {(isImport ? "import" : "export")} failed; falling back to Python callback {addonId}.{callbackName}.");
			SendCallbackToPython(addonId, callbackName);
		}

		bool TrySpzGoRunHeadlessImportOrExportFromPanel(string panelId, bool isImport) {
			var panel = FindUIElement(panelId);
			if (panel == null) {
				UnityEngine.Debug.LogWarning($"[AddonUI_MGR] SPZ GO: panel {panelId} not found for headless {(isImport ? "import" : "export")}.");
				SpzGoStatusLine(isImport ? "Import: panel not ready" : "Export: panel not ready", false);
				return false;
			}
			// Do not only scan immediate children: a panel prefab can wrap the stack (Title / Content) so
			// TextInput_* are nested; depth-first order matches add_text_input call order in register().
			var tr = panel.transform;
			var allT = tr.GetComponentsInChildren<Transform>(true);
			var textRowRoots = new List<Transform>(3);
			for (int i = 0; i < allT.Length; i++) {
				var t = allT[i];
				if (t == null || t == tr) continue;
				if (t.name == null) continue;
				if (t.name.StartsWith("TextInput_", StringComparison.Ordinal))
					textRowRoots.Add(t);
			}
			var field = FindSpzGoPathField(textRowRoots, isImport);
			if (field == null) {
				UnityEngine.Debug.LogWarning("[AddonUI_MGR] SPZ GO: Import/Export path field not found (got " + textRowRoots.Count + " text rows).");
				SpzGoStatusLine("Set Import/Export paths in the add-on panel", false);
				return false;
			}
			string path = field.text;
			if (string.IsNullOrWhiteSpace(path)) {
				SpzGoStatusLine(isImport ? "Import: path is empty" : "Export: path is empty", false);
				return false;
			}
			path = path.Trim().Trim('"');
			try {
				path = Path.GetFullPath(path);
			} catch (Exception e) {
				UnityEngine.Debug.LogWarning("[AddonUI_MGR] SPZ GO path: " + e.Message);
				SpzGoStatusLine("Invalid path", false);
				return false;
			}
			if (isImport) {
				if (!File.Exists(path)) {
					SpzGoStatusLine("Import: file not found", false);
					return false;
				}
			} else {
				if (string.IsNullOrEmpty(Path.GetFileName(path))) {
					SpzGoStatusLine("Export: set a file path (e.g. from_spz.fbx)", false);
					return false;
				}
			}
			var fp = FastPath_API.instance;
			if (fp == null) {
				SpzGoStatusLine("3D / API not ready", false);
				return false;
			}
			if (isImport) {
				bool importStarted = fp.Import3DModelFromFile(path);
				if (!importStarted) {
					SpzGoStatusLine("Import failed (see log)", false);
					return false;
				}
				StartCoroutine(CoSpzGoFinishImportWhenIdle());
				return true;
			}
			// Mesh write is sync; albedo/AO encode continues under Save_MGR._isSaving.
			// Returning true here only means "started" — do not fall back to Python mid-write.
			bool exportStarted = fp.Export3DWithTexturesToPath(path);
			if (!exportStarted) {
				SpzGoStatusLine("Export failed (valid path / API ready?)", false);
				return false;
			}
			StartCoroutine(CoSpzGoFinishExportWhenSaveIdle(path));
			return true;
		}

		/// <summary>
		/// After native headless export starts, wait for texture pipeline before claiming success
		/// (same contract as deferred TCP <c>export_3d_with_textures_to_path</c>).
		/// </summary>
		IEnumerator CoSpzGoFinishExportWhenSaveIdle(string exportMeshPath) {
			SpzGoStatusLine("Export: writing textures…", true);
			const float timeoutSec = 300f;
			float elapsed = 0f;
			var sm = Save_MGR.instance;
			while (sm != null && sm._isSaving && elapsed < timeoutSec) {
				elapsed += Time.unscaledDeltaTime;
				yield return null;
				sm = Save_MGR.instance;
			}
			// sm == null must not count as success (previously: sm == null || !sm._isSaving).
			bool saveIdle = sm != null && !sm._isSaving;
			if (sm == null)
				UnityEngine.Debug.LogWarning("[AddonUI_MGR] SPZ GO native export: Save_MGR unavailable during texture write.");
			else if (!saveIdle)
				UnityEngine.Debug.LogWarning("[AddonUI_MGR] SPZ GO native export: texture write still in progress after timeout.");

			bool stampOk = false;
			if (saveIdle) {
				string stampMeshPath = exportMeshPath;
				var mhExport = ModelsHandler_3D.instance;
				if (mhExport != null && !string.IsNullOrEmpty(mhExport._path_recentlyExported))
					stampMeshPath = mhExport._path_recentlyExported;
				if (!string.IsNullOrEmpty(stampMeshPath)) {
					try {
						string stamp = Path.Combine(
							Path.GetDirectoryName(stampMeshPath) ?? "",
							Path.GetFileNameWithoutExtension(stampMeshPath) + ".spz_go_ready");
						stampOk = File.Exists(stamp);
						if (!stampOk)
							UnityEngine.Debug.LogWarning("[AddonUI_MGR] SPZ GO native export: ready stamp missing — Blender auto-import will not fire: " + stamp);
					} catch (Exception ex) {
						UnityEngine.Debug.LogWarning("[AddonUI_MGR] SPZ GO native export: stamp check failed: " + ex.Message);
					}
				}
			}

			bool ok = saveIdle && stampOk;
			string status;
			if (ok)
				status = "Export OK — Blender can auto-import";
			else if (saveIdle && !stampOk)
				status = "Export wrote mesh/textures; ready stamp missing (Blender auto-import may not run)";
			else
				status = "Export failed (texture write timeout)";
			SpzGoStatusLine(status, ok);
		}

		IEnumerator CoSpzGoFinishImportWhenIdle() {
			SpzGoStatusLine("Import: loading mesh…", true);
			const float timeoutSec = 300f;
			float elapsed = 0f;
			var mh = ModelsHandler_3D.instance;
			while (mh != null && mh._isImportingModel && elapsed < timeoutSec) {
				elapsed += Time.unscaledDeltaTime;
				yield return null;
				mh = ModelsHandler_3D.instance;
			}
			bool ok = mh != null && !mh._isImportingModel && mh._lastImportSucceeded;
			if (mh == null)
				UnityEngine.Debug.LogWarning("[AddonUI_MGR] SPZ GO native import: ModelsHandler unavailable during load.");
			else if (mh._isImportingModel)
				UnityEngine.Debug.LogWarning("[AddonUI_MGR] SPZ GO native import: still importing after timeout.");
			SpzGoStatusLine(ok ? "Import OK" : "Import failed (see log)", ok);
		}
		
		void SpzGoStatusLine(string text, bool ok) {
			if (Viewport_StatusText.instance != null)
				Viewport_StatusText.instance.ShowStatusText(text, false, ok ? 4f : 5f, false);
		}
		
		/// <summary>
		/// Registers a callback function for a button
		/// </summary>
		public void RegisterButtonCallback(string addonId, string callbackName, Action callback) {
			string callbackId = $"{addonId}_{callbackName}";
			_buttonCallbacks[callbackId] = callback;
		}
		
		/// <summary>
		/// Sends a callback event to the Python server (HTTP POST /invoke_callback). Runs the addon's function by name.
		/// </summary>
		void SendCallbackToPython(string addonId, string callbackName) {
			if (Addon_MGR.IsAddonApiShuttingDown()) {
				UnityEngine.Debug.Log($"[AddonUI_MGR] Skipping callback during shutdown: {addonId}.{callbackName}");
				return;
			}
			UnityEngine.Debug.Log($"[AddonUI_MGR] Invoking addon callback: {addonId}.{callbackName}");
			StartCoroutine(SendCallbackToPythonCrtn(addonId, callbackName));
		}

		IEnumerator SendCallbackToPythonCrtn(string addonId, string callbackName) {
			if (Addon_MGR.IsAddonApiShuttingDown())
				yield break;
			int port = Addon_MGR.instance != null ? Addon_MGR.instance.GetHttpServerPort() : 5557;
			string url = $"http://127.0.0.1:{port}/invoke_callback";
			string body = $"{{\"addon_id\":\"{JsonEscape(addonId)}\",\"callback\":\"{JsonEscape(callbackName)}\"}}";
			using (var req = new UnityWebRequest(url, "POST")) {
				req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
				req.downloadHandler = new DownloadHandlerBuffer();
				req.SetRequestHeader("Content-Type", "application/json");
				req.timeout = 8; // avoid long hangs when local addon HTTP server is down/unresponsive
				yield return req.SendWebRequest();
				if (req.result != UnityWebRequest.Result.Success) {
					UnityEngine.Debug.LogWarning($"[AddonUI_MGR] invoke_callback failed: {req.error}");
					ShowAddonButtonStatus($"Add-on action failed (HTTP): {addonId}.{callbackName}", false);
					yield break;
				}
				bool callbackSucceeded = false;
				try {
					var json = JObject.Parse(req.downloadHandler?.text ?? "{}");
					callbackSucceeded = json["success"]?.Value<bool>() ?? false;
				} catch {
					// Response not valid JSON or missing success
				}
				if (callbackSucceeded)
					UnityEngine.Debug.Log($"[AddonUI_MGR] Callback invoked: {addonId}.{callbackName}");
				else {
					UnityEngine.Debug.LogWarning($"[AddonUI_MGR] Addon callback failed or not found: {addonId}.{callbackName}");
					ShowAddonButtonStatus($"Add-on action failed: {addonId}.{callbackName}", false);
				}
			}
		}
		
		static string JsonEscape(string s) {
			if (string.IsNullOrEmpty(s)) return "";
			return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
		}
		
		/// <summary>
		/// Finds a UI element by its ID
		/// </summary>
		GameObject FindUIElement(string elementId) {
			int instanceId;
			if (int.TryParse(elementId, out instanceId)) {
				// Search all registered UI elements
				foreach (var elements in _addonUIElements.Values) {
					foreach (var element in elements) {
						if (element != null && element.GetInstanceID() == instanceId) {
							return element;
						}
					}
				}
			}
			return null;
		}
		
		/// <summary>
		/// Adds a slider to a panel
		/// </summary>
		public string AddSlider(string addonId, string panelId, string label, float min, float max, float defaultValue) {
			GameObject panelObj = FindUIElement(panelId);
			if (panelObj == null) {
				UnityEngine.Debug.LogError($"[AddonUI_MGR] Panel {panelId} not found");
				return null;
			}
			
			// Create slider container
			GameObject sliderObj = new GameObject($"Slider_{label}");
			sliderObj.transform.SetParent(panelObj.transform, false);
			
			var sliderRect = sliderObj.AddComponent<RectTransform>();
			sliderRect.sizeDelta = new Vector2(280, 48);
			var sliderLe = sliderObj.AddComponent<LayoutElement>();
			sliderLe.preferredHeight = 48f;
			sliderLe.minHeight = 40f;
			sliderLe.preferredWidth = 280f;
			sliderLe.flexibleWidth = 1f;
			
			// Add label
			var labelObj = new GameObject("Label");
			labelObj.transform.SetParent(sliderObj.transform, false);
			var labelRect = labelObj.AddComponent<RectTransform>();
			labelRect.anchorMin = new Vector2(0, 0);
			labelRect.anchorMax = new Vector2(1, 0.5f);
			labelRect.sizeDelta = Vector2.zero;
			var labelText = labelObj.AddComponent<TextMeshProUGUI>();
			labelText.text = label;
			labelText.fontSize = 12;
			labelText.color = Color.white;
			labelText.enableWordWrapping = false;
			labelText.overflowMode = TextOverflowModes.Ellipsis;
			labelText.raycastTarget = false;
			ApplyRuntimeTmpFont(labelText);
			
			// Add slider
			var sliderObj2 = new GameObject("Slider");
			sliderObj2.transform.SetParent(sliderObj.transform, false);
			var sliderRect2 = sliderObj2.AddComponent<RectTransform>();
			sliderRect2.anchorMin = new Vector2(0, 0.5f);
			sliderRect2.anchorMax = new Vector2(1, 1);
			sliderRect2.sizeDelta = Vector2.zero;
			
			var sliderBg = sliderObj2.AddComponent<Image>();
			sliderBg.sprite = UiRuntimeSprites.SolidRect;
			sliderBg.type = Image.Type.Simple;
			sliderBg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
			SpzUiThemeOps.ApplyRoundedControlSprite(sliderBg, markEligible: true);
			
			var sliderFill = new GameObject("Fill");
			sliderFill.transform.SetParent(sliderObj2.transform, false);
			var fillRect = sliderFill.AddComponent<RectTransform>();
			fillRect.anchorMin = Vector2.zero;
			fillRect.anchorMax = new Vector2(0.5f, 1);
			fillRect.sizeDelta = Vector2.zero;
			var fillImage = sliderFill.AddComponent<Image>();
			fillImage.sprite = UiRuntimeSprites.SolidRect;
			fillImage.type = Image.Type.Simple;
			fillImage.color = new Color(0.3f, 0.6f, 1f, 1f);
			
			var sliderHandle = new GameObject("Handle");
			sliderHandle.transform.SetParent(sliderObj2.transform, false);
			var handleRect = sliderHandle.AddComponent<RectTransform>();
			handleRect.anchorMin = new Vector2(0.5f, 0);
			handleRect.anchorMax = new Vector2(0.5f, 1);
			handleRect.sizeDelta = new Vector2(20, 0);
			var handleImage = sliderHandle.AddComponent<Image>();
			handleImage.sprite = UiRuntimeSprites.SolidRect;
			handleImage.type = Image.Type.Simple;
			handleImage.color = Color.white;
			
			var slider = sliderObj2.AddComponent<Slider>();
			slider.minValue = min;
			slider.maxValue = max;
			slider.value = defaultValue;
			slider.fillRect = fillRect;
			slider.handleRect = handleRect;
			slider.targetGraphic = handleImage;
			
			// Add value text
			var valueObj = new GameObject("Value");
			valueObj.transform.SetParent(sliderObj.transform, false);
			var valueRect = valueObj.AddComponent<RectTransform>();
			valueRect.anchorMin = new Vector2(0.7f, 0.5f);
			valueRect.anchorMax = new Vector2(1, 1);
			valueRect.sizeDelta = Vector2.zero;
			var valueText = valueObj.AddComponent<TextMeshProUGUI>();
			valueText.text = defaultValue.ToString("F2");
			valueText.fontSize = 12;
			valueText.color = Color.white;
			valueText.alignment = TextAlignmentOptions.Right;
			valueText.raycastTarget = false;
			ApplyRuntimeTmpFont(valueText);
			
			// Update value text when slider changes
			slider.onValueChanged.AddListener((value) => {
				valueText.text = value.ToString("F2");
				string elementId = sliderObj.GetInstanceID().ToString();
				_uiElementValues[elementId] = value;
				SendValueChangeToPython(addonId, elementId, "slider", value);
			});
			
			// Register
			string elementId = sliderObj.GetInstanceID().ToString();
			_uiElementValues[elementId] = defaultValue;
			_uiElementComponents[elementId] = slider;
			
			if (_addonUIElements.ContainsKey(addonId)) {
				_addonUIElements[addonId].Add(sliderObj);
			}
			SpzUiThemeOps.ApplyToAddonUiRoot(sliderObj);

			if (string.Equals(addonId, NomadThemeAddonId, StringComparison.Ordinal)) {
				if (string.Equals(label, "Font scale", StringComparison.Ordinal))
					_nomadFontScaleSliderId = elementId;
				else if (string.Equals(label, "Spacing scale", StringComparison.Ordinal))
					_nomadSpacingScaleSliderId = elementId;
			}
			
			return elementId;
		}
		
		/// <summary>
		/// Adds a text input field to a panel
		/// </summary>
		public string AddTextInput(string addonId, string panelId, string label, string defaultValue) {
			GameObject panelObj = FindUIElement(panelId);
			if (panelObj == null) {
				UnityEngine.Debug.LogError($"[AddonUI_MGR] Panel {panelId} not found");
				return null;
			}
			
			// Stacked label-above-field (full width) — side-by-side 30/70 clipped long SPZ GO path labels.
			GameObject inputObj = new GameObject($"TextInput_{label}");
			inputObj.transform.SetParent(panelObj.transform, false);
			
			var inputRect = inputObj.AddComponent<RectTransform>();
			float rowH = ProjectUiScale.Space(2) + ProjectUiScale.Space(4) + 4f; // label ~16 + field ~36
			inputRect.sizeDelta = new Vector2(0f, rowH);
			var rowLe = inputObj.AddComponent<LayoutElement>();
			rowLe.preferredHeight = rowH;
			rowLe.minHeight = rowH;
			rowLe.flexibleWidth = 1f;

			var rowLayout = inputObj.AddComponent<VerticalLayoutGroup>();
			rowLayout.spacing = 0f;
			rowLayout.padding = new RectOffset(0, 0, 0, 0);
			// Must control child heights or LayoutElement on Label/InputField is ignored and rows collapse to 0.
			rowLayout.childControlHeight = true;
			rowLayout.childControlWidth = true;
			rowLayout.childForceExpandHeight = false;
			rowLayout.childForceExpandWidth = true;
			
			// Label (full width, above field)
			var labelObj = new GameObject("Label");
			labelObj.transform.SetParent(inputObj.transform, false);
			var labelRt = labelObj.GetComponent<RectTransform>() ?? labelObj.AddComponent<RectTransform>();
			labelRt.sizeDelta = new Vector2(0f, ProjectUiScale.Space(2));
			var labelLe = labelObj.AddComponent<LayoutElement>();
			labelLe.preferredHeight = ProjectUiScale.Space(2);
			labelLe.minHeight = ProjectUiScale.Space(2);
			labelLe.flexibleWidth = 1f;
			labelLe.flexibleHeight = 0f;
			var labelText = labelObj.AddComponent<TextMeshProUGUI>();
			labelText.text = label;
			labelText.fontSize = 12;
			labelText.color = Color.white;
			labelText.raycastTarget = false;
			labelText.enableWordWrapping = false;
			labelText.overflowMode = TextOverflowModes.Ellipsis;
			ApplyRuntimeTmpFont(labelText);
			
			// Input field (full width)
			var fieldObj = new GameObject("InputField");
			fieldObj.transform.SetParent(inputObj.transform, false);
			float fieldH = ProjectUiScale.Space(4) + 4f;
			var fieldRt = fieldObj.GetComponent<RectTransform>() ?? fieldObj.AddComponent<RectTransform>();
			fieldRt.sizeDelta = new Vector2(0f, fieldH);
			var fieldLe = fieldObj.AddComponent<LayoutElement>();
			fieldLe.preferredHeight = fieldH;
			fieldLe.minHeight = fieldH;
			fieldLe.flexibleWidth = 1f;
			fieldLe.flexibleHeight = 0f;
			
			var fieldBg = fieldObj.AddComponent<Image>();
			if (fieldBg.sprite == null) {
				fieldBg.sprite = UiRuntimeSprites.SolidRect;
				fieldBg.type = Image.Type.Simple;
			}
			SpzUiThemeOps.ApplyRoundedControlSprite(fieldBg, markEligible: true);
			fieldBg.color = new Color(0.15f, 0.15f, 0.15f, 1f);

			// Viewport + RectMask2D clips long paths inside the dark field (Overflow without a mask paints past the panel).
			var textAreaObj = new GameObject("Text Area");
			textAreaObj.transform.SetParent(fieldObj.transform, false);
			var textAreaRect = textAreaObj.AddComponent<RectTransform>();
			textAreaRect.anchorMin = Vector2.zero;
			textAreaRect.anchorMax = Vector2.one;
			textAreaRect.sizeDelta = Vector2.zero;
			textAreaRect.offsetMin = new Vector2(ProjectUiScale.Space(1), 2);
			textAreaRect.offsetMax = new Vector2(-ProjectUiScale.Space(1), -2);
			textAreaObj.AddComponent<RectMask2D>();

			var textObj = new GameObject("Text");
			textObj.transform.SetParent(textAreaObj.transform, false);
			var textRect = textObj.AddComponent<RectTransform>();
			textRect.anchorMin = Vector2.zero;
			textRect.anchorMax = Vector2.one;
			textRect.sizeDelta = Vector2.zero;
			textRect.offsetMin = Vector2.zero;
			textRect.offsetMax = Vector2.zero;
			var text = textObj.AddComponent<TextMeshProUGUI>();
			text.text = defaultValue;
			text.fontSize = 12;
			text.color = Color.white;
			// Do not use Ellipsis on the editable component — long paths must remain fully editable/selectable.
			text.overflowMode = TextOverflowModes.Overflow;
			text.enableWordWrapping = false;
			text.maskable = true;
			ApplyRuntimeTmpFont(text);

			var placeholderObj = new GameObject("Placeholder");
			placeholderObj.transform.SetParent(textAreaObj.transform, false);
			var placeholderRect = placeholderObj.AddComponent<RectTransform>();
			placeholderRect.anchorMin = Vector2.zero;
			placeholderRect.anchorMax = Vector2.one;
			placeholderRect.sizeDelta = Vector2.zero;
			placeholderRect.offsetMin = Vector2.zero;
			placeholderRect.offsetMax = Vector2.zero;
			var placeholder = placeholderObj.AddComponent<TextMeshProUGUI>();
			placeholder.text = "Path…";
			placeholder.fontSize = 12;
			placeholder.color = new Color(0.5f, 0.5f, 0.5f, 1f);
			placeholder.maskable = true;
			placeholder.gameObject.SetActive(string.IsNullOrEmpty(defaultValue));
			ApplyRuntimeTmpFont(placeholder);

			var inputField = fieldObj.AddComponent<TMP_InputField>();
			inputField.textViewport = textAreaRect;
			inputField.textComponent = text;
			inputField.placeholder = placeholder;
			inputField.lineType = TMP_InputField.LineType.SingleLine;
			inputField.text = defaultValue;
			
			// Update value when text changes
			inputField.onValueChanged.AddListener((value) => {
				string elementId = inputObj.GetInstanceID().ToString();
				_uiElementValues[elementId] = value;
				SendValueChangeToPython(addonId, elementId, "text", value);
			});
			
			// Register
			string elementId = inputObj.GetInstanceID().ToString();
			_uiElementValues[elementId] = defaultValue;
			_uiElementComponents[elementId] = inputField;
			
			if (_addonUIElements.ContainsKey(addonId)) {
				_addonUIElements[addonId].Add(inputObj);
			}
			SpzUiThemeOps.ApplyToAddonUiRoot(inputObj);
			
			return elementId;
		}
		
		/// <summary>
		/// Adds a dropdown to a panel
		/// </summary>
		public string AddDropdown(string addonId, string panelId, string label, List<string> options, int defaultIndex) {
			GameObject panelObj = FindUIElement(panelId);
			if (panelObj == null) {
				UnityEngine.Debug.LogError($"[AddonUI_MGR] Panel {panelId} not found");
				return null;
			}
			
			// Edge case: Empty or null options
			if (options == null || options.Count == 0) {
				UnityEngine.Debug.LogError($"[AddonUI_MGR] Dropdown requires at least one option");
				return null;
			}
			
			// Edge case: Invalid default index - clamp to valid range
			if (defaultIndex < 0 || defaultIndex >= options.Count) {
				UnityEngine.Debug.LogWarning($"[AddonUI_MGR] Default index {defaultIndex} out of range, clamping to 0");
				defaultIndex = 0;
			}
			
			// Create dropdown container
			GameObject dropdownObj = new GameObject($"Dropdown_{label}");
			dropdownObj.transform.SetParent(panelObj.transform, false);
			
			var dropdownRect = dropdownObj.AddComponent<RectTransform>();
			dropdownRect.sizeDelta = new Vector2(280, 48);
			var dropdownLe = dropdownObj.AddComponent<LayoutElement>();
			dropdownLe.preferredHeight = 48f;
			dropdownLe.minHeight = 40f;
			dropdownLe.preferredWidth = 280f;
			dropdownLe.flexibleWidth = 1f;
			// Ensure entire row can receive pointer events (not only inner field).
			// Null sprite Images often fail raycasts — assign SolidRect with near-clear alpha.
			var rowBg = dropdownObj.AddComponent<Image>();
			rowBg.sprite = UiRuntimeSprites.SolidRect;
			rowBg.type = Image.Type.Simple;
			rowBg.color = new Color(0f, 0f, 0f, 0.001f);
			rowBg.raycastTarget = true;
			
			// Add label (stacked top band — side 30% clipped long Nomad labels).
			var labelObj = new GameObject("Label");
			labelObj.transform.SetParent(dropdownObj.transform, false);
			var labelRect = labelObj.AddComponent<RectTransform>();
			labelRect.anchorMin = new Vector2(0, 0.5f);
			labelRect.anchorMax = new Vector2(1, 1);
			labelRect.sizeDelta = Vector2.zero;
			var labelText = labelObj.AddComponent<TextMeshProUGUI>();
			labelText.text = label;
			labelText.fontSize = 12;
			labelText.color = Color.white;
			labelText.enableWordWrapping = false;
			labelText.overflowMode = TextOverflowModes.Ellipsis;
			labelText.raycastTarget = false;
			ApplyRuntimeTmpFont(labelText);
			
			// Add dropdown
			var fieldObj = new GameObject("Dropdown");
			fieldObj.transform.SetParent(dropdownObj.transform, false);
			var fieldRect = fieldObj.AddComponent<RectTransform>();
			fieldRect.anchorMin = new Vector2(0, 0);
			fieldRect.anchorMax = new Vector2(1, 0.5f);
			fieldRect.sizeDelta = Vector2.zero;
			
			var fieldBg = fieldObj.AddComponent<Image>();
			fieldBg.sprite = UiRuntimeSprites.SolidRect;
			fieldBg.type = Image.Type.Simple;
			fieldBg.color = new Color(0.15f, 0.15f, 0.15f, 1f);
			SpzUiThemeOps.ApplyRoundedControlSprite(fieldBg, markEligible: true);
			
			var labelObj2 = new GameObject("Label");
			labelObj2.transform.SetParent(fieldObj.transform, false);
			var labelRect2 = labelObj2.AddComponent<RectTransform>();
			labelRect2.anchorMin = Vector2.zero;
			labelRect2.anchorMax = Vector2.one;
			labelRect2.sizeDelta = Vector2.zero;
			labelRect2.offsetMin = new Vector2(10, 2);
			labelRect2.offsetMax = new Vector2(-25, -2);
			var labelText2 = labelObj2.AddComponent<TextMeshProUGUI>();
			labelText2.text = options.Count > defaultIndex ? options[defaultIndex] : "";
			labelText2.fontSize = 12;
			labelText2.color = Color.white;
			labelText2.raycastTarget = false;
			ApplyRuntimeTmpFont(labelText2);
			
			var arrowObj = new GameObject("Arrow");
			arrowObj.transform.SetParent(fieldObj.transform, false);
			var arrowRect = arrowObj.AddComponent<RectTransform>();
			arrowRect.anchorMin = new Vector2(1, 0);
			arrowRect.anchorMax = new Vector2(1, 1);
			arrowRect.sizeDelta = new Vector2(20, 0);
			arrowRect.anchoredPosition = new Vector2(-10, 0);
			var arrowText = arrowObj.AddComponent<TextMeshProUGUI>();
			arrowText.text = "▼";
			arrowText.fontSize = 10;
			arrowText.color = Color.white;
			arrowText.alignment = TextAlignmentOptions.Center;
			arrowText.raycastTarget = false;
			ApplyRuntimeTmpFont(arrowText);
			
			var dropdown = fieldObj.AddComponent<TMP_Dropdown>();
			dropdown.captionText = labelText2;
			dropdown.options = new List<TMP_Dropdown.OptionData>();
			foreach (var option in options) {
				dropdown.options.Add(new TMP_Dropdown.OptionData(option));
			}
			dropdown.value = defaultIndex;

			void CycleDropdownValue() {
				if (dropdown.options == null || dropdown.options.Count == 0) return;
				int next = (dropdown.value + 1) % dropdown.options.Count;
				dropdown.SetValueWithoutNotify(next);
				labelText2.text = dropdown.options[next].text;
				string idLocal = dropdownObj.GetInstanceID().ToString();
				_uiElementValues[idLocal] = next;
				SendValueChangeToPython(addonId, idLocal, "dropdown", next);
			}
			// Button-only click path — MouseClickSensor + Button both firing skipped every other option.
			var clickBtn = fieldObj.GetComponent<Button>();
			if (clickBtn == null) clickBtn = fieldObj.AddComponent<Button>();
			clickBtn.targetGraphic = fieldBg;
			clickBtn.onClick.AddListener(CycleDropdownValue);
			// Also make the entire row clickable (users often click label/empty area).
			var rowBtn = dropdownObj.GetComponent<Button>();
			if (rowBtn == null) rowBtn = dropdownObj.AddComponent<Button>();
			rowBtn.targetGraphic = rowBg;
			rowBtn.onClick.AddListener(CycleDropdownValue);
			
			// Update value when selection changes
			dropdown.onValueChanged.AddListener((index) => {
				string elementId = dropdownObj.GetInstanceID().ToString();
				_uiElementValues[elementId] = index;
				if (index >= 0 && index < dropdown.options.Count) {
					labelText2.text = dropdown.options[index].text;
				}
				SendValueChangeToPython(addonId, elementId, "dropdown", index);
			});
			
			// Register
			string elementId = dropdownObj.GetInstanceID().ToString();
			_uiElementValues[elementId] = defaultIndex;
			_uiElementComponents[elementId] = dropdown;
			
			if (_addonUIElements.ContainsKey(addonId)) {
				_addonUIElements[addonId].Add(dropdownObj);
			}
			SpzUiThemeOps.ApplyToAddonUiRoot(dropdownObj);
			
			return elementId;
		}
		
		/// <summary>
		/// Gets the value of a UI element
		/// </summary>
		public object GetUIElementValue(string elementId) {
			if (string.IsNullOrEmpty(elementId))
				return null;
			if (_uiElementComponents.TryGetValue(elementId, out var component) && !IsLiveUiComponent(component)) {
				_uiElementComponents.Remove(elementId);
				_uiElementValues.Remove(elementId);
				return null;
			}
			if (_uiElementValues.ContainsKey(elementId)) {
				return _uiElementValues[elementId];
			}
			return null;
		}
		
		/// <summary>
		/// Sets the value of a UI element (with type safety)
		/// </summary>
		public bool SetUIElementValue(string elementId, object value) {
			if (string.IsNullOrEmpty(elementId) || !_uiElementComponents.ContainsKey(elementId)) return false;
			if (value == null) return false;
			
			var component = _uiElementComponents[elementId];
			if (!IsLiveUiComponent(component)) {
				_uiElementComponents.Remove(elementId);
				_uiElementValues.Remove(elementId);
				return false;
			}
			
			try {
				if (component is Slider slider) {
					// Type safety: Only accept numeric types
					if (!(value is float || value is int || value is double)) {
						UnityEngine.Debug.LogWarning($"[AddonUI_MGR] Cannot set non-numeric value to slider: {value.GetType()}");
						return false;
					}
					float floatValue = Convert.ToSingle(value);
					// Clamp to slider's min/max range
					floatValue = Mathf.Clamp(floatValue, slider.minValue, slider.maxValue);
					slider.value = floatValue;
					_uiElementValues[elementId] = slider.value;
					return true;
				} else if (component is TMP_InputField inputField) {
					// Type safety: Convert to string
					inputField.text = value.ToString();
					_uiElementValues[elementId] = inputField.text;
					return true;
				} else if (component is TMP_Dropdown dropdown) {
					// Type safety: integer index (also accept whole-number floats from JSON leftovers).
					int intValue;
					if (value is int || value is short || value is byte)
						intValue = Convert.ToInt32(value);
					else if (value is float f && Mathf.Approximately(f, Mathf.Round(f)))
						intValue = Mathf.RoundToInt(f);
					else if (value is double d && Math.Abs(d - Math.Round(d)) < 1e-6)
						intValue = (int)Math.Round(d);
					else {
						UnityEngine.Debug.LogWarning($"[AddonUI_MGR] Cannot set non-integer value to dropdown: {value.GetType()}");
						return false;
					}
					// Edge case: out-of-range must fail (not clamp) — clamping looked like success to Python set_value.
					if (dropdown.options == null || dropdown.options.Count == 0) {
						UnityEngine.Debug.LogWarning($"[AddonUI_MGR] Dropdown has no options; rejecting index {intValue}");
						return false;
					}
					if (intValue < 0 || intValue >= dropdown.options.Count) {
						UnityEngine.Debug.LogWarning($"[AddonUI_MGR] Dropdown index {intValue} out of range [0-{dropdown.options.Count-1}], rejecting");
						return false;
					}
					dropdown.value = intValue;
					_uiElementValues[elementId] = dropdown.value;
					return true;
				} else if (component is Toggle toggle) {
					// Accept bool, int, whole-number float/double 0/1 (JSON leftovers), or parseable string.
					bool on;
					if (value is bool b)
						on = b;
					else if (value is int i)
						on = i != 0;
					else if (value is float f && Mathf.Approximately(f, Mathf.Round(f)))
						on = Mathf.RoundToInt(f) != 0;
					else if (value is double d && Math.Abs(d - Math.Round(d)) < 1e-6)
						on = (int)Math.Round(d) != 0;
					else if (!bool.TryParse(value.ToString(), out on)) {
						UnityEngine.Debug.LogWarning($"[AddonUI_MGR] Cannot set non-bool value to toggle: {value.GetType()}");
						return false;
					}
					toggle.isOn = on;
					_uiElementValues[elementId] = toggle.isOn;
					return true;
				}
			} catch (Exception e) {
				UnityEngine.Debug.LogError($"[AddonUI_MGR] Error setting UI element value: {e.Message}");
				return false;
			}
			
			return false;
		}
		
		/// <summary>
		/// Builds POST body for /notify_value_change (Unity widget → Python).
		/// </summary>
		public static string BuildNotifyValueChangeRequestBody(string addonId, string elementId, string elementType, object value) {
			string valueJson = JsonConvert.SerializeObject(value);
			return $"{{\"addon_id\":\"{JsonEscape(addonId)}\",\"element_id\":\"{JsonEscape(elementId)}\",\"element_type\":\"{JsonEscape(elementType)}\",\"value\":{valueJson}}}";
		}

		/// <summary>
		/// Sends value change event to Python via HTTP POST /notify_value_change
		/// (optional add-on hook: on_value_change(element_id, element_type, value)).
		/// </summary>
		void SendValueChangeToPython(string addonId, string elementId, string elementType, object value) {
			if (Addon_MGR.IsAddonApiShuttingDown()) {
				UnityEngine.Debug.Log($"[AddonUI_MGR] Skipping value change during shutdown: {addonId}.{elementId}");
				return;
			}
			StartCoroutine(SendValueChangeToPythonCrtn(addonId, elementId, elementType, value));
		}

		IEnumerator SendValueChangeToPythonCrtn(string addonId, string elementId, string elementType, object value) {
			if (Addon_MGR.IsAddonApiShuttingDown())
				yield break;
			int port = Addon_MGR.instance != null ? Addon_MGR.instance.GetHttpServerPort() : 5557;
			string url = $"http://127.0.0.1:{port}/notify_value_change";
			string body = BuildNotifyValueChangeRequestBody(addonId, elementId, elementType, value);
			using (var req = new UnityWebRequest(url, "POST")) {
				req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
				req.downloadHandler = new DownloadHandlerBuffer();
				req.SetRequestHeader("Content-Type", "application/json");
				req.timeout = 8;
				yield return req.SendWebRequest();
				if (req.result != UnityWebRequest.Result.Success) {
					UnityEngine.Debug.LogWarning($"[AddonUI_MGR] notify_value_change failed: {req.error} ({addonId}.{elementId})");
					yield break;
				}
				bool ok = false;
				try {
					var json = JObject.Parse(req.downloadHandler?.text ?? "{}");
					ok = json["success"]?.Value<bool>() ?? false;
				} catch {
					// Response not valid JSON or missing success
				}
				if (!ok)
					UnityEngine.Debug.LogWarning($"[AddonUI_MGR] notify_value_change rejected: {addonId}.{elementId}");
			}
		}

		/// <summary>
		/// Destroys all UI elements for an add-on
		/// </summary>
		public void DestroyAddonUI(string addonId) {
			if (string.IsNullOrEmpty(addonId)) return;
			// Match Python NomadThemeSPZ.unregister(): drop active/orphan preset when the owner unloads.
			CleanupNomadThemeOwnershipIfNeeded(addonId);
			// Drop parking-lot bookkeeping first so migrate cannot reparent a dying panel.
			for (int i = _parkedForRibbon.Count - 1; i >= 0; i--) {
				ParkedPanel parked = _parkedForRibbon[i];
				if (parked == null
				    || string.Equals(parked.addonId, addonId, StringComparison.Ordinal)
				    || parked.panel == null) {
					_parkedForRibbon.RemoveAt(i);
				}
			}
			DestroyOrphanFallbackPanelsForAddon(addonId);

			// Always strip this add-on's callbacks, even if panel roots were already gone.
			var keysToRemove = new List<string>();
			foreach (var key in _buttonCallbacks.Keys) {
				if (IsCallbackOwnedByAddon(key, addonId))
					keysToRemove.Add(key);
			}
			foreach (var key in keysToRemove)
				_buttonCallbacks.Remove(key);

			if (!_addonUIElements.ContainsKey(addonId)) return;
			
			foreach (var element in _addonUIElements[addonId]) {
				if (element != null) {
					// Remove cached values/components for this element and all descendants.
					var transforms = element.GetComponentsInChildren<Transform>(true);
					for (int i = 0; i < transforms.Length; i++) {
						var t = transforms[i];
						if (t == null) continue;
						string id = t.gameObject.GetInstanceID().ToString();
						_uiElementValues.Remove(id);
						_uiElementComponents.Remove(id);
					}
					Destroy(element);
				}
			}
			
			_addonUIElements.Remove(addonId);
		}

		/// <summary>
		/// Native Apply can leave <c>nomad-inspired</c> registered/active after HTTP unregister never runs.
		/// </summary>
		void CleanupNomadThemeOwnershipIfNeeded(string addonId) {
			if (!string.Equals(addonId, NomadThemeAddonId, StringComparison.Ordinal))
				return;
			if (string.Equals(SpzUiThemeOps.ActiveThemeId, NomadThemeId, StringComparison.Ordinal)) {
				SpzUiThemeOps.ResetTheme();
				RestoreNomadSkyboxNative();
			} else if (_nomadSkyboxCaptured) {
				// Theme already reset elsewhere; still undo compose if we had captured.
				RestoreNomadSkyboxNative();
			}
			SpzUiThemeOps.TryUnregisterTheme(NomadThemeId, out _);
			_nomadFontScaleSliderId = null;
			_nomadSpacingScaleSliderId = null;
		}

		/// <summary>
		/// Callback ids are <c>{addonId}_{callbackName}</c>. A naive StartsWith(addonId+"_")
		/// also matches longer ids (e.g. addon <c>X</c> vs <c>X_Extra</c>).
		/// </summary>
		bool IsCallbackOwnedByAddon(string callbackId, string addonId) {
			if (string.IsNullOrEmpty(callbackId) || string.IsNullOrEmpty(addonId))
				return false;
			if (!callbackId.StartsWith(addonId + "_", StringComparison.Ordinal))
				return false;
			foreach (var otherId in _addonUIElements.Keys) {
				if (string.IsNullOrEmpty(otherId) || otherId.Length <= addonId.Length)
					continue;
				if (callbackId.StartsWith(otherId + "_", StringComparison.Ordinal))
					return false;
			}
			if (Addon_MGR.instance != null) {
				foreach (var kvp in Addon_MGR.instance.GetAddons()) {
					string otherId = kvp.Key;
					if (string.IsNullOrEmpty(otherId) || otherId.Length <= addonId.Length)
						continue;
					if (callbackId.StartsWith(otherId + "_", StringComparison.Ordinal))
						return false;
				}
			}
			return true;
		}

		/// <summary>
		/// True when <paramref name="goName"/> is this addon's panel for <paramref name="title"/>
		/// (exact name or longest-prefix parse). Does not treat <c>Foo</c> as matching <c>Foo_Bar</c>.
		/// </summary>
		bool IsExactAddonPanelChild(string goName, string addonId, string title, string expectedPanelName) {
			if (string.IsNullOrEmpty(goName) || string.IsNullOrEmpty(addonId)) return false;
			if (string.Equals(goName, expectedPanelName, StringComparison.Ordinal)
			    || string.Equals(goName, "AddonPanel_" + addonId, StringComparison.Ordinal))
				return true;
			if (!TryParseAddonPanelName(goName, out string parsedId, out string parsedTitle))
				return false;
			return string.Equals(parsedId, addonId, StringComparison.Ordinal)
			       && string.Equals(parsedTitle, title, StringComparison.Ordinal);
		}

		/// <summary>True if <paramref name="goName"/> is any panel belonging to <paramref name="addonId"/> (longest-prefix safe).</summary>
		public bool IsAddonPanelOwnedBy(string goName, string addonId) {
			if (string.IsNullOrEmpty(goName) || string.IsNullOrEmpty(addonId)) return false;
			if (string.Equals(goName, "AddonPanel_" + addonId, StringComparison.Ordinal))
				return true;
			return TryParseAddonPanelName(goName, out string parsedId, out _)
			       && string.Equals(parsedId, addonId, StringComparison.Ordinal);
		}

		/// <summary>
		/// Destroys panel widgets for reload/reuse and drops their get/set_value map entries.
		/// Without the map scrub, Python still reads ghost values and set_value can hit destroyed components.
		/// </summary>
		void ClearAddonPanelChildren(Transform panelRoot) {
			if (panelRoot == null) return;
			for (int i = panelRoot.childCount - 1; i >= 0; i--) {
				var c = panelRoot.GetChild(i);
				if (c == null) continue;
				PurgeUiElementMapsUnder(c);
				// DestroyImmediate so same-frame create_panel/add_* does not see leftover Title/widgets.
				UnityEngine.Object.DestroyImmediate(c.gameObject);
			}
		}

		void PurgeUiElementMapsUnder(Transform root) {
			if (root == null) return;
			var transforms = root.GetComponentsInChildren<Transform>(true);
			for (int i = 0; i < transforms.Length; i++) {
				var t = transforms[i];
				if (t == null) continue;
				string id = t.gameObject.GetInstanceID().ToString();
				_uiElementValues.Remove(id);
				_uiElementComponents.Remove(id);
				if (string.Equals(id, _nomadFontScaleSliderId, StringComparison.Ordinal))
					_nomadFontScaleSliderId = null;
				if (string.Equals(id, _nomadSpacingScaleSliderId, StringComparison.Ordinal))
					_nomadSpacingScaleSliderId = null;
			}
		}

		static bool IsLiveUiComponent(Component c) {
			return c != null && !ReferenceEquals(c, null) && c;
		}

		/// <summary>Panels parented to the floating fallback root (when the command ribbon was unavailable at create time) are not always in <see cref="CommandRibbon_UI"/> maps; remove strays when unloading.</summary>
		void DestroyOrphanFallbackPanelsForAddon(string addonId) {
			DestroyAddonPanelChildrenForId(_addonPanelsParent, addonId);
			// Parking may differ from the current _addonPanelsParent if the ribbon later took over the field.
			var parkingGo = GameObject.Find("AddonPanelsParking");
			if (parkingGo != null) {
				var parkingRt = parkingGo.transform as RectTransform;
				if (parkingRt != null && parkingRt != _addonPanelsParent)
					DestroyAddonPanelChildrenForId(parkingRt, addonId);
			}
		}

		void DestroyAddonPanelChildrenForId(RectTransform parent, string addonId) {
			if (parent == null || string.IsNullOrEmpty(addonId)) return;
			for (int i = parent.childCount - 1; i >= 0; i--) {
				var c = parent.GetChild(i);
				if (c == null) continue;
				bool match = string.Equals(c.name, "AddonPanel_" + addonId, StringComparison.Ordinal);
				if (!match && TryParseAddonPanelName(c.name, out string parsedId, out _)
				    && string.Equals(parsedId, addonId, StringComparison.Ordinal))
					match = true;
				if (match)
					Destroy(c.gameObject);
			}
		}
	}
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_EDITOR
using System.Diagnostics;
#endif
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using Lavender.Systems;
#endif

namespace spz {

	/// <summary>Optional manifest beside <c>__init__.py</c> for Add-on Manager list (reference-style “v1.0 • description”).</summary>
	[Serializable]
	public class AddonJsonManifest {
		public string version;
		public string description;
		/// <summary>Short label in Add-on Manager (optional; else folder <c>addonId</c>).</summary>
		public string displayName;
		/// <summary>Optional maintainer / author shown in expanded host preferences.</summary>
		public string author;
	}

	/// <summary>Kill any process listening on the given port (Windows). Avoids [Errno 10048] when starting FastAPI on 5557. Never kills Unity.exe or Unity Hub.</summary>
	internal static class AddonPortHelper
	{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
		/// <summary>Returns true if the process for this PID is one we must never kill: Unity.exe, Unity Hub.exe, or StableProjectorz.exe (our own game).
		/// Returns true on ANY failure (fail-safe: when in doubt, don't kill).</summary>
		static bool IsProcessUnityOrHub(uint processId)
		{
			string tempFile = Path.Combine(Path.GetTempPath(), "spz_tasklist_" + processId + "_" + Guid.NewGuid().ToString("N") + ".txt");
			string workDir = Path.GetTempPath();
			try
			{
				string cmd = "tasklist /FI \"PID eq " + processId + "\" > \"" + tempFile + "\"";
				uint cmdPid = StartExternalProcess.Run_Bat_or_Shortcut_or_Command(cmd, isJustFile: false, workDir, keepWindow: false, hidden: true, attachToConsole: false);
				if (cmdPid == 0) return true;
				StartExternalProcess.WaitForProcessExit(cmdPid, 1500);
				if (!File.Exists(tempFile)) return true;
				string output = File.ReadAllText(tempFile);
				if (output.IndexOf("Unity.exe", StringComparison.OrdinalIgnoreCase) >= 0) return true;
				if (output.IndexOf("Unity Hub.exe", StringComparison.OrdinalIgnoreCase) >= 0) return true;
				if (output.IndexOf("StableProjectorz.exe", StringComparison.OrdinalIgnoreCase) >= 0) return true;
				return false;
			}
			catch { return true; }
			finally { try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { } }
		}

		/// <summary>Finds PIDs listening on the port via netstat -ano, then taskkill /PID x /F. Never kills Unity.exe or Unity Hub. Uses StartExternalProcess so it works in IL2CPP build.</summary>
		public static void TryKillProcessesOnPort(int port)
		{
			// Port 5555 is the Unity/addon socket; never free it from here (Editor may be using it; we never kill Unity).
			if (port == 5555) return;
			string tempFile = Path.Combine(Path.GetTempPath(), "spz_netstat_" + port + "_" + Guid.NewGuid().ToString("N") + ".txt");
			string workDir = Path.GetTempPath();
			try
			{
				// Use colon prefix ":{port}" so we match the port in the address column (e.g. "127.0.0.1:5557")
				// and never false-positive on PIDs that happen to contain the port number as a substring.
				string cmd = "netstat -ano | find \":" + port + "\" > \"" + tempFile + "\"";
				uint pid = StartExternalProcess.Run_Bat_or_Shortcut_or_Command(cmd, isJustFile: false, workDir, keepWindow: false, hidden: true, attachToConsole: false);
				if (pid == 0) return;
				StartExternalProcess.WaitForProcessExit(pid, 2000);
				if (!File.Exists(tempFile)) return;
				string output = File.ReadAllText(tempFile);
				// netstat -ano line e.g. "  TCP    127.0.0.1:5557    0.0.0.0:0    LISTENING    12345"
				// parts[1] = local address (e.g. "127.0.0.1:5557"), parts[last] = PID
				string portSuffix = ":" + port;
				var pids = new HashSet<uint>();
				foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
				{
					string trimmed = line.Trim();
					if (string.IsNullOrEmpty(trimmed) || !trimmed.Contains("LISTENING")) continue;
					string[] parts = trimmed.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
					if (parts.Length < 4) continue;
					// Verify the local address column actually ends with the exact port
					// (prevents matching port 55570 when looking for 5557)
					if (!parts[1].EndsWith(portSuffix)) continue;
					if (uint.TryParse(parts[parts.Length - 1], out uint p))
						pids.Add(p);
				}
				uint currentPid = StartExternalProcess.GetCurrentPid();
				foreach (uint p in pids)
				{
					if (p == currentPid)
					{
						UnityEngine.Debug.Log($"[Addon_MGR] Skipping current process PID {p} (Unity/game window); not killing self.");
						continue;
					}
					// Never kill Unity Editor or Unity Hub, regardless of port (safeguard against any matching bug)
					if (IsProcessUnityOrHub(p))
					{
						UnityEngine.Debug.Log($"[Addon_MGR] Skipping PID {p} (Unity/Unity Hub); will not kill Editor.");
						continue;
					}
					string killCmd = "taskkill /PID " + p + " /F";
					uint killPid = StartExternalProcess.Run_Bat_or_Shortcut_or_Command(killCmd, isJustFile: false, workDir, keepWindow: false, hidden: true, attachToConsole: false);
					if (killPid != 0)
					{
						StartExternalProcess.WaitForProcessExit(killPid, 1500);
						UnityEngine.Debug.Log($"[Addon_MGR] Freed port {port}: killed process PID {p} (was holding 127.0.0.1:{port}).");
					}
				}
			}
			catch (Exception e)
			{
				UnityEngine.Debug.LogWarning($"[Addon_MGR] Could not free port {port}: {e.Message}");
			}
			finally
			{
				try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
			}
		}
#endif
	}

	/// <summary>
	/// Manages add-on discovery, lifecycle, and Python server process.
	/// </summary>
	[DefaultExecutionOrder(-100)]  // Run before Addon_SocketServer (0) so instance exists when socket binds
	public class Addon_MGR : MonoBehaviour {
		public static Addon_MGR instance { get; private set; }

		/// <summary>Fired when an addon's enabled state was changed by the system (e.g. load failure). UI should refresh the list.</summary>
		public static event Action<string> OnAddonEnabledStateChanged;

		[SerializeField] string _pythonServerScript = "addon_server.py";
		[SerializeField] int _serverPort = 5555;
		[SerializeField] int _httpServerPort = 5557;
		[SerializeField] int _webSocketPort = 5558;
		[SerializeField] bool _enableHttpServer = true; // FastAPI in Python (recommended)
		[SerializeField] bool _enableCSharpHttpServer = false; // Legacy C# HttpListener (deprecated)
		[SerializeField] bool _enableWebSocketServer = false;
		[SerializeField] bool _autoRestartWithAddonsOnServerFail = false; // Avoid surprise relaunch/flicker in player unless explicitly enabled.
		
#if UNITY_EDITOR
		private Process _pythonProcess;
#endif
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
		private uint _pythonServerPid; // When started via StartExternalProcess (IL2CPP-safe path)
#endif
		private Dictionary<string, AddonInfo> _registeredAddons = new Dictionary<string, AddonInfo>();
		private bool _isServerRunning = false;
		/// <summary>One in-flight load/unload HTTP op per addon — rapid toggle must not race register/unregister.</summary>
		readonly Dictionary<string, Coroutine> _addonLifecycleOpById = new Dictionary<string, Coroutine>();
		readonly Dictionary<string, int> _addonLifecycleEpochById = new Dictionary<string, int>();
		/// <summary>Deferred ribbon tab create when CommandRibbon_UI is not ready at Enable / prefs restore.</summary>
		readonly Dictionary<string, Coroutine> _ribbonShellEnsureById = new Dictionary<string, Coroutine>();
		/// <summary>Single FULL/SRN dock ensure — Gen Art finish / enable / load-fail must not run parallel attach loops.</summary>
		Coroutine _ribbonOnlyDockEnsureCrtn;
		/// <summary>Python unregister skipped while :5557 was down — retry so watchers/routes do not stay live after dial-off.</summary>
		readonly HashSet<string> _pendingPythonUnloadIds = new HashSet<string>();
		/// <summary>Python load failed but dial stayed on (native/dock fallback) — Load Now soft-fail honesty.</summary>
		readonly HashSet<string> _pythonLoadSoftFailedIds = new HashSet<string>();
		Coroutine _pendingPythonUnloadFlushCrtn;
		/// <summary>Single shared /ready poll so parallel Enable/Load-now do not storm HTTP + socket.</summary>
		Coroutine _sharedAddonReadyWaitCrtn;
		/// <summary>Set before StartCoroutine so two WaitForAddonServerReady callers in one frame cannot both spawn polls.</summary>
		bool _sharedAddonReadyPollActive;
		readonly List<Action<bool>> _sharedAddonReadyWaiters = new List<Action<bool>>();
		bool _sharedAddonReadyKnownOk;

		static bool s_addonApiQuitShutdownDone;
		static bool s_appliedRememberedEnabledOnFirstDiscover;
		static bool s_appliedAddonPrefsOnFirstDiscover;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		static void ResetAddonQuitStatics() {
			s_addonApiQuitShutdownDone = false;
			s_appliedRememberedEnabledOnFirstDiscover = false;
			s_appliedAddonPrefsOnFirstDiscover = false;
		}

		const string PrefsKeyRememberEnabledAddons = "spz.addons.rememberEnabled.v2";
		const string PrefsKeyEnabledAddonIdsJson = "spz.addons.enabledIdsJson.v2";
		/// <summary>Sparse per-add-on prefs bag: <c>{ "AddonId": { "show_in_command_ribbon": false } }</c>.</summary>
		const string PrefsKeyAddonPrefsByIdJson = "spz.addons.prefsByIdJson.v1";

		/// <summary>Host pref: show a Command Ribbon tab while the add-on is enabled (default true).</summary>
		public const string PrefKeyShowInCommandRibbon = "show_in_command_ribbon";

		/// <summary>When true, enabled add-on ids are saved and restored on the next app launch. Default off — add-ons stay disabled until the user enables and saves.</summary>
		public static bool GetRememberEnabledAddonsPreference() {
			return PlayerPrefs.GetInt(PrefsKeyRememberEnabledAddons, 0) == 1;
		}

		/// <summary>Persists the “remember add-ons” option. On → write current enabled set; off → clear stored ids so next launch cannot restore a stale selection.</summary>
		public static void SetRememberEnabledAddonsPreference(bool remember) {
			PlayerPrefs.SetInt(PrefsKeyRememberEnabledAddons, remember ? 1 : 0);
			if (remember && instance != null) {
				instance.MaybePersistEnabledAddonSelection();
			} else if (!remember) {
				PlayerPrefs.DeleteKey(PrefsKeyEnabledAddonIdsJson);
			}
			PlayerPrefs.Save();
		}

		/// <summary>Writes the list of currently enabled add-on folder ids to disk when the remember option is on.</summary>
		public void MaybePersistEnabledAddonSelection() {
			if (!GetRememberEnabledAddonsPreference() || _registeredAddons == null) {
				return;
			}
			PersistEnabledAddonSelectionNow();
		}

		/// <summary>Always writes the current enabled set when Remember is on; clears stored ids when Remember is off (Save must not imply next-launch restore).</summary>
		public void PersistEnabledAddonSelectionNow() {
			if (_registeredAddons == null) {
				return;
			}
			if (!GetRememberEnabledAddonsPreference()) {
				PlayerPrefs.DeleteKey(PrefsKeyEnabledAddonIdsJson);
				PlayerPrefs.Save();
				return;
			}
			var arr = new JArray();
			foreach (var kvp in _registeredAddons) {
				if (kvp.Value != null && kvp.Value.isEnabled) {
					arr.Add(kvp.Key);
				}
			}
			PlayerPrefs.SetString(PrefsKeyEnabledAddonIdsJson, arr.ToString(Formatting.None));
			PlayerPrefs.Save();
		}

		/// <summary>Always writes sparse per-add-on host prefs (Add-on Manager Save settings).</summary>
		public void PersistAddonPrefsNow() {
			if (_registeredAddons == null)
				return;
			var root = new JObject();
			foreach (var kvp in _registeredAddons) {
				if (kvp.Value == null || string.IsNullOrEmpty(kvp.Key))
					continue;
				JObject bag = EnsurePrefsBag(kvp.Value);
				if (bag == null || !bag.HasValues)
					continue;
				// Only persist non-default host ribbon off (and any other explicit keys).
				var sparse = new JObject();
				foreach (var prop in bag.Properties()) {
					if (prop == null || string.IsNullOrEmpty(prop.Name) || prop.Value == null || prop.Value.Type == JTokenType.Null)
						continue;
					if (string.Equals(prop.Name, PrefKeyShowInCommandRibbon, StringComparison.Ordinal)) {
						bool show = prop.Value.Type == JTokenType.Boolean && prop.Value.Value<bool>();
						if (show)
							continue; // default true — omit
						sparse[prop.Name] = false;
						continue;
					}
					sparse[prop.Name] = prop.Value.DeepClone();
				}
				if (sparse.HasValues)
					root[kvp.Key] = sparse;
			}
			PlayerPrefs.SetString(PrefsKeyAddonPrefsByIdJson, root.ToString(Formatting.None));
			PlayerPrefs.Save();
		}

		static JObject EnsurePrefsBag(AddonInfo info) {
			if (info == null)
				return null;
			if (info.prefs == null)
				info.prefs = new JObject();
			return info.prefs;
		}

		/// <summary>Reads a bool host/addon pref; missing key returns <paramref name="defaultValue"/>.</summary>
		public bool GetAddonPrefBool(string addonId, string key, bool defaultValue = false) {
			if (string.IsNullOrEmpty(addonId) || string.IsNullOrEmpty(key) || _registeredAddons == null)
				return defaultValue;
			if (!_registeredAddons.TryGetValue(addonId, out var info) || info == null)
				return defaultValue;
			JObject bag = info.prefs;
			if (bag == null || !bag.TryGetValue(key, out var token) || token == null || token.Type == JTokenType.Null)
				return defaultValue;
			if (token.Type == JTokenType.Boolean)
				return token.Value<bool>();
			if (token.Type == JTokenType.Integer)
				return token.Value<int>() != 0;
			if (token.Type == JTokenType.String
			    && bool.TryParse(token.Value<string>(), out bool parsed))
				return parsed;
			return defaultValue;
		}

		public static bool GetAddonPrefBoolStatic(string addonId, string key, bool defaultValue = false) {
			if (instance == null)
				return defaultValue;
			return instance.GetAddonPrefBool(addonId, key, defaultValue);
		}

		/// <summary>Writes a bool pref and runs ribbon sync when the host ribbon key changes.</summary>
		public void SetAddonPrefBool(string addonId, string key, bool value) {
			if (string.IsNullOrEmpty(addonId) || string.IsNullOrEmpty(key) || _registeredAddons == null)
				return;
			if (!_registeredAddons.TryGetValue(addonId, out var info) || info == null)
				return;
			if (string.Equals(addonId, RibbonOnlyFullscreenAddonId, StringComparison.Ordinal)
			    && string.Equals(key, PrefKeyShowInCommandRibbon, StringComparison.Ordinal)) {
				// Viewport-dock add-on never owns a command-ribbon tab.
				value = false;
			}
			JObject bag = EnsurePrefsBag(info);
			bool prev = GetAddonPrefBool(addonId, key, string.Equals(key, PrefKeyShowInCommandRibbon, StringComparison.Ordinal));
			bag[key] = value;
			// Ribbon presence only matters while loaded; syncing while disabled was destroying UI unnecessarily.
			if (string.Equals(key, PrefKeyShowInCommandRibbon, StringComparison.Ordinal)
			    && prev != value
			    && IsAddonEnabled(addonId))
				SyncRibbonTabWithEnabledState(addonId);
		}

		/// <summary>True when an enabled add-on should expose a Command Ribbon tab (host pref; default true).</summary>
		public bool ShouldShowInCommandRibbon(string addonId) {
			if (string.IsNullOrEmpty(addonId))
				return false;
			if (string.Equals(addonId, RibbonOnlyFullscreenAddonId, StringComparison.Ordinal))
				return false;
			return GetAddonPrefBool(addonId, PrefKeyShowInCommandRibbon, true);
		}

		public static bool ShouldShowInCommandRibbonStatic(string addonId) {
			if (string.IsNullOrEmpty(addonId))
				return false;
			if (string.Equals(addonId, RibbonOnlyFullscreenAddonId, StringComparison.Ordinal))
				return false;
			if (instance == null)
				return true; // default show when manager not ready
			return instance.ShouldShowInCommandRibbon(addonId);
		}

		/// <summary>Convenience: set host ribbon visibility without unloading the add-on.</summary>
		public void SetShowInCommandRibbon(string addonId, bool show) {
			SetAddonPrefBool(addonId, PrefKeyShowInCommandRibbon, show);
		}

		static void ApplyAddonPrefsFromPlayerPrefsOnFirstDiscover() {
			if (s_appliedAddonPrefsOnFirstDiscover)
				return;
			s_appliedAddonPrefsOnFirstDiscover = true;
			if (instance == null || instance._registeredAddons == null)
				return;
			string s = PlayerPrefs.GetString(PrefsKeyAddonPrefsByIdJson, "{}");
			JObject root;
			try {
				root = JObject.Parse(string.IsNullOrWhiteSpace(s) ? "{}" : s);
			} catch {
				return;
			}
			int applied = 0;
			foreach (var prop in root.Properties()) {
				if (prop == null || string.IsNullOrEmpty(prop.Name) || !(prop.Value is JObject bagSrc))
					continue;
				if (!instance._registeredAddons.TryGetValue(prop.Name, out var info) || info == null)
					continue;
				JObject bag = EnsurePrefsBag(info);
				foreach (var p in bagSrc.Properties()) {
					if (p == null || string.IsNullOrEmpty(p.Name) || p.Value == null)
						continue;
					bag[p.Name] = p.Value.DeepClone();
				}
				if (string.Equals(prop.Name, RibbonOnlyFullscreenAddonId, StringComparison.Ordinal))
					bag[PrefKeyShowInCommandRibbon] = false;
				applied++;
			}
			UnityEngine.Debug.Log(
				"[Addon_MGR] Restored add-on preferences from saved prefs ("
				+ applied
				+ " add-on(s) with stored keys)."
			);
		}

		static void ApplyRememberedEnabledStateFromPlayerPrefsOnFirstDiscover() {
			if (s_appliedRememberedEnabledOnFirstDiscover) {
				return;
			}
			s_appliedRememberedEnabledOnFirstDiscover = true;
			if (!GetRememberEnabledAddonsPreference()) {
				return;
			}
			if (instance == null || instance._registeredAddons == null) {
				return;
			}
			string s = PlayerPrefs.GetString(PrefsKeyEnabledAddonIdsJson, "[]");
			JArray arr;
			try {
				arr = JArray.Parse(s);
			} catch {
				return;
			}
			var set = new HashSet<string>(StringComparer.Ordinal);
			foreach (var t in arr) {
				if (t == null) {
					continue;
				}
				string id = t.ToString();
				if (!string.IsNullOrEmpty(id)) {
					set.Add(id);
				}
			}
			foreach (var kvp in instance._registeredAddons) {
				if (kvp.Value == null) {
					continue;
				}
				kvp.Value.isEnabled = set.Contains(kvp.Key);
			}
			int on = 0;
			foreach (var kvp in instance._registeredAddons) {
				if (kvp.Value != null && kvp.Value.isEnabled) on++;
			}
			UnityEngine.Debug.Log(
				"[Addon_MGR] Restored enabled add-on selection from saved preferences ("
				+ set.Count
				+ " id(s) in prefs, "
				+ on
				+ " matched and enabled)."
			);
			// isEnabled alone does not create tabs — mirror EnableAddon's ribbon half without HTTP load.
			if (instance != null)
				instance.EnsureRibbonShellsForAllEnabledAddons();
		}

		static void HandleApplicationQuitting() {
			ShutdownAddonApiBeforeQuit();
		}

		/// <summary>
		/// Stops add-on API in order: legacy C# HTTP (if any), Python FastAPI/socket client process, then Unity TCP listener.
		/// Idempotent. Call from <see cref="Application.wantsToQuit"/> (after user confirms exit) and/or rely on <see cref="Application.quitting"/>.
		/// </summary>
		public static void ShutdownAddonApiBeforeQuit() {
			if (s_addonApiQuitShutdownDone)
				return;
			s_addonApiQuitShutdownDone = true;
			try {
				if (Addon_HttpServer.instance != null)
					Addon_HttpServer.instance.ShutdownForQuit();
			} catch (Exception e) {
				UnityEngine.Debug.LogWarning("[Addon_MGR] ShutdownAddonApiBeforeQuit (HTTP): " + e.Message);
			}
			try {
				if (instance != null)
					instance.TerminatePythonAddonServerProcess(waitForExit: false);
			} catch (Exception e) {
				UnityEngine.Debug.LogWarning("[Addon_MGR] ShutdownAddonApiBeforeQuit (Python): " + e.Message);
			}
			try {
				if (Addon_SocketServer.instance != null)
					Addon_SocketServer.instance.ShutdownNetworkingForQuit();
			} catch (Exception e) {
				UnityEngine.Debug.LogWarning("[Addon_MGR] ShutdownAddonApiBeforeQuit (socket): " + e.Message);
			}
			try {
				FastPath_API.TryDeleteSpzGoSessionFolderOnQuit();
			} catch (Exception e) {
				UnityEngine.Debug.LogWarning("[Addon_MGR] ShutdownAddonApiBeforeQuit (SPZ GO session dir): " + e.Message);
			}
		}

		/// <summary>True once add-on API shutdown started (quitting / destroy path). Use to skip new HTTP/socket work.</summary>
		public static bool IsAddonApiShuttingDown() {
			return s_addonApiQuitShutdownDone;
		}

		void TerminatePythonAddonServerProcess(bool waitForExit = true) {
#if UNITY_EDITOR
			if (_pythonProcess != null) {
				// Clear async stdout/stderr handlers BEFORE Cancel/Kill: late callbacks marshalling Debug.Log into Unity
				// during shutdown have been observed to stall the Editor on quit (background ThreadPool thread vs
				// Unity main thread tearing down logging). Drop them first so any in-flight callback is a no-op.
				try { _pythonProcess.OutputDataReceived -= OnPythonStdout_LogToUnity; } catch { }
				try { _pythonProcess.ErrorDataReceived  -= OnPythonStderr_LogToUnity; } catch { }
				try {
					_pythonProcess.CancelOutputRead();
					_pythonProcess.CancelErrorRead();
				} catch { }
				if (!_pythonProcess.HasExited) {
					try { _pythonProcess.Kill(); } catch { }
				}
				try { _pythonProcess.Dispose(); } catch { }
				_pythonProcess = null;
			}
#elif UNITY_STANDALONE_WIN && !UNITY_EDITOR
			if (_pythonServerPid != 0) {
				try {
					string workDir = Path.GetTempPath();
					string cmd = "taskkill /PID " + _pythonServerPid + " /T /F";
					uint killPid = StartExternalProcess.Run_Bat_or_Shortcut_or_Command(cmd, isJustFile: false, workDir, keepWindow: false, hidden: true, attachToConsole: false);
					if (waitForExit && killPid != 0)
						StartExternalProcess.WaitForProcessExit(killPid, 3000);
				} catch (Exception e) {
					UnityEngine.Debug.LogWarning("[Addon_MGR] Could not terminate Python addon server: " + e.Message);
				}
				_pythonServerPid = 0;
			}
#endif
			_isServerRunning = false;
			InvalidateSharedAddonReadyCache();
		}

#if UNITY_EDITOR
		/// <summary>Static so an in-flight callback after we set _pythonProcess=null is still safe (no instance state).
		/// Bails out if Editor has begun teardown (ApplicationQuitting fired) — guarded by <see cref="s_addonApiQuitShutdownDone"/>.</summary>
		static void OnPythonStdout_LogToUnity(object sender, System.Diagnostics.DataReceivedEventArgs e) {
			if (s_addonApiQuitShutdownDone) return;
			if (!string.IsNullOrEmpty(e.Data)) UnityEngine.Debug.Log("[Python Server] " + e.Data);
		}
		static void OnPythonStderr_LogToUnity(object sender, System.Diagnostics.DataReceivedEventArgs e) {
			if (s_addonApiQuitShutdownDone) return;
			if (!string.IsNullOrEmpty(e.Data)) UnityEngine.Debug.LogError("[Python Server Error] " + e.Data);
		}
#endif

		public class AddonInfo {
			public string id;
			public string path;
			public bool isEnabled;
			public List<GameObject> uiElements = new List<GameObject>();
			/// <summary>Optional; from <c>addon.json</c> <c>displayName</c> (Add-on Manager row title).</summary>
			public string displayName;
			/// <summary>List row subtitle: e.g. <c>v1.2.0 • Advanced camera controls…</c> from <c>addon.json</c> or <c>__init__.py</c>.</summary>
			public string listSubtitle;
			/// <summary>From <c>addon.json</c> / <c>__version__</c> (expanded host prefs).</summary>
			public string version;
			/// <summary>From <c>addon.json</c> author or <c>bl_info</c> (expanded host prefs).</summary>
			public string author;
			/// <summary>Short summary of what the add-on does (expanded host prefs).</summary>
			public string description;
			/// <summary>Sparse host/addon preferences (Blender-like Manager prefs). Missing keys use defaults.</summary>
			public JObject prefs;
		}
		
		void Awake() {
			UnityEngine.Debug.Log("[Addon_MGR] Awake (Tool_AddonSystem scene loaded).");
			if (instance != null) { DestroyImmediate(this); return; }
			instance = this;
			// If the scene's Addon_SocketServer is missing (e.g. broken script ref in build), create it at runtime so Python can always connect.
			if (Addon_SocketServer.instance == null) {
				var go = new GameObject("Addon_SocketServer_Runtime");
				go.AddComponent<Addon_SocketServer>();
				UnityEngine.Debug.Log("[Addon_MGR] Addon_SocketServer was missing in scene; created at runtime so listener can bind.");
			}
			if (AddonUI_MGR.instance == null) {
				var uiMgr = FindObjectOfType<AddonUI_MGR>(true);
				if (uiMgr == null) {
					var go = new GameObject("AddonUI_MGR_Runtime");
					go.AddComponent<AddonUI_MGR>();
					UnityEngine.Debug.Log("[Addon_MGR] AddonUI_MGR was missing in scene; created at runtime so addon panels can be built.");
				}
			}
			// Zip install / remove (drag-drop, Add-on Manager) — same connectivity as socket + UI.
			if (AddonInstaller_MGR.instance == null) {
				var installer = FindObjectOfType<AddonInstaller_MGR>(true);
				if (installer == null) {
					var go = new GameObject("AddonInstaller_MGR_Runtime");
					go.transform.SetParent(transform, false);
					go.AddComponent<AddonInstaller_MGR>();
					UnityEngine.Debug.Log("[Addon_MGR] AddonInstaller_MGR was missing in scene; created at runtime for zip install / remove.");
				}
			}
			if (GetComponent<AddonDebugCapture>() == null)
				gameObject.AddComponent<AddonDebugCapture>();
			if (GetComponent<Launch_Addons_Bat_File>() == null)
				gameObject.AddComponent<Launch_Addons_Bat_File>();
			Application.quitting += HandleApplicationQuitting;
			GenerateButtons_UI._Act_OnGenerate_finished += OnGenerateFinished_MaybeRestoreFullscreenDock;
		}
		
		void Start() {
			StartCoroutine(InitializeAddonSystem());
		}

		/// <summary>
		/// Gen Art layout / cancel strip can leave the FULL/SRN dock missing while the add-on stays enabled.
		/// Re-attach after generation so the button does not stay gone until a manual re-enable.
		/// </summary>
		void OnGenerateFinished_MaybeRestoreFullscreenDock(bool canceled) {
			if (!IsAddonEnabled(RibbonOnlyFullscreenAddonId))
				return;
			if (RibbonViewportFullViewOnScreen_Toggle_UI.IsAnyVisibleBuiltDock())
				return;
			StartEnsureRibbonOnlyFullscreenViewportDock();
		}
		
		IEnumerator InitializeAddonSystem() {
			// Start addon system immediately on exe start (do not block on FastPath_API).
			// FastPath_API is only needed for addon commands (camera/mesh); Addon_SocketServer returns "FastPath_API not ready" until then.
			UnityEngine.Debug.Log("[Addon_MGR] Addon system initializing: discovering addons and starting Python server.");
			
			// Discover add-ons
			DiscoverAddons();
			
			// Wait until Addon_SocketServer is listening on 5555 so Python can connect (avoids WinError 10061 "connection refused").
			// Execution order: Addon_MGR.Start runs first and yields; Addon_SocketServer.Start then binds; next frame we check and start Python.
			yield return null;
			const int maxWaitFrames = 120;  // ~2 s at 60 fps
			for (int i = 0; i < maxWaitFrames; i++) {
				if (Addon_SocketServer.instance != null && Addon_SocketServer.instance.IsListening) {
					if (i > 2) UnityEngine.Debug.Log("[Addon_MGR] Socket server is listening; starting Python.");
					break;
				}
				if (i == 0) UnityEngine.Debug.Log("[Addon_MGR] Waiting for addon socket server to bind to port...");
				if (i == maxWaitFrames - 1) {
					UnityEngine.Debug.LogError("[Addon_MGR] Addon_SocketServer not listening after " + maxWaitFrames + " frames. Is the addon socket server GameObject active in the scene? Python will likely fail to connect (WinError 10061).");
				}
				yield return null;
			}
			
			// Start Python server (includes FastAPI HTTP server if enabled). Visibility from Settings (default hide).
			StartPythonServer();
			
			// If server failed and we have addons, auto-restart via Run_with_Addons.bat (built player only; never in Editor to avoid quitting Unity)
			if (_autoRestartWithAddonsOnServerFail && !_isServerRunning && _enableHttpServer && HasAnyEnabledAddon() && !WasLaunchedByAddonsBat() && !Application.isEditor) {
				StartCoroutine(AutoRestartWithAddonsAfterDelay());
				yield break;
			}
			
			// Request Python to load each enabled addon (server exposes POST /load_addon)
			if (_enableHttpServer) {
				StartCoroutine(RequestLoadEnabledAddonsAfterDelay());
			} else {
				// No HTTP auto-load — still wire ribbon shells / FULL dock for remember-restored enables.
				EnsureRibbonShellsForAllEnabledAddons();
				SeedNativeFallbacksForEnabledAddonsWhenHttpOff();
				if (IsAddonEnabled(RibbonOnlyFullscreenAddonId))
					StartEnsureRibbonOnlyFullscreenViewportDock();
			}
			
			// Start legacy C# HTTP server if enabled (deprecated - use FastAPI instead)
			if (_enableCSharpHttpServer && Addon_HttpServer.instance == null) {
				GameObject httpServerObj = new GameObject("Addon_HttpServer");
				httpServerObj.AddComponent<Addon_HttpServer>();
				UnityEngine.Debug.LogWarning("[Addon_MGR] Using legacy C# HTTP server. Consider using FastAPI instead (enabled by default in Python server).");
			}
		}
		
		/// <summary>True if we were started by Run_with_Addons.bat (bat sets SPZ_ADDONS_LAUNCHED=1). Used to avoid auto-restart loop.</summary>
		static bool WasLaunchedByAddonsBat() {
			try {
				string v = Environment.GetEnvironmentVariable("SPZ_ADDONS_LAUNCHED");
				return !string.IsNullOrWhiteSpace(v);
			} catch { return false; }
		}
		
		bool HasAnyEnabledAddon() {
			foreach (var kvp in _registeredAddons) { if (kvp.Value.isEnabled) return true; }
			return false;
		}
		
		IEnumerator AutoRestartWithAddonsAfterDelay() {
			UnityEngine.Debug.Log("[Addon_MGR] Python server did not start; auto-launching Run_with_Addons.bat so the game runs with addons (one-time restart).");
			yield return new WaitForSeconds(1.5f);
			if (Launch_Addons_Bat_File.GetAddonsBatFilePath(logIfNotFound: false).Length > 0) {
				Launch_Addons_Bat_File.RestartWithAddons();
			} else {
				UnityEngine.Debug.LogWarning("[Addon_MGR] Run_with_Addons.bat not found. Start the game with that bat for addon support.");
			}
		}
		
		static void EnrichAddonListSubtitle(AddonInfo info) {
			if (info == null) return;
			if (string.IsNullOrEmpty(info.path) || !Directory.Exists(info.path)) {
				info.listSubtitle = "Installed add-on";
				info.description = info.description ?? "Installed add-on";
				return;
			}
			string ver = null;
			string desc = null;
			string author = null;
			string jsonPath = Path.Combine(info.path, "addon.json");
			if (File.Exists(jsonPath)) {
				try {
					string json = File.ReadAllText(jsonPath);
					var m = JsonUtility.FromJson<AddonJsonManifest>(json);
					if (m != null) {
						if (!string.IsNullOrWhiteSpace(m.version)) ver = m.version.Trim();
						if (!string.IsNullOrWhiteSpace(m.description)) desc = m.description.Trim();
						if (!string.IsNullOrWhiteSpace(m.displayName)) info.displayName = m.displayName.Trim();
						if (!string.IsNullOrWhiteSpace(m.author)) author = m.author.Trim();
					}
				} catch (Exception e) {
					UnityEngine.Debug.LogWarning($"[Addon_MGR] addon.json read failed for {info.id}: {e.Message}");
				}
			}
			TryParseInitPyMetadata(Path.Combine(info.path, "__init__.py"), ref ver, ref desc, ref author);
			info.version = string.IsNullOrWhiteSpace(ver) ? null : ver.Trim();
			info.author = string.IsNullOrWhiteSpace(author) ? null : author.Trim();
			info.description = NormalizeSubtitleLine(desc, 220);
			info.listSubtitle = BuildAddonListSubtitle(ver, desc, info.path);
		}
		
		static void TryParseInitPyMetadata(string initPath, ref string ver, ref string desc, ref string author) {
			if (!File.Exists(initPath)) return;
			string text;
			try {
				text = File.ReadAllText(initPath);
			} catch {
				return;
			}
			if (text.Length > 24576)
				text = text.Substring(0, 24576);
			if (string.IsNullOrEmpty(ver)) {
				var vm = Regex.Match(text, @"__version__\s*=\s*[""']([^""']+)[""']");
				if (vm.Success)
					ver = vm.Groups[1].Value.Trim();
			}
			if (string.IsNullOrEmpty(author)) {
				var am = Regex.Match(text, @"[""']author[""']\s*:\s*[""']([^""']+)[""']");
				if (am.Success)
					author = am.Groups[1].Value.Trim();
			}
			if (!string.IsNullOrEmpty(desc)) return;
			int a = text.IndexOf("\"\"\"", StringComparison.Ordinal);
			if (a < 0) return;
			int b = text.IndexOf("\"\"\"", a + 3, StringComparison.Ordinal);
			if (b <= a) return;
			string body = text.Substring(a + 3, b - a - 3).Trim();
			var lines = body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
			if (lines.Length == 0) return;
			string l0 = lines[0].Trim();
			if (lines.Length >= 2) {
				string l1 = lines[1].Trim();
				if (l1.Length > 0 && (l0.EndsWith("Add-on", StringComparison.OrdinalIgnoreCase) || l0.Length < 28))
					desc = l1;
				else
					desc = l0;
			} else {
				desc = l0;
			}
		}
		
		static string BuildAddonListSubtitle(string ver, string desc, string dirPath) {
			desc = NormalizeSubtitleLine(desc, 160);
			string vPart = null;
			if (!string.IsNullOrEmpty(ver)) {
				ver = ver.Trim();
				vPart = ver.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? ver : "v" + ver;
			}
			if (vPart != null && !string.IsNullOrEmpty(desc))
				return $"{vPart} • {desc}";
			if (vPart != null)
				return $"{vPart} • Installed add-on";
			if (!string.IsNullOrEmpty(desc))
				return desc;
			return $"Installed add-on · {Path.GetFileName(dirPath)}";
		}
		
		static string NormalizeSubtitleLine(string s, int maxLen) {
			if (string.IsNullOrEmpty(s)) return null;
			s = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
			while (s.Contains("  "))
				s = s.Replace("  ", " ");
			if (s.Length > maxLen)
				s = s.Substring(0, maxLen - 1) + "…";
			return s;
		}
		
		/// <summary>
		/// Scans StreamingAssets/Addons/ for add-on directories
		/// </summary>
		public void DiscoverAddons() {
			try {
				string addonsPath = Path.Combine(Application.streamingAssetsPath, "Addons");
				
				if (string.IsNullOrEmpty(addonsPath)) {
					UnityEngine.Debug.LogError("[Addon_MGR] StreamingAssets path is null or empty");
					return;
				}
				
				if (!Directory.Exists(addonsPath)) {
					try {
						Directory.CreateDirectory(addonsPath);
						UnityEngine.Debug.Log($"[Addon_MGR] Created Addons directory at {addonsPath}");
					} catch (System.Exception e) {
						UnityEngine.Debug.LogError($"[Addon_MGR] Failed to create Addons directory: {e.Message}");
						return;
					}
					return;
				}
				
				string[] addonDirs = null;
				try {
					addonDirs = Directory.GetDirectories(addonsPath);
				} catch (System.Exception e) {
					UnityEngine.Debug.LogError($"[Addon_MGR] Failed to get directories from {addonsPath}: {e.Message}");
					return;
				}
				
				UnityEngine.Debug.Log($"[Addon_MGR] Scanning for addons in: {addonsPath}");
				UnityEngine.Debug.Log($"[Addon_MGR] Found {addonDirs.Length} directories");
				
				var foundIds = new HashSet<string>();
				foreach (var dir in addonDirs) {
					try {
						string initFile = Path.Combine(dir, "__init__.py");
						string addonId = Path.GetFileName(dir);
						
						if (string.IsNullOrEmpty(addonId)) {
							continue;
						}
						
						if (File.Exists(initFile)) {
							foundIds.Add(addonId);
							// Update existing or add new; preserve enabled state + prefs when re-discovering. New addons default disabled (no persistence = all "new" each run; avoids auto-load storm and auto-restart loops when Python fails).
							bool wasEnabled = false;
							JObject wasPrefs = null;
							if (_registeredAddons.TryGetValue(addonId, out var prevInfo) && prevInfo != null) {
								wasEnabled = prevInfo.isEnabled;
								wasPrefs = prevInfo.prefs;
							}
							_registeredAddons[addonId] = new AddonInfo {
								id = addonId,
								path = dir,
								isEnabled = wasEnabled,
								prefs = wasPrefs ?? new JObject()
							};
							EnrichAddonListSubtitle(_registeredAddons[addonId]);
							UnityEngine.Debug.Log($"[Addon_MGR] Discovered add-on: {addonId} (enabled: {wasEnabled})");
						} else {
							UnityEngine.Debug.LogWarning($"[Addon_MGR] Directory '{addonId}' found but missing __init__.py, skipping");
						}
					} catch (System.Exception e) {
						UnityEngine.Debug.LogWarning($"[Addon_MGR] Error processing directory '{dir}': {e.Message}");
						continue;
					}
				}
				
				// Remove addons that are no longer on disk (uninstalled) — tear down ribbon/Python first
				// so Refresh/Discover cannot leave orphan tabs after a folder vanishes.
				var toRemove = new List<string>();
				foreach (var key in _registeredAddons.Keys) {
					if (!foundIds.Contains(key)) toRemove.Add(key);
				}
				foreach (var key in toRemove) {
					// Already unloaded (e.g. RemoveAddon waited) — only shell cleanup + drop registry.
					if (_registeredAddons.TryGetValue(key, out var gone) && gone != null && !gone.isEnabled) {
						DestroyAddonUiShell(key);
					} else {
						UnloadAddon(key);
					}
					_registeredAddons.Remove(key);
				}

				// First discover only: restore prefs OR force default-off. Later Refresh/install rediscover
				// must preserve in-session enables (otherwise dials/ribbon desync and tabs orphan).
				bool firstDiscoverPass = !s_appliedRememberedEnabledOnFirstDiscover;
				ApplyAddonPrefsFromPlayerPrefsOnFirstDiscover();
				ApplyRememberedEnabledStateFromPlayerPrefsOnFirstDiscover();
				if (firstDiscoverPass && !GetRememberEnabledAddonsPreference()) {
					int forced = 0;
					foreach (var kvp in _registeredAddons) {
						if (kvp.Value == null || !kvp.Value.isEnabled) continue;
						kvp.Value.isEnabled = false;
						forced++;
					}
					if (forced > 0)
						UnityEngine.Debug.Log($"[Addon_MGR] Restore-off: forced {forced} add-on(s) disabled at first discover.");
				}
				
				UnityEngine.Debug.Log($"[Addon_MGR] Total addons discovered: {_registeredAddons.Count}");
			} catch (System.Exception e) {
				UnityEngine.Debug.LogError($"[Addon_MGR] Fatal error in DiscoverAddons(): {e.Message}\n{e.StackTrace}");
			}
		}
		
		/// <summary>
		/// Resolves python.exe path so the addon server can start when the exe launches (dual-trigger),
		/// even if Python is not on PATH. Mirrors Run_with_Addons.bat search order.
		/// </summary>
		static string TryResolvePythonExe() {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
			string localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
			string programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
			string[] candidates = new string[] {
				localAppData != null ? Path.Combine(localAppData, "Programs", "Python", "Python312", "python.exe") : null,
				localAppData != null ? Path.Combine(localAppData, "Programs", "Python", "Python311", "python.exe") : null,
				localAppData != null ? Path.Combine(localAppData, "Programs", "Python", "Python310", "python.exe") : null,
				localAppData != null ? Path.Combine(localAppData, "Programs", "Python", "Python313", "python.exe") : null,
				programFiles != null ? Path.Combine(programFiles, "Python312", "python.exe") : null,
				programFiles != null ? Path.Combine(programFiles, "Python311", "python.exe") : null,
				programFiles != null ? Path.Combine(programFiles, "Python310", "python.exe") : null,
				programFiles != null ? Path.Combine(programFiles, "Python313", "python.exe") : null,
			};
			foreach (string p in candidates) {
				if (!string.IsNullOrEmpty(p) && File.Exists(p)) {
					UnityEngine.Debug.Log($"[Addon_MGR] Resolved Python: {p}");
					return p;
				}
			}
			UnityEngine.Debug.Log("[Addon_MGR] Python not found in standard locations; will try 'python' from PATH.");
#endif
			return null; // fallback: use "python" so PATH is used when available
		}

		/// <summary>
		/// Wrapper cmd PID can exit while we keep <see cref="_isServerRunning"/> true, blocking restart.
		/// Also clear when the tracked Process has exited (Editor path).
		/// </summary>
		void ClearStalePythonServerRunningFlag() {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
			if (_isServerRunning && _pythonServerPid != 0
			    && !StartExternalProcess.IsProcessRunning(_pythonServerPid)) {
				UnityEngine.Debug.LogWarning(
					$"[Addon_MGR] Addon server launcher PID {_pythonServerPid} is gone — clearing running flag so restart can proceed.");
				_pythonServerPid = 0;
				_isServerRunning = false;
				InvalidateSharedAddonReadyCache();
			}
#elif UNITY_EDITOR
			if (_isServerRunning && _pythonProcess != null && _pythonProcess.HasExited) {
				UnityEngine.Debug.LogWarning(
					"[Addon_MGR] Addon Python process has exited — clearing running flag so restart can proceed.");
				_pythonProcess = null;
				_isServerRunning = false;
				InvalidateSharedAddonReadyCache();
			}
#endif
		}

		/// <summary>
		/// Starts the Python server process (dual-trigger: runs automatically when exe loads, like quick-start flow).
		/// Console visibility follows Settings → Show external process windows (default off = hidden).
		/// </summary>
		void StartPythonServer() {
			ClearStalePythonServerRunningFlag();
			if (_isServerRunning) return;
			InvalidateSharedAddonReadyCache();

			string serverScriptPath = null;
			try {
				serverScriptPath = Path.Combine(Application.streamingAssetsPath, "AddonSystem", _pythonServerScript);
			} catch (System.Exception e) {
				UnityEngine.Debug.LogError($"[Addon_MGR] Failed to construct server script path: {e.Message}");
				return;
			}
			
			bool fileExists = false;
			try {
				fileExists = File.Exists(serverScriptPath);
			} catch (System.Exception e) {
				UnityEngine.Debug.LogError($"[Addon_MGR] Failed to check if server script exists: {e.Message}");
				return;
			}
			
			if (!fileExists) {
				UnityEngine.Debug.LogError($"[Addon_MGR] Python server script not found at {serverScriptPath}");
				return;
			}
			
			string pythonExe = TryResolvePythonExe();
			if (string.IsNullOrEmpty(pythonExe)) pythonExe = "python";
			bool showExternalWindows = LaunchWebUIBatFile.PrefsWantShowExternalProcessWindows();
			UnityEngine.Debug.Log($"[Addon_MGR] Starting Python server: socket port {_serverPort}, HTTP port {_httpServerPort}, exe: {pythonExe}");

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
			// Stale FastAPI on 5557 keeps _isServerRunning true with no /ready — free before spawn (same helper as WebUI :7860).
			if (_enableHttpServer) {
				try {
					AddonPortHelper.TryKillProcessesOnPort(_httpServerPort);
				} catch (Exception e) {
					UnityEngine.Debug.LogWarning($"[Addon_MGR] Could not free HTTP port {_httpServerPort} before start: {e.Message}");
				}
			}
#endif
			
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
			// IL2CPP: System.Diagnostics.Process.Start() triggers "Process::CreateProcess_internal" assertion.
			// Use Win32 CreateProcessW via StartExternalProcess (same as WebUI/addon bat launcher).
			try {
				string addonsPath = Path.Combine(Application.streamingAssetsPath, "Addons");
				string workDir = Path.GetDirectoryName(serverScriptPath);
				// Write launcher bat to TEMP — StreamingAssets is often read-only (Program Files / locked installs).
				string batPath = Path.Combine(Path.GetTempPath(), "spz_start_addon_server.bat");
				string httpArg = _enableHttpServer ? $"--http-port {_httpServerPort}" : "--no-http";
				// Tell Python whether we bound 5555: if not (Editor has it), Python must NOT kill anything on 5557 or it may kill the Editor.
				string socketBound = (Addon_SocketServer.instance != null && Addon_SocketServer.instance.IsListening) ? "1" : "0";
				string pyCmd = "\"" + pythonExe.Replace("\"", "\"\"") + "\" \"" + serverScriptPath.Replace("\"", "\"\"")
					+ "\" --port " + _serverPort + " --addons-dir \"" + addonsPath.Replace("\"", "\"\"") + "\" " + httpArg;
				string addonServerLogPath = Path.Combine(Path.GetTempPath(), "spz_addon_server.log");
				string batContent;
				if (showExternalWindows) {
					// Do not redirect — a visible CMD with >> log looks empty and hides FastAPI ImportError.
					batContent = "@echo off\r\ncd /d \"" + workDir + "\"\r\nset SPZ_SOCKET_BOUND=" + socketBound + "\r\n" + pyCmd + "\r\n";
				} else {
					try {
						File.WriteAllText(addonServerLogPath,
							"=== SPZ addon server spawn " + DateTime.Now.ToString("o") + " ===\r\n");
					} catch { /* best-effort */ }
					batContent = "@echo off\r\ncd /d \"" + workDir + "\"\r\nset SPZ_SOCKET_BOUND=" + socketBound + "\r\n"
						+ pyCmd + " >> \"" + addonServerLogPath + "\" 2>&1\r\n";
				}
				File.WriteAllText(batPath, batContent);
				// Stale FastAPI-fail markers must not poison a fresh start.
				TryClearAddonHttpFailMarker();
				UnityEngine.Debug.Log(showExternalWindows
					? "[Addon_MGR] Starting addon server with visible console (Settings)."
					: "[Addon_MGR] Starting addon server in background (hidden console; Settings default). Log: " + addonServerLogPath);
				// keepWindow false: /K would leave CMD open after python exits and block ClearStale restart.
				uint pid = StartExternalProcess.Run_Bat_or_Shortcut_or_Command(
					batPath,
					isJustFile: true,
					workDir,
					keepWindow: false,
					hidden: !showExternalWindows,
					attachToConsole: false
				);
				if (pid != 0) {
					_pythonServerPid = pid;
					// Launcher PID only — HTTP :5557 /ready is verified later. Do not treat spawn as FastAPI-ready.
					_isServerRunning = true;
					UnityEngine.Debug.Log(
						$"[Addon_MGR] Python server launcher started (PID {pid}); waiting for HTTP :{_httpServerPort} /ready (not verified yet).");
				} else {
					UnityEngine.Debug.LogError("[Addon_MGR] Failed to start Python server (CreateProcess returned 0).");
				}
			} catch (Exception e) {
				UnityEngine.Debug.LogError($"[Addon_MGR] Failed to start Python server: {e.Message}");
			}
#elif UNITY_EDITOR
			try {
				string addonsPath = Path.Combine(Application.streamingAssetsPath, "Addons");
				string arguments = $"\"{serverScriptPath}\" --port {_serverPort} --addons-dir \"{addonsPath}\"";
				if (_enableHttpServer) {
					arguments += $" --http-port {_httpServerPort}";
				} else {
					arguments += " --no-http";
				}
				// Same as IL2CPP: stale FastAPI-fail markers must not poison Editor Load-now / Enable.
				TryClearAddonHttpFailMarker();
				string socketBound = (Addon_SocketServer.instance != null && Addon_SocketServer.instance.IsListening) ? "1" : "0";
				_pythonProcess = new Process {
					StartInfo = new ProcessStartInfo {
						FileName = pythonExe,
						Arguments = arguments,
						UseShellExecute = false,
						RedirectStandardOutput = true,
						RedirectStandardError = true,
						CreateNoWindow = !showExternalWindows,
						WorkingDirectory = Path.GetDirectoryName(serverScriptPath)
					}
				};
				// Mirror IL2CPP bat: when Unity socket is not listening, Python must fail fast (not wait ~90s).
				_pythonProcess.StartInfo.EnvironmentVariables["SPZ_SOCKET_BOUND"] = socketBound;
				// Named handlers (instead of inline lambdas) so TerminatePythonAddonServerProcess can detach them
				// before Cancel/Kill — prevents shutdown stalls from late stdout callbacks calling Debug.Log
				// during Unity teardown.
				_pythonProcess.OutputDataReceived += OnPythonStdout_LogToUnity;
				_pythonProcess.ErrorDataReceived  += OnPythonStderr_LogToUnity;
				_pythonProcess.Start();
				_pythonProcess.BeginOutputReadLine();
				_pythonProcess.BeginErrorReadLine();
				_isServerRunning = true;
				UnityEngine.Debug.Log($"[Addon_MGR] Python server started on port {_serverPort} (SPZ_SOCKET_BOUND={socketBound})");
			} catch (Exception e) {
				UnityEngine.Debug.LogError($"[Addon_MGR] Failed to start Python server: {e.Message}");
			}
#else
			UnityEngine.Debug.LogWarning("[Addon_MGR] Python server auto-start not supported on this platform in build; start addon server manually.");
#endif
		}
		
		IEnumerator RequestLoadEnabledAddonsAfterDelay() {
			UnityEngine.Debug.Log("[Addon_MGR] Auto-loading enabled addons in 2.5s (spin loading)...");
			yield return new WaitForSeconds(2.5f);
			// Prefs-restored enables never called EnableAddon — create ribbon shells before Python load.
			EnsureRibbonShellsForAllEnabledAddons();
			int count = 0;
			foreach (var kvp in _registeredAddons) {
				if (kvp.Value != null && kvp.Value.isEnabled) {
					count++;
					UnityEngine.Debug.Log($"[Addon_MGR] Auto-load addon {count}: {kvp.Key}");
					yield return RequestLoadAddon(kvp.Key);
				}
			}
			if (count == 0)
				UnityEngine.Debug.Log("[Addon_MGR] Auto-load: no enabled add-ons — ribbon stays clear (enable + Save settings to load).");
			else
				UnityEngine.Debug.Log($"[Addon_MGR] Auto-load finished. Requested {count} addon(s).");
		}

		/// <summary>Call from Add-on Manager \"Load addons now\" button. Requests Python to load all enabled addons.</summary>
		/// <param name="onComplete">requested, hard-fail (dial off), soft-fail (kept enabled with native/dock fallback after Python fail).</param>
		public void RequestLoadAllEnabledAddonsNow(Action<int, int, int> onComplete = null) {
			StartCoroutine(RequestLoadAllEnabledAddonsNowCrtn(onComplete));
		}

		IEnumerator RequestLoadAllEnabledAddonsNowCrtn(Action<int, int, int> onComplete) {
			// Same as auto-load: prefs-restored enables never called EnableAddon — shells must exist
			// before Python create_panel or panels park off-ribbon with "Load finished" false success.
			EnsureRibbonShellsForAllEnabledAddons();
			int count = 0;
			int hardFail = 0;
			int softFail = 0;
			_pythonLoadSoftFailedIds.Clear();
			// Snapshot ids — MarkAddonLoadFailed may mutate enabled flags mid-loop.
			var toLoad = new List<string>();
			foreach (var kvp in _registeredAddons) {
				if (kvp.Value != null && kvp.Value.isEnabled)
					toLoad.Add(kvp.Key);
			}
			foreach (string addonId in toLoad) {
				count++;
				UnityEngine.Debug.Log($"[Addon_MGR] Load addons now: requesting addon {count}: {addonId}");
				yield return RequestLoadAddon(addonId);
				if (!IsAddonEnabled(addonId))
					hardFail++;
				else if (_pythonLoadSoftFailedIds.Contains(addonId))
					softFail++;
			}
			UnityEngine.Debug.Log(
				$"[Addon_MGR] Load addons now finished. Requested {count} addon(s), hard-fail {hardFail}, soft-fail {softFail}. Check [Addon_SocketServer] and [AddonUI_MGR] logs to see if create_panel ran.");
			AddonDebugCapture.MarkLoadAddonsFinished();
			onComplete?.Invoke(count, hardFail, softFail);
		}
		
		/// <summary>Polls GET /ready until Python has connected to Unity (socket 5555), so create_panel works when we POST /load_addon.</summary>
		/// <remarks>Joins a single shared poll — parallel loaders must not each run 60× GET /ready.</remarks>
		IEnumerator WaitForAddonServerReady(Action<bool> readyOut, int maxAttempts = 60, float interval = 0.5f) {
			if (readyOut == null)
				yield break;
			if (IsAddonApiShuttingDown()) {
				readyOut(false);
				yield break;
			}
			if (_sharedAddonReadyKnownOk) {
				// Cached success must not skip a dead :5557 (PID alive, FastAPI gone).
				ClearStalePythonServerRunningFlag();
				if (!_isServerRunning || TryReadAddonHttpFailMarker(out _)) {
					InvalidateSharedAddonReadyCache();
				} else {
					bool probeOk = false;
					yield return CoProbeAddonReadyOnce(ok => probeOk = ok);
					if (probeOk) {
						readyOut(true);
						yield break;
					}
					UnityEngine.Debug.LogWarning(
						"[Addon_MGR] Cached addon /ready is stale (liveness probe failed) — re-polling HTTP.");
					InvalidateSharedAddonReadyCache();
				}
			}
			_sharedAddonReadyWaiters.Add(readyOut);
			if (!_sharedAddonReadyPollActive) {
				_sharedAddonReadyPollActive = true;
				_sharedAddonReadyWaitCrtn = StartCoroutine(CoSharedAddonServerReadyPoll(maxAttempts, interval));
			}
			// Caller continues after their callback fires from the shared poll (not when this IEnumerator ends).
			// Keep this method as a join: wait until our waiter is no longer pending.
			while (_sharedAddonReadyWaiters.Contains(readyOut))
				yield return null;
		}

		/// <summary>Single GET /ready used to validate <see cref="_sharedAddonReadyKnownOk"/> before short-circuit.</summary>
		IEnumerator CoProbeAddonReadyOnce(Action<bool> done) {
			if (done == null)
				yield break;
			string readyUrl = $"http://127.0.0.1:{_httpServerPort}/ready";
			using (var req = new UnityWebRequest(readyUrl)) {
				req.downloadHandler = new DownloadHandlerBuffer();
				req.timeout = 3;
				yield return req.SendWebRequest();
				if (req.result == UnityWebRequest.Result.Success) {
					try {
						var json = JObject.Parse(req.downloadHandler?.text ?? "{}");
						if (json["ready"]?.Value<bool>() == true) {
							done(true);
							yield break;
						}
					} catch { }
				}
			}
			done(false);
		}

		void NotifySharedAddonReadyWaiters(bool ok) {
			_sharedAddonReadyKnownOk = ok;
			_sharedAddonReadyPollActive = false;
			var waiters = _sharedAddonReadyWaiters.ToArray();
			_sharedAddonReadyWaiters.Clear();
			_sharedAddonReadyWaitCrtn = null;
			for (int i = 0; i < waiters.Length; i++) {
				try { waiters[i]?.Invoke(ok); } catch (Exception e) {
					UnityEngine.Debug.LogWarning($"[Addon_MGR] Shared ready waiter threw: {e.Message}");
				}
			}
		}

		void InvalidateSharedAddonReadyCache() {
			_sharedAddonReadyKnownOk = false;
		}

		IEnumerator CoSharedAddonServerReadyPoll(int maxAttempts, float interval) {
			if (IsAddonApiShuttingDown()) {
				NotifySharedAddonReadyWaiters(false);
				yield break;
			}
			ClearStalePythonServerRunningFlag();
			if (!_isServerRunning) {
				UnityEngine.Debug.LogWarning("[Addon_MGR] Python server not running; attempting to start it now...");
				StartPythonServer();
				if (!_isServerRunning) {
					UnityEngine.Debug.LogError("[Addon_MGR] Could not start Python server. Check python is installed and on PATH. Addon load aborted.");
					NotifySharedAddonReadyWaiters(false);
					yield break;
				}
				yield return new WaitForSeconds(2f);
			}

			string readyUrl = $"http://127.0.0.1:{_httpServerPort}/ready";
			bool loggedHttpReachable = false;
			int consecutiveConnectionErrors = 0;
			bool didMidPollRestart = false;
			for (int i = 0; i < maxAttempts; i++) {
				if (IsAddonApiShuttingDown()) {
					NotifySharedAddonReadyWaiters(false);
					yield break;
				}
				// Python writes this when FastAPI is missing or :5557 never binds — fail fast vs ~30s of "Cannot connect".
				if (TryReadAddonHttpFailMarker(out string httpFailReason)) {
					UnityEngine.Debug.LogError(
						$"[Addon_MGR] Python HTTP :{_httpServerPort} failed to start:\n{httpFailReason}\n" +
						"Also check %TEMP%\\spz_addon_server.log");
					TryLogAddonServerLogTail();
					NotifySharedAddonReadyWaiters(false);
					yield break;
				}
				using (var req = new UnityWebRequest(readyUrl)) {
					req.downloadHandler = new DownloadHandlerBuffer();
					req.timeout = 4;
					yield return req.SendWebRequest();
					if (req.result == UnityWebRequest.Result.Success) {
						consecutiveConnectionErrors = 0;
						if (!loggedHttpReachable) {
							loggedHttpReachable = true;
							UnityEngine.Debug.Log($"[Addon_MGR] Addon HTTP server (FastAPI) responding on port {_httpServerPort} (Unity connected to FastAPI, same role as Unity→SD).");
						}
						try {
							var json = JObject.Parse(req.downloadHandler?.text ?? "{}");
							if (json["ready"]?.Value<bool>() == true) {
								UnityEngine.Debug.Log("[Addon_MGR] Addon server ready (Python connected to Unity socket 5555).");
								NotifySharedAddonReadyWaiters(true);
								yield break;
							}
						} catch { }
					} else {
						consecutiveConnectionErrors++;
						if (consecutiveConnectionErrors >= 10 && !loggedHttpReachable
						    && (consecutiveConnectionErrors == 10 || consecutiveConnectionErrors % 10 == 0)) {
							UnityEngine.Debug.LogWarning($"[Addon_MGR] Cannot reach Python HTTP server after {consecutiveConnectionErrors} attempts. Is Python running? Error: {req.error}");
						}
						// PID may be alive while HTTP is dead — one shared restart mid-poll.
						if (!didMidPollRestart && consecutiveConnectionErrors >= 12) {
							didMidPollRestart = true;
							UnityEngine.Debug.LogWarning(
								$"[Addon_MGR] HTTP still unreachable after {consecutiveConnectionErrors} probes — restarting Python addon server once.");
							InvalidateSharedAddonReadyCache();
							TerminatePythonAddonServerProcess(waitForExit: true);
							TryClearAddonHttpFailMarker();
							StartPythonServer();
							if (_isServerRunning)
								yield return new WaitForSeconds(2f);
							consecutiveConnectionErrors = 0;
							loggedHttpReachable = false;
						}
					}
				}
				yield return new WaitForSeconds(interval);
			}
			UnityEngine.Debug.LogWarning("[Addon_MGR] Addon server /ready did not become true within timeout. Check: (1) Is Python running? (2) Does Player.log show [Addon_SocketServer] Started listening on 127.0.0.1:5555? (3) %TEMP%\\spz_addon_server.log / FastAPI deps.");
			TryLogAddonServerLogTail();
			NotifySharedAddonReadyWaiters(false);
		}

		string AddonHttpFailMarkerPath() {
			return Path.Combine(Path.GetTempPath(), $"spz_addon_http_{_httpServerPort}_failed.txt");
		}

		void TryClearAddonHttpFailMarker() {
			try {
				string path = AddonHttpFailMarkerPath();
				if (File.Exists(path))
					File.Delete(path);
			} catch (Exception e) {
				UnityEngine.Debug.LogWarning($"[Addon_MGR] Could not clear HTTP fail marker: {e.Message}");
			}
		}

		bool TryReadAddonHttpFailMarker(out string reason) {
			reason = null;
			try {
				string path = AddonHttpFailMarkerPath();
				if (!File.Exists(path))
					return false;
				reason = File.ReadAllText(path);
				return !string.IsNullOrWhiteSpace(reason);
			} catch {
				return false;
			}
		}

		/// <summary>Surfaces hidden-console Python output when /ready fails (IL2CPP redirects stdout to this file).</summary>
		void TryLogAddonServerLogTail(int maxChars = 1200) {
			try {
				string path = Path.Combine(Path.GetTempPath(), "spz_addon_server.log");
				if (!File.Exists(path)) {
					UnityEngine.Debug.LogWarning("[Addon_MGR] No %TEMP%\\spz_addon_server.log yet (visible console mode, or spawn never wrote).");
					return;
				}
				string text = File.ReadAllText(path);
				if (string.IsNullOrWhiteSpace(text)) {
					UnityEngine.Debug.LogWarning("[Addon_MGR] spz_addon_server.log is empty.");
					return;
				}
				if (text.Length > maxChars)
					text = "…\n" + text.Substring(text.Length - maxChars);
				UnityEngine.Debug.LogWarning("[Addon_MGR] Tail of spz_addon_server.log:\n" + text.TrimEnd());
			} catch (Exception e) {
				UnityEngine.Debug.LogWarning($"[Addon_MGR] Could not read spz_addon_server.log: {e.Message}");
			}
		}

		IEnumerator RequestLoadAddon(string addonId) {
			// Always take a lifecycle epoch so a user re-enable mid-wait cannot be MarkAddonLoadFailed by this stale op.
			int epoch = BumpLifecycleEpoch(addonId);
			yield return RequestLoadAddon(addonId, epoch);
		}

		IEnumerator RequestLoadAddon(string addonId, int epoch) {
			if (IsAddonApiShuttingDown())
				yield break;
			bool serverReady = false;
			yield return WaitForAddonServerReady(ok => serverReady = ok);
			if (IsAddonApiShuttingDown())
				yield break;
			if (epoch >= 0 && !IsLifecycleEpochCurrent(addonId, epoch))
				yield break;
			if (!IsAddonEnabled(addonId)) {
				UnityEngine.Debug.Log($"[Addon_MGR] Skipping stale load request for disabled add-on: {addonId}");
				yield break;
			}
			if (!serverReady) {
				UnityEngine.Debug.LogError(
					$"[Addon_MGR] Cannot load '{addonId}': Python addon server never became ready (empty ribbon tabs without create_panel).");
				if (epoch < 0 || IsLifecycleEpochCurrent(addonId, epoch))
					MarkAddonLoadFailed(addonId);
				yield break;
			}
			UnityEngine.Debug.Log($"[Addon_MGR] Sending load request to Python for: {addonId}");
			string url = $"http://127.0.0.1:{_httpServerPort}/load_addon";
			string body = "{\"addon_id\":\"" + JsonEscape(addonId) + "\"}";
			using (var req = new UnityWebRequest(url, "POST")) {
				req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
				req.downloadHandler = new DownloadHandlerBuffer();
				req.SetRequestHeader("Content-Type", "application/json");
				req.timeout = 8; // avoid indefinite stalls on addon load wiring
				yield return req.SendWebRequest();
				if (epoch >= 0 && !IsLifecycleEpochCurrent(addonId, epoch))
					yield break;
				if (!IsAddonEnabled(addonId)) {
					UnityEngine.Debug.Log($"[Addon_MGR] Add-on disabled during load; skipping apply for: {addonId}");
					yield break;
				}
				if (req.result != UnityWebRequest.Result.Success) {
					UnityEngine.Debug.LogError($"[Addon_MGR] load_addon failed for {addonId}: {req.error}. Ensure Python server is running on port {_httpServerPort}");
					InvalidateSharedAddonReadyCache();
					if (epoch < 0 || IsLifecycleEpochCurrent(addonId, epoch))
						MarkAddonLoadFailed(addonId);
					yield break;
				}
				bool loadSucceeded = false;
				try {
					var json = JObject.Parse(req.downloadHandler?.text ?? "{}");
					loadSucceeded = json["success"]?.Value<bool>() ?? false;
				} catch {
					// Response not valid JSON or missing success
				}
				string responseBody = req.downloadHandler?.text ?? "";
				if (loadSucceeded) {
					UnityEngine.Debug.Log($"[Addon_MGR] Successfully loaded addon: {addonId}. Response: {responseBody}");
					// Stale load (user disabled mid-flight): ask Python to unload so UI stays removed.
					if (!IsAddonEnabled(addonId))
						StartAddonLifecycleOp(addonId, false);
				} else {
					UnityEngine.Debug.LogError($"[Addon_MGR] Python reported addon load failure for {addonId}. Raw response: {responseBody}. Check Python console for register()/socket errors.");
					InvalidateSharedAddonReadyCache();
					if (epoch < 0 || IsLifecycleEpochCurrent(addonId, epoch))
						MarkAddonLoadFailed(addonId);
				}
			}
		}

		IEnumerator RequestUnloadAddon(string addonId) {
			yield return RequestUnloadAddon(addonId, -1);
		}

		IEnumerator RequestUnloadAddon(string addonId, int epoch) {
			if (!_enableHttpServer || !_isServerRunning) {
				UnityEngine.Debug.LogWarning(
					$"[Addon_MGR] Cannot notify Python to unload {addonId}; add-on HTTP server is unavailable.");
				QueuePendingPythonUnload(addonId);
				yield break;
			}
			if (epoch >= 0 && !IsLifecycleEpochCurrent(addonId, epoch))
				yield break;
			// Re-enabled while a prior unload was queued — do not unregister in Python.
			if (IsAddonEnabled(addonId)) {
				UnityEngine.Debug.Log($"[Addon_MGR] Skipping unload for re-enabled add-on: {addonId}");
				_pendingPythonUnloadIds.Remove(addonId);
				yield break;
			}
			// Dead :5557 / stale fail marker: do not burn 3×8s POSTs while /ready polls also storm.
			if (TryReadAddonHttpFailMarker(out string failReason)) {
				UnityEngine.Debug.LogWarning(
					$"[Addon_MGR] Skipping unload for {addonId}; Python HTTP fail marker present:\n{failReason}");
				QueuePendingPythonUnload(addonId);
				yield break;
			}
			bool httpReady = false;
			yield return CoProbeAddonReadyOnce(ok => httpReady = ok);
			if (epoch >= 0 && !IsLifecycleEpochCurrent(addonId, epoch))
				yield break;
			if (IsAddonEnabled(addonId)) {
				UnityEngine.Debug.Log($"[Addon_MGR] Skipping unload for re-enabled add-on: {addonId}");
				_pendingPythonUnloadIds.Remove(addonId);
				yield break;
			}
			if (!httpReady) {
				UnityEngine.Debug.LogWarning(
					$"[Addon_MGR] Skipping unload for {addonId}; Python HTTP :{_httpServerPort} not ready (Unity will still tear UI after this op).");
				InvalidateSharedAddonReadyCache();
				QueuePendingPythonUnload(addonId);
				yield break;
			}

			string url = $"http://127.0.0.1:{_httpServerPort}/unload_addon";
			string body = "{\"addon_id\":\"" + JsonEscape(addonId) + "\"}";
			for (int attempt = 1; attempt <= 3; attempt++) {
				if (epoch >= 0 && !IsLifecycleEpochCurrent(addonId, epoch))
					yield break;
				if (IsAddonEnabled(addonId)) {
					UnityEngine.Debug.Log($"[Addon_MGR] Skipping unload mid-flight; add-on re-enabled: {addonId}");
					yield break;
				}
				using (var req = new UnityWebRequest(url, "POST")) {
					req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
					req.downloadHandler = new DownloadHandlerBuffer();
					req.SetRequestHeader("Content-Type", "application/json");
					req.timeout = 8;
					yield return req.SendWebRequest();

					bool unloadSucceeded = false;
					if (req.result == UnityWebRequest.Result.Success) {
						try {
							var json = JObject.Parse(req.downloadHandler?.text ?? "{}");
							unloadSucceeded = json["success"]?.Value<bool>() ?? false;
						} catch { }
					}
					if (unloadSucceeded) {
						UnityEngine.Debug.Log($"[Addon_MGR] Python unloaded add-on: {addonId}");
						_pendingPythonUnloadIds.Remove(addonId);
						// Stale unload (user re-enabled mid-flight) or enabled again: reload so the ribbon returns.
						if (IsAddonEnabled(addonId))
							StartAddonLifecycleOp(addonId, true);
						yield break;
					}
					if (attempt == 3) {
						UnityEngine.Debug.LogWarning(
							$"[Addon_MGR] unload_addon failed for {addonId} after {attempt} attempts: " +
							$"{req.error ?? req.downloadHandler?.text}");
						InvalidateSharedAddonReadyCache();
						QueuePendingPythonUnload(addonId);
						yield break;
					}
				}
				yield return new WaitForSeconds(0.5f);
			}
		}

		bool IsLifecycleEpochCurrent(string addonId, int epoch) {
			return _addonLifecycleEpochById.TryGetValue(addonId, out int cur) && cur == epoch;
		}

		int BumpLifecycleEpoch(string addonId) {
			int next = (_addonLifecycleEpochById.TryGetValue(addonId, out int cur) ? cur : 0) + 1;
			_addonLifecycleEpochById[addonId] = next;
			return next;
		}

		void StartAddonLifecycleOp(string addonId, bool load) {
			if (string.IsNullOrEmpty(addonId) || !_enableHttpServer)
				return;
			int epoch = BumpLifecycleEpoch(addonId);
			// Do not StopCoroutine in-flight HTTP — stale ops finish then re-sync via epoch checks.
			Coroutine op = StartCoroutine(load
				? RequestLoadAddon(addonId, epoch)
				: RequestUnloadAddon(addonId, epoch));
			_addonLifecycleOpById[addonId] = op;
		}

		void QueuePendingPythonUnload(string addonId) {
			if (string.IsNullOrEmpty(addonId) || IsAddonEnabled(addonId))
				return;
			_pendingPythonUnloadIds.Add(addonId);
			if (_pendingPythonUnloadFlushCrtn == null && isActiveAndEnabled)
				_pendingPythonUnloadFlushCrtn = StartCoroutine(CoFlushPendingPythonUnloads());
		}

		IEnumerator CoFlushPendingPythonUnloads() {
			try {
				while (_pendingPythonUnloadIds.Count > 0) {
					if (IsAddonApiShuttingDown())
						yield break;
					var ids = new List<string>(_pendingPythonUnloadIds);
					foreach (var id in ids) {
						if (IsAddonEnabled(id))
							_pendingPythonUnloadIds.Remove(id);
					}
					if (_pendingPythonUnloadIds.Count == 0)
						yield break;
					if (TryReadAddonHttpFailMarker(out _)) {
						yield return new WaitForSecondsRealtime(2f);
						continue;
					}
					bool ready = false;
					yield return CoProbeAddonReadyOnce(ok => ready = ok);
					if (!ready) {
						yield return new WaitForSecondsRealtime(2f);
						continue;
					}
					ids = new List<string>(_pendingPythonUnloadIds);
					foreach (var id in ids) {
						if (IsAddonEnabled(id)) {
							_pendingPythonUnloadIds.Remove(id);
							continue;
						}
						// Kick a real unload epoch; success path removes from the pending set.
						StartAddonLifecycleOp(id, false);
					}
					yield return new WaitForSecondsRealtime(1f);
				}
			} finally {
				_pendingPythonUnloadFlushCrtn = null;
			}
		}

		/// <summary>
		/// When Python reports load failure (or HTTP timeout), disable for this session and tear down ribbon UI.
		/// Does not rewrite remember prefs — Save settings owns persistence (transient stalls must not forget the add-on).
		/// Native-capable add-ons (SPZ GO / Nomad) keep the ribbon tab and seed in-process UI instead of vanishing.
		/// </summary>
		void MarkAddonLoadFailed(string addonId) {
			if (!_registeredAddons.TryGetValue(addonId, out var addon) || addon == null)
				return;
			// RibbonOnlyFullscreen dock is Unity-driven (Gen Art strip). Python register() is optional;
			// HTTP timeouts during Gen Art must not flip the dial off or destroy the FULL/SRN button.
			if (string.Equals(addonId, RibbonOnlyFullscreenAddonId, StringComparison.Ordinal)) {
				_pythonLoadSoftFailedIds.Add(addonId);
				UnityEngine.Debug.LogWarning(
					$"[Addon_MGR] Python load failed for {addonId}, but keeping add-on enabled — viewport dock does not require Python. Response/timeout is non-fatal.");
				if (!RibbonViewportFullViewOnScreen_Toggle_UI.IsAnyVisibleBuiltDock())
					StartEnsureRibbonOnlyFullscreenViewportDock();
				return;
			}
			// SPZ GO / Nomad Theme already work in-process when HTTP :5557 is down. Do not disable or remove the tab —
			// that erased native fallback and left users with a vanished SPZ GO after a blank wait.
			if (SupportsNativeUiWithoutPython(addonId)) {
				_pythonLoadSoftFailedIds.Add(addonId);
				UnityEngine.Debug.LogWarning(
					$"[Addon_MGR] Python load failed for {addonId}, but keeping enabled — seeding native UI (HTTP :{_httpServerPort} unavailable).");
				// Ribbon shell only when host pref allows; CreatePanel still parks when ribbon is hidden.
				if (ShouldShowInCommandRibbon(addonId))
					EnsureRibbonShellForEnabledAddon(addonId);
				if (AddonUI_MGR.instance != null)
					AddonUI_MGR.instance.EnsureNativeFallbackUiWhenPythonMissing(addonId, force: true);
				return;
			}
			addon.isEnabled = false;
			// Eager EnableAddon may have created a ribbon shell before Python failed — tear it down.
			if (AddonUI_MGR.instance != null)
				AddonUI_MGR.instance.DestroyAddonUI(addonId);
			var ribbon = AddonRibbonIntegration.ResolveCommandRibbon();
			if (ribbon != null)
				ribbon.RemoveAddonPanel(addonId);
			// Align Python if register() finished after a client timeout / false success.
			if (_enableHttpServer)
				StartAddonLifecycleOp(addonId, false);
			OnAddonEnabledStateChanged?.Invoke(addonId);
		}

		/// <summary>Add-ons that ship Unity-side ribbon UI when Python FastAPI never loads.</summary>
		public static bool SupportsNativeUiWithoutPython(string addonId) {
			if (string.IsNullOrEmpty(addonId))
				return false;
			return string.Equals(addonId, StableProjectorzGoAddonId, StringComparison.Ordinal)
			       || string.Equals(addonId, NomadThemeAddonId, StringComparison.Ordinal);
		}

		/// <summary>
		/// True when native SPZ GO/Nomad UI should be seeded. False while HTTP is up and /ready succeeded
		/// (or still starting) so we do not flash incomplete native panels before Python create_panel.
		/// </summary>
		public bool ShouldSeedNativeAddonFallback() {
			if (!_enableHttpServer)
				return true;
			if (TryReadAddonHttpFailMarker(out _))
				return true;
			if (_sharedAddonReadyKnownOk)
				return false;
			// Launcher running / mid /ready — wait for Python or MarkAddonLoadFailed.
			if (_isServerRunning)
				return false;
			return true;
		}

		/// <summary>Static gate for ribbon activate / UI_MGR when instance may be null (seed if unsure).</summary>
		public static bool ShouldSeedNativeAddonFallbackStatic() {
			return instance == null || instance.ShouldSeedNativeAddonFallback();
		}

		/// <summary>StreamingAssets add-on id for on-screen full view ribbon dock (matches folder name and Python <c>ADDON_ID</c>).</summary>
		public const string RibbonOnlyFullscreenAddonId = "RibbonOnlyFullscreen";
		public const string StableProjectorzGoAddonId = "StableProjectorzGO";
		public const string NomadThemeAddonId = "NomadThemeSPZ";

		/// <summary>True when the add-on is enabled in the manager (ribbon/Python load allowed).</summary>
		public bool IsAddonEnabled(string addonId) {
			if (string.IsNullOrEmpty(addonId) || _registeredAddons == null) {
				return false;
			}
			return _registeredAddons.TryGetValue(addonId, out var info) && info != null && info.isEnabled;
		}

		/// <summary>Static convenience for UI/socket gates when <see cref="instance"/> may be null.</summary>
		public static bool IsAddonEnabledStatic(string addonId) {
			return instance != null && instance.IsAddonEnabled(addonId);
		}

		/// <summary>
		/// <see cref="RibbonOnlyFullscreenAddonId"/>: run <c>spz.ui.attach_viewport_fullview_toggle</c> from Unity on the main thread
		/// until the Gen Art column dock is visible. Does not use the right command-ribbon tab strip; add-on is driven from <see cref="EnableAddon"/> only.
		/// When HTTP is off, Python <c>register()</c> may not run, so this path is required.
		/// </summary>
		void StartEnsureRibbonOnlyFullscreenViewportDock() {
			if (_ribbonOnlyDockEnsureCrtn != null)
				StopCoroutine(_ribbonOnlyDockEnsureCrtn);
			_ribbonOnlyDockEnsureCrtn = StartCoroutine(CoEnsureRibbonOnlyFullscreenViewportDock());
		}

		IEnumerator CoEnsureRibbonOnlyFullscreenViewportDock() {
			try {
				yield return null;
				const int maxFrames = 600;
				bool attachKicked = false;
				int lastForceGenStripFrame = -999;
				for (int f = 0; f < maxFrames; f++) {
					if (this == null) {
						yield break;
					}
					if (!IsAddonEnabled(RibbonOnlyFullscreenAddonId)) {
						yield break;
					}
					// Kick attach once (or again only if nothing is building). Per-frame NotifyAttachRequested
					// previously tore down CoBuildWhenGenArtReady every frame → dial ON, no FULL/SRN button.
					if (!RibbonViewportFullViewOnScreen_Toggle_UI.IsAnyVisibleBuiltDock()) {
						bool inFlight = RibbonViewportFullViewOnScreen_Toggle_UI.IsAnyDockBuildInFlight();
						bool alreadyBuiltOrBuilding = RibbonViewportFullViewOnScreen_Toggle_UI.IsAnyDockBuiltOrBuilding();
						// Do not re-call attach while a dock already exists/builds — that tore/rebuilt and flashed.
						if (!alreadyBuiltOrBuilding && (!attachKicked || !inFlight)) {
							Addon_SocketServer.TryAttachViewportFullViewToggleFromCore(null);
							attachKicked = true;
						}
						// Every ~1s without a visible dock, force a single dock on the Gen Art strip
						// (migrates off inactive workflow hosts — never dual-mount).
						if (!inFlight && f - lastForceGenStripFrame >= 60) {
							lastForceGenStripFrame = f;
							RibbonViewportFullViewOnScreen_Toggle_UI.TryEnsureOnGenerateButtonsStrip(
								RibbonDock_ButtonSpec.FromRpc(null));
						}
					} else {
						UnityEngine.Debug.Log(
							"[Addon_MGR] RibbonOnlyFullscreen: viewport FULL/SRN dock next to Gen Art is visible (add-on manager path).");
						yield break;
					}
					yield return null;
				}
				UnityEngine.Debug.LogWarning(
					"[Addon_MGR] RibbonOnlyFullscreen: no visible viewport dock after "
					+ maxFrames
					+ " frames. Use Play with the main scene, SD/Gen Art UI loaded, and enable the add-on. If HTTP is on, check Python <c>register()</c> and the Console for attach errors.");
			} finally {
				_ribbonOnlyDockEnsureCrtn = null;
			}
		}
		
		/// <summary>
		/// Registers UI elements created by an add-on
		/// </summary>
		public void RegisterAddonUI(string addonId, GameObject uiElement) {
			if (_registeredAddons.ContainsKey(addonId)) {
				_registeredAddons[addonId].uiElements.Add(uiElement);
			}
		}
		
		/// <summary>
		/// Unloads an add-on and destroys its UI elements (panel content, ribbon tab, and clears all registries).
		/// When HTTP is up, waits for Python <c>unregister()</c> first so panel get_value/save can still run.
		/// </summary>
		/// <param name="onComplete">Fired after UI tear-down (and HTTP unload when enabled). May run sync when HTTP is off.</param>
		public void UnloadAddon(string addonId, Action onComplete = null) {
			if (!_registeredAddons.ContainsKey(addonId)) {
				onComplete?.Invoke();
				return;
			}
			
			var addon = _registeredAddons[addonId];
			addon.isEnabled = false;

			if (string.Equals(addonId, RibbonOnlyFullscreenAddonId, StringComparison.Ordinal)) {
				RibbonViewportFullViewOnScreen_Toggle_UI.TeardownAllDocksForAddonDisabled();
			}

			if (_enableHttpServer) {
				// Do not DestroyAddonUI before POST /unload_addon — GenerationDoneAudio/GpuFlow unregister
				// still read panel values via get_value (AddonDebug showed get_value after RemoveAddonPanel).
				StartCoroutine(CoPythonUnloadThenDestroyUi(addonId, onComplete));
				return;
			}

			DestroyAddonUiShell(addonId);
			UnityEngine.Debug.Log($"[Addon_MGR] Unloaded add-on: {addonId}");
			OnAddonEnabledStateChanged?.Invoke(addonId);
			onComplete?.Invoke();
		}

		IEnumerator CoPythonUnloadThenDestroyUi(string addonId, Action onComplete = null) {
			int epoch = BumpLifecycleEpoch(addonId);
			Coroutine op = StartCoroutine(RequestUnloadAddon(addonId, epoch));
			_addonLifecycleOpById[addonId] = op;
			yield return op;
			if (IsAddonEnabled(addonId)) {
				onComplete?.Invoke();
				yield break;
			}
			if (!IsLifecycleEpochCurrent(addonId, epoch)) {
				onComplete?.Invoke();
				yield break;
			}
			DestroyAddonUiShell(addonId);
			UnityEngine.Debug.Log($"[Addon_MGR] Unloaded add-on: {addonId}");
			OnAddonEnabledStateChanged?.Invoke(addonId);
			onComplete?.Invoke();
		}

		void DestroyAddonUiShell(string addonId) {
			if (string.IsNullOrEmpty(addonId))
				return;
			// Must not require a live registry entry: Discover may Remove the add-on while
			// CoPythonUnloadThenDestroyUi is still pending after HTTP unload.
			if (AddonUI_MGR.instance != null)
				AddonUI_MGR.instance.DestroyAddonUI(addonId);
			var ribbon = AddonRibbonIntegration.ResolveCommandRibbon();
			if (ribbon != null)
				ribbon.RemoveAddonPanel(addonId);
			if (_registeredAddons.TryGetValue(addonId, out var addon) && addon != null) {
				if (addon.uiElements != null) {
					foreach (var go in addon.uiElements) {
						if (go != null)
							UnityEngine.Object.Destroy(go);
					}
					addon.uiElements.Clear();
				}
			}
		}
		
		static string JsonEscape(string s) {
			if (string.IsNullOrEmpty(s)) return "";
			return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
		}
		
		/// <summary>
		/// Gets list of discovered add-ons
		/// </summary>
		public IReadOnlyDictionary<string, AddonInfo> GetAddons() {
			return _registeredAddons;
		}

		/// <summary>
		/// The Gen Art column dock may live on a host that is disabled when switching right-panel tabs. Call from
		/// <see cref="RibbonViewportFullViewOnScreen_Toggle_UI.OnDisable"/> to decide whether to remove injected UI:
		/// only when <see cref="RibbonOnlyFullscreenAddonId"/> is <b>off</b> in the manager, not on every parent deactivate.
		/// </summary>
		public static bool ShouldTearDownViewportFullViewDockOnHostDisabled() {
			if (instance == null) {
				return false;
			}
			if (!instance._registeredAddons.TryGetValue(RibbonOnlyFullscreenAddonId, out var info) || info == null) {
				return false;
			}
			return !info.isEnabled;
		}
		
		/// <summary>
		/// Keeps command-ribbon tab presence aligned with live enable + host ribbon pref.
		/// Enabled + show → ensure tab+shell; enabled + hide → salvage UI to parking and remove tab;
		/// disabled → remove tab+shell (and destroy UI).
		/// </summary>
		public void SyncRibbonTabWithEnabledState(string addonId) {
			if (string.IsNullOrEmpty(addonId) || !_registeredAddons.ContainsKey(addonId))
				return;
			if (string.Equals(addonId, RibbonOnlyFullscreenAddonId, StringComparison.Ordinal)) {
				var ribbonOnly = AddonRibbonIntegration.ResolveCommandRibbon();
				if (ribbonOnly != null)
					ribbonOnly.RemoveAddonPanel(addonId);
				if (IsAddonEnabled(addonId))
					StartEnsureRibbonOnlyFullscreenViewportDock();
				else
					RibbonViewportFullViewOnScreen_Toggle_UI.TeardownAllDocksForAddonDisabled();
				return;
			}
			if (IsAddonEnabled(addonId) && ShouldShowInCommandRibbon(addonId)) {
				EnsureRibbonShellForEnabledAddon(addonId);
				if (AddonUI_MGR.instance != null)
					AddonUI_MGR.instance.RequestMigrateParkedPanelsNow();
				return;
			}
			if (IsAddonEnabled(addonId) && !ShouldShowInCommandRibbon(addonId)) {
				// Still active — park panels and drop the ribbon tab only.
				var ribbonHide = AddonRibbonIntegration.ResolveCommandRibbon();
				if (ribbonHide != null)
					ribbonHide.RemoveAddonPanelPreservingContent(addonId);
				return;
			}
			if (AddonUI_MGR.instance != null)
				AddonUI_MGR.instance.DestroyAddonUI(addonId);
			var ribbon = AddonRibbonIntegration.ResolveCommandRibbon();
			if (ribbon != null)
				ribbon.RemoveAddonPanel(addonId);
		}

		/// <summary>
		/// Enables an add-on, creates its command-ribbon tab when the host ribbon pref allows, and requests Python to load it.
		/// </summary>
		public void EnableAddon(string addonId) {
			if (!_registeredAddons.ContainsKey(addonId)) {
				UnityEngine.Debug.LogWarning($"[Addon_MGR] Add-on '{addonId}' not found");
				return;
			}
			
			_pendingPythonUnloadIds.Remove(addonId);
			_registeredAddons[addonId].isEnabled = true;
			UnityEngine.Debug.Log($"[Addon_MGR] Enabled add-on: {addonId}");

			// Ribbon tab appears as soon as the manager turns the dial on (when prefs allow).
			if (ShouldShowInCommandRibbon(addonId)) {
				EnsureRibbonShellForEnabledAddon(addonId);
				if (AddonUI_MGR.instance != null)
					AddonUI_MGR.instance.RequestMigrateParkedPanelsNow();
			} else {
				var ribbonHide = AddonRibbonIntegration.ResolveCommandRibbon();
				if (ribbonHide != null)
					ribbonHide.RemoveAddonPanelPreservingContent(addonId);
			}

			if (_enableHttpServer) {
				StartAddonLifecycleOp(addonId, true);
			}
			else {
				UnityEngine.Debug.LogWarning(
					"[Addon_MGR] Add-on HTTP is disabled: Python will not run register().");
				// No create_panel race — seed known in-process UIs with the ribbon shell (connectivity).
				if (SupportsNativeUiWithoutPython(addonId) && AddonUI_MGR.instance != null)
					AddonUI_MGR.instance.EnsureNativeFallbackUiWhenPythonMissing(addonId, force: true);
			}
			// On-screen full view: must run from Unity (Python register may never run, or may race). No command-ribbon tab.
			if (string.Equals(addonId, RibbonOnlyFullscreenAddonId, StringComparison.Ordinal)) {
				var ribbon = AddonRibbonIntegration.ResolveCommandRibbon();
				if (ribbon != null) {
					ribbon.RemoveAddonPanel(addonId);
				}
				StartEnsureRibbonOnlyFullscreenViewportDock();
			}
			OnAddonEnabledStateChanged?.Invoke(addonId);
			// Persistence is owned by Add-on Manager "Save settings" (not every dial click).
		}

		/// <summary>
		/// Creates/repairs the command-ribbon tab+shell for an enabled add-on so manager dials stay linked to the strip.
		/// If the ribbon is not ready yet, retries until it is (or the add-on is disabled / ribbon-hidden).
		/// </summary>
		void EnsureRibbonShellForEnabledAddon(string addonId) {
			if (string.IsNullOrEmpty(addonId)) return;
			if (string.Equals(addonId, RibbonOnlyFullscreenAddonId, StringComparison.Ordinal))
				return;
			if (!IsAddonEnabled(addonId) || !ShouldShowInCommandRibbon(addonId)) return;
			if (TryCreateRibbonShellNow(addonId))
				return;
			StartEnsureRibbonShellWhenReady(addonId);
		}

		void EnsureRibbonShellsForAllEnabledAddons() {
			if (_registeredAddons == null) return;
			foreach (var kvp in _registeredAddons) {
				if (kvp.Value == null || !kvp.Value.isEnabled) continue;
				if (!ShouldShowInCommandRibbon(kvp.Key)) continue;
				EnsureRibbonShellForEnabledAddon(kvp.Key);
			}
			if (AddonUI_MGR.instance != null)
				AddonUI_MGR.instance.RequestMigrateParkedPanelsNow();
		}

		/// <summary>When FastAPI is off, Python never create_panel — fill SPZ GO/Nomad shells at enable/boot (not only on tab click).</summary>
		void SeedNativeFallbacksForEnabledAddonsWhenHttpOff() {
			if (_enableHttpServer || _registeredAddons == null || AddonUI_MGR.instance == null)
				return;
			foreach (var kvp in _registeredAddons) {
				if (kvp.Value == null || !kvp.Value.isEnabled) continue;
				if (!SupportsNativeUiWithoutPython(kvp.Key)) continue;
				AddonUI_MGR.instance.EnsureNativeFallbackUiWhenPythonMissing(kvp.Key, force: true);
			}
		}

		bool TryCreateRibbonShellNow(string addonId) {
			if (!IsAddonEnabled(addonId) || !ShouldShowInCommandRibbon(addonId))
				return true; // treat as done — do not retry
			var ribbon = AddonRibbonIntegration.ResolveCommandRibbon();
			if (ribbon == null)
				return false;
			string title = addonId;
			if (_registeredAddons.TryGetValue(addonId, out var info) && info != null
			    && !string.IsNullOrWhiteSpace(info.displayName))
				title = info.displayName.Trim();
			var shell = ribbon.GetOrCreatePanelForAddon(addonId, title);
			if (shell == null)
				return false;
			UnityEngine.Debug.Log($"[Addon_MGR] Ribbon tab ready for enabled add-on: {addonId} ({title})");
			// Do not seed native SPZ GO/Nomad here — when HTTP is up, Python create_panel arrives
			// immediately and ClearAddonPanelChildren wipes the native seed (flash). Native seed runs
			// from MarkAddonLoadFailed / tab activate only when ShouldSeedNativeAddonFallback().
			return true;
		}

		void StartEnsureRibbonShellWhenReady(string addonId) {
			if (string.IsNullOrEmpty(addonId)) return;
			if (_ribbonShellEnsureById.TryGetValue(addonId, out var existing) && existing != null)
				StopCoroutine(existing);
			_ribbonShellEnsureById[addonId] = StartCoroutine(CoEnsureRibbonShellWhenReady(addonId));
		}

		IEnumerator CoEnsureRibbonShellWhenReady(string addonId) {
			UnityEngine.Debug.LogWarning(
				$"[Addon_MGR] Enabled '{addonId}' but CommandRibbon_UI not ready — retrying ribbon tab create.");
			const int maxFrames = 600;
			for (int f = 0; f < maxFrames; f++) {
				if (this == null)
					yield break;
				if (!IsAddonEnabled(addonId)
				    || !ShouldShowInCommandRibbon(addonId)
				    || string.Equals(addonId, RibbonOnlyFullscreenAddonId, StringComparison.Ordinal)) {
					_ribbonShellEnsureById.Remove(addonId);
					yield break;
				}
				if (TryCreateRibbonShellNow(addonId)) {
					_ribbonShellEnsureById.Remove(addonId);
					yield break;
				}
				yield return null;
			}
			_ribbonShellEnsureById.Remove(addonId);
			UnityEngine.Debug.LogWarning(
				$"[Addon_MGR] Timed out waiting for CommandRibbon_UI to create tab for enabled '{addonId}'.");
		}
		
		/// <summary>
		/// Disables an add-on (unloads it)
		/// </summary>
		public void DisableAddon(string addonId) {
			if (!_registeredAddons.ContainsKey(addonId)) {
				UnityEngine.Debug.LogWarning($"[Addon_MGR] Add-on '{addonId}' not found");
				return;
			}
			
			UnloadAddon(addonId);
		}
		
		/// <summary>
		/// Forces a re-scan of the Addons directory. Preserves enabled state for addons that are still present.
		/// </summary>
		public void RefreshAddons() {
			DiscoverAddons();
		}
		
		/// <summary>
		/// Gets the server port
		/// </summary>
		public int GetServerPort() {
			return _serverPort;
		}
		
		public int GetHttpServerPort() {
			return _httpServerPort;
		}
		
		public int GetWebSocketPort() {
			return _webSocketPort;
		}
		
		public bool IsHttpServerEnabled() {
			return _enableHttpServer;
		}

		public bool IsCSharpHttpServerEnabled() {
			return _enableCSharpHttpServer;
		}
		
		public bool IsWebSocketServerEnabled() {
			return _enableWebSocketServer;
		}
		
		void OnDestroy() {
			GenerateButtons_UI._Act_OnGenerate_finished -= OnGenerateFinished_MaybeRestoreFullscreenDock;
			Application.quitting -= HandleApplicationQuitting;
			ShutdownAddonApiBeforeQuit();
			if (instance == this)
				instance = null;
		}
	}
}

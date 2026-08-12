using System;
using System.Collections;
#if UNITY_EDITOR
using System.Diagnostics;
#endif
using System.IO;
using UnityEngine;
using Lavender.Systems;

namespace spz {

	/// <summary>
	/// When the built exe runs, this component auto-launches the WebUI batch file (run_noQuickEdit.bat).
	/// Aggressive: env SPZ_WEBUI_RUN_PATH, then search exe dir + dataPath + CurrentDirectory, each up to 10 parent levels.
	/// Retries at 0.5s, 1.5s, 3s, 6s, 12s until bat is found and launched.
	/// Folder candidates: true Forge Neo preferred, then classic Forge, then legacy reForge (spec forge-neo-swap R1).
	/// </summary>
	public class LaunchWebUIBatFile : MonoBehaviour{
	    public static LaunchWebUIBatFile instance { get; private set; } = null;

	    /// <summary>Legacy classic Forge folder (serialized Restart defaults historically; still discoverable).</summary>
	    public const string WebuiFolderName = "stable-diffusion-webui-forge";
	    /// <summary>True Forge Neo official clone folder (Haoming02 sd-webui-forge-classic --branch neo).</summary>
	    public const string WebuiFolderNameNeo = "sd-webui-forge-neo";
	    /// <summary>Legacy mistaken "Neo" label — Panchovix reForge; discoverable only, not preferred.</summary>
	    public const string WebuiFolderNameReForgeLegacy = "stable-diffusion-webui-reForge";
	    /// <summary>Search order: true Neo first, classic, then legacy reForge. Env <c>SPZ_WEBUI_RUN_PATH</c> still wins.</summary>
	    public static readonly string[] WebuiCandidateFolderNames = new string[] {
	        WebuiFolderNameNeo,
	        WebuiFolderName,
	        WebuiFolderNameReForgeLegacy,
	    };
	    public static readonly string[] WebuiLaunchFileNames = new string[] { "run_noQuickEdit.bat", "webui-user.bat", "run.bat", "run_forge.bat", "webui.bat", "run_noQuickEdit.lnk" };
	    /// <summary>Gradio: prefer not to open a browser (some versions).</summary>
	    const string GradioSuppressInBrowser_bat = "set \"GRADIO_INBROWSER=0\"\r\n";
	    /// <summary>Forge <c>webui.py</c> sets <c>inbrowser</c> from config ("Local") unless this env is set (same as internal UI-reload). Required; GRADIO_INBROWSER alone does not stop Forge.</summary>
	    const string Forge_SuppressBrowserAutolaunch_bat = "set \"SD_WEBUI_RESTARTING=1\"\r\n";
	    static readonly string SpzWebuiNoBrowserEnv_bat = GradioSuppressInBrowser_bat + Forge_SuppressBrowserAutolaunch_bat;

	    /// <summary>Search up to this many parent levels from each root. Aggressive so bat is found even if exe is deep.</summary>
	    const int MaxParentDepth = 10;
	    /// <summary>Retry delays in seconds: first at 0.5s, then 1.5, 3, 6, 12.</summary>
	    static readonly float[] AutoLaunchRetryDelays = new float[] { 0.5f, 1.5f, 3f, 6f, 12f };

	    static uint _lastLaunchedWebUiPid;
	    // Classic Forge/WebUI Gradio+API port (matches ConnectionPanel default).
	    const int WebUiHttpPort = 7860;
	    Coroutine _waitForWebUiReady_crtn;
	    bool _isWaitingForWebUiReady;
	    bool _openBrowserWhenReadyRequested;
	    bool _suppressBrowserOpenForCurrentLaunch;
	    bool _requireDisconnectBeforeReady;

	    /// <summary>
	    /// Settings default is hide (0). Read PlayerPrefs directly so a live <see cref="Settings_MGR"/>
	    /// before its Awake tryLoad cannot report the field default (false) while prefs are already 1.
	    /// </summary>
	    public static bool PrefsWantShowExternalProcessWindows() {
	        return UnityEngine.PlayerPrefs.GetInt("ShowExternalProcessWindows", 0) == 1;
	    }

	    /// <summary>Attempts to close the previously launched WebUI process (and its tree on Windows). Call before launching a new instance so the old window closes automatically.</summary>
	    public static void TryCloseLastLaunchedWebUi() {
	        bool triedPidClose = false;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
	        if (_lastLaunchedWebUiPid != 0) {
	            triedPidClose = true;
	            if (StartExternalProcess.IsProcessRunning(_lastLaunchedWebUiPid)) {
	                try {
	                    // IL2CPP: System.Diagnostics.Process.Start triggers "Process::CreateProcess_internal" assertion. Use CreateProcessW path only.
	                    string workDir = Path.GetTempPath();
	                    uint cmdPid = StartExternalProcess.Run_Bat_or_Shortcut_or_Command(
	                        $"taskkill /PID {_lastLaunchedWebUiPid} /T /F", isJustFile: false, workDir, keepWindow: false, hidden: true, attachToConsole: false);
	                    if (cmdPid != 0)
	                        StartExternalProcess.WaitForProcessExit(cmdPid, 5000);
	                    UnityEngine.Debug.Log($"[LaunchWebUI] Closed previous WebUI process tree (PID {_lastLaunchedWebUiPid}).");
	                } catch (Exception e) {
	                    UnityEngine.Debug.LogWarning($"[LaunchWebUI] Could not close previous WebUI (PID {_lastLaunchedWebUiPid}): {e.Message}");
	                }
	            } else {
	                UnityEngine.Debug.Log($"[LaunchWebUI] Previous launcher PID {_lastLaunchedWebUiPid} already exited; falling back to port-based cleanup.");
	            }
	        }
	        // PID can point to a short-lived wrapper cmd.exe while WebUI python keeps running.
	        // Always sweep the WebUI listener port so GPU-switch restarts are deterministic.
	        try {
	            AddonPortHelper.TryKillProcessesOnPort(WebUiHttpPort);
	        } catch (Exception e) {
	            UnityEngine.Debug.LogWarning($"[LaunchWebUI] Could not run port cleanup on {WebUiHttpPort}: {e.Message}");
	        }
	        if (!triedPidClose) {
	            UnityEngine.Debug.Log("[LaunchWebUI] No tracked WebUI PID; performed port-based cleanup before relaunch.");
	        }
	        _lastLaunchedWebUiPid = 0;
#endif
	    }

	    /// <summary>Record the PID of the WebUI launcher we just started so we can close it on next restart.</summary>
	    public static void SetLastLaunchedWebUiPid(uint pid) {
	        _lastLaunchedWebUiPid = pid;
	    }

	    const string SdLoadingStickyMsg = "Diffusion Model is loading";

	    /// <summary>Show "loading" status and then "ready" when Connection_MGR reports SD connected.</summary>
	    public static void NotifyWebUiLaunchStarted() {
	        if (instance == null) return;
	        instance.BeginWebUiReadyWait();
	    }

	    void BeginWebUiReadyWait() {
	        ShowSdLoadingNotification();
	        // If SD was already connected when restart started, do not declare "ready" until
	        // we observe an actual disconnect->reconnect cycle for this launch.
	        _requireDisconnectBeforeReady = Connection_MGR.is_sd_connected;
	        _openBrowserWhenReadyRequested = !_suppressBrowserOpenForCurrentLaunch
	            && UnityEngine.PlayerPrefs.GetInt("WebUI_OpenBrowserOnStartup", 0) == 1;
	        // Consume per-launch suppress so a later Restart WebUI / manual launch can open the browser.
	        _suppressBrowserOpenForCurrentLaunch = false;
	        if (_waitForWebUiReady_crtn != null) {
	            StopCoroutine(_waitForWebUiReady_crtn);
	            _waitForWebUiReady_crtn = null;
	        }
	        _isWaitingForWebUiReady = false;
	        _waitForWebUiReady_crtn = StartCoroutine(WaitForWebUiReadyCoroutine());
	    }

	    void ShowSdLoadingNotification() {
	        if (Viewport_StatusText.instance == null) return;
	        // Keep visible beyond the short status fade — Forge often takes 30–120s.
	        Viewport_StatusText.instance.ShowStatusText(SdLoadingStickyMsg, false, 120f, true);
	        Viewport_StatusText.instance.ShowStickyMsg(SdLoadingStickyMsg, new Color(1f, 0.85f, 0.35f, 1f));
	    }

	    void ClearSdLoadingNotification(string readyOrFailMsg, bool ok) {
	        if (Viewport_StatusText.instance == null) return;
	        Viewport_StatusText.instance.StopStickyMsg(SdLoadingStickyMsg);
	        Viewport_StatusText.instance.ShowStatusText(readyOrFailMsg, false, ok ? 4f : 8f, false);
	    }

	    IEnumerator WaitForWebUiReadyCoroutine() {
	        _isWaitingForWebUiReady = true;
	        float timeoutSec = 180f;
	        float elapsed = 0f;
	        // Restart path can momentarily keep stale "connected" state from the previous process.
	        // Prefer disconnect->reconnect. Only use sustained-connected fallback when we did not
	        // start from an already-connected state.
	        bool sawDisconnected = !Connection_MGR.is_sd_connected;
	        const float warmupSec = 1.5f; // avoid immediate false-positive on stale connection state
	        const float connectedFallbackSec = 6f; // only for cold-start style launches
	        float connectedSince = -1f;
	        int connectedStreak = 0;
	        const int readyDebounceTicks = 3; // 3 * 0.5s = 1.5s stable connected before announcing ready
	        while (elapsed < timeoutSec) {
	            bool connected = Connection_MGR.is_sd_connected;
	            if (!connected) {
	                sawDisconnected = true;
	                connectedSince = -1f;
	                connectedStreak = 0;
	            } else if (sawDisconnected && elapsed >= warmupSec) {
	                connectedStreak++;
	                if (connectedStreak >= readyDebounceTicks) {
	                    ClearSdLoadingNotification("Stable Diffusion is ready.", true);
	                    TryOpenBrowserWhenReady();
	                    _isWaitingForWebUiReady = false;
	                    _waitForWebUiReady_crtn = null;
	                    yield break;
	                }
	            } else if (!_requireDisconnectBeforeReady && !sawDisconnected && elapsed >= warmupSec) {
	                if (connectedSince < 0f) connectedSince = elapsed;
	                if (elapsed - connectedSince >= connectedFallbackSec) {
	                    connectedStreak++;
	                    if (connectedStreak >= readyDebounceTicks) {
	                        ClearSdLoadingNotification("Stable Diffusion is ready.", true);
	                        TryOpenBrowserWhenReady();
	                        _isWaitingForWebUiReady = false;
	                        _waitForWebUiReady_crtn = null;
	                        yield break;
	                    }
	                }
	            } else {
	                connectedStreak = 0;
	            }
	            // Re-assert loading status every ~20s so it is not lost under other viewport messages.
	            if (Mathf.FloorToInt(elapsed) % 20 == 0 && Mathf.Approximately(elapsed % 20f, 0f) && elapsed > 0.5f)
	                ShowSdLoadingNotification();
	            yield return new WaitForSecondsRealtime(0.5f);
	            elapsed += 0.5f;
	        }
	        string msg = _requireDisconnectBeforeReady
	            ? "Stable Diffusion restart not confirmed yet... (waiting for reconnect on :" + WebUiHttpPort + ")"
	            : "Stable Diffusion still loading... (ping http://127.0.0.1:" + WebUiHttpPort + " / check connection port)";
	        ClearSdLoadingNotification(msg, false);
	        _isWaitingForWebUiReady = false;
	        _waitForWebUiReady_crtn = null;
	    }

	    void TryOpenBrowserWhenReady() {
	        // Live Settings win at ready time (toggle during wait must apply without app restart).
	        if (UnityEngine.PlayerPrefs.GetInt("WebUI_OpenBrowserOnStartup", 0) == 0) {
	            _openBrowserWhenReadyRequested = false;
	            return;
	        }
	        _openBrowserWhenReadyRequested = false;
	        OpenWebUiInBrowserNow();
	    }

	    void OpenWebUiInBrowserNow() {
	        const string webUiUrl = "http://127.0.0.1:7860";
	        try {
	            Application.OpenURL(webUiUrl);
	            UnityEngine.Debug.Log($"[LaunchWebUI] Opened browser for WebUI: {webUiUrl}");
	        } catch (Exception e) {
	            UnityEngine.Debug.LogWarning($"[LaunchWebUI] Could not open WebUI browser URL: {e.Message}");
	        }
	    }

	    /// <summary>
	    /// Settings → Open WebUI in browser: apply without restarting the app.
	    /// ON while waiting for ready → open when connected; ON while already connected → open now; OFF → cancel pending open.
	    /// </summary>
	    public static void ApplyOpenBrowserSettingInSession(bool wantOpen) {
	        if (instance == null) {
	            if (wantOpen && Connection_MGR.is_sd_connected) {
	                try { Application.OpenURL("http://127.0.0.1:7860"); } catch { /* best-effort */ }
	            }
	            return;
	        }
	        if (!wantOpen) {
	            instance._openBrowserWhenReadyRequested = false;
	            return;
	        }
	        if (instance._isWaitingForWebUiReady) {
	            instance._openBrowserWhenReadyRequested = true;
	            return;
	        }
	        if (Connection_MGR.is_sd_connected)
	            instance.OpenWebUiInBrowserNow();
	    }

	    /// <summary>
	    /// Settings → Show external process windows: apply without restarting the app.
	    /// Prefer ShowWindow/HideWindow on live PIDs; if show is requested but processes were launched with CREATE_NO_WINDOW
	    /// (no HWND), relaunch WebUI when safe (not generating). Addon server is handled by <see cref="Addon_MGR"/>.
	    /// </summary>
	    public static void ApplyExternalProcessWindowsSettingInSession(bool wantShow) {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
	        var pids = new System.Collections.Generic.HashSet<uint>();
	        if (_lastLaunchedWebUiPid != 0)
	            pids.Add(_lastLaunchedWebUiPid);
	        foreach (uint p in AddonPortHelper.TryGetListeningPidsOnPort(WebUiHttpPort))
	            pids.Add(p);

	        int touched = StartExternalProcess.TrySetWindowsVisibleForProcessIds(pids, wantShow);
	        UnityEngine.Debug.Log($"[LaunchWebUI] In-session external windows {(wantShow ? "show" : "hide")}: touched={touched}, pids={pids.Count}");

	        if (wantShow && touched == 0 && pids.Count > 0) {
	            // CREATE_NO_WINDOW → no HWND to raise. Relaunch with visible console when not mid-gen.
	            bool busy = GenerateButtons_UI.isGenerating;
	            if (busy) {
	                if (Viewport_StatusText.instance != null)
	                    Viewport_StatusText.instance.ShowStatusText(
	                        "Show external windows saved — restart WebUI after generation to open the console.", false, 5f, false);
	            } else if (instance != null) {
	                if (Viewport_StatusText.instance != null)
	                    Viewport_StatusText.instance.ShowStatusText(
	                        "Restarting WebUI so the console can appear…", false, 4f, false);
	                bool suppressBrowser = UnityEngine.PlayerPrefs.GetInt("WebUI_OpenBrowserOnStartup", 0) == 0;
	                instance.LaunchWebui_Manually(printStatusText_ifNotFound: false, suppressBrowserOpenForThisLaunch: suppressBrowser);
	            }
	        }
#endif
	        if (Addon_MGR.instance != null)
	            Addon_MGR.instance.ApplyExternalProcessWindowsSettingInSession(wantShow);
	    }

	    /// <summary>Cached result of nvidia-smi query for Settings UI to show "0: Name0, 1: Name1". Empty if not yet queried or nvidia-smi failed.</summary>
	    static string _cudaDeviceListString = null;

	    /// <summary>Query nvidia-smi for CUDA devices; returns e.g. "0: Tesla T10, 1: RTX 3080". Cached. Call from main thread or before UI use.</summary>
	    public static string GetCudaDeviceListString() {
	        if (_cudaDeviceListString != null)
	            return _cudaDeviceListString;
#if !UNITY_STANDALONE_WIN && !UNITY_EDITOR_WIN
	        return "";
#else
	        try {
	            string stdout;
#if UNITY_EDITOR
	            var si = new ProcessStartInfo {
	                FileName = "nvidia-smi",
	                Arguments = "--query-gpu=index,name --format=csv,noheader,nounits",
	                CreateNoWindow = true,
	                UseShellExecute = false,
	                RedirectStandardOutput = true,
	                RedirectStandardError = true
	            };
	            using (var p = Process.Start(si)) {
	                if (p == null) { _cudaDeviceListString = ""; return ""; }
	                stdout = p.StandardOutput?.ReadToEnd() ?? "";
	                p.WaitForExit(3000);
	                if (p.ExitCode != 0) { _cudaDeviceListString = ""; return ""; }
	            }
#else
	            // IL2CPP: Process.Start triggers Process::CreateProcess_internal assertion. Run via cmd and capture to temp file.
	            string tempFile = Path.Combine(Path.GetTempPath(), "spz_nvidia_smi_" + System.Guid.NewGuid().ToString("N") + ".txt");
	            string cmd = "nvidia-smi --query-gpu=index,name --format=csv,noheader,nounits > \"" + tempFile + "\"";
	            string workDir = Path.GetTempPath();
	            uint pid = StartExternalProcess.Run_Bat_or_Shortcut_or_Command(cmd, isJustFile: false, workDir, keepWindow: false, hidden: true, attachToConsole: false);
	            if (pid == 0) { _cudaDeviceListString = ""; return ""; }
	            StartExternalProcess.WaitForProcessExit(pid, 3000);
	            stdout = File.Exists(tempFile) ? File.ReadAllText(tempFile) : "";
	            try { File.Delete(tempFile); } catch { }
#endif
	            var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
	            var parts = new System.Collections.Generic.List<string>();
	            foreach (var line in lines) {
	                var trimmed = line.Trim();
	                if (string.IsNullOrEmpty(trimmed)) continue;
	                int firstComma = trimmed.IndexOf(',');
	                if (firstComma >= 0) {
	                    string idx = trimmed.Substring(0, firstComma).Trim();
	                    string name = trimmed.Substring(firstComma + 1).Trim();
	                    if (!string.IsNullOrEmpty(name)) parts.Add(idx + ": " + name);
	                }
	            }
	            _cudaDeviceListString = parts.Count > 0 ? string.Join(", ", parts) : "";
	            return _cudaDeviceListString;
	        } catch (Exception e) {
	            UnityEngine.Debug.LogWarning($"[LaunchWebUI] Could not query nvidia-smi for CUDA devices: {e.Message}");
	            _cudaDeviceListString = "";
	            return "";
	        }
#endif
	    }

	    /// <summary>Write the selected SD GPU device id to the Forge folder. Single programmable location: run_noQuickEdit.bat can read it with "set /p SPZ_DEVICE=&lt;spz_sd_device.txt" and "set COMMANDLINE_ARGS=--api --gpu-device-id %SPZ_DEVICE%" to use the same device when launching outside Unity.</summary>
	    public static void WriteSdDeviceToForgeFolder(string forgeWorkingDir, int gpuId) {
	        if (string.IsNullOrEmpty(forgeWorkingDir) || gpuId < 0) return;
	        try {
	            string path = Path.Combine(forgeWorkingDir, "spz_sd_device.txt");
	            File.WriteAllText(path, gpuId.ToString());
	        } catch (Exception e) {
	            UnityEngine.Debug.LogWarning($"[LaunchWebUI] Could not write spz_sd_device.txt: {e.Message}");
	        }
	    }

    const string EnvVarWebuiPath = "SPZ_WEBUI_RUN_PATH";

    /// <summary>Aggressive search: env var, then exe dir + dataPath + CurrentDirectory, each up to MaxParentDepth levels.</summary>
    string GetWebuiFilePath(bool printStatusText_ifNotFound = false) {
        return GetWebuiFilePathAggressive(printStatusText_ifNotFound);
    }

    /// <summary>Static aggressive search so it works without the component. Env var first, then multiple roots, 10 levels each.</summary>
    public static string GetWebuiFilePathStatic(bool printStatusTextIfNotFound = false) {
        return GetWebuiFilePathAggressiveStatic(printStatusTextIfNotFound);
    }

    static string GetWebuiFilePathAggressive(bool printStatusText_ifNotFound) {
        return GetWebuiFilePathAggressiveStatic(printStatusText_ifNotFound);
    }

    static string GetWebuiFilePathAggressiveStatic(bool printStatusText_ifNotFound) {
        try {
            string envPath = Environment.GetEnvironmentVariable(EnvVarWebuiPath);
            if (!string.IsNullOrWhiteSpace(envPath)) {
                string trimmed = envPath.Trim();
                if (File.Exists(trimmed)) {
                    UnityEngine.Debug.Log($"[LaunchWebUI] Found via {EnvVarWebuiPath}: {trimmed}");
                    return trimmed;
                }
            }
        } catch (Exception e) {
            UnityEngine.Debug.LogWarning($"[LaunchWebUI] Env check failed: {e.Message}");
        }

        var roots = new System.Collections.Generic.List<string>();
        try {
            string exeDir = Directory.GetParent(Application.dataPath).FullName;
            if (!string.IsNullOrEmpty(exeDir)) roots.Add(Path.GetFullPath(exeDir));
        } catch { }
        try {
            if (!string.IsNullOrEmpty(Application.dataPath)) roots.Add(Path.GetFullPath(Application.dataPath));
        } catch { }
        try {
            string cur = Environment.CurrentDirectory;
            if (!string.IsNullOrEmpty(cur)) roots.Add(Path.GetFullPath(cur));
        } catch { }

        UnityEngine.Debug.Log($"[LaunchWebUI] Application.dataPath = {Application.dataPath}");
        UnityEngine.Debug.Log($"[LaunchWebUI] Search roots: " + string.Join(" | ", roots));

        var checkedPaths = new System.Collections.Generic.List<string>();
        foreach (string root in roots) {
            string currentDir = root;
            for (int depth = 0; depth < MaxParentDepth; depth++) {
                if (string.IsNullOrEmpty(currentDir)) break;
                string foundAtDepth = TryPickLaunchAmongCandidateFolders(currentDir, depth, checkedPaths, underBuildIl2Cpp: false);
                if (!string.IsNullOrEmpty(foundAtDepth))
                    return foundAtDepth;
                // Built player often lives under Build_IL2CPP/; empty repo-root forge stub must not hide that install.
                string buildIl2CppRoot = Path.Combine(currentDir, "Build_IL2CPP");
                if (Directory.Exists(buildIl2CppRoot)) {
                    string foundBuild = TryPickLaunchAmongCandidateFolders(buildIl2CppRoot, depth, checkedPaths, underBuildIl2Cpp: true);
                    if (!string.IsNullOrEmpty(foundBuild))
                        return foundBuild;
                }
                try {
                    var parent = Directory.GetParent(currentDir);
                    if (parent == null) break;
                    currentDir = parent.FullName;
                } catch { break; }
            }
        }

        string candidates = string.Join(" | ", WebuiCandidateFolderNames);
        string msg = $"[LaunchWebUI] Bat NOT FOUND. Roots: [{string.Join(", ", roots)}]. Up to {MaxParentDepth} levels each (also checks Build_IL2CPP/{{{candidates}}}). Set {EnvVarWebuiPath} to full bat path. Note: empty stub folders are skipped.";
        UnityEngine.Debug.LogWarning(msg);
        if (printStatusText_ifNotFound && Viewport_StatusText.instance != null)
            Viewport_StatusText.instance.ShowStatusText(msg, false, 10, false);
        return "";
    }

    /// <summary>Candidate folder names under <paramref name="parentDir"/> in Neo-first order (pure helper for tests).</summary>
    public static string[] GetCandidateWebuiDirsUnder(string parentDir) {
        if (string.IsNullOrEmpty(parentDir))
            return System.Array.Empty<string>();
        var dirs = new string[WebuiCandidateFolderNames.Length];
        for (int i = 0; i < WebuiCandidateFolderNames.Length; i++)
            dirs[i] = Path.Combine(parentDir, WebuiCandidateFolderNames[i]);
        return dirs;
    }

    /// <summary>Resolve a launch file under one parent directory (Neo-first). Public for EditMode contracts / forge-neo-swap R1.</summary>
    public static string TryResolveLaunchFileUnderParent(string parentDir) {
        return TryPickLaunchAmongCandidateFolders(parentDir, depth: 0, checkedPaths: null, underBuildIl2Cpp: false);
    }

    static string TryPickLaunchAmongCandidateFolders(
        string parentDir,
        int depth,
        System.Collections.Generic.List<string> checkedPaths,
        bool underBuildIl2Cpp) {
        if (string.IsNullOrEmpty(parentDir)) return "";
        foreach (string folderName in WebuiCandidateFolderNames) {
            string forgeDir = Path.Combine(parentDir, folderName);
            bool exists = false;
            try { exists = Directory.Exists(forgeDir); } catch { }
            string label = underBuildIl2Cpp ? $"Build_IL2CPP/{folderName}" : folderName;
            if (!exists) {
                UnityEngine.Debug.Log($"[LaunchWebUI] Search [{depth}] {forgeDir} → not found");
                continue;
            }
            string found = TryPickLaunchFileInForgeDir(forgeDir, checkedPaths);
            if (!string.IsNullOrEmpty(found)) {
                UnityEngine.Debug.Log($"[LaunchWebUI] Search [{depth}] {label} → FOUND: {found}");
                return found;
            }
            UnityEngine.Debug.LogWarning(
                $"[LaunchWebUI] Search [{depth}] {forgeDir} → EMPTY (folder exists but no run_noQuickEdit.bat / run.bat / .lnk). Skipping stub.");
        }
        return "";
    }

    static string TryPickLaunchFileInForgeDir(string forgeDir, System.Collections.Generic.List<string> checkedPaths) {
        if (string.IsNullOrEmpty(forgeDir)) return "";
        foreach (string name in WebuiLaunchFileNames) {
            try {
                string full = Path.GetFullPath(Path.Combine(forgeDir, name));
                if (checkedPaths != null)
                    checkedPaths.Add(full);
                if (File.Exists(full))
                    return full;
            } catch { }
        }
        return "";
    }


	    /// <summary>Returns path to launch (or a wrapper that sets COMMANDLINE_ARGS and runs launch.py so GPU id is applied). Forge reads args from COMMANDLINE_ARGS, not from bat arguments. Public so RestartTheWebui can apply GPU when user restarts from UI.</summary>
	    /// <param name="preferNoConsole">When true, use start /B for .lnk fallbacks so no extra console box appears. Forge itself always uses python.exe; window hide is CREATE_NO_WINDOW.</param>
	    public static string GetLaunchPathWithGpuSetting(string webuiFilePath, out string workingDir, bool preferNoConsole = true) {
	        string SplitLaunchPathAndExtraArgs(string raw, out string extraArgs) {
	            extraArgs = "";
	            if (string.IsNullOrWhiteSpace(raw))
	                return raw;
	            string t = raw.Trim();
	            // If it's quoted path + args: "C:\...\run.bat" --precision full
	            if (t.StartsWith("\"")) {
	                int q2 = t.IndexOf('"', 1);
	                if (q2 > 1) {
	                    string p = t.Substring(1, q2 - 1);
	                    extraArgs = t.Substring(q2 + 1).Trim();
	                    return p;
	                }
	            }
	            // If unquoted path + args, split after known launch extensions.
	            string[] exts = { ".bat", ".cmd", ".lnk", ".exe", ".py" };
	            foreach (string ext in exts) {
	                int i = t.IndexOf(ext + " ", StringComparison.OrdinalIgnoreCase);
	                if (i > 0) {
	                    int end = i + ext.Length;
	                    extraArgs = t.Substring(end).Trim();
	                    return t.Substring(0, end).Trim();
	                }
	            }
	            return t;
	        }

	        webuiFilePath = SplitLaunchPathAndExtraArgs(webuiFilePath, out string launchExtraArgs);
	        var parent = Directory.GetParent(webuiFilePath);
	        workingDir = parent != null ? parent.FullName : Path.GetDirectoryName(webuiFilePath) ?? "";
	        // Settings (PlayerPrefs) always wins when user has set a device (>= 0). File is fallback when Settings is "default" (-1) so external .bat can set device.
	        int gpuId = UnityEngine.PlayerPrefs.GetInt("SD_GPU_DeviceId", -1);
	        string deviceFile = Path.Combine(workingDir, "spz_sd_device.txt");
	        if (gpuId < 0 && File.Exists(deviceFile)) {
	            try {
	                string s = File.ReadAllText(deviceFile).Trim();
	                if (int.TryParse(s, out int fileId) && fileId >= 0) gpuId = fileId;
	            } catch { }
	        }
	        if (gpuId >= 0)
	            gpuId = Mathf.Clamp(gpuId, 0, 31);
	        UnityEngine.Debug.Log($"[LaunchWebUI] SD_GPU_DeviceId = {gpuId} (from Settings; file used only when Settings = default).");
	        if (gpuId >= 0)
	            WriteSdDeviceToForgeFolder(workingDir, gpuId);
	        // Forge layout: launch.py and venv live under {root}/webui/. Also check {root}/ for non-standard setups.
	        string launchPy = Path.Combine(workingDir, "webui", "launch.py");
	        if (!File.Exists(launchPy))
	            launchPy = Path.Combine(workingDir, "launch.py");
	        string pythonExe = Path.Combine(workingDir, "webui", "venv", "Scripts", "python.exe");
	        if (!File.Exists(pythonExe))
	            pythonExe = Path.Combine(workingDir, "venv", "Scripts", "python.exe");
	        if (!File.Exists(pythonExe))
	            pythonExe = Path.Combine(workingDir, "system", "python", "python.exe");
	        // Always launch Forge with python.exe. Visibility is controlled by CREATE_NO_WINDOW / SW_HIDE on the
	        // wrapper — pythonw.exe was a common silent-fail (PID then no :7860 listener) when Settings hide windows.
	        string pythonLaunch = pythonExe;
	        // When user picked an SD GPU in Settings, pass both:
	        // 1) CUDA_VISIBLE_DEVICES (hard mask)
	        // 2) --gpu-device-id (Forge CLI hint)
	        // This avoids ambiguous fallback to GPU 0 across different Forge launch paths.
	        // Pin Gradio/API to WebUiHttpPort so launch matches ConnectionPanel ping target.
	        // Neo warns on py≠3.13 / torch pin — skip checks (host uses proven 3.11 + cu128).
	        const string neoSkipChecks = " --skip-python-version-check --skip-version-check";
	        string argsBase = gpuId >= 0
	            ? ("--api --port " + WebUiHttpPort + " --gpu-device-id " + gpuId + neoSkipChecks)
	            : ("--api --port " + WebUiHttpPort + neoSkipChecks);
	        string args = string.IsNullOrWhiteSpace(launchExtraArgs) ? argsBase : (argsBase + " " + launchExtraArgs);
	        bool hasLaunchPy = File.Exists(launchPy);
	        bool hasVenvPython = File.Exists(pythonExe);
	        bool canDirectLaunch = hasLaunchPy;
	        if (canDirectLaunch) {
	            try {
	                string wrapperPath = Path.Combine(Path.GetTempPath(), "spz_webui_gpu_wrapper.bat");
	                string launchDir = Path.GetDirectoryName(launchPy) ?? workingDir;
	                string envBat = Path.Combine(workingDir, "environment.bat");
	                string envLine = File.Exists(envBat) ? "call \"" + envBat.Replace("\"", "\"\"") + "\"\r\n" : "";
	                // webui-user.bat forces "--api --gpu-device-id 0"; bypassing run.bat/webui-user.bat avoids always locking to physical GPU 0 when Settings = default (-1).
	                string cudaLine = gpuId >= 0 ? ("set CUDA_DEVICE_ORDER=PCI_BUS_ID\r\nset CUDA_VISIBLE_DEVICES=" + gpuId + "\r\n") : "";
	                string pythonCmd = hasVenvPython ? ("\"" + pythonLaunch.Replace("\"", "\"\"") + "\"") : "python";
	                string content = "@echo off\r\n" + SpzWebuiNoBrowserEnv_bat
	                    + "set COMMANDLINE_ARGS=" + args + "\r\nset REDUCE_DISPLAY_GPU_LOAD=1\r\n" + envLine + cudaLine + "cd /d \"" + launchDir.Replace("\"", "\"\"") + "\"\r\n" + pythonCmd + " \"" + launchPy.Replace("\"", "\"\"") + "\"\r\n";
	                File.WriteAllText(wrapperPath, content);
	                if (gpuId >= 0)
	                    UnityEngine.Debug.Log($"[LaunchWebUI] Direct launch.py with SD GPU={gpuId}, CUDA_VISIBLE_DEVICES={gpuId}, COMMANDLINE_ARGS='{args}', python={(hasVenvPython ? Path.GetFileName(pythonLaunch) : "PATH")}. Wrapper: {wrapperPath}");
	                else
	                    UnityEngine.Debug.Log($"[LaunchWebUI] Direct launch.py (default GPU; webui-user.bat bypassed), python={(hasVenvPython ? Path.GetFileName(pythonLaunch) : "PATH")}. Wrapper: {wrapperPath}");
	                workingDir = Path.GetTempPath();
	                return wrapperPath;
	            } catch (Exception e) {
	                UnityEngine.Debug.LogWarning($"[LaunchWebUI] Could not create direct-launch wrapper: {e.Message}");
	            }
	        }
	        UnityEngine.Debug.LogWarning($"[LaunchWebUI] Direct launch path unavailable (launch.py exists={hasLaunchPy}, venv python exists={hasVenvPython}). Falling back to bat/lnk path.");
	        if (gpuId < 0) {
	            try {
	                // Raw bat would start Grado with the default in-browser tab; only wrap to inject GRADIO_INBROWSER=0.
	                string wrapperPathPass = Path.Combine(Path.GetTempPath(), "spz_webui_nobrowser_call.bat");
	                string extPass = Path.GetExtension(webuiFilePath).ToLowerInvariant();
	                // start title must be ""; /B runs without a new window when hiding.
	                string callLinePass = (extPass == ".lnk")
	                    ? (preferNoConsole
	                        ? "start \"\" /B \"" + webuiFilePath.Replace("\"", "\"\"") + "\""
	                        : "start \"\" \"" + webuiFilePath.Replace("\"", "\"\"") + "\"")
	                    : "call \"" + webuiFilePath.Replace("\"", "\"\"") + "\"";
	                string contentPass = "@echo off\r\n" + SpzWebuiNoBrowserEnv_bat + "cd /d \"" + workingDir.Replace("\"", "\"\"") + "\"\r\n" + callLinePass + "\r\n";
	                File.WriteAllText(wrapperPathPass, contentPass);
	                workingDir = Path.GetTempPath();
	                UnityEngine.Debug.Log($"[LaunchWebUI] No direct launch: using Gradio in-browser=0 call wrapper: {webuiFilePath}");
	                return wrapperPathPass;
	            } catch (Exception e0) {
	                UnityEngine.Debug.LogWarning($"[LaunchWebUI] Could not create no-browser call wrapper, falling back to direct bat: {e0.Message}");
	                return webuiFilePath;
	            }
	        }
	        try {
	            // Fallback when venv/launch.py missing: set env then call their bat (may still run webui-user.bat).
	            string wrapperPath2 = Path.Combine(Path.GetTempPath(), "spz_webui_gpu_wrapper.bat");
	            string ext = Path.GetExtension(webuiFilePath).ToLowerInvariant();
	            string callLine = (ext == ".lnk")
	                ? (preferNoConsole
	                    ? "start \"\" /B \"" + webuiFilePath.Replace("\"", "\"\"") + "\""
	                    : "start \"\" \"" + webuiFilePath.Replace("\"", "\"\"") + "\"")
	                : "call \"" + webuiFilePath.Replace("\"", "\"\"") + "\"";
	            if (ext == ".lnk")
	                UnityEngine.Debug.Log($"[LaunchWebUI] Using GPU {gpuId} (CUDA_VISIBLE_DEVICES; launch.py/venv not found for direct launch).");
	            string content2 = "@echo off\r\n" + SpzWebuiNoBrowserEnv_bat
	                + "set CUDA_DEVICE_ORDER=PCI_BUS_ID\r\nset CUDA_VISIBLE_DEVICES=" + gpuId + "\r\nset COMMANDLINE_ARGS=" + args + "\r\ncd /d \"" + workingDir.Replace("\"", "\"\"") + "\"\r\n" + callLine + "\r\n";
	            File.WriteAllText(wrapperPath2, content2);
	            workingDir = Path.GetTempPath();
	            return wrapperPath2;
	        } catch (Exception e) {
	            UnityEngine.Debug.LogWarning($"[LaunchWebUI] Could not create GPU wrapper, launching without GPU override: {e.Message}");
	            return webuiFilePath;
	        }
	    }

	    string GetLaunchPathAndWorkingDir(string webuiFilePath, out string workingDir) {
	        // Hide console when Settings say so (default off = hide).
	        bool preferNoConsole = !PrefsWantShowExternalProcessWindows();
	        return GetLaunchPathWithGpuSetting(webuiFilePath, out workingDir, preferNoConsole);
	    }

	    /// <returns>True if a WebUI process was started (PID != 0).</returns>
	    public bool LaunchWebui_Manually(bool printStatusText_ifNotFound = false, bool suppressBrowserOpenForThisLaunch = false) {
	        _suppressBrowserOpenForCurrentLaunch = suppressBrowserOpenForThisLaunch;
	        ShowSdLoadingNotification();
	        string filePath = GetWebuiFilePath(printStatusText_ifNotFound);
	        if (string.IsNullOrEmpty(filePath)) {
	            UnityEngine.Debug.Log("[LaunchWebUI] No bat file path; skipping launch (see above for search path).");
	            ClearSdLoadingNotification(
	                "Stable Diffusion bat not found next to the EXE (sd-webui-forge-neo or stable-diffusion-webui-forge). Set SPZ_WEBUI_RUN_PATH (e.g. true Neo under FORGE_NEO_TRUE_REPO\\neo).",
	                false);
	            _suppressBrowserOpenForCurrentLaunch = false;
	            return false;
	        }

	        UnityEngine.Debug.Log($"[LaunchWebUI] Bat found, launching: {filePath}");
	        TryCloseLastLaunchedWebUi();

	        string workingDir;
	        string launchPath = GetLaunchPathAndWorkingDir(filePath, out workingDir);
	        if (string.IsNullOrEmpty(workingDir))
	            workingDir = Path.GetDirectoryName(filePath);
	        if (string.IsNullOrEmpty(workingDir))
	            workingDir = Path.GetTempPath();

	        try {
	            // Default first-run: hide (ShowExternalProcessWindows = 0). Toggle ON shows the Forge console.
	            bool showExternalWindows = PrefsWantShowExternalProcessWindows();
	            // keepWindow must stay false: /K leaves a zombie CMD after Forge exits and falsifies IsProcessRunning.
	            // Visibility is only hidden/CREATE_NO_WINDOW vs CREATE_NEW_CONSOLE.
	            uint pid = StartExternalProcess.Run_Bat_or_Shortcut_or_Command(
	                launchPath,
	                isJustFile: true,
	                workingDir,
	                keepWindow: false,
	                hidden: !showExternalWindows,
	                attachToConsole: false
	            );
	            if (pid != 0) {
	                SetLastLaunchedWebUiPid(pid);
	                UnityEngine.Debug.Log($"[LaunchWebUI] Process launched successfully with PID: {pid} (showWindow={showExternalWindows})");
	                NotifyWebUiLaunchStarted();
	                return true;
	            }
	            UnityEngine.Debug.LogError("[LaunchWebUI] Failed to launch process (PID 0).");
	            ClearSdLoadingNotification("Stable Diffusion failed to launch (process PID 0).", false);
	            _suppressBrowserOpenForCurrentLaunch = false;
	            return false;
	        } catch (Exception e) {
	            UnityEngine.Debug.LogError($"[LaunchWebUI] Error launching process: {e.Message}");
	            ClearSdLoadingNotification("Stable Diffusion launch error: " + e.Message, false);
	            _suppressBrowserOpenForCurrentLaunch = false;
	            return false;
	        }
	    }

	    /// <summary>When the exe runs, auto-launch WebUI. Aggressive retries at 0.5s, 1.5s, 3s, 6s, 12s until bat is found and launched.</summary>
	    void Start() {
#if UNITY_EDITOR
	        UnityEngine.Debug.LogWarning(
	            "[LaunchWebUI] Auto-launch skipped in Unity Editor (by design). Use a built player, or Settings → Start WebUI / Top menu Launch SD.");
	        return;
#endif
	        UnityEngine.Debug.Log("[LaunchWebUI] Auto-launch aggressive: retries at 0.5s, 1.5s, 3s, 6s, 12s (window visibility from Settings; Unity OpenURL only if open-browser is on).");
	        StartCoroutine(AggressiveAutoLaunchLoop());
	    }

	    IEnumerator AggressiveAutoLaunchLoop() {
	        bool showStatus = true;
	        bool launchedOnce = false;
	        for (int i = 0; i < AutoLaunchRetryDelays.Length; i++) {
	            yield return new WaitForSecondsRealtime(AutoLaunchRetryDelays[i]);
	            if (Connection_MGR.is_sd_connected)
	                yield break;
	            if (_lastLaunchedWebUiPid != 0 && StartExternalProcess.IsProcessRunning(_lastLaunchedWebUiPid))
	                yield break;
	            // Wrapper cmd often exits immediately after spawning python. A relaunch would call
	            // TryCloseLastLaunchedWebUi → kill :7860 and abort a healthy Forge still loading.
	            if (launchedOnce) {
	                UnityEngine.Debug.Log(
	                    "[LaunchWebUI] Prior auto-launch handed off (launcher PID gone); waiting for SD without relaunch/port-kill.");
	                continue;
	            }
	            if (i > 0)
	                UnityEngine.Debug.Log($"[LaunchWebUI] Retry {i + 1}/{AutoLaunchRetryDelays.Length} (after {AutoLaunchRetryDelays[i]}s).");
	            try {
	                // Honor Settings only (default = hide). Never force a visible console on auto-start.
	                bool suppressBrowser = UnityEngine.PlayerPrefs.GetInt("WebUI_OpenBrowserOnStartup", 0) == 0;
	                if (LaunchWebui_Manually(showStatus, suppressBrowserOpenForThisLaunch: suppressBrowser))
	                    launchedOnce = true;
	            } catch (Exception e) {
	                UnityEngine.Debug.LogError($"[LaunchWebUI] Auto-launch attempt failed: {e.Message}");
	                ClearSdLoadingNotification("Stable Diffusion auto-launch failed: " + e.Message, false);
	                _lastLaunchedWebUiPid = 0;
	            }
	            if (_lastLaunchedWebUiPid != 0) {
	                yield return new WaitForSecondsRealtime(2f);
	                if (Connection_MGR.is_sd_connected)
	                    yield break;
	                if (StartExternalProcess.IsProcessRunning(_lastLaunchedWebUiPid))
	                    yield break;
	                // Hand-off: python may still be starting. Keep launchedOnce so later iterations wait only.
	                UnityEngine.Debug.LogWarning(
	                    $"[LaunchWebUI] Auto-launch PID {_lastLaunchedWebUiPid} exited before SD connected — waiting (no kill/relaunch).");
	                _lastLaunchedWebUiPid = 0;
	            }
	        }
	    }

	    void Awake() {
	        if (instance != null) { DestroyImmediate(this); return; }
	        instance = this;
	        UnityEngine.Debug.Log("[LaunchWebUI] Awake: instance set. Aggressive auto-launch (run_noQuickEdit.bat) will run from Start().");
	    }
	}
}//end namespace

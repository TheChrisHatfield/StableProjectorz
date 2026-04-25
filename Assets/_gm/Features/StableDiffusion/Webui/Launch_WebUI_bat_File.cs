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
	/// </summary>
	public class LaunchWebUIBatFile : MonoBehaviour{
	    public static LaunchWebUIBatFile instance { get; private set; } = null;

	    public const string WebuiFolderName = "stable-diffusion-webui-forge";
	    public static readonly string[] WebuiLaunchFileNames = new string[] { "run_noQuickEdit.bat", "run.bat", "run_forge.bat", "run_noQuickEdit.lnk" };

	    /// <summary>Search up to this many parent levels from each root. Aggressive so bat is found even if exe is deep.</summary>
	    const int MaxParentDepth = 10;
	    /// <summary>Retry delays in seconds: first at 0.5s, then 1.5, 3, 6, 12.</summary>
	    static readonly float[] AutoLaunchRetryDelays = new float[] { 0.5f, 1.5f, 3f, 6f, 12f };

	    static uint _lastLaunchedWebUiPid;

	    /// <summary>Attempts to close the previously launched WebUI process (and its tree on Windows). Call before launching a new instance so the old window closes automatically.</summary>
	    public static void TryCloseLastLaunchedWebUi() {
	        if (_lastLaunchedWebUiPid == 0) return;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
	        if (!StartExternalProcess.IsProcessRunning(_lastLaunchedWebUiPid)) {
	            _lastLaunchedWebUiPid = 0;
	            return;
	        }
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
	        _lastLaunchedWebUiPid = 0;
#endif
	    }

	    /// <summary>Record the PID of the WebUI launcher we just started so we can close it on next restart.</summary>
	    public static void SetLastLaunchedWebUiPid(uint pid) {
	        _lastLaunchedWebUiPid = pid;
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
                string forgeDir = Path.Combine(currentDir, WebuiFolderName);
                bool exists = false;
                try { exists = Directory.Exists(forgeDir); } catch { }
                UnityEngine.Debug.Log($"[LaunchWebUI] Search [{depth}] {forgeDir} → {(exists ? "EXISTS" : "not found")}");
                if (exists) {
                    foreach (string name in WebuiLaunchFileNames) {
                        try {
                            string full = Path.GetFullPath(Path.Combine(forgeDir, name));
                            checkedPaths.Add(full);
                            if (File.Exists(full)) {
                                UnityEngine.Debug.Log($"[LaunchWebUI] FOUND: {full}");
                                return full;
                            }
                        } catch { }
                    }
                }
                try {
                    var parent = Directory.GetParent(currentDir);
                    if (parent == null) break;
                    currentDir = parent.FullName;
                } catch { break; }
            }
        }

        string msg = $"[LaunchWebUI] Bat NOT FOUND. Roots: [{string.Join(", ", roots)}]. Up to {MaxParentDepth} levels each. Set {EnvVarWebuiPath} to full bat path.";
        UnityEngine.Debug.LogWarning(msg);
        if (printStatusText_ifNotFound && Viewport_StatusText.instance != null)
            Viewport_StatusText.instance.ShowStatusText(msg, false, 10, false);
        return "";
    }


	    /// <summary>Returns path to launch (or a wrapper that sets COMMANDLINE_ARGS and runs launch.py so GPU id is applied). Forge reads args from COMMANDLINE_ARGS, not from bat arguments. Public so RestartTheWebui can apply GPU when user restarts from UI.</summary>
	    public static string GetLaunchPathWithGpuSetting(string webuiFilePath, out string workingDir) {
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
	        // When user picked an SD GPU in Settings, pass both:
	        // 1) CUDA_VISIBLE_DEVICES (hard mask)
	        // 2) --gpu-device-id (Forge CLI hint)
	        // This avoids ambiguous fallback to GPU 0 across different Forge launch paths.
	        string argsBase = gpuId >= 0 ? ("--api --gpu-device-id " + gpuId) : "--api";
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
	                string pythonCmd = hasVenvPython ? ("\"" + pythonExe.Replace("\"", "\"\"") + "\"") : "python";
	                string content = "@echo off\r\nset COMMANDLINE_ARGS=" + args + "\r\nset REDUCE_DISPLAY_GPU_LOAD=1\r\n" + envLine + cudaLine + "cd /d \"" + launchDir.Replace("\"", "\"\"") + "\"\r\n" + pythonCmd + " \"" + launchPy.Replace("\"", "\"\"") + "\"\r\n";
	                File.WriteAllText(wrapperPath, content);
	                if (gpuId >= 0)
	                    UnityEngine.Debug.Log($"[LaunchWebUI] Direct launch.py with SD GPU={gpuId}, CUDA_VISIBLE_DEVICES={gpuId}, COMMANDLINE_ARGS='{args}', python={(hasVenvPython ? "venv" : "PATH")}. Wrapper: {wrapperPath}");
	                else
	                    UnityEngine.Debug.Log($"[LaunchWebUI] Direct launch.py (default GPU; webui-user.bat bypassed), python={(hasVenvPython ? "venv" : "PATH")}. Wrapper: {wrapperPath}");
	                workingDir = Path.GetTempPath();
	                return wrapperPath;
	            } catch (Exception e) {
	                UnityEngine.Debug.LogWarning($"[LaunchWebUI] Could not create direct-launch wrapper: {e.Message}");
	            }
	        }
	        UnityEngine.Debug.LogWarning($"[LaunchWebUI] Direct launch path unavailable (launch.py exists={hasLaunchPy}, venv python exists={hasVenvPython}). Falling back to bat/lnk path.");
	        if (gpuId < 0)
	            return webuiFilePath;
	        try {
	            // Fallback when venv/launch.py missing: set env then call their bat (may still run webui-user.bat).
	            string wrapperPath2 = Path.Combine(Path.GetTempPath(), "spz_webui_gpu_wrapper.bat");
	            string ext = Path.GetExtension(webuiFilePath).ToLowerInvariant();
	            string callLine = (ext == ".lnk")
	                ? "start \"\" \"" + webuiFilePath.Replace("\"", "\"\"") + "\""
	                : "call \"" + webuiFilePath.Replace("\"", "\"\"") + "\"";
	            if (ext == ".lnk")
	                UnityEngine.Debug.Log($"[LaunchWebUI] Using GPU {gpuId} (CUDA_VISIBLE_DEVICES; launch.py/venv not found for direct launch).");
	            string content2 = "@echo off\r\nset CUDA_DEVICE_ORDER=PCI_BUS_ID\r\nset CUDA_VISIBLE_DEVICES=" + gpuId + "\r\nset COMMANDLINE_ARGS=" + args + "\r\ncd /d \"" + workingDir.Replace("\"", "\"\"") + "\"\r\n" + callLine + "\r\n";
	            File.WriteAllText(wrapperPath2, content2);
	            workingDir = Path.GetTempPath();
	            return wrapperPath2;
	        } catch (Exception e) {
	            UnityEngine.Debug.LogWarning($"[LaunchWebUI] Could not create GPU wrapper, launching without GPU override: {e.Message}");
	            return webuiFilePath;
	        }
	    }

	    string GetLaunchPathAndWorkingDir(string webuiFilePath, out string workingDir) {
	        return GetLaunchPathWithGpuSetting(webuiFilePath, out workingDir);
	    }

	    public void LaunchWebui_Manually(bool printStatusText_ifNotFound = false) {
	        string filePath = GetWebuiFilePath(printStatusText_ifNotFound);
	        if (string.IsNullOrEmpty(filePath)) {
	            UnityEngine.Debug.Log("[LaunchWebUI] No bat file path; skipping launch (see above for search path).");
	            return;
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
	            uint pid = StartExternalProcess.Run_Bat_or_Shortcut_or_Command(launchPath, isJustFile: true, workingDir, keepWindow: true, hidden: false, attachToConsole: false);
	            if (pid != 0) {
	                SetLastLaunchedWebUiPid(pid);
	                UnityEngine.Debug.Log($"[LaunchWebUI] Process launched successfully with PID: {pid}");
	            } else {
	                UnityEngine.Debug.LogError("[LaunchWebUI] Failed to launch process (PID 0).");
	            }
	        } catch (Exception e) {
	            UnityEngine.Debug.LogError($"[LaunchWebUI] Error launching process: {e.Message}");
	        }
	    }

	    /// <summary>When the exe runs, auto-launch WebUI. Aggressive retries at 0.5s, 1.5s, 3s, 6s, 12s until bat is found and launched.</summary>
	    void Start() {
#if UNITY_EDITOR
	        return;
#endif
	        UnityEngine.Debug.Log("[LaunchWebUI] Auto-launch aggressive: retries at 0.5s, 1.5s, 3s, 6s, 12s.");
	        StartCoroutine(AggressiveAutoLaunchLoop());
	    }

	    IEnumerator AggressiveAutoLaunchLoop() {
	        bool showStatus = true;
	        for (int i = 0; i < AutoLaunchRetryDelays.Length; i++) {
	            yield return new WaitForSecondsRealtime(AutoLaunchRetryDelays[i]);
	            if (_lastLaunchedWebUiPid != 0) yield break;
	            if (i > 0)
	                UnityEngine.Debug.Log($"[LaunchWebUI] Retry {i + 1}/{AutoLaunchRetryDelays.Length} (after {AutoLaunchRetryDelays[i]}s).");
	            try {
	                LaunchWebui_Manually(showStatus);
	            } catch (Exception e) {
	                UnityEngine.Debug.LogError($"[LaunchWebUI] Auto-launch attempt failed: {e.Message}");
	            }
	            if (_lastLaunchedWebUiPid != 0) yield break;
	        }
	    }

	    void Awake() {
	        if (instance != null) { DestroyImmediate(this); return; }
	        instance = this;
	        UnityEngine.Debug.Log("[LaunchWebUI] Awake: instance set. Aggressive auto-launch (run_noQuickEdit.bat) will run from Start().");
	    }
	}
}//end namespace

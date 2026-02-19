using System;
#if UNITY_EDITOR
using System.Diagnostics;
#endif
using System.IO;
using UnityEngine;
using Lavender.Systems;

namespace spz {

	public class LaunchWebUIBatFile : MonoBehaviour{
	    public static LaunchWebUIBatFile instance { get; private set; } = null;

	    /// <summary>Last PID from launching WebUI (bat or wrapper). Used to auto-close previous launcher when restarting (e.g. after GPU change).</summary>
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

    string GetWebuiFilePath( bool printStatusText_ifNotFound = false){
        // Fallback: environment variable (e.g. set locally or in editor for development)
        const string envVarName = "SPZ_WEBUI_RUN_PATH";
        try {
            string envPath = Environment.GetEnvironmentVariable(envVarName);
            if (!string.IsNullOrWhiteSpace(envPath)) {
                string trimmed = envPath.Trim();
                if (File.Exists(trimmed)) {
                    UnityEngine.Debug.Log($"Webui file found via {envVarName}: {trimmed}");
                    return trimmed;
                }
            }
        } catch (Exception e) {
            UnityEngine.Debug.LogWarning($"[LaunchWebUI] Could not check {envVarName}: {e.Message}");
        }

        string exeDirectory = Directory.GetParent(Application.dataPath).FullName;
        
        // Prefer .bat over .lnk so we can pass --gpu-device-id when user selects a specific GPU.
        string[] possiblePaths = new string[] {
            Path.Combine(exeDirectory, "stable-diffusion-webui-forge", "run_noQuickEdit.bat"),
            Path.Combine(exeDirectory, "stable-diffusion-webui-forge", "run.bat"),
            Path.Combine(exeDirectory, "stable-diffusion-webui-forge", "run_forge.bat"),
            Path.Combine(exeDirectory, "stable-diffusion-webui-forge", "run_noQuickEdit.lnk"),
            Path.Combine(exeDirectory, "..", "stable-diffusion-webui-forge", "run_noQuickEdit.bat"),
            Path.Combine(exeDirectory, "..", "stable-diffusion-webui-forge", "run.bat"),
            Path.Combine(exeDirectory, "..", "stable-diffusion-webui-forge", "run_noQuickEdit.lnk"),
        };

        // Try each possible path
        foreach(string filePath in possiblePaths){
            try{
                string fullPath = Path.GetFullPath(filePath);
                if(File.Exists(fullPath)){
                    UnityEngine.Debug.Log($"Webui file found, launching it automatically: {fullPath}");
                    return fullPath;
                }
            }catch{
                // Skip invalid paths
                continue;
            }
        }

        // Also try searching in parent directories (like RestartTheWebui does)
        string[] searchFiles = new string[] { "run_noQuickEdit.bat", "run.bat", "run_forge.bat", "run_noQuickEdit.lnk" };
        string currentDir = exeDirectory;
        for(int i = 0; i < 3; i++){ // Search up to 3 levels up
            foreach(string filename in searchFiles){
                try {
                    string attemptPath = Path.Combine(currentDir, "stable-diffusion-webui-forge", filename);
                    if(File.Exists(attemptPath)){
                        UnityEngine.Debug.Log($"Webui file found (searched parent dirs), launching it automatically: {attemptPath}");
                        return attemptPath;
                    }
                } catch {
                    // Skip invalid paths (e.g. ArgumentException from invalid path characters)
                    continue;
                }
            }
            DirectoryInfo parentDir = Directory.GetParent(currentDir);
            if (parentDir == null) break;
            currentDir = parentDir.FullName;
        }

        string msg = $"Webui file not found, can't launch it automatically. User will have to launch their own. Searched in: {exeDirectory}. (Optional: set {envVarName} to the full path to run.bat for a dev fallback.)";
        if (printStatusText_ifNotFound){
            Viewport_StatusText.instance.ShowStatusText(msg, textIsETA_number: false, 10, false);
        }
        UnityEngine.Debug.Log(msg);
        return "";
    }


	    /// <summary>Returns path to launch (or a wrapper that sets COMMANDLINE_ARGS and runs launch.py so GPU id is applied). Forge reads args from COMMANDLINE_ARGS, not from bat arguments. Public so RestartTheWebui can apply GPU when user restarts from UI.</summary>
	    public static string GetLaunchPathWithGpuSetting(string webuiFilePath, out string workingDir) {
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
	        UnityEngine.Debug.Log($"[LaunchWebUI] SD_GPU_DeviceId = {gpuId} (from Settings; file used only when Settings = default).");
	        if (gpuId >= 0)
	            WriteSdDeviceToForgeFolder(workingDir, gpuId);
	        if (gpuId < 0)
	            return webuiFilePath;
	        try {
	            // Forge reads args from COMMANDLINE_ARGS; run.bat does not forward --gpu-device-id. Prefer direct launch.
	            string launchPy = Path.Combine(workingDir, "launch.py");
	            string pythonExe = Path.Combine(workingDir, "webui", "venv", "Scripts", "python.exe");
	            if (!File.Exists(pythonExe))
	                pythonExe = Path.Combine(workingDir, "venv", "Scripts", "python.exe");
	            if (File.Exists(launchPy) && File.Exists(pythonExe)) {
	                string wrapperPath = Path.Combine(Path.GetTempPath(), "spz_webui_gpu_wrapper.bat");
	                string args = "--api --gpu-device-id " + gpuId + " --device-id " + gpuId;
	                string content = "@echo off\r\nset CUDA_VISIBLE_DEVICES=" + gpuId + "\r\nset COMMANDLINE_ARGS=" + args + "\r\ncd /d \"" + workingDir.Replace("\"", "\"\"") + "\"\r\n\"" + pythonExe.Replace("\"", "\"\"") + "\" \"" + launchPy.Replace("\"", "\"\"") + "\"\r\n";
	                File.WriteAllText(wrapperPath, content);
	                UnityEngine.Debug.Log($"[LaunchWebUI] Using GPU {gpuId} via COMMANDLINE_ARGS and direct launch.py, wrapper: {wrapperPath}");
	                workingDir = Path.GetTempPath();
	                return wrapperPath;
	            }
	            // Fallback: wrapper that calls their bat (args may be ignored if bat sets COMMANDLINE_ARGS).
	            string wrapperPath2 = Path.Combine(Path.GetTempPath(), "spz_webui_gpu_wrapper.bat");
	            string ext = Path.GetExtension(webuiFilePath).ToLowerInvariant();
	            string callLine = (ext == ".lnk")
	                ? "start \"\" \"" + webuiFilePath.Replace("\"", "\"\"") + "\""
	                : "call \"" + webuiFilePath.Replace("\"", "\"\"") + "\" --gpu-device-id " + gpuId;
	            if (ext == ".lnk")
	                UnityEngine.Debug.Log($"[LaunchWebUI] Using GPU {gpuId} (CUDA_VISIBLE_DEVICES only; launch.py/venv not found for direct launch).");
	            string content2 = "@echo off\r\nset CUDA_VISIBLE_DEVICES=" + gpuId + "\r\ncd /d \"" + workingDir.Replace("\"", "\"\"") + "\"\r\n" + callLine + "\r\n";
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

	    public void LaunchWebui_Manually( bool printStatusText_ifNotFound = false){
	        string filePath = GetWebuiFilePath(printStatusText_ifNotFound);
	        if(filePath==""){ return; }

	        TryCloseLastLaunchedWebUi();

	        string workingDir;
	        string launchPath = GetLaunchPathAndWorkingDir(filePath, out workingDir);
	        try{
	            uint pid = StartExternalProcess.Run_Bat_or_Shortcut_or_Command(launchPath, isJustFile:true, workingDir);
	            if (pid != 0){
	                SetLastLaunchedWebUiPid(pid);
	                UnityEngine.Debug.Log($"Process launched successfully with PID: {pid}");
	            }else{
	                UnityEngine.Debug.LogError("Failed to launch process.");
	            }
	        }
	        catch (Exception e){
	            UnityEngine.Debug.LogError($"Error launching process: {e.Message}");
	        }
	    }


	    void Start(){
	        #if UNITY_EDITOR
	        return; //else keeps bothering me
	        #endif
	        bool printStatusText_ifNotFound = true;
	        LaunchWebui_Manually(printStatusText_ifNotFound);
	    }

	    void Awake(){
	        if (instance != null){ DestroyImmediate(this); return; }
	        instance = this;
	    }
	}
}//end namespace

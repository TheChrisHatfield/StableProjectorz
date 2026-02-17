using System;
using System.Diagnostics;
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
	            // Kill process tree so the WebUI python process is closed too (PID we have is the cmd/bat launcher).
	            var startInfo = new ProcessStartInfo {
	                FileName = "taskkill",
	                Arguments = $"/PID {_lastLaunchedWebUiPid} /T /F",
	                CreateNoWindow = true,
	                UseShellExecute = false
	            };
	            using (var p = Process.Start(startInfo)) {
	                p?.WaitForExit(5000);
	            }
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
    

    string GetWebuiFilePath( bool printStatusText_ifNotFound = false){
        // Fallback: environment variable (e.g. set locally or in editor for development)
        const string envVarName = "SPZ_WEBUI_RUN_PATH";
        try {
            string envPath = Environment.GetEnvironmentVariable(envVarName);
            if (!string.IsNullOrWhiteSpace(envPath)) {
                string trimmed = envPath.Trim();
                if (File.Exists(trimmed)) {
                    Debug.Log($"Webui file found via {envVarName}: {trimmed}");
                    return trimmed;
                }
            }
        } catch (Exception e) {
            Debug.LogWarning($"[LaunchWebUI] Could not check {envVarName}: {e.Message}");
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
                    Debug.Log($"Webui file found, launching it automatically: {fullPath}");
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
                        Debug.Log($"Webui file found (searched parent dirs), launching it automatically: {attemptPath}");
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
        Debug.Log(msg);
        return "";
    }


	    /// <summary>Returns path to launch (or a wrapper that sets COMMANDLINE_ARGS and runs launch.py so GPU id is applied). Forge reads args from COMMANDLINE_ARGS, not from bat arguments. Public so RestartTheWebui can apply GPU when user restarts from UI.</summary>
	    public static string GetLaunchPathWithGpuSetting(string webuiFilePath, out string workingDir) {
	        var parent = Directory.GetParent(webuiFilePath);
	        workingDir = parent != null ? parent.FullName : Path.GetDirectoryName(webuiFilePath) ?? "";
	        int gpuId = UnityEngine.PlayerPrefs.GetInt("SD_GPU_DeviceId", -1);
	        UnityEngine.Debug.Log($"[LaunchWebUI] SD_GPU_DeviceId from PlayerPrefs = {gpuId} (-1 = default, 0/1/2 = GPU index).");
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
	                Debug.Log($"[LaunchWebUI] Using GPU {gpuId} via COMMANDLINE_ARGS and direct launch.py, wrapper: {wrapperPath}");
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
	                Debug.Log($"[LaunchWebUI] Using GPU {gpuId} (CUDA_VISIBLE_DEVICES only; launch.py/venv not found for direct launch).");
	            string content2 = "@echo off\r\nset CUDA_VISIBLE_DEVICES=" + gpuId + "\r\ncd /d \"" + workingDir.Replace("\"", "\"\"") + "\"\r\n" + callLine + "\r\n";
	            File.WriteAllText(wrapperPath2, content2);
	            workingDir = Path.GetTempPath();
	            return wrapperPath2;
	        } catch (Exception e) {
	            Debug.LogWarning($"[LaunchWebUI] Could not create GPU wrapper, launching without GPU override: {e.Message}");
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
	                Debug.Log($"Process launched successfully with PID: {pid}");
	            }else{
	                Debug.LogError("Failed to launch process.");
	            }
	        }
	        catch (Exception e){
	            Debug.LogError($"Error launching process: {e.Message}");
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

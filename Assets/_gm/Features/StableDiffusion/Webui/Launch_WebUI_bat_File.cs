using System;
using System.IO;
using UnityEngine;
using Lavender.Systems;

namespace spz {

	public class LaunchWebUIBatFile : MonoBehaviour{
	    public static LaunchWebUIBatFile instance { get; private set; } = null;
    

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
        
        // Try multiple possible locations
        string[] possiblePaths = new string[] {
            Path.Combine(exeDirectory, "stable-diffusion-webui-forge", "run_noQuickEdit.lnk"),
            Path.Combine(exeDirectory, "stable-diffusion-webui-forge", "run_noQuickEdit.bat"),
            Path.Combine(exeDirectory, "stable-diffusion-webui-forge", "run.bat"),
            Path.Combine(exeDirectory, "stable-diffusion-webui-forge", "run_forge.bat"),
            Path.Combine(exeDirectory, "..", "stable-diffusion-webui-forge", "run_noQuickEdit.lnk"),
            Path.Combine(exeDirectory, "..", "stable-diffusion-webui-forge", "run_noQuickEdit.bat"),
            Path.Combine(exeDirectory, "..", "stable-diffusion-webui-forge", "run.bat"),
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
        string[] searchFiles = new string[] { "run_noQuickEdit.lnk", "run_noQuickEdit.bat", "run.bat", "run_forge.bat" };
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


	    public void LaunchWebui_Manually( bool printStatusText_ifNotFound = false){
	        string filePath = GetWebuiFilePath(printStatusText_ifNotFound);
	        if(filePath==""){ return; }

	        try{
	            uint pid = StartExternalProcess.Run_Bat_or_Shortcut_or_Command(filePath, isJustFile:true, Directory.GetParent(filePath).FullName);
	            if (pid != 0){
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

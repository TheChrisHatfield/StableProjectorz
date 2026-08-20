using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace spz {

	public class SD_SysInfo_MGR : MonoBehaviour{
	    public static SD_SysInfo_MGR instance { get; private set; } = null;

	    public static string INTERNAL_API_URL => Connection_MGR.A1111_IP_AND_PORT + "/internal";

	    //continiously fetched from the server (every few seconds).
	    //Can tell us the setup that user has, number of control units etc.
	    public SD_SysInfo sysInfo { get; private set; } = new SD_SysInfo();
    
	    public bool isForgeWebui_detected(){
	        // Classic / reForge / sd-webui-forge-neo junctions contain "forge".
	        // True Haoming02 checkout often uses DataPath …\neo (no "forge" substring) — still Forge-family (forge-neo-swap R2).
	        if (PathLooksForgeFamily(sysInfo?.DataPath) || PathLooksForgeFamily(sysInfo?.ScriptPath))
	            return true;
	        string ver = sysInfo?.Version?.Trim() ?? "";
	        if (ver.Length > 0 && ver.StartsWith("neo", StringComparison.OrdinalIgnoreCase))
	            return true;
	        return false;
	    }

	    /// <summary>True when sysinfo paths/Version look like Forge-family WebUI (classic, reForge, or true Forge Neo).</summary>
	    public bool isForgeFamilyWebui_detected() => isForgeWebui_detected();

	    static bool PathLooksForgeFamily(string path) {
	        if (string.IsNullOrWhiteSpace(path)) return false;
	        string p = path.Replace('\\', '/').ToLowerInvariant();
	        if (p.Contains("forge")) return true; // forge, reForge, forge-classic, sd-webui-forge-neo, …
	        if (p.Contains("forge_neo") || p.Contains("forge-neo")) return true;
	        // Bare Haoming02 folder name "neo" (…/FORGE_NEO_TRUE_REPO/neo or …/neo)
	        if (p.EndsWith("/neo") || p.Contains("/neo/") || p.Contains("forge_neo_true"))
	            return true;
	        return false;
	    }
	    /// <summary>Non-empty WebUI DataPath from last sysinfo (forge-neo-swap R2).</summary>
	    public static bool TryGetSdDataPath(out string dataPath) {
	        dataPath = "";
	        if (instance == null || instance.sysInfo == null) return false;
	        string raw = instance.sysInfo.DataPath;
	        if (string.IsNullOrWhiteSpace(raw)) return false;
	        dataPath = raw.Trim().TrimEnd('/', '\\');
	        return dataPath.Length > 0;
	    }

	    /// <summary>Forge-family CN models dir under DataPath, or false if DataPath missing (do not fall back to A1111 blindly).</summary>
	    public static bool TryResolveControlNetModelsDir(string forgeRelativeSubdir, string a1111RelativeSubdir, out string absoluteDir, out string denyReason) {
	        absoluteDir = "";
	        denyReason = "";
	        if (!TryGetSdDataPath(out string dataPath)) {
	            denyReason = "WebUI DataPath empty (wait for /internal/sysinfo before model/VAE/ControlNet folder resolve).";
	            return false;
	        }
	        bool forgeFamily = instance != null && instance.isForgeFamilyWebui_detected();
	        string rel = forgeFamily ? forgeRelativeSubdir : a1111RelativeSubdir;
	        if (string.IsNullOrEmpty(rel)) rel = forgeFamily ? "/models/ControlNet/" : "/extensions/sd-webui-controlnet/models/";
	        absoluteDir = (dataPath + rel).Replace('\\', '/');
	        return true;
	    }

	    /// <summary>Checkpoint dir under DataPath (<c>/models/Stable-diffusion/</c>). Fails closed when DataPath empty.</summary>
	    public static bool TryResolveCheckpointModelsDir(out string absoluteDir, out string denyReason) {
	        return TryResolveControlNetModelsDir(
	            "/models/Stable-diffusion/",
	            "/models/Stable-diffusion/",
	            out absoluteDir,
	            out denyReason);
	    }

	    /// <summary>VAE dir under DataPath (<c>/models/VAE/</c>). Fails closed when DataPath empty.</summary>
	    public static bool TryResolveVaeModelsDir(out string absoluteDir, out string denyReason) {
	        return TryResolveControlNetModelsDir(
	            "/models/VAE/",
	            "/models/VAE/",
	            out absoluteDir,
	            out denyReason);
	    }


	    void Awake(){
	        if (instance != null){ DestroyImmediate(this); return; }
	        instance = this; 
	    }

	    void Start(){
	        StartCoroutine( FetchContiniously() );
	    }


	    IEnumerator FetchContiniously(){
	        while (true){
	            if (!Connection_MGR.is_sd_connected){ 
	                yield return new WaitForSeconds(0.25f); 
	                continue; 
	            }
	            yield return StartCoroutine( FetchInfo_crtn() );
	            yield return new WaitForSeconds(5f);
	        }
	    }

	    IEnumerator FetchInfo_crtn(){
	      //models list:
	        bool success = false;
	        Action<bool,string> onResult =  (isSuccess,text) => { 
	            success=isSuccess;
	            this.sysInfo = success? SD_SysInfo.CreateFromJSON(text) 
	                                  : new SD_SysInfo();//error, so just an empty sysInfo.
	        };
	        yield return StartCoroutine( FetchData_crtn(INTERNAL_API_URL + "/sysinfo", onResult) );
	        if (!success){ yield break; }
	    }

	    IEnumerator FetchData_crtn( string url,  Action<bool,string> onResult ){
	        //Don't send network request to webui if rendering, else it seems to stuck it sometimes.
	        if(StableDiffusion_Hub.instance._generating){ yield break; }

	        UnityWebRequest request = UnityWebRequest.Get(url);
	        try {
	            yield return request.SendWebRequest();

	            bool isBad = request.result == UnityWebRequest.Result.ConnectionError;
	                isBad |= request.result == UnityWebRequest.Result.ProtocolError;
	            if (isBad){
	                onResult?.Invoke(false, "");
	            }else{
	                onResult?.Invoke(true, request.downloadHandler.text);
	            }
	        } finally {
	            request.Dispose();
	        }
	    }
	}


	[Serializable]//returns from stableDiffusion, back into StableProjectorz
	public class SD_SysInfo{
	    public string Platform = "";
	    public string Python = "";
	    public string Version = "";
	    public string Commit = "";
	    [JsonProperty("Script path")]
	    public string ScriptPath = "";
	    [JsonProperty("Data path")]
	    public string DataPath = "";
	    [JsonProperty("Extensions dir")]
	    public string ExtensionsDir = "";
	    public string Checksum = "";
	    public List<string> Commandline;
	    public Config Config = new Config();

	    public static SD_SysInfo CreateFromJSON(string jsonString){
	        // Use class-type information, to support inheritance of objects:
	        var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto, };
	        return JsonConvert.DeserializeObject<SD_SysInfo>(jsonString, settings);
	    }
	}


	[Serializable]//returns from stableDiffusion, back into StableProjectorz
	public class Config{
	    public int control_net_unit_count;
	    public int control_net_model_cache_size;
	    public int control_net_max_models_num;
	    public int num_units(){
	        return Mathf.Max(control_net_unit_count, control_net_max_models_num);
	    }
	}
}//end namespace

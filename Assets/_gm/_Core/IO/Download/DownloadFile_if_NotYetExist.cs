using System.IO;
using UnityEngine;

namespace spz {

	public class DownloadFile_if_NotYetExist : MonoBehaviour{
	    [SerializeField] string _fileUrl = "https://huggingface.co/lllyasviel/ControlNet-v1-1/resolve/main/control_v11f1p_sd15_depth.pth?download=true";
	    [SerializeField] string _subdirectory_rel_webuiDatapath_FORGE = "/models/ControlNet/";//If using 'Forge' (priority) Replace with your subdirectory name. 
	    [SerializeField] string _subdirectory_relExe_A1111 = "/extensions/sd-webui-controlnet/models/"; //If using Automatic1111 (legacy) Replace with your subdirectory name
	    [Header("if empty, will find+use the filename inside url")]
	    [SerializeField] string _fileName_withExten = "";

	    /// <summary>
	    /// Returns true only when the download was actually handed to <see cref="Download_MGR"/>.
	    /// Callers gate UI on an in-flight download, so a refused start must be distinguishable from
	    /// "0% progress" — reporting progress alone left those gates stuck closed forever.
	    /// </summary>
	    public bool DownloadFile( string fileUrl="",  string absFilepath_withExten = "",  System.Action<float>onProgress = null){
	      #if UNITY_EDITOR && false
	            // In the Unity Editor, use a different subdirectory within the project folder
	            string dir = Path.Combine( Directory.GetParent(Application.dataPath).FullName, "TestDownloadsIntoHere");
	                   dir = Path.Combine(dir, _fileName_withExten).Replace('\\', '/'); // Normalize the path
	      #else
	        if (string.IsNullOrEmpty(absFilepath_withExten)) {
	            if (!SD_SysInfo_MGR.TryResolveControlNetModelsDir(
	                    _subdirectory_rel_webuiDatapath_FORGE,
	                    _subdirectory_relExe_A1111,
	                    out string modelsDir,
	                    out string denyReason)) {
	                UnityEngine.Debug.LogWarning("[DownloadFile] " + denyReason);
	                onProgress?.Invoke(0f);
	                return false;
	            }
	            absFilepath_withExten = Path.Combine(modelsDir, _fileName_withExten).Replace('\\', '/');
	        }
	        string dir = absFilepath_withExten;
	      #endif
	        fileUrl =  fileUrl!=""?  fileUrl : _fileUrl;
	        absFilepath_withExten = absFilepath_withExten != ""? absFilepath_withExten : dir;
	        if (Download_MGR.instance == null){
	            UnityEngine.Debug.LogWarning("[DownloadFile] no Download_MGR — cannot start download.");
	            onProgress?.Invoke(0f);
	            return false;
	        }
	        Download_MGR.instance.DownloadFile(fileUrl, absFilepath_withExten, onProgress);
	        return true;
	    }


	    void Awake(){
	        if (_fileName_withExten == "" && _fileUrl!=""){
	            _fileName_withExten = GetFileNameFromUrl(_fileUrl);
	        }
	    }


	    string GetFileNameFromUrl(string url){ 
	        var uri = new System.Uri(url);
	        string filename = Path.GetFileName(uri.LocalPath);
	        return filename;
	    }

	}
}//end namespace

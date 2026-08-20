using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace spz {

	//never gets switched of (unlike the UI panels that request these downloads).
	public class Download_MGR : MonoBehaviour{
	    public static Download_MGR instance { get; private set; } = null;

	    // has all current downloads:
	    Dictionary<string, UnityWebRequest> _url_to_Request = new Dictionary<string, UnityWebRequest>();
	    Dictionary<string,Coroutine> _url_to_Download_crtn  = new Dictionary<string, Coroutine>();

	    public bool IsDownloading(string fileUrl) =>  _url_to_Download_crtn.ContainsKey(fileUrl);


	    public void CancelDownload(string fileUrl){
	        if (_url_to_Request.TryGetValue(fileUrl, out UnityWebRequest request))
	        {
	            request.Abort();
	            request.Dispose();
	            _url_to_Request.Remove(fileUrl);
	        }

	        if (_url_to_Download_crtn.TryGetValue(fileUrl, out Coroutine crtn))
	        {
	            StopCoroutine(crtn);
	            _url_to_Download_crtn.Remove(fileUrl);
	        }
	    }


	    public void DownloadFile( string fileUrl="",  string absFilepath_withExten = "", 
	                              System.Action<float>onProgress = null,  bool printStatusMsg=true){
	        Coroutine crtn;
	        _url_to_Download_crtn.TryGetValue(fileUrl, out crtn);
	        if(crtn != null){ return; }//already downloading

	        crtn = StartCoroutine(DownloadFile_crtn(fileUrl, absFilepath_withExten, onProgress, printStatusMsg) );
	        _url_to_Download_crtn[fileUrl] = crtn;
	    }


	    IEnumerator DownloadFile_crtn( string fileUrl,  string absFilepath_withExten,  
	                                   System.Action<float> onProgress, bool printStatusMsg ){

	        UnityWebRequest request = UnityWebRequest.Get(fileUrl);
	        _url_to_Request[fileUrl] = request;
	        try {
	            request.SendWebRequest();

	            while (!request.isDone){
	                string msg = $"<b>Downloading</b>  {fileUrl}  <b>and storing it into</b>  {absFilepath_withExten}";
	                prnt(printStatusMsg, msg, request.downloadProgress, showProgress:true);
	                onProgress?.Invoke(request.downloadProgress);
	                yield return null;
	            }

	            if (request.result != UnityWebRequest.Result.Success){
	                if (printStatusMsg){
	                    string msg = "Downloading failed: " + request.error;
	                    prnt(printStatusMsg, msg, request.downloadProgress, showProgress:false);
	                }
	                // Terminal failure: never report 1.0 — callers (ControlNet gate) treat 1.0 as "file landed".
	                // Negative progress is the failure sentinel; Gen3D/ShadowR still use File.Exists + IsDownloading.
	                onProgress?.Invoke(-1f);
	            }else{
	                string dir = Path.GetDirectoryName(absFilepath_withExten);
	                if (!string.IsNullOrEmpty(dir))
	                    Directory.CreateDirectory(dir);
	                File.WriteAllBytes(absFilepath_withExten, request.downloadHandler.data);

	                if (printStatusMsg){
	                    string msg = "<b>File downloaded and saved to</b> " + absFilepath_withExten;
	                    prnt(printStatusMsg, msg, request.downloadProgress, showProgress:false);
	                }
	                onProgress?.Invoke(1.0f);//ensure 100% only after bytes are on disk
	            }
	        } finally {
	            _url_to_Download_crtn.Remove(fileUrl);
	            _url_to_Request.Remove(fileUrl);
	            try { request.Dispose(); } catch { /* already disposed by CancelDownload */ }
	        }
	    }


	    void prnt(bool print, string msg, float progress01, bool showProgress){
	        if(!print){ return; }
	        Viewport_StatusText.instance?.ShowStatusText(msg, false,  showProgress?12:10,  showProgress?false:true);
	        Viewport_StatusText.instance?.ReportProgress(progress01);
	    }
    

	    void Awake(){
	        if(instance != null){ DestroyImmediate(this.gameObject); return; }
	        instance = this;
	    }

	    void OnDestroy(){
	        // Clean up any ongoing requests
	        foreach (var request in _url_to_Request.Values){
	            if (request != null){
	                request.Abort();
	                request.Dispose();
	            }
	        }
	    }
	}
}//end namespace

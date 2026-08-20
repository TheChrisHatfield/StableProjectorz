using System;
using System.Linq;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

namespace spz {

	public class SD_Generate_NetworkSender : MonoBehaviour{

	    Action<UnityWebRequest> _onProgress = null;
	    Action<UnityWebRequest> _onCompleted = null;
	    /// <summary>In-flight generate/detect POST. Cancel must Abort this — StopAllCoroutines alone leaves the HTTP body running.</summary>
	    UnityWebRequest _activeRequest = null;

	    // Other serialized fields to capture input from the UI
	    public void Send_GenerateRequest( SD_txt2img_payload req,  Action<UnityWebRequest> onProgress,  Action<UnityWebRequest> onCompleted ){
	        _onProgress = onProgress;
	        _onCompleted = onCompleted;
	        string url = Connection_MGR.A1111_SD_API_URL + "/txt2img";
	        StartCoroutine( Send_GenerateRequest_crtn( url, req, req.width, req.height, withProgress:true) );
	    }

    
	    public void Send_GenerateRequest( SD_img2img_payload payload,  Action<UnityWebRequest> onProgress,  Action<UnityWebRequest> onCompleted ){
	        _onProgress = onProgress;
	        _onCompleted = onCompleted;
	        string url = Connection_MGR.A1111_SD_API_URL + "/img2img";
	        StartCoroutine( Send_GenerateRequest_crtn( url, payload, payload.width, payload.height, withProgress:true) );
	    }

    
	    public void Send_GenerateRequest( SD_img2extra_payload payload,  Action<UnityWebRequest> onProgress,  Action<UnityWebRequest> onCompleted ){
	        _onProgress = onProgress;
	        _onCompleted = onCompleted;
	        string url = Connection_MGR.A1111_SD_API_URL + "/extra-batch-images";
	        StartCoroutine( Send_GenerateRequest_crtn(url, payload, payload.rslt_imageWidths, payload.rslt_imageHeights, withProgress:true) );
	    }


	    public void Send_GenerateRequest(SD_ControlnetDetect_payload payload, Action<UnityWebRequest> onComplete){
	        _onProgress = null;
	        _onCompleted = onComplete;
	        string url = Connection_MGR.A1111_CTRLNET_API_URL + "/detect";
	        StartCoroutine( Send_GenerateRequest_crtn(url, payload, -1, -1, withProgress:false ));
	    }


	    public void Send_StopGenerateRequest(Action onInterruptSettled = null){
	        // Interrupt must not invoke the generate OnCompleted handler — that would parse /interrupt
	        // JSON as a txt2img result (or double-finish the cancelled job).
	        _onProgress = null;
	        _onCompleted = null;
	        string url = Connection_MGR.A1111_SD_API_URL + "/interrupt";
	        // Cloud Inference: POST /interrupt BEFORE Abort so fal can register cancel_url while the
	        // generate handler is still alive. Aborting first tore down the shim worker mid-submit.
	        var generateReq = _activeRequest;
	        StopAllCoroutines();
	        StartCoroutine(SendInterruptThenAbort_crtn(url, generateReq, onInterruptSettled));
	    }

	    IEnumerator SendInterruptThenAbort_crtn(string url, UnityWebRequest generateReq, Action onInterruptSettled){
	        using (UnityWebRequest request = new UnityWebRequest(url, "POST")){
	            request.downloadHandler = new DownloadHandlerBuffer();
	            try {
	                yield return request.SendWebRequest();
	            } finally { /* interrupt request only */ }
	        }
	        if (generateReq != null){
	            try {
	                if (!generateReq.isDone)
	                    generateReq.Abort();
	            } catch (Exception e) {
	                UnityEngine.Debug.LogWarning("[SD_Generate_NetworkSender] Abort failed: " + e.Message);
	            }
	        }
	        if (ReferenceEquals(_activeRequest, generateReq))
	            _activeRequest = null;
	        onInterruptSettled?.Invoke();
	    }

	    void AbortActiveRequest(){
	        var req = _activeRequest;
	        _activeRequest = null;
	        if (req == null) return;
	        try {
	            if (!req.isDone)
	                req.Abort();
	        } catch (Exception e) {
	            UnityEngine.Debug.LogWarning("[SD_Generate_NetworkSender] Abort failed: " + e.Message);
	        }
	    }


	    IEnumerator Send_GenerateRequest_crtn<T>(string urlSuffix, T payloadStruct, int width, int height, bool withProgress, bool paceGpuFlow = true)
	    {
	        if (paceGpuFlow) {
	            yield return GpuFlowUnityHooks.PaceFromAddonHttpCoroutine(source: "sd_sender", phase: "pre_request");
	        }

	        Coroutine progressRoutine = null;
	        if (withProgress){
	            progressRoutine = StartCoroutine(CheckProgress_crtn(width, height));
	        }

	        using (UnityWebRequest request = new UnityWebRequest(urlSuffix, "POST")){
	            if (payloadStruct != null){
	                // TypeNameHandling.Auto injects $type on polymorphic AlwaysOn_Value — Neo Gradio API rejects/ignores that.
	                // Outbound generate bodies must not put $type under alwayson_scripts (forge-neo-swap R4.3).
	                var settings = new JsonSerializerSettings{
	                    Formatting = Formatting.Indented,
	                    TypeNameHandling = TypeNameHandling.None
	                };
	                string json = JsonConvert.SerializeObject(payloadStruct, settings);
	                byte[] jsonToSend = new UTF8Encoding().GetBytes(json);
	                request.uploadHandler = new UploadHandlerRaw(jsonToSend);
	                request.SetRequestHeader("Content-Type", "application/json");
	            }
	            request.downloadHandler = new DownloadHandlerBuffer();

	            _activeRequest = request;
	            try {
	                yield return request.SendWebRequest();
	            } finally {
	                if (ReferenceEquals(_activeRequest, request))
	                    _activeRequest = null;
	            }

	            if (progressRoutine != null){
	                StopCoroutine(progressRoutine);
	            }
	            // Aborted / cancelled requests must not finish the generate UI as a success.
	            if (request.result == UnityWebRequest.Result.ConnectionError
	                && (request.error ?? "").IndexOf("Abort", StringComparison.OrdinalIgnoreCase) >= 0) {
	                yield break;
	            }
	            if (paceGpuFlow && request.result == UnityWebRequest.Result.Success) {
	                yield return GpuFlowUnityHooks.PaceFromAddonHttpCoroutine(source: "sd_sender", phase: "post_request");
	            }
	            _onCompleted?.Invoke(request);
	        }
	    }


	    IEnumerator CheckProgress_crtn(int width, int height){
	        string progressUrl = Connection_MGR.A1111_SD_API_URL + "/progress";
    
	        while (true){
	            yield return new WaitForSeconds( CalculateWaitTime(width,height) );

	             using (UnityWebRequest request = UnityWebRequest.Get(progressUrl)){
	                yield return request.SendWebRequest();
	                _onProgress?.Invoke(request);
	                if(request.result == UnityWebRequest.Result.ConnectionError){ yield break; }
	                if(request.result == UnityWebRequest.Result.ProtocolError){ yield break;  }
	             }
	        }//end while
	    }

	    float CalculateWaitTime(int width, int height) {
	        int totalPixels = width*height;
	        float spacing = CalculateSpacing(totalPixels);
	        return spacing;
	    }

	    float CalculateSpacing(int totalPixels) {
	        if (totalPixels <= 256*256){ return 0.5f; }
	        else if (totalPixels <= 512*512){ return 1f; }
	        else if (totalPixels <= 750*750){ return 3f; }
	        else if (totalPixels <= 1024*1024){ return 5f; }
	        else if (totalPixels <= 1600*1600){ return 6f; }
	        else if (totalPixels <= 2048*2048){ return 15f; }
	        else if (totalPixels <= 3000*3000){ return 15f; }
	        else if (totalPixels <= 3500*3500){ return 15f; }
	        return 12;
	    }
	}
}//end namespace

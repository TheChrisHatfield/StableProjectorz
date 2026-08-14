using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.IO;
using System;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace spz {

	public class Gen3D_API : MonoBehaviour{
	    public static Gen3D_API instance { get; private set; } = null;

	    Coroutine _gen_or_resume_crtn = null;
	    Coroutine _progress_crtn = null;
	    Coroutine _getSupportedOper_crtn = null;
	    Coroutine _submit_crtn = null;
	    Coroutine _download_crtn = null;

	    /// <summary>Set by <see cref="CancelGeneration"/> — nested submit/poll/download must not deliver mesh after cancel.</summary>
	    bool _cancelRequested;

	    TaskStatus _generateStatus = TaskStatus.COMPLETE;
	    GenerationResponse _generateResponse = null;


	    public bool IsServerAvailable => Connection_MGR.is_3d_connected;

	    /// <summary>
	    /// True while a generate/resume or final-mesh download coroutine is live.
	    /// Do not gate on status alone — status becomes COMPLETE before Gen_downloadFinalData finishes,
	    /// which left Cancel a no-op and allowed a second StartGeneration mid-download.
	    /// </summary>
	    public bool isBusy => _gen_or_resume_crtn != null || _download_crtn != null;


	    //uppercase, must ensure the capitalization exactly matches the one in python script.
	    public enum TaskStatus{
	        PROCESSING,
	        PREVIEW_READY,
	        COMPLETE,
	        FAILED
	    }

	    [Serializable]
	    public class PreviewUrls{
	        public string gaussian;
	        public string mesh;
	        public string radiance;
	    }

	    [Serializable]
	    public class GenerationResponse{
	        public TaskStatus status;
	        public int progress;
	        public string message;
	        public PreviewUrls preview_urls;
	        public string model_url;
	    }

	    [Serializable]
	    public class GenerationStatus{
	        // This matches the /status response from the single-generation approach:
	        public string status; // direct string from JSON
	        public int progress;
	        public string message;
	        public bool busy;
	    }


	    public enum GenerateWhat{
	        make_meshes_and_tex, //makes a mesh and its texture.
	        retexture, //prepares 2d textures for our existing mesh.
	    }

	    // other c# scripts that invoke our functions can supply these callbacks. We'll invoke them.
	    public class GenerationCallbacks{
	        public Action<float> onProgress;             // 0..1
	        public Action<string, byte[]> onPreviewReady;// (previewType, data)
	        public Action<byte[]> onDataDownloaded;
	        public Action<string> onError;
	        public Action onComplete;
	    }


	    public void CancelGeneration(){
	        _cancelRequested = true;
	        _generateStatus = TaskStatus.FAILED;
	        if (_progress_crtn != null) {
	            try { StopCoroutine(_progress_crtn); } catch { /* already stopped */ }
	            _progress_crtn = null;
	        }
	        if (_submit_crtn != null) {
	            try { StopCoroutine(_submit_crtn); } catch { /* already stopped */ }
	            _submit_crtn = null;
	        }
	        if (_download_crtn != null) {
	            try { StopCoroutine(_download_crtn); } catch { /* already stopped */ }
	            _download_crtn = null;
	        }
	        if (_gen_or_resume_crtn != null) {
	            try { StopCoroutine(_gen_or_resume_crtn); } catch { /* already stopped */ }
	            _gen_or_resume_crtn = null;
	        }
	        // Also call the server's /interrupt endpoint:
	        StartCoroutine( InterruptOnServer() );
	    }


	    //<string,object>  object could be a dictionary, a list of base64 images, a float, etc.
	    public void StartGeneration( GenerateWhat what,  Dictionary<string,object> inputs,  GenerationCallbacks callbacks ){
	        if (!IsServerAvailable){
	            callbacks.onError?.Invoke("Server is not available");
	            return;
	        }
	        _cancelRequested = false;
	        if(_gen_or_resume_crtn!=null){ StopCoroutine(_gen_or_resume_crtn);  }
	        if(_progress_crtn!=null){ StopCoroutine(_progress_crtn); _progress_crtn = null; }
	        if(_submit_crtn!=null){ StopCoroutine(_submit_crtn); _submit_crtn = null; }
	        if(_download_crtn!=null){ StopCoroutine(_download_crtn); _download_crtn = null; }
	        _gen_or_resume_crtn = StartCoroutine( Generate_crtn(what, inputs, callbacks) );
	    }


	    public void GetSupportedOperations(Action<List<string>> onSuccess, Action<string> onError = null) {
	       if (_getSupportedOper_crtn != null){ StopCoroutine(_getSupportedOper_crtn); }
	        _getSupportedOper_crtn = StartCoroutine(GetSupportedOperations_crtn(onSuccess, onError));
	    }


	    IEnumerator GetSupportedOperations_crtn(Action<List<string>> onSuccess, Action<string> onError){
	        using (UnityWebRequest www = UnityWebRequest.Get($"{Connection_MGR.GEN3D_URL}/info/supported_operations")) {
	            yield return www.SendWebRequest();
	            try{
	                if (www.result == UnityWebRequest.Result.Success){
	                    List<string> operations = JsonConvert.DeserializeObject<List<string>>( www.downloadHandler.text );
	                    onSuccess?.Invoke(operations);
	                } else {
	                    onError?.Invoke($"Failed to get operations: {www.error}");
	                } 
	            }catch{
	                onError?.Invoke($"Failed to get operations: {www.error}");
	            }
	        }
	        _getSupportedOper_crtn = null;
	    }

	    IEnumerator Generate_crtn( GenerateWhat what,  Dictionary<string,object> inputs,  GenerationCallbacks callbacks ){
  
	        // Decide which endpoint:
	        string destin_url = $"{Connection_MGR.GEN3D_URL}/generate";

	        var jsonString = JsonConvert.SerializeObject(inputs);

	        yield return GpuFlowUnityHooks.PaceFromAddonHttpCoroutine(source: "gen3d", phase: "pre_generate");

	        // Start the generation, but don't yield yet:
	        _generateStatus = TaskStatus.PROCESSING;
	        _generateResponse = null;
	        _submit_crtn = StartCoroutine( GenerateSubmit_crtn(destin_url, jsonString) );

	        {//keep checking the progress:
	            if(_progress_crtn != null){  StopCoroutine(_progress_crtn); }
	            _progress_crtn = StartCoroutine( PollGenerationProgress(callbacks.onProgress) );
	            while(_generateStatus == TaskStatus.PROCESSING && !_cancelRequested){ yield return null; }
        
	            if(_progress_crtn!=null){ StopCoroutine(_progress_crtn); }
	            _progress_crtn = null;
	        }

	        if (_cancelRequested) {
	            _gen_or_resume_crtn = null;
	            yield break;
	        }

	        if (_generateResponse == null){
	            // We might have had an error or something else
	            callbacks.onError?.Invoke("No response from generation request");
	            _gen_or_resume_crtn = null;
	            yield break;
	        }

	        string download_endpoint = "";
	        switch (what){
	            case GenerateWhat.make_meshes_and_tex: download_endpoint = "/download/model"; break;
	            case GenerateWhat.retexture: download_endpoint = "/download/texture"; break;
	            default: Debug.LogError("unknown download endpoint"); break;
	        }

	        if (_generateStatus == TaskStatus.FAILED){// Show the error from the server (if any)
	            callbacks.onError?.Invoke($"Generation failed: {_generateResponse.message}");
	        }
	        else if (_generateStatus == TaskStatus.PREVIEW_READY && !_cancelRequested){
	            // Download previews then keep polling until COMPLETE/FAILED (do not leave GenerateButtons stuck).
	            yield return Gen_downloadPreviews(callbacks);
	            if (!_cancelRequested && _generateStatus == TaskStatus.PREVIEW_READY)
		            _generateStatus = TaskStatus.PROCESSING;
	            if (_progress_crtn != null){ StopCoroutine(_progress_crtn); }
	            _progress_crtn = StartCoroutine( PollGenerationProgress(callbacks.onProgress) );
	            while((_generateStatus == TaskStatus.PROCESSING || _generateStatus == TaskStatus.PREVIEW_READY)
	                  && !_cancelRequested){ yield return null; }
	            if(_progress_crtn!=null){ StopCoroutine(_progress_crtn); }
	            _progress_crtn = null;
	            if (_cancelRequested) {
		            _gen_or_resume_crtn = null;
		            yield break;
	            }
	            if (_generateStatus == TaskStatus.FAILED){
		            callbacks.onError?.Invoke($"Generation failed: {_generateResponse?.message}");
	            }
	            else if (_generateStatus == TaskStatus.COMPLETE){
		            _download_crtn = StartCoroutine(Gen_downloadFinalData(callbacks, download_endpoint));
		            yield return _download_crtn;
		            _download_crtn = null;
		            if (!_cancelRequested)
			            yield return GpuFlowUnityHooks.PaceFromAddonHttpCoroutine(source: "gen3d", phase: "post_download");
	            }
	            else {
		            callbacks.onError?.Invoke($"Generation stalled after preview (status={_generateStatus})");
	            }
	        }
	        else if (_generateStatus == TaskStatus.COMPLETE && !_cancelRequested){// Download the final mesh
	            _download_crtn = StartCoroutine(Gen_downloadFinalData(callbacks, download_endpoint));
	            yield return _download_crtn;
	            _download_crtn = null;
	            if (!_cancelRequested)
	                yield return GpuFlowUnityHooks.PaceFromAddonHttpCoroutine(source: "gen3d", phase: "post_download");
	        }
	        else if (!_cancelRequested) {
	            callbacks.onError?.Invoke($"Unexpected generation status: {_generateStatus}");
	        }
	        _gen_or_resume_crtn = null;
	    }


	    IEnumerator GenerateSubmit_crtn(string url, string jsonString){

	        using (UnityWebRequest www = UnityWebRequest.Post(url, jsonString, "application/json")){
	            yield return www.SendWebRequest();
	            if (_cancelRequested) {
	                _submit_crtn = null;
	                yield break;
	            }

	            if (www.result != UnityWebRequest.Result.Success){
	                Debug.LogError($"Generation request failed: {www.error}");
	                _generateResponse = null;
	                _generateStatus = TaskStatus.FAILED;
	                _submit_crtn = null;
	                yield break;
	            }
	            try{
	                _generateResponse = JsonConvert.DeserializeObject<GenerationResponse>(www.downloadHandler.text);
	                _generateStatus = _generateResponse.status;
	            }catch (Exception e){
	                Debug.LogError($"JSON parse failed: {e.Message}\n{www.downloadHandler.text}");
	                _generateResponse = null;
	                _generateStatus = TaskStatus.FAILED;
	                _generateResponse = null;
	            }
	        }
	        _submit_crtn = null;
	    }


	    // Poll /status (without trailing slash) until preview_ready, complete, or failed.
	    IEnumerator PollGenerationProgress(Action<float> onProgressUpdate){
	        float spacing_sec = 1f;

	        while (true){
	            if (_cancelRequested) break;
	            string endpoint = $"{Connection_MGR.GEN3D_URL}/status"; 
	            using (UnityWebRequest www = UnityWebRequest.Get(endpoint))
	            {
	                yield return www.SendWebRequest();
	                if (_cancelRequested) break;
	                if (www.result != UnityWebRequest.Result.Success){
	                    Debug.LogError($"PollGenerationProgress => WebRequest {www.result} ");
	                    if (_generateStatus == TaskStatus.PROCESSING)
		                    _generateStatus = TaskStatus.FAILED;
	                    break; 
	                }
	                GenerationStatus st = null;
	                try{
	                    st = JsonConvert.DeserializeObject<GenerationStatus>(www.downloadHandler.text);
	                }
	                catch (Exception e){
	                    Debug.LogError("PollGenerationProgress => JSON parse error: " + e.Message);
	                    if (_generateStatus == TaskStatus.PROCESSING)
		                    _generateStatus = TaskStatus.FAILED;
	                    break;
	                }
	                if (st == null){
	                    Debug.LogError("PollGenerationProgress => status payload null");
	                    if (_generateStatus == TaskStatus.PROCESSING)
		                    _generateStatus = TaskStatus.FAILED;
	                    break;
	                }

	                onProgressUpdate?.Invoke(Mathf.Clamp01(st.progress / 100f));
	                if (TryParseGen3DStatus(st.status, out TaskStatus polled))
		                _generateStatus = polled;
                
	                if(_generateStatus != TaskStatus.PROCESSING){ break; }
	            }
	            yield return new WaitForSeconds(spacing_sec);
	        }//end while
	        _progress_crtn = null;
	    }

	    static bool TryParseGen3DStatus(string raw, out TaskStatus status){
	        status = TaskStatus.PROCESSING;
	        if (string.IsNullOrEmpty(raw)) return false;
	        string s = raw.Trim();
	        if (string.Equals(s, "PROCESSING", StringComparison.OrdinalIgnoreCase)
	            || string.Equals(s, "processing", StringComparison.Ordinal)) {
	            status = TaskStatus.PROCESSING;
	            return true;
	        }
	        if (string.Equals(s, "PREVIEW_READY", StringComparison.OrdinalIgnoreCase)
	            || string.Equals(s, "preview_ready", StringComparison.Ordinal)) {
	            status = TaskStatus.PREVIEW_READY;
	            return true;
	        }
	        if (string.Equals(s, "COMPLETE", StringComparison.OrdinalIgnoreCase)
	            || string.Equals(s, "completed", StringComparison.OrdinalIgnoreCase)
	            || string.Equals(s, "complete", StringComparison.OrdinalIgnoreCase)) {
	            status = TaskStatus.COMPLETE;
	            return true;
	        }
	        if (string.Equals(s, "FAILED", StringComparison.OrdinalIgnoreCase)
	            || string.Equals(s, "failed", StringComparison.OrdinalIgnoreCase)
	            || string.Equals(s, "error", StringComparison.OrdinalIgnoreCase)) {
	            status = TaskStatus.FAILED;
	            return true;
	        }
	        return false;
	    }

    
	    IEnumerator ResumeAfterPreview_crtn(float meshSimplifyRatio, int textureSize, GenerationCallbacks callbacks){
	        yield return GpuFlowUnityHooks.PaceFromAddonHttpCoroutine(source: "gen3d", phase: "pre_resume");

	        //not yielding the coroutine, just starting and continuing
	        _generateStatus = TaskStatus.PROCESSING;
	        _generateResponse = null;
	        StartCoroutine( ResumeSubmit_crtn(meshSimplifyRatio, textureSize, callbacks) );
        
	        {//keep checking the progress:
	            if(_progress_crtn != null){  StopCoroutine(_progress_crtn); }
	            _progress_crtn = StartCoroutine( PollGenerationProgress(callbacks.onProgress) );
	            while(_generateStatus == TaskStatus.PROCESSING && !_cancelRequested){ yield return null; }
        
	            if(_progress_crtn!=null){ StopCoroutine(_progress_crtn); }
	            _progress_crtn = null;
	        }

	        if (_cancelRequested) {
	            _generateStatus = TaskStatus.FAILED;
	            _gen_or_resume_crtn = null;
	            yield break;
	        }

	        if (_generateStatus == TaskStatus.COMPLETE && !_cancelRequested){
	            yield return StartCoroutine(Gen_downloadFinalData(callbacks));
	            yield return GpuFlowUnityHooks.PaceFromAddonHttpCoroutine(source: "gen3d", phase: "post_resume_download");
	        }
	        else if (_generateStatus == TaskStatus.FAILED){
	            callbacks.onError?.Invoke("Resume generation => task failed");
	        }
	        _gen_or_resume_crtn = null;
	    }


	    IEnumerator ResumeSubmit_crtn(float meshSimplifyRatio, int textureSize, GenerationCallbacks callbacks){
	        string resumeUrl = $"{Connection_MGR.GEN3D_URL}/resume_from_preview" +
	            $"?mesh_simplify_ratio={meshSimplifyRatio}" +
	            $"&texture_size={textureSize}";

	        using (UnityWebRequest www = UnityWebRequest.PostWwwForm(resumeUrl, "")){
	            yield return www.SendWebRequest();
	            if (www.result != UnityWebRequest.Result.Success){
	                callbacks.onError?.Invoke("Resume generation failed: " + www.error);
	                _generateStatus = TaskStatus.FAILED;
	                _gen_or_resume_crtn = null;
	                yield break;
	            }

	            try{
	                _generateResponse = JsonConvert.DeserializeObject<GenerationResponse>(www.downloadHandler.text);
	                _generateStatus = _generateResponse.status;
	            }catch (Exception e){
	                Debug.LogError($"JSON parse failed: {e.Message}\n{www.downloadHandler.text}");
	                _generateResponse = null;
	                _generateStatus = TaskStatus.FAILED;
	                callbacks.onError?.Invoke("Resume generation: bad JSON from server");
	            }
	        }
	    }


	    IEnumerator InterruptOnServer(){
	        using (UnityWebRequest www = UnityWebRequest.PostWwwForm($"{Connection_MGR.GEN3D_URL}/interrupt", "")){
	            yield return www.SendWebRequest();
	            if (www.result == UnityWebRequest.Result.Success){
	                Debug.Log("Server interrupt request sent.");
	            }else{
	                Debug.LogError("Failed to send interrupt request: " + www.error);
	            }
	        }
	    }


	    IEnumerator Gen_downloadPreviews(GenerationCallbacks callbacks)
	    {
	        if (_generateResponse.preview_urls == null) yield break;

	        // The simplified server endpoints: /download/preview/gaussian, etc.
	        // No extra ID here; we assume a single generation context

	        // NOTICE: we only generated gaussian to save performance, so skip downloading the other 2:
	        string[] previewTypes = { "gaussian"/*, "mesh", "radiance" */};
	        foreach (string previewType in previewTypes)
	        {
	            string previewUrl = $"{Connection_MGR.GEN3D_URL}/download/preview/{previewType}";
	            using (UnityWebRequest www = UnityWebRequest.Get(previewUrl))
	            {
	                yield return www.SendWebRequest();
	                if (www.result == UnityWebRequest.Result.Success){
	                    callbacks.onPreviewReady?.Invoke(previewType, www.downloadHandler.data);
	                }else{
	                    Debug.LogWarning($"Failed to download {previewType} preview: {www.error}");
	                }
	            }
	        }//end foreach
	    }


	    //mesh, textures, etc.
	    IEnumerator Gen_downloadFinalData(GenerationCallbacks callbacks, string download_endpoint="/download/model")
	    {
	        using (UnityWebRequest www = UnityWebRequest.Get($"{Connection_MGR.GEN3D_URL}{download_endpoint}")){
	            yield return www.SendWebRequest();
	            if (_cancelRequested) {
	                yield break;
	            }

			if (www.result == UnityWebRequest.Result.Success){
				// Apply-data can throw (empty mesh, handler missing, import failure). Without this
				// catch the coroutine died silently: no onError, Cancel stuck, isBusy stuck —
				// or worse, onComplete reported success with nothing imported.
				bool dataOk = true;
				try {
					callbacks.onDataDownloaded?.Invoke(www.downloadHandler.data);
				} catch (Exception ex) {
					dataOk = false;
					Debug.LogError("Gen3D: applying downloaded data failed: " + ex.Message);
					callbacks.onError?.Invoke("downloaded data could not be applied: " + ex.Message);
				}
				if (dataOk){
					callbacks.onComplete?.Invoke();
				}
			}else{
				callbacks.onError?.Invoke($"Failed to download data: {www.error}");
			}
	        }
	    }//end()



	    void Awake(){
	        if (instance != null){ DestroyImmediate(this); return;}
	        instance = this;
	    }

	}
}//end namespace

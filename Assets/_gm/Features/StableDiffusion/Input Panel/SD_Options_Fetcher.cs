using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System;

namespace spz {

	//For example, after sending GET at the start of the app,
	//to find out what model is actually being used in web-ui.
	[Serializable]
	public class SD_OptionsPacket{
	    public string sd_model_checkpoint; //currently used neural net (base model) for stable diffusion. For example "ema-pruned-1.5"
	    public string sd_vae;
	    /// <summary>Classic A1111/Forge only. Neo has no such opt — must not be POSTed (KeyError).</summary>
	    public string hires_fix_refiner_pass;
	    public string upscaler_for_img2img;
	    public bool tiling;
	    /// <summary>Forge Neo extra weights (text encoder / VAE). Required for Flux.2 Klein.</summary>
	    public string[] forge_additional_modules;
	    //there are a lot more fields, see GET /sdapi/v1/options

	    public const string KleinTextEncoderModule = "qwen3_4b_flux2_klein.safetensors";
	    public const string KleinVaeModule = "flux2_klein_4b_vae.safetensors";

	    public static bool CheckpointNeedsKleinModules(string checkpointName){
	        if (string.IsNullOrEmpty(checkpointName)) return false;
	        string n = checkpointName.ToLowerInvariant();
	        // Modules are Klein-4B specific (qwen3_4b_flux2_klein + flux2_klein_4b_vae).
	        // Bare FLUX.2 / flux2 names must not pull those — different stack, would KeyError/assert.
	        bool hasKlein = n.Contains("klein");
	        bool hasFlux2 = n.Contains("flux2") || n.Contains("flux-2") || n.Contains("flux.2");
	        return hasKlein && hasFlux2;
	    }

	    /// <summary>
	    /// True Forge Neo <c>set_config</c> KeyErrors on unknown option keys.
	    /// Only emit keys SPZ intentionally manages (forge-neo-swap).
	    /// </summary>
	    public string ToOutboundJson(){
	        var parts = new System.Collections.Generic.List<string>(5);
	        if (!string.IsNullOrEmpty(sd_model_checkpoint))
	            parts.Add("\"sd_model_checkpoint\":" + QuoteJson(sd_model_checkpoint));
	        if (!string.IsNullOrEmpty(sd_vae))
	            parts.Add("\"sd_vae\":" + QuoteJson(sd_vae));
	        parts.Add("\"tiling\":" + (tiling ? "true" : "false"));
	        if (!string.IsNullOrEmpty(upscaler_for_img2img))
	            parts.Add("\"upscaler_for_img2img\":" + QuoteJson(upscaler_for_img2img));
	        if (forge_additional_modules != null){
	            var quoted = new string[forge_additional_modules.Length];
	            for (int i = 0; i < forge_additional_modules.Length; i++)
	                quoted[i] = QuoteJson(forge_additional_modules[i] ?? "");
	            parts.Add("\"forge_additional_modules\":[" + string.Join(",", quoted) + "]");
	        }
	        // Omit hires_fix_refiner_pass — absent from Neo shared.opts.data_labels.
	        return "{" + string.Join(",", parts) + "}";
	    }

	    static string QuoteJson(string s){
	        if (s == null) return "\"\"";
	        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
	    }
	}

	public class SD_Options_Fetcher : MonoBehaviour{
	    public static SD_Options_Fetcher instance { get; private set; }
	    public static Action<SD_OptionsPacket> Act_onOptionsRetrieved { get; set; }
	    public static Action<SD_OptionsPacket> Act_onWillSendOptions_AmmendPlz { get; set; }
	    public static Action<UnityWebRequest.Result,string> Act_OnSendOptions_done { get; set; }
	    public SD_OptionsPacket currentOptions { get; private set; }

	    bool _isSendingReceiving = false;
	    bool _wantsToSend_Asap = false;
	    bool _neverSentYet = true;

	    void Awake(){
	        if (instance != null){ DestroyImmediate(this); return; }
	        instance = this;
	    }

	    void Start(){
	        StartCoroutine(FetchOptionsPeriodically());
	    }

	    public void SubmitOptions_Asap(){
	        if(currentOptions == null) { 
	            return; //keep waiting until we receive options from webui at least once.
	        }
	        _wantsToSend_Asap = true;
	        if (!_isSendingReceiving){
	            StartCoroutine(SendOptionsRequest());
	        }
	    }

	    IEnumerator FetchOptionsPeriodically(){
	        while (true){
	            if (!_isSendingReceiving){
	                yield return StartCoroutine(FetchOptions());
	            }
	            yield return new WaitForSeconds(3);
	        }
	    }

	    IEnumerator FetchOptions(){
	        //Don't send network request to webui if rendering, else it seems to stuck it sometimes.
	        if(StableDiffusion_Hub.instance._generating){ yield break; }

	        _isSendingReceiving = true;
	        UnityWebRequest request = UnityWebRequest.Get(Connection_MGR.A1111_SD_API_URL + "/options");
	        yield return request.SendWebRequest();
	        bool isBad = request.result == UnityWebRequest.Result.ConnectionError;
	             isBad |= request.result == UnityWebRequest.Result.ProtocolError;
	        if (isBad){
	            _isSendingReceiving = false;
	            yield break;
	        }
	        //else, all good:

	        currentOptions = JsonUtility.FromJson<SD_OptionsPacket>(request.downloadHandler.text);
	        Act_onOptionsRetrieved?.Invoke(currentOptions);

	        if (_wantsToSend_Asap || _neverSentYet){// if never sent yet, send once.
	            yield return (SendOptionsRequest());// It gives ability for important settings to activate from start.
	        }                                       // For example, refiner wants to be in done the first pass, not after upscale.
	        _isSendingReceiving = false;
	    }

	    IEnumerator SendOptionsRequest(){
	        _neverSentYet = false;
	        _isSendingReceiving = true;
	        _wantsToSend_Asap = false;
        
	        Act_onWillSendOptions_AmmendPlz?.Invoke(currentOptions);

	        string json = currentOptions.ToOutboundJson();
	        byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(json);
	        UnityWebRequest request = new UnityWebRequest(Connection_MGR.A1111_SD_API_URL + "/options", "POST");
	        request.uploadHandler = new UploadHandlerRaw(jsonToSend);
	        request.downloadHandler = new DownloadHandlerBuffer();

	        request.SetRequestHeader("Content-Type", "application/json");

	        yield return request.SendWebRequest();
	        bool isBad = request.result == UnityWebRequest.Result.ConnectionError;
	             isBad |= request.result == UnityWebRequest.Result.ProtocolError;
	        if (isBad){
	            Debug.LogError("Error sending options: " + request.error);
	        }
	        Act_OnSendOptions_done?.Invoke(request.result, request.error);
	        _isSendingReceiving = false;
	    }
	}
}//end namespace

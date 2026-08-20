using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace spz {


	public class ControlNetUnit_DownloadHelper : MonoBehaviour{
	              public ControlNetUnit_UI myUnit => _myUnit;
	    [SerializeField] ControlNetUnit_UI _myUnit;
	    [SerializeField] CanvasGroup _contentsCanvGroup;
	    [Space(10)]
	    [SerializeField] TMP_Dropdown _modelsDropdown;
	    [SerializeField] SlideOut_Widget_UI _getMore_slideOut;
	    [Space(10)]
	    [SerializeField] Button _download_mandatoryDepthModel; //only shown if there are no models (happens after install).
	    [SerializeField] RectTransform _mandatDepthModel_progress; //we stretch it to show how much was downloaded.
	    [SerializeField] DownloadFile_if_NotYetExist _downloadModel_ifNotExist;//file that will perform actual downloading.
	    [SerializeField] GameObject _downloaded_mandatDepthModel_go;//small UI element, contains a message "please restart StableProjectorz".
   
	    public static bool isSomeUnit_downloadingModels { get; private set; } = false;
	    public static Action<ControlNetUnit_DownloadHelper> _onSomeUnit_startedDownloadModel { get; set; } = null;
	    public static Action<ControlNetUnit_DownloadHelper, float> _onSomeUnit_downloadModelPcnt { get; set; } = null;
	    /// <summary>Second arg is false when the download never started (or failed), so listeners can
	    /// restore their chrome without claiming the model was installed.</summary>
	    public static Action<ControlNetUnit_DownloadHelper, bool> _onSomeUnit_stoppedDownloadModel { get; set; } = null;
	    /// <summary>Unit that currently holds <see cref="isSomeUnit_downloadingModels"/> (null when free).</summary>
	    static ControlNetUnit_DownloadHelper _downloadGateOwner;

	    /// <summary>True when <paramref name="t"/> lives under the "download more" slide-out ownership root.</summary>
	    public bool OwnsTransform(Transform t) {
	        if (t == null || _getMore_slideOut == null) return false;
	        return t == _getMore_slideOut.transform || t.IsChildOf(_getMore_slideOut.transform);
	    }

	    public void OnRefreshInfoComplete(bool isNeedDownloadMandatoryModel ){
	        bool someDownloading = ControlNetUnit_DownloadHelper.isSomeUnit_downloadingModels;
	        _download_mandatoryDepthModel.gameObject.SetActive( isNeedDownloadMandatoryModel && !someDownloading);
	        _mandatDepthModel_progress.parent.gameObject.SetActive( isNeedDownloadMandatoryModel && someDownloading);
	    }

	     // The big large button that's shown instead of the models-dropdown if it's empty.
	    // Happens after installing the StableProjectors, when there are no control-net models initially.
	    void OnDownload_MandatoryDepthModel_button(){
	        if(ControlNetUnit_DownloadHelper.isSomeUnit_downloadingModels){ return; }

	        // FLUX.2-dev needs Fun-Union (~8GB), not the default SD1.5 depth .pth — open the HF page
	        // (same destination as ControlNet Download More) instead of starting a wrong/huge in-app pull.
	        try {
	            string sd = SD_InputPanel_UI.instance != null
	                ? SD_InputPanel_UI.instance.models?.selectedModel_name : null;
	            if (SD_OptionsPacket.CheckpointLooksFlux2Dev(sd)){
	                const string funUnionPage =
	                    "https://huggingface.co/alibaba-pai/FLUX.2-dev-Fun-Controlnet-Union/tree/main";
	                Application.OpenURL(funUnionPage);
	                if (SD_SysInfo_MGR.TryResolveControlNetModelsDir(
	                        "/models/ControlNet/",
	                        "/extensions/sd-webui-controlnet/models/",
	                        out string modelsDir,
	                        out _)){
	                    try { Application.OpenURL(modelsDir); } catch { /* folder open best-effort */ }
	                }
	                if (Viewport_StatusText.instance != null){
	                    Viewport_StatusText.instance.ShowStatusText(
	                        "FLUX.2-dev: download Fun-Union-2602 into models/ControlNet, then restart SPZ.",
	                        false, 8f, false);
	                }
	                return;
	            }
	        } catch { /* fall through to SD1.5 depth download */ }

	        ControlNetUnit_DownloadHelper.isSomeUnit_downloadingModels = true;//will prevent other controlnet units from downloading.
	        _downloadGateOwner = this;

	        _onSomeUnit_startedDownloadModel?.Invoke(this);
	        if (_downloadModel_ifNotExist == null){
	            AbortDownloadGate("ControlNet download helper missing — use Download More instead.");
	            return;
	        }
	        if (!_downloadModel_ifNotExist.DownloadFile("", "", onProgress)){
	            // Nothing is in flight, so no progress callback will ever reach 1.0 to reopen the gate.
	            // Leaving it shut disables every unit and blocks Gen Art (see StableDiffusion_Hub).
	            AbortDownloadGate("Couldn't start the ControlNet download — use Download More instead.");
	            return;
	        }

	        void onProgress(float pcnt01){
	            // Download_MGR reports -1 on network/write failure (never 1.0 unless bytes landed).
	            if (pcnt01 < 0f) {
	                AbortDownloadGate("ControlNet download failed — use Download More instead.");
	                return;
	            }
	            _onSomeUnit_downloadModelPcnt?.Invoke(this, pcnt01);
	            if(pcnt01<1.0f){ return;}
	            ControlNetUnit_DownloadHelper.isSomeUnit_downloadingModels = false;
	            _downloadGateOwner = null;
	            _onSomeUnit_stoppedDownloadModel?.Invoke(this, true);
	        }
	    }

	    // Reopens the shared gate and hands every unit back its chrome after a download that never began.
	    void AbortDownloadGate(string userMessage){
	        ControlNetUnit_DownloadHelper.isSomeUnit_downloadingModels = false;
	        _downloadGateOwner = null;
	        _onSomeUnit_stoppedDownloadModel?.Invoke(this, false);
	        if (Viewport_StatusText.instance != null)
	            Viewport_StatusText.instance.ShowStatusText(userMessage, false, 5f, false);
	    }

	    void OnSomeUnit_StartDownloadModel(ControlNetUnit_DownloadHelper who){
	        _download_mandatoryDepthModel.gameObject.SetActive(false);//keep button hidden.
	        _mandatDepthModel_progress.parent.gameObject.SetActive(true);//ensure progress is shown (instead of button).
	        _contentsCanvGroup.interactable = false;
	    }

	    void OnSomeUnit_StopDownloadModel(ControlNetUnit_DownloadHelper who, bool didDownload){
	        _mandatDepthModel_progress.parent.gameObject.SetActive(false);
	        _contentsCanvGroup.interactable = true;
	        // Failed start: keep the CTA so the user can retry, and don't claim a model was installed.
	        _download_mandatoryDepthModel.gameObject.SetActive(!didDownload);
	        if (didDownload)
	            _downloaded_mandatDepthModel_go.SetActive(true);//tells user to restart StableProjectorz, to refresh controlnets.
	    }

	    void OnSomeUnit_DownloadModelPcnt(ControlNetUnit_DownloadHelper who, float progress01){
	        _mandatDepthModel_progress.transform.localScale = new Vector3(progress01, 1, 1);
	    }

	    /// <summary>
	    /// Nomad: readable list copy + row separation. Unit-wide CompactToolLabel uppercase/truncate
	    /// was stacking IP Adapter / flux / sdxl lines into illegible clutter.
	    /// </summary>
	    public void ApplyThemeTokens() {
	        if (_getMore_slideOut != null)
	            SpzUiThemeOps.ApplyDownloadMoreSlideChrome(_getMore_slideOut.transform);
	        // Mandatory depth download is a unit-level CTA — may sit outside the slide-out root.
	        if (_download_mandatoryDepthModel == null) return;
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            SpzUiThemeOps.RestoreBoundChromeUnder(_download_mandatoryDepthModel.transform);
	            return;
	        }
	        var t = SpzUiThemeOps.Active;
	        SpzUiThemeOps.EnsureSelectableHitFace(_download_mandatoryDepthModel);
	        if (SpzUiThemeOps.IsAuthoredIconFace(_download_mandatoryDepthModel.targetGraphic)) {
	            if (_download_mandatoryDepthModel.targetGraphic is Image iconFace)
	                SpzUiThemeOps.ApplyBoundChromeIconTint(iconFace, t.iconTint);
	        } else {
	            SpzUiThemeOps.ApplyBoundChromeSelectable(_download_mandatoryDepthModel, t.controlBg, t.accent);
	        }
	        foreach (var tmp in _download_mandatoryDepthModel.GetComponentsInChildren<TextMeshProUGUI>(true)) {
	            if (tmp != null)
	                SpzUiThemeOps.ApplyBoundChromeReadableBodyTmp(tmp, t.textPrimary, 12f);
	        }
	        SpzUiThemeOps.ClearNonFaceRaycastsForTheme(_download_mandatoryDepthModel);
	    }

    
	    void Update(){
	        _getMore_slideOut._dontAutoHide = true;
	        _getMore_slideOut.Toggle_if_Different(_modelsDropdown.IsExpanded);
	    }


	    void Awake(){
	        _download_mandatoryDepthModel.onClick.AddListener( OnDownload_MandatoryDepthModel_button );
	        _onSomeUnit_startedDownloadModel += OnSomeUnit_StartDownloadModel;
	        _onSomeUnit_stoppedDownloadModel += OnSomeUnit_StopDownloadModel;
	        _onSomeUnit_downloadModelPcnt  += OnSomeUnit_DownloadModelPcnt;
	        SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
	        ApplyThemeTokens();
	    }

	    void OnDestroy(){
	        // Mid-flight destroy of the gate owner left Gen Art blocked forever (Hub reads the static flag).
	        // Abort while still subscribed so peer units + this unit restore interactable chrome.
	        if (ReferenceEquals(_downloadGateOwner, this))
	            AbortDownloadGate("ControlNet download cancelled — unit closed.");
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	        _onSomeUnit_startedDownloadModel -= OnSomeUnit_StartDownloadModel;
	        _onSomeUnit_stoppedDownloadModel -= OnSomeUnit_StopDownloadModel;
	        _onSomeUnit_downloadModelPcnt -= OnSomeUnit_DownloadModelPcnt;
	    }
	}
}//end namespace

using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System;
using UnityEngine.EventSystems;
using UnityEngine.Networking;

namespace spz {

	// can either be for connecting to server that generates textures (A1111, Forge), 
	// or for connecting to server that generates 3d and meshes.
	// Knows about the connection button and the ui-panel with IP+Port inputs.
	// Will hide its panel and button depending on the 'DimensionMode_MGR' - either 3D, 2D or UV representations.
	public class ConnectionPanel_UI : MonoBehaviour{
    
	    [SerializeField] GraphicRaycaster _raycaster; //will raycast towards the help button
	    [SerializeField] NonDrawingGraphic _hoverSurface;
	    [SerializeField] Button _openPanel_button;
	    [SerializeField] RectTransform _panel; // Contains fields for IP, port, etc.
	    [Space(10)]
	    [SerializeField] TMP_InputField _ip_text;
	    [SerializeField] IntegerInputField _port_text; // Assuming this is a custom component
	    [SerializeField] Image _connectionIcon; // Shows status of connection (red/green)
	    [SerializeField] TextMeshProUGUI _dim_text; // Shows 2D or 3D (stable diffusion or Trellis)
	    [SerializeField] Button _resetToDefault_button;//we set ip and port to usual ones.
	    [Space(10)]
	    [SerializeField] string IP_PlayerPrefs_KEY = "StableDiffusionIP"; //player prefs (for saving settings to disk)
	    [SerializeField] string PORT_PlayerPrefs_KEY = "StableDiffusionPort";
	    [Space(10)]
	    [SerializeField] string _default_ip = "127.0.0.1";
	    [SerializeField] string _default_port = "7860";
	    enum ConnectionPanel_Kind{
	        StableDiffusion, /*for 2D generation*/
	        Trellis,/*for 3d generation*/
	    }
	    [Space(10)]
	    [SerializeField] ConnectionPanel_Kind _panelKind;

	    bool _did_init = false;
	    Coroutine _connectionCheckCoroutine;
	    float _time_clickedOpenPanelButton;
	    string _url_for_ping;

	    public string ip_text => _ip_text.text;
	    public int port => _port_text.recentVal;
	    public string ip_and_port => ip_text + ":" + port;
	    public Action<string,int> _connectDetailsChanged { get; set; } = null;

	    public bool isConnected { get; private set; } = false;

	    /// <summary>Last SD ping: Cloud Inference shim vs local Forge/WebUI. Trellis unused.</summary>
	    public bool isCloudInferenceConnected { get; private set; } = false;

	    string _authoredDimText;
	    // Sentence case + middot: keep chip identity (2D) and show source as metadata.
	    const string EmblemCloud = "2D \u00b7 Cloud";
	    const string EmblemLocal = "2D \u00b7 Local";


    
	    //after we launch stableProjectorz, there is some grace-period while the CMD webui window will activate and become ready.
	    bool isStill_warmingUp()=> Time.unscaledTime < 30;



	    void Update(){
	        ShowHide_ConnButton();
	        if(_panel.gameObject.activeSelf == false){ return; }
	        float elapsed = Time.time - _time_clickedOpenPanelButton;
	        if(elapsed < 0.1f){ return; }
	        if(!IsHovering_Panel()){ _panel.gameObject.SetActive(false); }
	    }


	    void ShowHide_ConnButton(){
	        // show or hide our connection button, if we are for StableDiffusion, but user is generating 3D, etc.
	        if( DimensionMode_MGR.instance == null || _openPanel_button == null ){ return; }
	        switch (DimensionMode_MGR.instance._dimensionMode){
	            case DimensionMode.dim_uv:
	            case DimensionMode.dim_sd:
	                _openPanel_button.gameObject.SetActive(_panelKind == ConnectionPanel_Kind.StableDiffusion);
	                break;
	            case DimensionMode.dim_gen_3d:
	                _openPanel_button.gameObject.SetActive(_panelKind == ConnectionPanel_Kind.Trellis);
	                break;
	        }
	    }

    
	    bool IsHovering_Panel(){
	        if (_panel != null && _panel.gameObject.activeInHierarchy){
	            // Prefer geometric contains so TMP/IP/port children keep the panel open while editing.
	            var panelRt = _panel.transform as RectTransform;
	            if (panelRt != null && RectTransformUtility.RectangleContainsScreenPoint(panelRt, Input.mousePosition, null))
	                return true;
	        }

	        if (_raycaster == null){ return false; }
	        PointerEventData eventData = new PointerEventData( EventSystem.current );
	        eventData.position = Input.mousePosition;

	        List<RaycastResult> results = new List<RaycastResult>();
	        _raycaster.Raycast(eventData, results);

	        foreach (var result in results){
	            if (result.gameObject == null) continue;
	            NonDrawingGraphic g = result.gameObject.GetComponent<NonDrawingGraphic>();
	            if (g==_hoverSurface){ return true; }
	            if (_hoverSurface != null && result.gameObject.transform.IsChildOf(_hoverSurface.transform))
	                return true;
	            if (_panel != null && result.gameObject.transform.IsChildOf(_panel.transform))
	                return true;
	        }
	        return false;
	    }


	    void OnOpenPanel_Button(){
	        _panel.gameObject.SetActive(true);
	        _time_clickedOpenPanelButton = Time.time;
	        ApplyThemeTokens();
	    }

    
	    IEnumerator CheckConnection(bool setColorToPending_once ){
	        Color pendingColor = new Color(1, 0.8f, 0, 1);//orange-yellow

	        float spacing = 0.5f;

	        while (true){
	            string url_for_ping = where_to_ping(this);

	            // Empty URL only — Trellis/Gen3D must ping even when StableDiffusion_Hub is late/null.
	            if (string.IsNullOrEmpty(url_for_ping)){
	                yield return new WaitForSeconds(spacing);
	                continue;
	            }
	            // SD panel still needs the hub for generate-aware timeouts; Trellis does not.
	            bool sdNeedsHub = _panelKind == ConnectionPanel_Kind.StableDiffusion
	                              && StableDiffusion_Hub.instance == null;
	            if (sdNeedsHub){
	                yield return new WaitForSeconds(spacing);
	                continue;
	            }

	            using (UnityWebRequest request = UnityWebRequest.Get(url_for_ping)){

	                bool noConn_butJustStarted =  !isConnected && isStill_warmingUp();

	                if (setColorToPending_once || noConn_butJustStarted ){
	                    setColorToPending_once = false;
	                    SetStatusColor( pendingColor );
	                }
	                // Increase threshold 'connected'/not to 20 during generation, or if already connected.
	                // Because people had disconnects during generation if just 4.
	                // So if not connected -> short timeout
	                // If generating or conencted -> longer timeout (trusting more that we're still connected)
	                request.timeout = 4;
	                request.timeout = isConnected? 12 : request.timeout;
	                bool generating = _panelKind == ConnectionPanel_Kind.StableDiffusion
	                                  && StableDiffusion_Hub.instance != null
	                                  && StableDiffusion_Hub.instance._generating;
	                request.timeout = generating ? 25 : request.timeout;
	                // Cloud Inference is a local shim — keep ping short so Disconnect does not leave
	                // SERV green for the 12–25s local-Forge trust window while :7860 is already down.
	                if (isCloudInferenceConnected)
	                    request.timeout = 2;
	                yield return request.SendWebRequest();

	                if (request.result == UnityWebRequest.Result.Success){//connection successful:
	                    PlayerPrefs_SaveConnDetails();
	                    SetStatusColor( Color.green );
	                    isConnected = true;
	                    bool cloud = _panelKind == ConnectionPanel_Kind.StableDiffusion
	                                 && PingJsonMarksCloudInference(request.downloadHandler != null
	                                     ? request.downloadHandler.text : null);
	                    ApplySdInferenceEmblem(connected: true, cloudInference: cloud);
	                }
	                else{//Connection failed:
	                    SetStatusColor( isStill_warmingUp()?  pendingColor : Color.red );
	                    isConnected = false;
	                    ApplySdInferenceEmblem(connected: false, cloudInference: false);
	                }
	            }
	            yield return new WaitForSeconds(spacing); // Check every 0.5 seconds
	        }
	        //_connectionCheckCoroutine = null;
	    }

    
	    /// <summary>
	    /// Cloud Inference shim ping JSON includes <c>cloud_inference: true</c>. Local Forge does not.
	    /// HTML / empty / non-JSON → not cloud (treat as local or down).
	    /// </summary>
	    public static bool PingJsonMarksCloudInference(string json) {
	        if (string.IsNullOrWhiteSpace(json))
	            return false;
	        int brace = json.IndexOf('{');
	        if (brace < 0)
	            return false;
	        try {
	            var dto = JsonUtility.FromJson<CloudInferencePingDto>(json.Substring(brace));
	            return dto != null && dto.cloud_inference;
	        } catch {
	            return false;
	        }
	    }

	    [Serializable]
	    class CloudInferencePingDto {
	        public bool cloud_inference;
	    }

	    /// <summary>
	    /// SD SERV chip: authored "2D" when down. When connected, keep 2D and append
	    /// Cloud vs Local so health color (green/red) is not mistaken for a local GPU.
	    /// </summary>
	    public void ApplySdInferenceEmblem(bool connected, bool cloudInference) {
	        if (_panelKind != ConnectionPanel_Kind.StableDiffusion || _dim_text == null)
	            return;
	        RememberAuthoredDimText();
	        isCloudInferenceConnected = connected && cloudInference;
	        if (!connected) {
	            if (!string.IsNullOrEmpty(_authoredDimText))
	                _dim_text.text = _authoredDimText;
	            return;
	        }
	        _dim_text.text = cloudInference ? EmblemCloud : EmblemLocal;
	    }

	    /// <summary>
	    /// Cloud Inference Disconnect stops the local shim immediately; do not wait for the ping loop
	    /// to time out before SERV goes red and the Cloud emblem clears (Hub CN skip uses the emblem).
	    /// </summary>
	    public void ForceMarkDisconnected() {
	        isConnected = false;
	        ApplySdInferenceEmblem(connected: false, cloudInference: false);
	        SetStatusColor(Color.red);
	    }

	    void RememberAuthoredDimText() {
	        if (_dim_text == null || !string.IsNullOrEmpty(_authoredDimText))
	            return;
	        string t = _dim_text.text ?? "";
	        if (t != EmblemCloud && t != EmblemLocal)
	            _authoredDimText = t;
	        if (string.IsNullOrEmpty(_authoredDimText))
	            _authoredDimText = "2D";
	    }

	    /// <summary>Missing/destroyed status graphics must not kill the ping loop (it never restarts).</summary>
	    void SetStatusColor( Color c ){
	        if (_dim_text != null) _dim_text.color = c;
	        if (_connectionIcon != null) _connectionIcon.color = c;
	    }

	    string where_to_ping(ConnectionPanel_UI panel){
	        switch (_panelKind){
	            case ConnectionPanel_Kind.StableDiffusion:
	                return Connection_MGR.A1111_INTERNAL_API_URL + "/ping";
	            case ConnectionPanel_Kind.Trellis:
	                return Connection_MGR.GEN3D_URL + "/ping";
	        }
	        return "";
	    }


	    public void Init_Maybe(){ 
	        if(_did_init){ return; }
	        _did_init = true;
	        //enable panel so that it can run Awake(). That way its text and input field manage to intialize its values.
	        //This is important for our IP_AND_PORT and other static variables that use its child components.
	        _panel.gameObject.SetActive(true);
	        _panel.gameObject.SetActive(false);

	        // Occasionally, those values are somehow not set (even after _panel.gameObject.SetActive(true).
	        // This especially happens in new users, during their first launch.
	        // It only connects after we manually click on the red-connection icon, which makes it green.
	        // So, I suspect these values are incorrect until the panel, opens. Let's manually set them here, just in case:
	        // Feb 2024
	        if (string.IsNullOrEmpty(_ip_text.text)){ _ip_text.text = "127.0.0.1"; }
	        if(_port_text.recentVal==0){ _port_text.SetValue( _panelKind==ConnectionPanel_Kind.StableDiffusion?"7860":"7960"); }
	        PlayerPrefs_LoadConnDetails();
	        RememberAuthoredDimText();

	        // Add listeners for changes in the IP and port input fields:
	        _ip_text.onValueChanged.AddListener(s=>{
	            _connectDetailsChanged?.Invoke(ip_text, port);
	            PlayerPrefs_SaveConnDetails();
	        });
	        _port_text.onValidInput.AddListener(i=>{
	            _connectDetailsChanged?.Invoke(ip_text, port);
	            PlayerPrefs_SaveConnDetails();
	        });
	    }


	    void PlayerPrefs_LoadConnDetails(){
	        if (PlayerPrefs.HasKey(IP_PlayerPrefs_KEY)){
	            _ip_text.text =  PlayerPrefs.GetString(IP_PlayerPrefs_KEY);
	        }
	        if (PlayerPrefs.HasKey(PORT_PlayerPrefs_KEY)){
	            string savedPort = PlayerPrefs.GetString(PORT_PlayerPrefs_KEY);
	            // Revert experimental :8188/:7878 prefs back to classic Forge/WebUI :7860.
	            if (_panelKind == ConnectionPanel_Kind.StableDiffusion
	                && (string.Equals(savedPort, "8188", StringComparison.Ordinal)
	                    || string.Equals(savedPort, "7878", StringComparison.Ordinal))) {
	                UnityEngine.Debug.Log(
	                    $"[ConnectionPanel] Migrating Stable Diffusion port prefs {savedPort} → {_default_port} (Forge listens on {_default_port}).");
	                savedPort = _default_port;
	                PlayerPrefs.SetString(PORT_PlayerPrefs_KEY, savedPort);
	                PlayerPrefs.Save();
	            }
	            _port_text.SetValueWithoutNotify(savedPort);
	        }
	    }


	    void PlayerPrefs_SaveConnDetails(){
	        // Save the new IP and Port to PlayerPrefs
	        PlayerPrefs.SetString(IP_PlayerPrefs_KEY, _ip_text.text);
	        PlayerPrefs.SetString(PORT_PlayerPrefs_KEY, _port_text.recentVal.ToString() );
	        PlayerPrefs.Save();
	    }

    
	    void OnResetToDefault_button(){
	        _ip_text.SetTextWithoutNotify(_default_ip);
	        _port_text.SetValueWithoutNotify(_default_port);
	        _connectDetailsChanged?.Invoke(ip_text, port);
	        PlayerPrefs_SaveConnDetails();
	    }


	    public void Save( StableProjectorz_SL spz ){
	        spz.connectionPanel = new ConnectionPanel_SL();
	        //COMMENTED OUT, KEPT FOR PRECAUTION:  do NOT save IP and Port of people (can expose person if they share save file)
	        //          spz.connectionPanel.ip = _ip_text.text;
	        //          spz.connectionPanel.port = _port_text.recentVal;
	    }

	    public void Load( StableProjectorz_SL spz ){
	        Init_Maybe();
	        // _ip_text.text = spz.connectionPanel.ip;
	        // _port_text.SetValue( spz.connectionPanel.port.ToString() );
	    }

    
	    void Awake(){
	        if (_resetToDefault_button != null)
	            _resetToDefault_button.onClick.AddListener( OnResetToDefault_button );
	        if (_openPanel_button != null)
	            _openPanel_button.onClick.AddListener( OnOpenPanel_Button );
	        SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
	        ApplyThemeTokens();
	    }

	    void OnDestroy() {
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	    }

	    /// <summary>
	    /// Colors SD SERV / 3D SERV chrome and the IP panel.
	    /// Leaves <see cref="_dim_text"/> / <see cref="_connectionIcon"/> RGB to connectivity logic (green/red/orange).
	    /// </summary>
	    void ApplyThemeTokens() {
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            // Full unwind: ColorBlocks / TMP tracking / rounded sprites — not colors alone.
	            SpzUiThemeOps.RestoreBoundChromeUnder(transform);
	            if (_panel != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_panel.transform);
	            // Open/reset may sit outside this host (status strip vs modal panel).
	            if (_openPanel_button != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_openPanel_button.transform);
	            if (_resetToDefault_button != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_resetToDefault_button.transform);
	            // Explicit leave: status icon must not stay raycast-off after Nomad→builtin
	            // (gen path: open SD SERV panel → connect → is_sd_connected → isCanGenerate).
	            if (_connectionIcon != null) {
	                SpzUiThemeOps.HideAuthoredGraphicForTheme(_connectionIcon);
	                _connectionIcon.raycastTarget = true;
	            }
	            ApplySdInferenceEmblem(isConnected, isCloudInferenceConnected);
	            return;
	        }
	        var t = SpzUiThemeOps.Active;
	        if (_openPanel_button != null) {
	            SpzUiThemeOps.EnsureSelectableHitFace(_openPanel_button);
	            SpzUiThemeOps.ApplyBoundChromeSelectable(_openPanel_button, t.controlBg, t.accent);
	            if (_openPanel_button.targetGraphic is Image face)
	                SpzUiThemeOps.ApplyRoundedControlSprite(face, markEligible: true);
	            // Never stamp Globe Monolith — it overlays green "2D"/"3D" status as a circle (SERV litmus).
	            Transform mono = SpzUiThemeOps.FindDirectChildIncludingInactive(
	                _openPanel_button.transform, "MonolithLineIcon");
	            if (mono != null)
	                mono.gameObject.SetActive(false);
	            SpzUiThemeOps.ClearNonFaceRaycastsForTheme(_openPanel_button);
	        }
	        // Do not recolor _dim_text / _connectionIcon — CheckConnection owns live status green/red.
	        // Apply Nomad tracking/outline without replacing status RGB.
	        // Live SERV status — ReadableBody + no Ellipsis. Ellipsis on the 51px chip clipped
	        // "2D · Local" to "2D - Lo" under the signal bars.
	        if (_dim_text != null && SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            Color status = _dim_text.color;
	            SpzUiThemeOps.ApplyBoundChromeReadableBodyTmp(_dim_text, status, 9f);
	            _dim_text.color = status;
	            _dim_text.raycastTarget = false;
	            _dim_text.enableWordWrapping = false;
	            _dim_text.overflowMode = TextOverflowModes.Overflow;
	            ApplySdInferenceEmblem(isConnected, isCloudInferenceConnected);
	        }
	        if (_connectionIcon != null) {
	            // Snapshot BEFORE hide/raycast so Restore SPZ can unwind (never snapshot after = false).
	            SpzUiThemeOps.SnapshotAuthoredGraphicForTheme(_connectionIcon);
	            _connectionIcon.raycastTarget = false;
	            // Bars overlay Local/Cloud on the tiny chip; status color stays on the caption.
	            SpzUiThemeOps.HideAuthoredGraphicForTheme(_connectionIcon);
	        }
	        if (_panel != null) {
	            var panelImg = _panel.GetComponent<Image>();
	            if (panelImg != null)
	                SpzUiThemeOps.ApplyBoundChromeGraphic(panelImg, t.panelBg);
	        }
	        if (_ip_text != null) {
	            var bg = _ip_text.GetComponent<Image>();
	            if (bg != null)
	                SpzUiThemeOps.ApplyBoundChromeGraphic(bg, t.fieldBg);
	            if (_ip_text.textComponent != null)
	                SpzUiThemeOps.ApplyBoundChromeTmp(_ip_text.textComponent, t.textPrimary);
	            if (_ip_text.placeholder is TMPro.TMP_Text ph)
	                SpzUiThemeOps.ApplyBoundChromeTmp(ph, t.textMuted);
	        }
	        if (_port_text != null) {
	            var portField = _port_text.GetComponentInChildren<TMP_InputField>(true);
	            if (portField != null) {
	                var portBg = portField.GetComponent<Image>();
	                if (portBg != null)
	                    SpzUiThemeOps.ApplyBoundChromeGraphic(portBg, t.fieldBg);
	                if (portField.textComponent != null)
	                    SpzUiThemeOps.ApplyBoundChromeTmp(portField.textComponent, t.textPrimary);
	                if (portField.placeholder is TMPro.TMP_Text portPh)
	                    SpzUiThemeOps.ApplyBoundChromeTmp(portPh, t.textMuted);
	            }
	        }
	        if (_resetToDefault_button != null) {
	            SpzUiThemeOps.EnsureSelectableHitFace(_resetToDefault_button);
	            SpzUiThemeOps.ApplyBoundChromeSelectable(_resetToDefault_button, t.controlBg, t.accent);
	            if (_resetToDefault_button.targetGraphic is Image resetImg) {
	                SpzUiThemeOps.ApplyRoundedControlSprite(resetImg, markEligible: true);
	            }
	            SpzUiThemeOps.ClearNonFaceRaycastsForTheme(_resetToDefault_button);
	            var resetLabel = _resetToDefault_button.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
	            if (resetLabel != null)
	                SpzUiThemeOps.ApplyBoundChromeReadableBodyTmp(resetLabel, t.textPrimary, 11f);
	            SpzUiThemeOps.ClearNonFaceRaycastsForTheme(_resetToDefault_button);
	        }
	    }

	    void Start(){
	        // Boot race: Coroutines_MGR may arrive a frame later — retry so ping is not permanently skipped.
	        StartCoroutine( EnsureConnectionCheckStarted_crtn() );
	    }

	    IEnumerator EnsureConnectionCheckStarted_crtn(){
	        while (Coroutines_MGR.instance == null)
	            yield return null;
	        if (_connectionCheckCoroutine != null) yield break;
	        _connectionCheckCoroutine = Coroutines_MGR.instance.StartCoroutine( CheckConnection(setColorToPending_once:true) );
	    }
	}
}//end namespace

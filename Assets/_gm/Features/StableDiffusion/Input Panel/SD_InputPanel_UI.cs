using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace spz {

	// Contains positive prompt, negative prompt, etc.
	// Also, capable of moving away the rectransform and shrinking the size of this panel.
	// This is useful when user wants to "free-up" the UI space.
	public class SD_InputPanel_UI : MonoBehaviour{
	    public static SD_InputPanel_UI instance { get; private set; } = null;

	              public ScrollRect inputColumn_scrollRect => _inputColumn_scrollRect;
	    [SerializeField] ScrollRect _inputColumn_scrollRect;

	              public SD_Neural_Models models  => SD_Neural_Models.instance;
	              public SD_VAE sd_vae            => SD_VAE.instance;
	              public SD_Upscalers sd_upscaler => SD_Upscalers.instance;
	              public SD_Samplers samplers     => SD_Samplers.instance;
	              public SD_Scheduler scheduler   => SD_Scheduler.instance;

	              public CircleSlider_Snapping_UI sampleSteps_slider => _sampleSteps_slider;
	    [SerializeField] CircleSlider_Snapping_UI _sampleSteps_slider;
	              public CircleSlider_Snapping_UI CFG_scale_slider => _CFG_scale_slider;
	    [SerializeField] CircleSlider_Snapping_UI _CFG_scale_slider;
	              public IntegerInputField seed_intField => _seed_intField;
	    [SerializeField] IntegerInputField _seed_intField;
	              public Animation seed_intFieldAnim => _seed_intFieldAnim;
	    [SerializeField] Animation _seed_intFieldAnim;

	    [SerializeField] IntegerInputField _width_input;
	    [SerializeField] IntegerInputField _height_input;
	    [SerializeField] IntegerInputField _batch_count_input;
	    [SerializeField] IntegerInputField _batch_size_input;
    
	    [Space(10)]
	    [SerializeField] Button _resolutionPreset_512;
	    [SerializeField] Button _resolutionPreset_768;
	    [SerializeField] Button _resolutionPreset_1024;
	    [SerializeField] Button _resolutionPreset_1536;
	    [SerializeField] Button _resolutionPreset_2048;

	    public int width  => _width_input.recentVal;
	    public int height => _height_input.recentVal;
	    public int batch_count => _batch_count_input.recentVal;
	    public int batch_size => _batch_size_input.recentVal;


	    [Space(10)]
	    //for the entire panel. We can move it, to "hide" this panel, moving it out of the way.
	    [SerializeField] RectTransform _movableRectTransform;
	    [SerializeField] LayoutElement _layoutElem;
	    [SerializeField] Vector2 _minAndPreferredWidth_whenHidden;

	    int _zoomRes_numHints_shown = 0;
	    int _resolutionSyncTicket = 0;


	    public Vector2 widthHeight(){
	        #if UNITY_EDITOR
	        if(UnityEditor.EditorApplication.isPlaying==false){ return Vector2.one*512; }
	        #endif
	        return new Vector2(width, height); 
	    }

	    /// <summary>Set generation resolution fields from code (used by viewport full-view enter/exit restore).</summary>
	    public void SetWidthHeight(int widthPx, int heightPx) {
	        if (_width_input == null || _height_input == null) {
	            return;
	        }
	        _width_input.SetValue(Mathf.Max(64, widthPx).ToString());
	        _height_input.SetValue(Mathf.Max(64, heightPx).ToString());
	        RefreshResolutionPresetChrome();
	    }

	    /// <summary>Deferred adaptive viewport-mode apply (FULL SRN vs OPEN RIGHT) after layout settles.</summary>
	    public void ScheduleAdaptiveResolutionFromViewportModeNextFrame() {
		    int ticket = ++_resolutionSyncTicket;
		    if (isActiveAndEnabled) {
			    StartCoroutine(CoAdaptiveResolutionAfterLayout(ticket));
		    }
		    else {
			    ViewportFullViewOnScreen_Driver.ApplyAdaptiveResolutionToSdInputsForCurrentSideState();
		    }
	    }

	    IEnumerator CoAdaptiveResolutionAfterLayout(int ticket) {
		    // Next player loop + end of frame so skeleton/main-viewport placement has settled.
		    yield return null;
		    yield return new WaitForEndOfFrame();
		    if (this == null) { yield break; }
		    if (ticket != _resolutionSyncTicket) { yield break; }
		    ViewportFullViewOnScreen_Driver.ApplyAdaptiveResolutionToSdInputsForCurrentSideState();
	    }

	    /// <summary>OPEN RIGHT path wrapper; now routed through unified adaptive scheduling.</summary>
	    public void ScheduleOpenRightMainSlotGenResolutionNextFrame() {
		    ScheduleAdaptiveResolutionFromViewportModeNextFrame();
	    }

	    /// <summary>FULL SRN path wrapper; now routed through unified adaptive scheduling.</summary>
	    public void ScheduleFullSrnScreenResolutionApplyNextFrame() {
		    ScheduleAdaptiveResolutionFromViewportModeNextFrame();
	    }

	    public void PasteSeedValue(int seed){
	        _seed_intField.SetValue( seed.ToString() );
	        _seed_intFieldAnim.Play();
	        _inputColumn_scrollRect.GetComponent<ScrollRect_AutoScroll>().ScrollToEnd(0.25f, true);
	    }


	    //helpful if we resized the entire window.
	    void Stretch(){
	        Vector2 parentSize = (_movableRectTransform.parent as RectTransform).rect.size;
	        _movableRectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, parentSize.x);
	    }

    
	    void OnResolutionPresetButton(int res){
	        _width_input.SetValue(res.ToString());
	        _height_input.SetValue(res.ToString());
	        RefreshResolutionPresetChrome();
	        if(res > 1024){
	            string msg = "Careful!  SD 1.5 is made for generating 512,  SDXL for 1024.  Might be slow + give weird results."
	                        + "\nEven if 512, it's only for one of the sides!  So the total texture will end up at least 2k anyway.";
	            Viewport_StatusText.instance.ShowStatusText(msg, false, 11, false);
	        }
	        else if(res >= 768 && _zoomRes_numHints_shown<3){
	            _zoomRes_numHints_shown++;
	            string msg = "Always zoom close to the 3D object,  to capture more pixels of your projections.\n" +
	                         "Maybe increase the total resolution of the scene  (use -+ next to the 'Save 2K')";
	            Viewport_StatusText.instance.ShowStatusText(msg, false, 7, false);
	        }
	    }
    


	    public void Save( SD_GenSettingsInput_UI fill_this ){
	        models.Save(fill_this);
	        samplers.Save(fill_this);
	        sd_upscaler.Save(fill_this);
	        fill_this.sampleSteps = Mathf.RoundToInt(_sampleSteps_slider.value);
	        fill_this.cfg_scale = _CFG_scale_slider.value;
	        fill_this.seed = _seed_intField.recentVal;

	        fill_this.width = width;
	        fill_this.height = height;
	        fill_this.batch_count = batch_count;
	        fill_this.batch_size = batch_size;
	    }

	    public void Load( StableProjectorz_SL spz ){
	        if (spz.sd_genSettingsInput == null) return;
	        models.Load(spz.sd_genSettingsInput);
	        samplers.Load(spz.sd_genSettingsInput);
	        sd_upscaler.Load(spz.sd_genSettingsInput);
	        _sampleSteps_slider.SetSliderValue(spz.sd_genSettingsInput.sampleSteps, true);
	        _CFG_scale_slider.SetSliderValue(spz.sd_genSettingsInput.cfg_scale, true);
        
	        _seed_intField.SetValue( spz.sd_genSettingsInput.seed.ToString() );

	        _width_input.SetValue( spz.sd_genSettingsInput.width.ToString() );
	        _height_input.SetValue( spz.sd_genSettingsInput.height.ToString() );

	        _batch_count_input.SetValue( spz.sd_genSettingsInput.batch_count.ToString() );
	        _batch_size_input.SetValue( spz.sd_genSettingsInput.batch_size.ToString() );
	    }


	    void Update(){
	        var movableParent = _movableRectTransform.parent as RectTransform;
	        float widthDifference = _movableRectTransform.rect.width - movableParent.rect.width;
	        if(Mathf.Abs(widthDifference) < 0.01f){ return; }//to avoid frequent layout recalculations

	        Stretch();
	    }
    
	    void Awake(){
	        if(instance != null){ DestroyImmediate(this); return; }
	        instance = this;
	        SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
	    }

	    void OnDestroy() {
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	        if (instance == this)
	            instance = null;
	    }

	    void Start(){
	        _resolutionPreset_512.onClick.AddListener( ()=>OnResolutionPresetButton(512) );
	        _resolutionPreset_768.onClick.AddListener( ()=>OnResolutionPresetButton(768) );
	        _resolutionPreset_1024.onClick.AddListener( ()=>OnResolutionPresetButton(1024) );
	        _resolutionPreset_1536.onClick.AddListener( ()=>OnResolutionPresetButton(1536) );
	        _resolutionPreset_2048.onClick.AddListener( ()=>OnResolutionPresetButton(2048) );
	        TrySyncResolutionFromCurrentViewportMode();
	        ApplyThemeTokens();
	    }

	    /// <summary>
	    /// Retints the SD input column: role matrix aligns Nomad with traditional controls;
	    /// prompt presets / web-find / resolution chips stay specialized composers.
	    /// </summary>
	    void ApplyThemeTokens() {
	        // Bound chrome: authored SPZ colors until a non-default theme is applied.
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            Transform rootRestore = _movableRectTransform != null ? _movableRectTransform : transform;
	            SpzUiThemeOps.RestoreBoundChromeUnder(rootRestore);
	            if (!ReferenceEquals(rootRestore, transform))
	                SpzUiThemeOps.RestoreBoundChromeUnder(transform);
	            // Resolution chips / web-find / presets may sit outside movable root after layout remaps.
	            RestorePreset(_resolutionPreset_512);
	            RestorePreset(_resolutionPreset_768);
	            RestorePreset(_resolutionPreset_1024);
	            RestorePreset(_resolutionPreset_1536);
	            RestorePreset(_resolutionPreset_2048);
	            RestoreWebFindAndPromptPresets(rootRestore);
	            if (!ReferenceEquals(rootRestore, transform))
	                RestoreWebFindAndPromptPresets(transform);
	            SpzUiThemeOps.RefreshScaledLayoutGroupsUnder(rootRestore);
	            return;
	        }
	        Transform root = _movableRectTransform != null ? _movableRectTransform : transform;
	        if (root == null) return;
	        var t = SpzUiThemeOps.Active;
	        var rootImg = root.GetComponent<Image>();
	        if (rootImg != null)
	            SpzUiThemeOps.ApplyBoundChromeGraphic(rootImg, t.panelBg);

	        SpzUiThemeOps.ApplyBoundChromeRolesUnder(root, new SpzUiThemeRoleMatrixOptions {
	            DetectPromptLabels = true,
	            Exclude = c => {
	                if (c is Button b && IsWebFindButton(b)) return true;
	                if (c is Toggle tog && IsPromptPresetToggle(tog)) return true;
	                if (IsResolutionPresetControl(c)) return true;
	                return false;
	            },
	        });

	        // Specialized traditional composers (not generic SelectableFace).
	        foreach (var btn in root.GetComponentsInChildren<Button>(true)) {
	            if (btn == null || !IsWebFindButton(btn)) continue;
	            SpzUiThemeOps.ThemePromptPresetSquareCell(btn, FlatCellFill(false, t), t.accent);
	            SpzUiThemeOps.ApplyControlLineIcon(btn.transform, StudioLineIcon.Globe, 16f);
	            foreach (var tmp in btn.GetComponentsInChildren<TextMeshProUGUI>(true)) {
	                if (tmp == null) continue;
	                SpzUiThemeOps.ApplyBoundChromeTmp(tmp, t.textPrimary);
	                SpzUiThemeOps.HideAuthoredGraphicForTheme(tmp);
	            }
	            SpzUiThemeOps.ClearNonFaceRaycastsForTheme(btn);
	        }
	        foreach (var toggle in root.GetComponentsInChildren<Toggle>(true)) {
	            if (toggle == null || !IsPromptPresetToggle(toggle)) continue;
	            ThemePromptPresetToggle(toggle, t);
	            SpzUiThemeOps.EnsurePromptPresetRowGaps(toggle.transform);
	        }
	        ThemeResolutionPreset(_resolutionPreset_512, 512, t);
	        ThemeResolutionPreset(_resolutionPreset_768, 768, t);
	        ThemeResolutionPreset(_resolutionPreset_1024, 1024, t);
	        ThemeResolutionPreset(_resolutionPreset_1536, 1536, t);
	        ThemeResolutionPreset(_resolutionPreset_2048, 2048, t);
	    }

	    bool IsResolutionPresetControl(Component c) {
	        if (c == null) return false;
	        Transform tr = c.transform;
	        return IsUnderPreset(tr, _resolutionPreset_512)
	            || IsUnderPreset(tr, _resolutionPreset_768)
	            || IsUnderPreset(tr, _resolutionPreset_1024)
	            || IsUnderPreset(tr, _resolutionPreset_1536)
	            || IsUnderPreset(tr, _resolutionPreset_2048);
	    }

	    static bool IsUnderPreset(Transform tr, Component preset) {
	        if (tr == null || preset == null) return false;
	        return tr == preset.transform || tr.IsChildOf(preset.transform);
	    }

	    /// <summary>Re-sync preset cell fills after a slot is selected (selection can change without ThemeChanged).</summary>
	    public void RefreshPromptPresetChrome() {
	        Transform root = _movableRectTransform != null ? _movableRectTransform : transform;
	        if (root == null) return;
	        var t = SpzUiThemeOps.Active;
	        foreach (var toggle in root.GetComponentsInChildren<Toggle>(true)) {
	            if (toggle == null || !IsPromptPresetToggle(toggle)) continue;
	            ThemePromptPresetToggle(toggle, t);
	        }
	    }

	    /// <summary>Re-tint 512…2048 resolution chips from current W×H (BoundChrome only).</summary>
	    public void RefreshResolutionPresetChrome() {
	        var t = SpzUiThemeOps.Active;
	        ThemeResolutionPreset(_resolutionPreset_512, 512, t);
	        ThemeResolutionPreset(_resolutionPreset_768, 768, t);
	        ThemeResolutionPreset(_resolutionPreset_1024, 1024, t);
	        ThemeResolutionPreset(_resolutionPreset_1536, 1536, t);
	        ThemeResolutionPreset(_resolutionPreset_2048, 2048, t);
	    }

	    static void ThemePromptPresetToggle(Toggle toggle, SpzUiThemeOps.ThemeTokens t) {
	        if (toggle == null) return;
	        Color fill = FlatCellFill(toggle.isOn, t);
	        SpzUiThemeOps.ThemePromptPresetSquareCell(toggle, fill, t.accent);
	    }

	    static Color FlatCellFill(bool selected, SpzUiThemeOps.ThemeTokens t) {
	        return selected
	            ? Color.Lerp(t.controlBg, t.accent, 0.35f)
	            : t.controlBg;
	    }

	    static bool IsPromptPresetToggle(Toggle toggle) {
	        if (toggle == null) return false;
	        string n = toggle.gameObject.name ?? "";
	        return n.IndexOf("preset", System.StringComparison.OrdinalIgnoreCase) >= 0;
	    }

	    static bool IsWebFindButton(Button btn) {
	        if (btn == null) return false;
	        string n = btn.gameObject.name ?? "";
	        return n.IndexOf("internet", System.StringComparison.OrdinalIgnoreCase) >= 0
	            || n.IndexOf("globe", System.StringComparison.OrdinalIgnoreCase) >= 0
	            || n.IndexOf("WebFind", System.StringComparison.OrdinalIgnoreCase) >= 0
	            || btn.GetComponentInParent<SD_PromptWord_WebFind>(true) != null;
	    }

	    static bool IsPromptHeaderLabel(TextMeshProUGUI tmp) {
	        if (tmp == null) return false;
	        if (IsPromptPolaritySignLabel(tmp)) return false;
	        string t = tmp.text ?? "";
	        if (t.IndexOf("prompt", System.StringComparison.OrdinalIgnoreCase) < 0)
	            return false;
	        string n = tmp.gameObject.name ?? "";
	        return n.IndexOf("header", System.StringComparison.OrdinalIgnoreCase) >= 0
	            || string.Equals(n, "header", System.StringComparison.OrdinalIgnoreCase)
	            || t.TrimStart().StartsWith("prompt", System.StringComparison.OrdinalIgnoreCase);
	    }

	    /// <summary>Negative/positive polarity glyph TMP beside the prompt header (prefab text "-" / "+").</summary>
	    static bool IsPromptPolaritySignLabel(TextMeshProUGUI tmp) {
	        if (tmp == null) return false;
	        string t = (tmp.text ?? "").Trim();
	        return t == "-" || t == "+" || t == "\u2212" || t == "\u2013" || t == "\u2014";
	    }

	    static void RestorePreset(Button btn) {
	        if (btn != null)
	            SpzUiThemeOps.RestoreBoundChromeUnder(btn.transform);
	    }

	    void RestoreWebFindAndPromptPresets(Transform root) {
	        if (root == null) return;
	        foreach (var btn in root.GetComponentsInChildren<Button>(true)) {
	            if (btn != null && IsWebFindButton(btn))
	                SpzUiThemeOps.RestoreBoundChromeUnder(btn.transform);
	        }
	        foreach (var toggle in root.GetComponentsInChildren<Toggle>(true)) {
	            if (toggle != null && IsPromptPresetToggle(toggle))
	                SpzUiThemeOps.RestoreBoundChromeUnder(toggle.transform);
	        }
	    }

	    void ThemeResolutionPreset(Button btn, int presetPx, SpzUiThemeOps.ThemeTokens t) {
	        if (btn == null) return;
	        bool selected = width == presetPx && height == presetPx;
	        SpzUiThemeOps.ApplyBoundChromeSelectable(btn, FlatCellFill(selected, t), t.accent);
	        if (btn.targetGraphic is Image img) {
	            SpzUiThemeOps.ApplyRoundedControlSprite(img, markEligible: true);
	        }
	        var label = btn.GetComponentInChildren<TextMeshProUGUI>(true);
	        if (label != null)
	            SpzUiThemeOps.ApplyBoundChromeCompactToolLabelTmp(label, t.textPrimary, 11f);
	        SpzUiThemeOps.ClearNonFaceRaycastsForTheme(btn);
	    }

	    void OnEnable() {
	        TrySyncResolutionFromCurrentViewportMode();
	        // Theme may change while the prompt panel is disabled.
	        ApplyThemeTokens();
	    }

	    void TrySyncResolutionFromCurrentViewportMode() {
	        // Only recover FULL SRN adaptive W/H if TryEnter already captured the user's presets.
	        // Do not rewrite 512/1024 just because the left column is collapsed (paint / open-right) — that diverged from OG and made Gen Art look wrong.
	        if (!ViewportFullViewOnScreen_Driver.HasCapturedGenResolutionForFullViewSession) {
	            return;
	        }
	        ScheduleAdaptiveResolutionFromViewportModeNextFrame();
	    }
	}
}//end namespace

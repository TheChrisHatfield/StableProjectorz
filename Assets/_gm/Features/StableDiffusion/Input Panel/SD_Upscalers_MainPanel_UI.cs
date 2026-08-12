using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace spz {

	public class SD_Upscalers_MainPanel_UI : MonoBehaviour
	{
	    [SerializeField] TMP_Dropdown _upscalersDropdown;
	    [SerializeField] Animation _anim;
	    [SerializeField] AnimationClip _attention_CLIP;
	    [Space(10)]
	    [SerializeField] Button _upscaleVisible_x2_button;
	    [SerializeField] Button _upscaleVisible_x4_button;

	    bool _lastSoftInteractable = true;
	    Coroutine _deferredSoftDisable;

	    void Awake() {
	        SpzUiThemeOps.ThemeChanged += OnThemeChanged;
	    }

	    void Start(){
	        StaticEvents.SubscribeAppend<List<string>>("SD_Upscalers:ListUpdated", Populate_Dropdown);
	        StaticEvents.SubscribeAppend<bool>("SD_Upscalers:SetButtonsInteractable", SetButtonsInteractable);
	        StaticEvents.SubscribeAppend("SD_Upscalers:PlayAttentionAnim", PlayAttentionAnim);
	        StaticEvents.SubscribeAppend<string>("SD_Upscalers:SetSelectedByName", SetSelectedUpscaler);

	        _upscalersDropdown.onValueChanged.AddListener((ix) => StaticEvents.Invoke<int>("SD_Upscalers_UI", ix));
	        _upscaleVisible_x2_button.onClick.AddListener(() => StaticEvents.Invoke("SD_Upscalers_UI:OnUpscaleX2"));
	        _upscaleVisible_x4_button.onClick.AddListener(() => StaticEvents.Invoke("SD_Upscalers_UI:OnUpscaleX4"));
	        ApplyThemeTokens();
	    }

	    void OnDestroy(){
	        SpzUiThemeOps.ThemeChanged -= OnThemeChanged;
	        if (_deferredSoftDisable != null) {
	            StopCoroutine(_deferredSoftDisable);
	            _deferredSoftDisable = null;
	        }
	        StaticEvents.Unsubscribe<List<string>>("SD_Upscalers:ListUpdated", Populate_Dropdown);
	        StaticEvents.Unsubscribe<bool>("SD_Upscalers:SetButtonsInteractable", SetButtonsInteractable);
	        StaticEvents.Unsubscribe("SD_Upscalers:PlayAttentionAnim", PlayAttentionAnim);
	        StaticEvents.Unsubscribe<string>("SD_Upscalers:SetSelectedByName", SetSelectedUpscaler);
	    }

	    void OnThemeChanged() {
	        ApplyThemeTokens();
	        OnThemeChanged_ReapplySoftDisable();
	    }

	    /// <summary>Own BoundChrome for x2/x4 + dropdown — SoftDisable alone left beige chips under Nomad.</summary>
	    void ApplyThemeTokens() {
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            if (_upscaleVisible_x2_button != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_upscaleVisible_x2_button.transform);
	            if (_upscaleVisible_x4_button != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_upscaleVisible_x4_button.transform);
	            if (_upscalersDropdown != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_upscalersDropdown.transform);
	            SetButtonsInteractable(_lastSoftInteractable);
	            return;
	        }
	        var t = SpzUiThemeOps.Active;
	        ThemeUpscaleChip(_upscaleVisible_x2_button, t);
	        ThemeUpscaleChip(_upscaleVisible_x4_button, t);
	        if (_upscalersDropdown != null) {
	            SpzUiThemeOps.EnsureSelectableHitFace(_upscalersDropdown);
	            SpzUiThemeOps.ApplyBoundChromeSelectable(_upscalersDropdown, t.fieldBg, t.accent);
	            if (_upscalersDropdown.captionText != null)
	                SpzUiThemeOps.ApplyBoundChromeReadableBodyTmp(_upscalersDropdown.captionText, t.textPrimary, 12f);
	            SpzUiThemeOps.ClearNonFaceRaycastsForTheme(_upscalersDropdown);
	        }
	        SetButtonsInteractable(_lastSoftInteractable);
	    }

	    static void ThemeUpscaleChip(Button btn, SpzUiThemeOps.ThemeTokens t) {
	        if (btn == null) return;
	        SpzUiThemeOps.EnsureSelectableHitFace(btn);
	        SpzUiThemeOps.ApplyBoundChromeSelectable(btn, t.controlBg, t.accent);
	        foreach (var tmp in btn.GetComponentsInChildren<TextMeshProUGUI>(true)) {
	            if (tmp != null)
	                SpzUiThemeOps.ApplyBoundChromeCompactToolLabelTmp(tmp, t.textPrimary, 11f);
	        }
	        SpzUiThemeOps.ClearNonFaceRaycastsForTheme(btn);
	    }

	    void OnThemeChanged_ReapplySoftDisable() {
	        // Other ThemeChanged listeners SolidSquare faces at full alpha after we run —
	        // defer one frame so soft-disable wins (GEN litmus: disabled x2/x4 stay dim).
	        if (!isActiveAndEnabled || !gameObject.activeInHierarchy) {
	            SetButtonsInteractable(_lastSoftInteractable);
	            return;
	        }
	        if (_deferredSoftDisable != null)
	            StopCoroutine(_deferredSoftDisable);
	        _deferredSoftDisable = StartCoroutine(CoReapplySoftDisableNextFrame());
	    }

	    IEnumerator CoReapplySoftDisableNextFrame() {
	        yield return null;
	        _deferredSoftDisable = null;
	        SetButtonsInteractable(_lastSoftInteractable);
	    }
    
	    private void PlayAttentionAnim(){
	        if (_anim == null || _attention_CLIP == null) return;
	        _anim.clip = _attention_CLIP;
	        _anim.Play();
	    }
    
	    private void SetButtonsInteractable(bool interactable){
	        _lastSoftInteractable = interactable;
	        if (_upscaleVisible_x2_button == null || _upscaleVisible_x4_button == null) return;

	        ApplySoftAlpha(_upscaleVisible_x2_button, interactable);
	        ApplySoftAlpha(_upscaleVisible_x4_button, interactable);
	    }

	    static void ApplySoftAlpha(Button btn, bool interactable) {
	        if (btn == null) return;
	        // Prefer BoundChrome face (targetGraphic) — .image may lag after theme apply.
	        var face = btn.targetGraphic != null ? btn.targetGraphic : btn.image;
	        if (face == null) return;
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            // Leave litmus: soft-dim must not become the Restore baseline (Gen Art soft-disable class).
	            SpzUiThemeOps.RestoreAuthoredGraphic(face);
	            var restored = face.color;
	            restored.a = 1f;
	            face.color = restored;
	            SpzUiThemeOps.ResnapshotAuthoredGraphicColor(face);
	            restored.a = interactable ? 1f : 0.5f;
	            face.color = restored;
	            return;
	        }
	        var c = face.color;
	        c.a = interactable ? 1f : 0.5f;
	        face.color = c;
	    }
    
	    private void Populate_Dropdown(List<string> upscalerNames){
	        if (_upscalersDropdown == null) return;
        
	        string previousSelection = (_upscalersDropdown.options.Count > _upscalersDropdown.value && _upscalersDropdown.value >= 0) 
	            ? _upscalersDropdown.options[_upscalersDropdown.value].text 
	            : "";

	        _upscalersDropdown.ClearOptions();
	        _upscalersDropdown.AddOptions(upscalerNames.Select(name => new TMP_Dropdown.OptionData(name)).ToList());

	        int newIndex = -1;
	        if (!string.IsNullOrEmpty(previousSelection)){
	            newIndex = _upscalersDropdown.options.FindIndex(opt => opt.text == previousSelection);
	        }
        
	        if (newIndex >= 0){
	            _upscalersDropdown.SetValueWithoutNotify(newIndex);
	        } else if (_upscalersDropdown.options.Count > 0) {
	            _upscalersDropdown.SetValueWithoutNotify(0);
	        }
        
	        _upscalersDropdown.RefreshShownValue();
	    }
    
	    private void SetSelectedUpscaler(string upscalerName){
	        if (_upscalersDropdown == null) return;
	        int index = _upscalersDropdown.options.FindIndex(opt => opt.text == upscalerName);
	        if (index >= 0){
	            _upscalersDropdown.SetValueWithoutNotify(index);
	        }
	    }
	}
}//end namespace

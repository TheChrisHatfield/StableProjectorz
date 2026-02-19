using System;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using TMPro;

namespace spz {

	public enum Panel{
	    Unknown, Input, Obj3D, CtrlNet, ArtBG, Art,
	}

	//has tab-buttons that allow us to flick between different panels (3d-objects, CTRLnets, Art, ArtBG panels).
	//It also has buttons such as Gen Art, etc.
	public class CommandRibbon_UI : MonoBehaviour{
	    public static CommandRibbon_UI instance { get; private set; } = null;

	    [SerializeField] TabsGroup_UI _tabGroup;
	    [Space(10)]
	    [SerializeField] RectTransform _SD_ArtList_Panel;
	    [SerializeField] RectTransform _SD_ArtBgList_Panel;
	    [SerializeField] RectTransform _SD_3D_Models_Panels;
	    [SerializeField] RectTransform _SD_ControlNets_List_Panel;
	    [Space(10)]
	    [SerializeField] Animation _ctrlNetButton_anim;

	    // One tab + one panel per addon (Blender N-panel style)
	    Dictionary<string, RectTransform> _addonPanelsById = new Dictionary<string, RectTransform>();
	    Dictionary<string, GameObject> _addonTabById = new Dictionary<string, GameObject>();

	    Coroutine _attention_toCtrlNetButton_crtn = null;


	    public Panel _currentPanel { get; private set; } = Panel.Unknown;


	    public void Attention_toCtrlNetButton(){
	        if(_attention_toCtrlNetButton_crtn != null){ StopCoroutine(_attention_toCtrlNetButton_crtn); }
	        _attention_toCtrlNetButton_crtn = StartCoroutine( Attention_toCtrlNetButton_crtn() );
	    }

	    public void clickArtList_toggle_manual() => _tabGroup.SwitchTab("art list");
	    public void clickArtBGList_toggle_manual() => _tabGroup.SwitchTab("art bg list");


	    IEnumerator Attention_toCtrlNetButton_crtn(){
	        int childCount = _ctrlNetButton_anim.transform.childCount;
	        _ctrlNetButton_anim.transform.GetChild(childCount-1).gameObject.SetActive(true);
	        _ctrlNetButton_anim.Stop();
	        _ctrlNetButton_anim.Rewind();
	        _ctrlNetButton_anim.Play();
	        yield return new WaitForSeconds(3);
	        _ctrlNetButton_anim.transform.GetChild(childCount-1).gameObject.SetActive(false);
	        _attention_toCtrlNetButton_crtn = null;
	    }


	    void ShowOnePanel(GameObject go){
	        go.SetActive(true);
	        if(go != _SD_ArtList_Panel.gameObject){ _SD_ArtList_Panel.gameObject.SetActive(false); }
	        if(go != _SD_ArtBgList_Panel.gameObject){ _SD_ArtBgList_Panel.gameObject.SetActive(false); }
	        if(go != _SD_3D_Models_Panels.gameObject){ _SD_3D_Models_Panels.gameObject.SetActive(false); }
	        if(go != _SD_ControlNets_List_Panel.gameObject){ _SD_ControlNets_List_Panel.gameObject.SetActive(false); }
	        foreach(var p in _addonPanelsById.Values)
	            if(p != null && p.gameObject != go) p.gameObject.SetActive(false);
	        if (KeyMousePenInput.isKey_Shift_pressed() == false){
	            string msg = "Use Shift+1, Shift+2, etc to switch tabs faster :)";
	            Viewport_StatusText.instance.ShowStatusText(msg, false, 1.2f, false);
	        }
	    }

	    void OnArtList_Toggle(TabsGroupElem_UI tab){
	        ShowOnePanel( _SD_ArtList_Panel.gameObject );
	        _currentPanel = Panel.Art;
	    }

	    void OnArtBgList_Toggle(TabsGroupElem_UI tab){
	        ShowOnePanel( _SD_ArtBgList_Panel.gameObject );
	        _currentPanel = Panel.ArtBG;
	    }

	    void On_3D_Meshes_Toggle(TabsGroupElem_UI tab){
	        ShowOnePanel( _SD_3D_Models_Panels.gameObject );
	        _currentPanel = Panel.Obj3D;
	    }

	    void On_ControlNets_Toggle(TabsGroupElem_UI tab){
	        ShowOnePanel( _SD_ControlNets_List_Panel.gameObject );
	        _currentPanel = Panel.CtrlNet;
	    }

	    void Update(){
	        if(KeyMousePenInput.isSomeInputFieldActive()){ return;} //maybe typing some exclamation mark etc.
	        if (KeyMousePenInput.isKey_Shift_pressed() == false){ return; }
	        if (_tabGroup == null) return; // TabsGroup_UI may not be found at init; avoid NullReferenceException on Shift+1..9
	        if (Input.GetKeyDown(KeyCode.Alpha1)){ _tabGroup.SwitchTab("art list"); }
	        if (Input.GetKeyDown(KeyCode.Alpha2)){ _tabGroup.SwitchTab("art bg list"); }
	        if (Input.GetKeyDown(KeyCode.Alpha3)){ _tabGroup.SwitchTab("mesh"); }
	        if (Input.GetKeyDown(KeyCode.Alpha4)){ _tabGroup.SwitchTab("controlnet"); }
	        // Dynamic shift keys for addons (_addonPanelsById keys are addonIds; TabsGroup tab id is "addon_" + addonId)
	        int addonIdx = 5;
	        foreach (string addonId in _addonPanelsById.Keys) {
	            if (Input.GetKeyDown(KeyCode.Alpha0 + addonIdx)) { _tabGroup.SwitchTab("addon_" + addonId); }
	            addonIdx++;
	            if (addonIdx > 9) break;
	        }
	    }
    

	    void Awake(){
	        if(instance != null){  DestroyImmediate(this); return; }
	        instance = this;

	        TryResolvePanelRefs();
	        if (_tabGroup != null) {
	            _tabGroup.SubscribeForTab("art list", OnArtList_Toggle);
	            _tabGroup.SubscribeForTab("art bg list", OnArtBgList_Toggle);
	            _tabGroup.SubscribeForTab("mesh", On_3D_Meshes_Toggle);
	            _tabGroup.SubscribeForTab("controlnet", On_ControlNets_Toggle);
	        }

	        // allows Awake() of panels to run, to init as singletons:
	        Action<Transform> flip_on_off =  (tr)=>{ if(tr != null) { tr.gameObject.SetActive(false); tr.gameObject.SetActive(true); tr.gameObject.SetActive(false); } };
	        flip_on_off(_SD_ArtList_Panel);
	        flip_on_off(_SD_ArtBgList_Panel);
	        flip_on_off(_SD_3D_Models_Panels);
	        flip_on_off(_SD_ControlNets_List_Panel);
	        if (_SD_ControlNets_List_Panel != null) {
	            _SD_ControlNets_List_Panel.gameObject.SetActive(true);
	            _currentPanel = Panel.CtrlNet;
	        }
	    }

	    /// <summary>Resolve serialized refs at runtime if missing (e.g. base prefab has nulls; parent prefab overrides may not apply in some builds).</summary>
	    void TryResolvePanelRefs(){
	        if (_tabGroup == null) _tabGroup = GetComponentInChildren<TabsGroup_UI>(true);
	        if (_SD_ControlNets_List_Panel != null) return;
	        // _SD_ControlNets_List_Panel is null in base CommandRibbon prefab; parent (e.g. RIGHT PANEL) overrides it. If we're in a context where override didn't apply, search.
	        Transform root = transform.parent;
	        if (root == null) return;
	        for (int i = 0; i < root.childCount; i++){
	            Transform ch = root.GetChild(i);
	            if (ch == transform) continue;
	            var rt = ch as RectTransform;
	            if (rt == null) continue;
	            string n = ch.name.ToLowerInvariant();
	            if (n.Contains("control") || n.Contains("ctrl") || n.Contains("controlnet")){
	                _SD_ControlNets_List_Panel = rt;
	                UnityEngine.Debug.Log($"[CommandRibbon_UI] Resolved _SD_ControlNets_List_Panel by name: {ch.name}");
	                return;
	            }
	        }
	        // Fallback: tab strip's sibling content area often has panels as children; 4th (index 3) is usually ControlNet
	        if (_tabGroup != null){
	            Transform strip = _tabGroup.GetTabStripTransform();
	            if (strip != null && strip.parent != null){
	                Transform container = strip.parent;
	                for (int i = 0; i < container.childCount; i++){
	                    Transform c = container.GetChild(i);
	                    if (c == strip) continue;
	                    if (c.childCount >= 4){
	                        var fourth = c.GetChild(3) as RectTransform;
	                        if (fourth != null){
	                            _SD_ControlNets_List_Panel = fourth;
	                            UnityEngine.Debug.Log($"[CommandRibbon_UI] Resolved _SD_ControlNets_List_Panel as 4th panel sibling of tab strip.");
	                            return;
	                        }
	                    }
	                }
	            }
	        }
	    }

	    /// <summary>One tab per addon (Blender N-panel style). Returns the panel content parent for this addon.</summary>
	    public RectTransform GetOrCreatePanelForAddon(string addonId, string displayTitle){
	        UnityEngine.Debug.Log($"[CommandRibbon_UI] GetOrCreatePanelForAddon: {addonId} ({displayTitle})");
	        
	        if(_tabGroup == null) _tabGroup = GetComponentInChildren<TabsGroup_UI>(true);
	        TryResolvePanelRefs();
	        UnityEngine.Debug.Log($"[CommandRibbon_UI] _tabGroup={(_tabGroup != null ? _tabGroup.name : "null")} _SD_ControlNets_List_Panel={(_SD_ControlNets_List_Panel != null ? "set" : "null")}");
	        
	        if(_SD_ControlNets_List_Panel == null || _tabGroup == null) {
	            UnityEngine.Debug.LogError($"[CommandRibbon_UI] Cannot create addon tab: _SD_ControlNets_List_Panel={(_SD_ControlNets_List_Panel!=null)} _tabGroup={(_tabGroup!=null)}. Check Right Panel prefab assigns these refs.");
	            return null;
	        }
	        if(_addonPanelsById.TryGetValue(addonId, out var existing)) {
	            UnityEngine.Debug.Log($"[CommandRibbon_UI] Returning existing panel for: {addonId}");
	            return existing;
	        }
	        Transform panelsParent = _SD_ControlNets_List_Panel.parent;
	        if(panelsParent == null) {
	            UnityEngine.Debug.LogError("[CommandRibbon_UI] Cannot create addon tab: panelsParent is null");
	            return null;
	        }

	        string tabId = "addon_" + addonId;
	        UnityEngine.Debug.Log($"[CommandRibbon_UI] Creating new tab and panel for: {addonId}. TabID: {tabId}");
	        
	        var panelGo = new GameObject("Panel_" + addonId);
	        panelGo.transform.SetParent(panelsParent, false);
	        var panelRect = panelGo.AddComponent<RectTransform>();
	        panelRect.anchorMin = Vector2.zero;
	        panelRect.anchorMax = Vector2.one;
	        panelRect.sizeDelta = Vector2.zero;
	        panelRect.anchoredPosition = Vector2.zero;
	        var panelBg = panelGo.AddComponent<Image>();
	        panelBg.color = new Color(0.22f, 0.22f, 0.22f, 1f);
	        panelBg.raycastTarget = true;
	        var panelLayout = panelGo.AddComponent<VerticalLayoutGroup>();
	        panelLayout.spacing = 8f;
	        panelLayout.padding = new RectOffset(8, 8, 8, 8);
	        panelLayout.childControlHeight = false;
	        panelLayout.childControlWidth = true;
	        panelLayout.childForceExpandHeight = false;
	        panelLayout.childForceExpandWidth = true;
	        _addonPanelsById[addonId] = panelRect;
	        panelGo.SetActive(false);

	        Transform tabStrip = _tabGroup.GetTabStripTransform();
	        UnityEngine.Debug.Log($"[CommandRibbon_UI] Tab strip: {tabStrip?.name ?? "null"}, childCount={tabStrip?.childCount ?? 0}");
	        var tabGo = new GameObject("Tab: " + displayTitle);
	        _addonTabById[addonId] = tabGo;
	        tabGo.transform.SetParent(tabStrip, false);
	        tabGo.transform.SetAsLastSibling();
	        tabGo.SetActive(true);
	        var tabRect = tabGo.AddComponent<RectTransform>();
	        tabRect.anchorMin = new Vector2(0, 0);
	        tabRect.anchorMax = new Vector2(0, 0);
	        tabRect.sizeDelta = Vector2.zero;
	        tabRect.anchoredPosition = Vector2.zero;
	        tabRect.pivot = new Vector2(0.5f, 0.5f);

	        // Ensure the tab has a proper size in the layout (HorizontalLayoutGroup on strip)
	        var tabLE = tabGo.AddComponent<LayoutElement>();
	        tabLE.flexibleWidth = 0f;
	        tabLE.minWidth = 60f;
	        tabLE.preferredWidth = 90f;
	        tabLE.preferredHeight = 30f;

	        var tabImg = tabGo.AddComponent<Image>();
	        tabImg.color = new Color(0f, 0.8f, 0.2f, 1f); // Bright green so it's very visible for testing
	        tabImg.raycastTarget = true;
	        var tabBtn = tabGo.AddComponent<Button>();
	        tabBtn.targetGraphic = tabImg;

	        var tabTextGo = new GameObject("Text");
	        tabTextGo.transform.SetParent(tabGo.transform, false);
	        var tabTextRect = tabTextGo.AddComponent<RectTransform>();
	        tabTextRect.anchorMin = Vector2.zero;
	        tabTextRect.anchorMax = Vector2.one;
	        tabTextRect.sizeDelta = Vector2.zero;
	        var tabText = tabTextGo.AddComponent<TextMeshProUGUI>();
	        tabText.text = displayTitle;
	        tabText.fontSize = 12;
	        tabText.color = Color.white;
	        tabText.alignment = TextAlignmentOptions.Center;
	        tabText.raycastTarget = false;
	        tabText.enableWordWrapping = false;
	        tabText.overflowMode = TMPro.TextOverflowModes.Ellipsis;

	        var tabElem = tabGo.AddComponent<TabsGroupElem_UI>();
	        tabElem.InitForRuntime(tabId, tabBtn);
	        _tabGroup.AddTab(tabElem);
	        
	        var panelToShow = panelRect;
	        _tabGroup.SubscribeForTab(tabId, _ => {
	            UnityEngine.Debug.Log($"[CommandRibbon_UI] Switching to addon tab: {addonId}");
	            ShowOnePanel(panelToShow.gameObject);
	            _currentPanel = Panel.Unknown;
	        });

	        UnityEngine.Debug.Log($"[CommandRibbon_UI] Addon tab created: {displayTitle}, tabGo.activeSelf={tabGo.activeSelf}, tabStrip.childCount={tabStrip.childCount}");
	        
	        // Force layout rebuild so the new tab appears (strip + parents for ScrollRect/ContentSizeFitter)
	        StartCoroutine(RebuildTabStripLayoutNextFrame(tabStrip));
	        
	        return panelRect;
	    }

	    IEnumerator RebuildTabStripLayoutNextFrame(Transform tabStrip){
	        yield return null;
	        if (tabStrip == null) yield break;
	        var stripRect = tabStrip as RectTransform;
	        if (stripRect != null) UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(stripRect);
	        // Rebuild parents so ScrollRect content size / ContentSizeFitter updates
	        Transform t = tabStrip.parent;
	        UnityEngine.UI.ScrollRect scrollRect = null;
	        while (t != null) {
	            var rt = t as RectTransform;
	            if (rt != null) UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
	            if (scrollRect == null) scrollRect = t.GetComponent<UnityEngine.UI.ScrollRect>();
	            t = t.parent;
	        }
	        // If tab bar is in a ScrollRect, scroll to right so new addon tab is visible
	        if (scrollRect != null && scrollRect.horizontal) {
	            scrollRect.horizontalNormalizedPosition = 1f;
	        }
	        Canvas.ForceUpdateCanvases();
	    }

	    /// <summary>Removes an addon's tab and panel (e.g. when addon is disabled). Call from Addon_MGR.UnloadAddon.</summary>
	    public void RemoveAddonPanel(string addonId){
	        if(!_addonPanelsById.TryGetValue(addonId, out var panelRect)) return;
	        if(_addonTabById.TryGetValue(addonId, out var tabGo)){
	            var tabElem = tabGo.GetComponent<TabsGroupElem_UI>();
	            if(tabElem != null && _tabGroup != null) _tabGroup.RemoveTab(tabElem);
	            UnityEngine.Object.Destroy(tabGo);
	            _addonTabById.Remove(addonId);
	        }
	        if(panelRect != null && panelRect.gameObject != null) UnityEngine.Object.Destroy(panelRect.gameObject);
	        _addonPanelsById.Remove(addonId);
	        UnityEngine.Debug.Log($"[CommandRibbon_UI] Removed addon tab/panel: {addonId}");
	    }


	}
}//end namespace

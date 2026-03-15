using System;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using TMPro;

namespace spz {

	public enum Panel{
	    Unknown, Input, Obj3D, CtrlNet, ArtBG, Art, Paint,
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
	    [Tooltip("Paint tab content: workflow toggles, brush options, alpha picker, palette swatches. Add a tab with title 'Paint' in TabsGroup and assign this panel.")]
	    [SerializeField] RectTransform _Paint_Panel;
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

	    public void clickArtList_toggle_manual() { if (_tabGroup != null) _tabGroup.SwitchTab("art list"); }
	    public void clickArtBGList_toggle_manual() { if (_tabGroup != null) _tabGroup.SwitchTab("art bg list"); }
	    public void clickPaint_toggle_manual() { if (_tabGroup != null) _tabGroup.SwitchTab("paint"); }


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
	        if (_SD_ArtList_Panel != null && go != _SD_ArtList_Panel.gameObject) _SD_ArtList_Panel.gameObject.SetActive(false);
	        if (_SD_ArtBgList_Panel != null && go != _SD_ArtBgList_Panel.gameObject) _SD_ArtBgList_Panel.gameObject.SetActive(false);
	        if (_SD_3D_Models_Panels != null && go != _SD_3D_Models_Panels.gameObject) _SD_3D_Models_Panels.gameObject.SetActive(false);
	        if (_SD_ControlNets_List_Panel != null && go != _SD_ControlNets_List_Panel.gameObject) _SD_ControlNets_List_Panel.gameObject.SetActive(false);
	        if (_Paint_Panel != null && go != _Paint_Panel.gameObject) _Paint_Panel.gameObject.SetActive(false);
	        foreach(var p in _addonPanelsById.Values)
	            if(p != null && p.gameObject != go) p.gameObject.SetActive(false);
	        if (KeyMousePenInput.isKey_Shift_pressed() == false){
	            string msg = "Use Shift+1, Shift+2, etc to switch tabs faster :)";
	            Viewport_StatusText.instance.ShowStatusText(msg, false, 1.2f, false);
	        }
	    }

	    void OnArtList_Toggle(TabsGroupElem_UI tab){
	        if (_SD_ArtList_Panel == null) return;
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

	    void On_Paint_Toggle(TabsGroupElem_UI tab){
	        if (_Paint_Panel == null) return;
	        ShowOnePanel( _Paint_Panel.gameObject );
	        _currentPanel = Panel.Paint;
	    }

	    void Update(){
	        if(KeyMousePenInput.isSomeInputFieldActive()){ return;} //maybe typing some exclamation mark etc.
	        if (KeyMousePenInput.isKey_Shift_pressed() == false){ return; }
	        if (_tabGroup == null) return; // TabsGroup_UI may not be found at init; avoid NullReferenceException on Shift+1..9
	        if (Input.GetKeyDown(KeyCode.Alpha1)){ _tabGroup.SwitchTab("art list"); }
	        if (Input.GetKeyDown(KeyCode.Alpha2)){ _tabGroup.SwitchTab("art bg list"); }
	        if (Input.GetKeyDown(KeyCode.Alpha3)){ _tabGroup.SwitchTab("mesh"); }
	        if (Input.GetKeyDown(KeyCode.Alpha4)){ _tabGroup.SwitchTab("controlnet"); }
	        if (Input.GetKeyDown(KeyCode.Alpha5)){ _tabGroup.SwitchTab("paint"); }
	        // Dynamic shift keys for addons (6+) (_addonPanelsById keys are addonIds; TabsGroup tab id is "addon_" + addonId)
	        int addonIdx = 6;
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
	        EnsurePaintTabExists();
	        if (_tabGroup != null) {
	            _tabGroup.SubscribeForTab("art list", OnArtList_Toggle);
	            _tabGroup.SubscribeForTab("art bg list", OnArtBgList_Toggle);
	            _tabGroup.SubscribeForTab("mesh", On_3D_Meshes_Toggle);
	            _tabGroup.SubscribeForTab("controlnet", On_ControlNets_Toggle);
	            if (_tabGroup.HasTab("paint"))
	                _tabGroup.SubscribeForTab("paint", On_Paint_Toggle);
	        }

	        // allows Awake() of panels to run, to init as singletons:
	        Action<Transform> flip_on_off =  (tr)=>{ if(tr != null) { tr.gameObject.SetActive(false); tr.gameObject.SetActive(true); tr.gameObject.SetActive(false); } };
	        flip_on_off(_SD_ArtList_Panel);
	        flip_on_off(_SD_ArtBgList_Panel);
	        flip_on_off(_SD_3D_Models_Panels);
	        flip_on_off(_SD_ControlNets_List_Panel);
	        flip_on_off(_Paint_Panel);
	        if (_SD_ControlNets_List_Panel != null) {
	            _SD_ControlNets_List_Panel.gameObject.SetActive(true);
	            _currentPanel = Panel.CtrlNet;
	        }

	        // Paint collector coroutine runs HERE (CommandRibbon_UI is always active).
	        // Cannot run on Paint panel itself because it's inactive and coroutines die on inactive GameObjects.
	        if (_Paint_Panel != null) {
	            var collector = _Paint_Panel.GetComponent<PaintTab_CollectPaintUI>();
	            if (collector != null) StartCoroutine(PaintCollect_WaitForSingletons_crtn(collector));
	        }
	    }

	    IEnumerator PaintCollect_WaitForSingletons_crtn(PaintTab_CollectPaintUI collector)
	    {
	        float elapsed = 0f;
	        const float maxWait = 15f;
	        const float pollInterval = 0.5f;
	        while (elapsed < maxWait)
	        {
	            if (collector.IsFullyCollected) yield break;
	            if (WorkflowRibbon_UI.instance != null || SD_WorkflowOptionsRibbon_UI.instance != null)
	            {
	                collector.CollectNow();
	                if (collector.IsFullyCollected) yield break;
	            }
	            elapsed += pollInterval;
	            yield return new WaitForSeconds(pollInterval);
	        }
	        // Final attempt
	        if (!collector.IsFullyCollected)
	        {
	            collector.CollectNow();
	            if (!collector.IsFullyCollected)
	                Debug.LogWarning("[CommandRibbon_UI] Paint tab: could not find WorkflowRibbon_UI/SD_WorkflowOptionsRibbon_UI after " + maxWait + "s. Paint tab will only show section headers.");
	        }
	    }

	    /// <summary>Creates the Paint tab and panel at runtime if missing, so Paint appears alongside Art list, Art BG, Mesh, ControlNet.</summary>
	    void EnsurePaintTabExists(){
	        if (_tabGroup == null) _tabGroup = GetComponentInChildren<TabsGroup_UI>(true);
	        if (_tabGroup == null) {
	            UnityEngine.Debug.LogWarning("[CommandRibbon_UI] Paint tab: no TabsGroup_UI found.");
	            return;
	        }
	        Transform tabStrip = _tabGroup.GetTabStripTransform();
	        if (tabStrip == null) return;
	        // Only skip when BOTH panel and tab exist; if panel exists but tab does not, we must create the tab below so the panel is reachable.
	        if (_Paint_Panel != null && _tabGroup.HasTab("paint")) return;

	        Transform panelsParent = (_SD_ControlNets_List_Panel != null ? _SD_ControlNets_List_Panel : _SD_ArtList_Panel)?.parent ?? tabStrip.parent;
	        RectTransform newPanelRect = null;
	        if (_Paint_Panel == null && panelsParent != null) {
	            var panelGo = new GameObject("Panel_Paint");
	            panelGo.transform.SetParent(panelsParent, false);
	            panelGo.transform.SetSiblingIndex(0); // draw behind tab strip and other panels so tabs stay visible and clickable
	            var panelRect = panelGo.AddComponent<RectTransform>();
	            panelRect.anchorMin = Vector2.zero;
	            panelRect.anchorMax = Vector2.one;
	            panelRect.sizeDelta = Vector2.zero;
	            panelRect.anchoredPosition = Vector2.zero;
	            var panelBg = panelGo.AddComponent<Image>();
	            panelBg.color = new Color(0.22f, 0.22f, 0.22f, 1f);
	            panelBg.raycastTarget = true;
	            var panelLayout = panelGo.AddComponent<VerticalLayoutGroup>();
	            panelLayout.spacing = 4f;
	            panelLayout.padding = new RectOffset(4, 4, 4, 4);
	            panelLayout.childControlHeight = true;
	            panelLayout.childControlWidth = true;
	            panelLayout.childForceExpandHeight = false;
	            panelLayout.childForceExpandWidth = true;
	            var layout = panelGo.AddComponent<PaintTab_KritaLayout_UI>();
	            layout.SetCreateSectionsIfMissing(true);
	            var collector = panelGo.AddComponent<PaintTab_CollectPaintUI>();
	            collector.SetLayout(layout);
	            panelGo.SetActive(false);
	            newPanelRect = panelRect;
	        }

	        // Skip creating a second tab only when the tab already exists; do not skip when panel exists but tab was never created.
	        if (_tabGroup.HasTab("paint")) {
	            if (newPanelRect != null)
	                _Paint_Panel = newPanelRect;
	            return;
	        }

	        // Connectivity rule: only create the tab if we have a panel (pre-assigned or just created). Never create a tab without a panel.
	        if (_Paint_Panel == null && newPanelRect == null) {
	            UnityEngine.Debug.LogWarning("[CommandRibbon_UI] Paint tab: cannot create tab without a panel (panelsParent was null).");
	            return;
	        }

	        var tabGo = new GameObject("Tab: Paint");
	        tabGo.transform.SetParent(tabStrip, false);
	        tabGo.transform.SetAsLastSibling();
	        tabGo.SetActive(true);
	        var tabRect = tabGo.AddComponent<RectTransform>();
	        tabRect.anchorMin = Vector2.zero;
	        tabRect.anchorMax = Vector2.one;
	        tabRect.sizeDelta = Vector2.zero;
	        tabRect.anchoredPosition = Vector2.zero;
	        tabRect.pivot = new Vector2(0.5f, 0.5f);
	        var tabLE = tabGo.AddComponent<LayoutElement>();
	        tabLE.flexibleWidth = 1f;
	        tabLE.flexibleHeight = 1f;
	        tabLE.minWidth = -1f;
	        tabLE.minHeight = -1f;
	        tabLE.preferredWidth = -1f;
	        tabLE.preferredHeight = -1f;
	        var tabImg = tabGo.AddComponent<Image>();
	        tabImg.color = new Color(0.35f, 0.35f, 0.38f, 1f); // dark gray to match other tabs so "Paint" text is visible
	        tabImg.raycastTarget = true;
	        var tabBtn = tabGo.AddComponent<Button>();
	        tabBtn.targetGraphic = tabImg;
	        // Active-state highlight (same structure as prefab tabs: show when selected)
	        var goActive = new GameObject("Active");
	        goActive.transform.SetParent(tabGo.transform, false);
	        var activeRect = goActive.AddComponent<RectTransform>();
	        activeRect.anchorMin = Vector2.zero;
	        activeRect.anchorMax = Vector2.one;
	        activeRect.sizeDelta = Vector2.zero;
	        activeRect.anchoredPosition = Vector2.zero;
	        var activeImg = goActive.AddComponent<Image>();
	        activeImg.color = new Color(0.45f, 0.6f, 0.8f, 1f); // lighter blue/green so it's more visible when selected
	        activeImg.raycastTarget = false;
	        goActive.SetActive(false);
	        var tabTextGo = new GameObject("Text");
	        tabTextGo.transform.SetParent(tabGo.transform, false);
	        var tabTextRect = tabTextGo.AddComponent<RectTransform>();
	        tabTextRect.anchorMin = Vector2.zero;
	        tabTextRect.anchorMax = Vector2.one;
	        tabTextRect.sizeDelta = Vector2.zero;
	        var tabText = tabTextGo.AddComponent<TextMeshProUGUI>();
	        tabText.text = "Paint";
	        tabText.fontSize = 12;
	        tabText.color = Color.white;
	        tabText.alignment = TextAlignmentOptions.Center;
	        tabText.raycastTarget = false;
	        tabText.enableWordWrapping = false;
	        tabText.overflowMode = TMPro.TextOverflowModes.Ellipsis;
	        var tabElem = tabGo.AddComponent<TabsGroupElem_UI>();
	        tabElem.InitForRuntime("paint", tabBtn);
	        tabElem.SetRuntimeActiveHighlight(goActive);
	        _tabGroup.AddTab(tabElem);
	        if (newPanelRect != null)
	            _Paint_Panel = newPanelRect;
	        var stripRect = tabStrip as RectTransform;
	        if (stripRect != null) UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(stripRect);
	        StartCoroutine(RebuildTabStripLayoutNextFrame(tabStrip));
	        UnityEngine.Debug.Log($"[CommandRibbon_UI] Paint tab created, parented to {tabGo.transform.parent.name}, siblingIndex={tabGo.transform.GetSiblingIndex()}, strip.childCount={tabStrip.childCount}");
	    }

	    /// <summary>When GetTabStripTransform returns TabsGroup itself, find the actual strip (child with HorizontalLayoutGroup or multiple children).</summary>
	    static Transform FindTabStripFallback(Transform tabGroupTransform){
	        for (int i = 0; i < tabGroupTransform.childCount; i++) {
	            Transform ch = tabGroupTransform.GetChild(i);
	            if (ch.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>() != null)
	                return ch;
	        }
	        for (int i = 0; i < tabGroupTransform.childCount; i++) {
	            Transform ch = tabGroupTransform.GetChild(i);
	            if (ch.childCount >= 2)
	                return ch;
	        }
	        return null;
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
	        
	        if(_tabGroup == null) {
	            UnityEngine.Debug.LogError($"[CommandRibbon_UI] Cannot create addon tab: _tabGroup is null.");
	            return null;
	        }
	        if(_addonPanelsById.TryGetValue(addonId, out var existing)) {
	            UnityEngine.Debug.Log($"[CommandRibbon_UI] Returning existing panel for: {addonId}");
	            return existing;
	        }
	        Transform tabStrip = _tabGroup.GetTabStripTransform();
	        if (tabStrip == _tabGroup.transform)
	            tabStrip = FindTabStripFallback(_tabGroup.transform) ?? tabStrip;
	        if (tabStrip == null) {
	            UnityEngine.Debug.LogError("[CommandRibbon_UI] Cannot create addon tab: tab strip is null.");
	            return null;
	        }

	        Transform panelsParent = (_SD_ControlNets_List_Panel != null ? _SD_ControlNets_List_Panel : _SD_ArtList_Panel)?.parent ?? tabStrip.parent;
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

	        UnityEngine.Debug.Log($"[CommandRibbon_UI] Tab strip: {tabStrip.name}, childCount={tabStrip.childCount}");
	        var tabGo = new GameObject("Tab: " + displayTitle);
	        _addonTabById[addonId] = tabGo;
	        tabGo.transform.SetParent(tabStrip, false);
	        tabGo.transform.SetAsLastSibling();
	        tabGo.SetActive(true);
	        var tabRect = tabGo.AddComponent<RectTransform>();
	        tabRect.anchorMin = Vector2.zero;
	        tabRect.anchorMax = Vector2.one;
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

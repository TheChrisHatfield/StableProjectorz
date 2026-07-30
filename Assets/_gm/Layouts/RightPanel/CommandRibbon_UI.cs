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
	    [Tooltip("Optional explicit parent for stacked tab bodies (Art, ControlNet, add-on shells). If unset, resolved from built-in panel parents.")]
	    [SerializeField] RectTransform _ribbonTabBodiesRootOverride;
	    [Tooltip("Optional sliced tab background for runtime strip tabs (Paint + add-ons). If unset, copied from the first prefab tab on the strip.")]
	    [SerializeField] Sprite _paintTabSliceSprite;
	    [Tooltip("Optional TMP font for all ribbon strip tab labels (built-in + runtime + add-ons). If unset, copied from the first prefab tab on the strip.")]
	    [SerializeField] TMP_FontAsset _paintTabFont;
	    [Space(10)]
	    [SerializeField] Animation _ctrlNetButton_anim;

	    /// <summary>Default label point size for ribbon strip tabs when no prefab reference is available (TMP; same basis for built-in, Paint, add-ons).</summary>
	    const float kRibbonStripTabLabelDefaultPt = 18f;
	    /// <summary>Horizontal padding added to TMP preferred width so TabBg / active slice fully covers the label (incl. sliced borders).</summary>
	    const float kRibbonStripTabLabelHorizontalPad = 28f;
	    const float kRibbonStripTabMinWidthFloor = 48f;
	    /// <summary>When many tabs exceed strip width, do not go below this min width before ellipsis (px).</summary>
	    const float kRibbonStripTabMinWidthWhenCrowded = 36f;
	    /// <summary>Fixed layout width for the soft vertical bar before each add-on strip tab (uGUI layout units).</summary>
	    const float kRibbonAddonDividerLayoutWidth = 6f;

	    static Sprite _addonRibbonDividerSprite;

	    /// <summary>Soft vertical grey bar (~#555 on transparent) for add-on tab separation; shared by all dividers.</summary>
	    static Sprite GetAddonRibbonDividerSprite()
	    {
		    if (_addonRibbonDividerSprite != null)
			    return _addonRibbonDividerSprite;
		    const int w = 7;
		    const int h = 32;
		    var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
		    tex.filterMode = FilterMode.Bilinear;
		    tex.wrapMode = TextureWrapMode.Clamp;
		    var edge = new Color(0.2f, 0.2f, 0.2f, 0f);
		    var core = new Color(0.33f, 0.33f, 0.33f, 0.92f);
		    for (int y = 0; y < h; y++)
		    {
			    for (int x = 0; x < w; x++)
			    {
				    float t = (x + 0.5f) / w;
				    float dist = Mathf.Abs(t - 0.5f) * 2f;
				    float blend = 1f - dist * dist * (3f - 2f * dist);
				    tex.SetPixel(x, y, Color.Lerp(edge, core, blend));
			    }
		    }
		    tex.Apply();
		    _addonRibbonDividerSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
		    return _addonRibbonDividerSprite;
	    }

	    static void ApplyStripTabMinWidthForLabel(LayoutElement le, TextMeshProUGUI tmp, float horizontalPad)
	    {
		    if (le == null || tmp == null) return;
		    string t = tmp.text;
		    if (string.IsNullOrEmpty(t)) return;
		    tmp.ForceMeshUpdate(true);
		    // Wide width budget: strip labels use NoWrap; measure full single-line width.
		    Vector2 pref = tmp.GetPreferredValues(t, 8192f, 128f);
		    float w = pref.x + horizontalPad;
		    le.minWidth = Mathf.Max(kRibbonStripTabMinWidthFloor, w);
	    }

	    static void ConfigureResponsiveRibbonTabText(TextMeshProUGUI tabText, TextMeshProUGUI referenceStripLabel, float fallbackMaxPt)
	    {
		    if (tabText == null) return;
		    float maxPt = fallbackMaxPt > 0.05f ? fallbackMaxPt : kRibbonStripTabLabelDefaultPt;
		    if (referenceStripLabel != null && referenceStripLabel.fontSize > 0.05f)
			    maxPt = Mathf.Max(maxPt, referenceStripLabel.fontSize);
		    maxPt = Mathf.Clamp(maxPt, 11f, 34f);
		    float minPt = Mathf.Clamp(maxPt * 0.42f, 7f, maxPt - 1f);
		    if (minPt >= maxPt)
			    minPt = Mathf.Max(7f, maxPt * 0.55f);
		    tabText.enableAutoSizing = true;
		    tabText.fontSizeMin = minPt;
		    tabText.fontSizeMax = maxPt;
		    tabText.textWrappingMode = TextWrappingModes.NoWrap;
		    tabText.overflowMode = TextOverflowModes.Ellipsis;
		    tabText.verticalAlignment = TMPro.VerticalAlignmentOptions.Middle;
	    }

	    /// <summary>Leftmost prefab/runtime strip tab label (direct children of <paramref name="strip"/>), excluding one cell if needed.</summary>
	    static TextMeshProUGUI GetRibbonStripTypographyReferenceTMP(Transform strip, Transform excludeTabCellRoot)
	    {
		    if (strip == null) return null;
		    TabsGroupElem_UI best = null;
		    int bestIx = int.MaxValue;
		    foreach (var elem in strip.GetComponentsInChildren<TabsGroupElem_UI>(true))
		    {
			    if (elem == null || elem.transform.parent != strip) continue;
			    if (excludeTabCellRoot != null && elem.transform == excludeTabCellRoot) continue;
			    int ix = elem.transform.GetSiblingIndex();
			    if (ix >= bestIx) continue;
			    bestIx = ix;
			    best = elem;
		    }
		    if (best == null) return null;
		    return best.GetComponentInChildren<TextMeshProUGUI>(true);
	    }

	    /// <summary>
	    /// The rect that owns <see cref="HorizontalLayoutGroup"/> for ribbon tab buttons (direct children = tab cells).
	    /// Do not confuse with a single tab GameObject (e.g. Art) — parenting runtime tabs there stacks labels on Art.
	    /// </summary>
	    Transform ResolveEffectiveTabStripTransform() => ResolveTabStripForGroup(_tabGroup);

	    void EnsureTabGroupResolved() {
		    if (_tabGroup == null) _tabGroup = GetComponentInChildren<TabsGroup_UI>(true);
	    }

	    /// <summary>Stack rect for tab bodies (built-in + add-on shells). Same parent Python/Unity content uses via <see cref="GetOrCreatePanelForAddon"/>.</summary>
	    public RectTransform GetRibbonTabBodiesParentRect() {
		    TryResolvePanelRefs();
		    if (_ribbonTabBodiesRootOverride != null)
			    return _ribbonTabBodiesRootOverride;
		    EnsureTabGroupResolved();
		    Transform strip = ResolveEffectiveTabStripTransform();
		    if (strip == null) return null;
		    return GetRibbonTabBodiesParent(strip) as RectTransform;
	    }

	    /// <summary>Select an add-on tab and show its body (same path as clicking the tab). No-op if unknown id.</summary>
	    public bool TrySelectAddonRibbonTab(string addonId) {
		    if (string.IsNullOrEmpty(addonId)) return false;
		    if (!_addonPanelsById.TryGetValue(addonId, out var shellRt) || shellRt == null || shellRt.gameObject == null)
			    return false;
		    EnsureTabGroupResolved();
		    if (_tabGroup == null) return false;
		    return _tabGroup.TrySwitchTab(AddonRibbonIntegration.TabIdForAddon(addonId));
	    }

	    /// <summary>Copy anchors/sizeDelta/position from a built-in ribbon panel so add-on shells occupy the same region below the tab strip.</summary>
	    void CopyRibbonTabBodyRectFromReference(RectTransform target) {
		    if (target == null) return;
		    RectTransform r = _SD_ControlNets_List_Panel ?? _SD_ArtList_Panel ?? _SD_ArtBgList_Panel ?? _SD_3D_Models_Panels ?? _Paint_Panel;
		    if (r == null) return;
		    // anchoredPosition/sizeDelta are in parent space — only safe when reference shares the same parent as the add-on shell.
		    if (r.parent != target.parent) return;
		    target.anchorMin = r.anchorMin;
		    target.anchorMax = r.anchorMax;
		    target.pivot = r.pivot;
		    target.anchoredPosition = r.anchoredPosition;
		    target.sizeDelta = r.sizeDelta;
		    target.localScale = r.localScale;
		    target.localRotation = r.localRotation;
	    }

	    static Transform ResolveTabStripForGroup(TabsGroup_UI tabGroup) {
		    if (tabGroup == null) return null;
		    Transform strip = tabGroup.GetTabStripTransform();
		    if (strip == null) return null;
		    // Typical RIGHT PANEL / CommandRibbon: HLG is on the same GameObject as <see cref="TabsGroup_UI"/>; tabs are siblings under it.
		    if (strip.GetComponent<HorizontalLayoutGroup>() != null)
			    return strip;
		    // Rare: TabsGroup wrapper with the real row as a child (must not pick a <see cref="TabsGroupElem_UI"/> tab cell).
		    if (strip == tabGroup.transform)
			    return FindTabStripFallback(tabGroup.transform) ?? strip;
		    return strip;
	    }

	    /// <summary>Strip HLG: control child widths but do not force equal expansion — that fights per-tab <see cref="LayoutElement.minWidth"/> when many add-ons load.</summary>
	    void PatchTabStripResponsiveLayout()
	    {
		    Transform strip = ResolveEffectiveTabStripTransform();
		    var h = strip != null ? strip.GetComponent<HorizontalLayoutGroup>() : null;
		    if (h == null) return;
		    h.childControlWidth = true;
		    h.childForceExpandWidth = false;
	    }

	    /// <summary>If sum of tab <see cref="LayoutElement.minWidth"/> exceeds the strip, scale mins down proportionally so the row reflows instead of breaking layout.</summary>
	    void RebalanceStripTabMinWidthsIfOverflowing(Transform strip)
	    {
		    var rt = strip as RectTransform;
		    var hlg = strip != null ? strip.GetComponent<HorizontalLayoutGroup>() : null;
		    if (rt == null || hlg == null) return;

		    var elements = new List<LayoutElement>();
		    foreach (var te in strip.GetComponentsInChildren<TabsGroupElem_UI>(true))
		    {
			    if (te == null || te.transform.parent != strip) continue;
			    var le = te.GetComponent<LayoutElement>();
			    if (le != null && !le.ignoreLayout)
				    elements.Add(le);
		    }
		    if (elements.Count == 0) return;

		    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
		    float width = rt.rect.width;
		    float budget = width - (float)(hlg.padding.left + hlg.padding.right)
		                   - hlg.spacing * Mathf.Max(0, elements.Count - 1);
		    if (budget < 8f) return;

		    float sumMin = 0f;
		    foreach (var le in elements)
			    sumMin += Mathf.Max(1f, le.minWidth);
		    if (sumMin <= budget) return;

		    float factor = budget / sumMin;
		    foreach (var le in elements)
		    {
			    float scaled = le.minWidth * factor;
			    le.minWidth = Mathf.Max(kRibbonStripTabMinWidthWhenCrowded, scaled);
		    }

		    sumMin = 0f;
		    foreach (var le in elements)
			    sumMin += Mathf.Max(1f, le.minWidth);
		    if (sumMin > budget && sumMin > 0.01f)
		    {
			    factor = budget / sumMin;
			    foreach (var le in elements)
				    le.minWidth = Mathf.Max(28f, le.minWidth * factor);
		    }

		    // 36px / 28px floors can still leave sum > budget (many tabs); shrink further until within budget or no progress.
		    sumMin = 0f;
		    foreach (var le in elements)
			    sumMin += Mathf.Max(1f, le.minWidth);
		    int guard = 0;
		    while (sumMin > budget + 0.5f && sumMin > 0.01f && guard++ < 28)
		    {
			    factor = budget / sumMin;
			    if (factor >= 0.999f)
				    break;
			    float beforeSum = sumMin;
			    sumMin = 0f;
			    foreach (var le in elements)
			    {
				    le.minWidth = Mathf.Max(1f, le.minWidth * factor);
				    sumMin += Mathf.Max(1f, le.minWidth);
			    }
			    if (sumMin >= beforeSum - 0.01f)
				    break;
		    }
	    }

	    /// <summary>Apply shared min/max auto-size to every tab label on the strip so width changes reflow text (built-in + Paint + add-ons).</summary>
	    void HarmonizeStripTabTypography()
	    {
		    Transform strip = ResolveEffectiveTabStripTransform();
		    if (strip == null) return;
		    TextMeshProUGUI refTmp = GetRibbonStripTypographyReferenceTMP(strip, null);
		    float designBasis = kRibbonStripTabLabelDefaultPt;
		    float basis = designBasis * SpzUiThemeOps.Active.fontScale;
		    // Match ApplyThemeTokens: icon-only widths only while non-builtin theme chrome is active.
		    bool iconOnly = SpzUiThemeOps.ShouldRecolorBoundChrome && SpzUiThemeOps.RibbonIconOnlyActive;
		    if (iconOnly)
			    return; // ApplyThemeTokens owns icon-only cell widths; do not auto-size hidden labels.
		    foreach (var elem in strip.GetComponentsInChildren<TabsGroupElem_UI>(true))
		    {
			    if (elem == null || elem.transform.parent != strip) continue;
			    var tmp = elem.GetComponentInChildren<TextMeshProUGUI>(true);
			    if (tmp == null) continue;
			    ConfigureResponsiveRibbonTabText(tmp, refTmp, basis);
			    var le = elem.GetComponent<LayoutElement>();
			    if (le != null)
				    ApplyStripTabMinWidthForLabel(le, tmp, kRibbonStripTabLabelHorizontalPad);
		    }
		    RebalanceStripTabMinWidthsIfOverflowing(strip);
	    }

	    // One tab + one panel per addon (Blender N-panel style)
	    Dictionary<string, RectTransform> _addonPanelsById = new Dictionary<string, RectTransform>();
	    Dictionary<string, GameObject> _addonTabById = new Dictionary<string, GameObject>();
	    /// <summary>Add-on folder ids sorted for Shift+6..9 (not <see cref="Dictionary{TKey,TValue}.Keys"/> order).</summary>
	    readonly List<string> _addonIdsShortcutOrder = new List<string>();
	    /// <summary>Per-addon ShowOnePanel handler so we can remove before re-<see cref="TabsGroup_UI.SubscribeForTab"/> (connectivity retry without duplicates).</summary>
	    Dictionary<string, Action<TabsGroupElem_UI>> _addonTabPanelShowByAddonId = new Dictionary<string, Action<TabsGroupElem_UI>>();
	    /// <summary>Vertical strip divider placed immediately before each add-on tab (not a <see cref="TabsGroupElem_UI"/>).</summary>
	    Dictionary<string, GameObject> _addonStripDividerById = new Dictionary<string, GameObject>();

	    Coroutine _attention_toCtrlNetButton_crtn = null;
	    Coroutine _rebuildTabStripLayout_crtn = null;
	    int _rebuildTabStripLayoutSeq = 0;
	    float _lastRibbonStripWidth = -1f;

	    public Panel _currentPanel { get; private set; } = Panel.Unknown;


	    public void Attention_toCtrlNetButton(){
	        if (_ctrlNetButton_anim == null) return;
	        if(_attention_toCtrlNetButton_crtn != null){ StopCoroutine(_attention_toCtrlNetButton_crtn); }
	        _attention_toCtrlNetButton_crtn = StartCoroutine( Attention_toCtrlNetButton_crtn() );
	    }

	    public void clickArtList_toggle_manual() { if (_tabGroup != null) _tabGroup.SwitchTab("art list"); }
	    public void clickArtBGList_toggle_manual() { if (_tabGroup != null) _tabGroup.SwitchTab("art bg list"); }
	    public void clickPaint_toggle_manual() { if (_tabGroup != null) _tabGroup.SwitchTab("paint"); }

	    /// <summary>Add-on / automation: switch ribbon tab by <see cref="TabsGroupElem_UI.title"/> (e.g. <c>paint</c>, <c>addon_MyAddon</c>).</summary>
	    public bool TrySwitchRibbonTabByTitle(string tabTitle) {
		    EnsureTabGroupResolved();
		    if (_tabGroup == null || string.IsNullOrEmpty(tabTitle)) return false;
		    return _tabGroup.TrySwitchTab(tabTitle);
	    }

	    /// <summary>Tab titles currently registered on the strip (for discovery via JSON-RPC).</summary>
	    public List<string> GetRibbonTabTitles() {
		    EnsureTabGroupResolved();
		    if (_tabGroup == null) return new List<string>();
		    return _tabGroup.GetTabTitles();
	    }


	    IEnumerator Attention_toCtrlNetButton_crtn(){
	        if (_ctrlNetButton_anim == null || _ctrlNetButton_anim.transform.childCount < 1) yield break;
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
	        if (go == null) return;
	        go.SetActive(true);
	        if (_SD_ArtList_Panel != null && go != _SD_ArtList_Panel.gameObject) _SD_ArtList_Panel.gameObject.SetActive(false);
	        if (_SD_ArtBgList_Panel != null && go != _SD_ArtBgList_Panel.gameObject) _SD_ArtBgList_Panel.gameObject.SetActive(false);
	        if (_SD_3D_Models_Panels != null && go != _SD_3D_Models_Panels.gameObject) _SD_3D_Models_Panels.gameObject.SetActive(false);
	        if (_SD_ControlNets_List_Panel != null && go != _SD_ControlNets_List_Panel.gameObject) _SD_ControlNets_List_Panel.gameObject.SetActive(false);
	        if (_Paint_Panel != null && go != _Paint_Panel.gameObject) _Paint_Panel.gameObject.SetActive(false);
	        foreach(var p in _addonPanelsById.Values)
	            if(p != null && p.gameObject != go) p.gameObject.SetActive(false);
	        // Eager EnableAddon shells can exist before Python create_panel — activate nested content and surface empty state.
	        // Only real add-on shells (in _addonPanelsById). Built-in Paint is also named Panel_Paint and must not get the HTTP :5557 placeholder.
	        if (IsRegisteredAddonShell(go))
		        ActivateAddonShellContentOrPlaceholder(go.transform);
	        else
		        ClearMistakenAddonShellPlaceholder(go.transform);
	        if (KeyMousePenInput.isKey_Shift_pressed() == false && Viewport_StatusText.instance != null){
	            string msg = "Use Shift+1, Shift+2, etc to switch tabs faster :)";
	            Viewport_StatusText.instance.ShowStatusText(msg, false, 1.2f, false);
	        }
	    }

	    /// <summary>True when <paramref name="go"/> is an add-on ribbon body registered via <see cref="GetOrCreatePanelForAddon"/>.</summary>
	    bool IsRegisteredAddonShell(GameObject go) {
		    if (go == null || _addonPanelsById == null || _addonPanelsById.Count == 0) return false;
		    foreach (var shell in _addonPanelsById.Values) {
			    if (shell != null && shell.gameObject == go) return true;
		    }
		    return false;
	    }

	    /// <summary>Removes a placeholder wrongly attached to built-in panels (e.g. Panel_Paint matched the Panel_ prefix).</summary>
	    static void ClearMistakenAddonShellPlaceholder(Transform shell) {
		    if (shell == null) return;
		    Transform ph = shell.Find("AddonShell_WaitingPlaceholder");
		    if (ph != null)
			    Destroy(ph.gameObject);
	    }

	    /// <summary>
	    /// Ensures <c>AddonPanel_*</c> children are active under an add-on shell; if Python has not populated UI yet, show a visible placeholder
	    /// and (for known add-ons) seed a native fallback panel so the tab is not blank when HTTP :5557 is down.
	    /// </summary>
	    void ActivateAddonShellContentOrPlaceholder(Transform shell) {
		    if (shell == null) return;
		    string addonId = null;
		    if (shell.name != null && shell.name.StartsWith("Panel_", StringComparison.Ordinal) && shell.name.Length > 6)
			    addonId = shell.name.Substring("Panel_".Length);

		    // Always try native seed for known add-ons (title-only shells must not skip this).
		    if (!string.IsNullOrEmpty(addonId) && AddonUI_MGR.instance != null)
			    AddonUI_MGR.instance.EnsureNativeFallbackUiWhenPythonMissing(addonId);

		    bool hasAddonContent = ShellHasAddonPanelWidgets(shell);

		    const string placeholderName = "AddonShell_WaitingPlaceholder";
		    Transform existingPh = shell.Find(placeholderName);
		    if (hasAddonContent) {
			    if (existingPh != null)
				    Destroy(existingPh.gameObject);
		    } else if (existingPh == null) {
			    CreateVisibleAddonShellPlaceholder(shell, placeholderName, addonId);
		    } else {
			    existingPh.gameObject.SetActive(true);
		    }
		    var rt = shell as RectTransform;
		    if (rt != null)
			    LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

		    if (!hasAddonContent && Viewport_StatusText.instance != null) {
			    Viewport_StatusText.instance.ShowStatusText(
				    "Add-on UI empty — Python HTTP :5557 not ready. Enable FastAPI (pip install fastapi uvicorn) or rebuild with latest fixes.",
				    false, 5f, false);
		    }
	    }

	    /// <summary>True when shell has an AddonPanel with at least one real control (not Title-only).</summary>
	    static bool ShellHasAddonPanelWidgets(Transform shell) {
		    if (shell == null) return false;
		    for (int i = 0; i < shell.childCount; i++) {
			    Transform ch = shell.GetChild(i);
			    if (ch == null) continue;
			    string cn = ch.name ?? "";
			    if (!cn.StartsWith("AddonPanel_", StringComparison.Ordinal)) continue;
			    ch.gameObject.SetActive(true);
			    if (AddonPanelHasWidgetDescendant(ch))
				    return true;
		    }
		    return false;
	    }

	    static bool AddonPanelHasWidgetDescendant(Transform panel) {
		    if (panel == null) return false;
		    var transforms = panel.GetComponentsInChildren<Transform>(true);
		    for (int i = 0; i < transforms.Length; i++) {
			    Transform w = transforms[i];
			    if (w == null || w == panel) continue;
			    string wn = w.name ?? "";
			    if (string.Equals(wn, "Title", StringComparison.Ordinal)) continue;
			    if (wn.StartsWith("Button_", StringComparison.Ordinal)
			        || wn.StartsWith("TextInput_", StringComparison.Ordinal)
			        || wn.StartsWith("Slider_", StringComparison.Ordinal)
			        || wn.StartsWith("Dropdown_", StringComparison.Ordinal)
			        || wn.StartsWith("Toggle_", StringComparison.Ordinal))
				    return true;
		    }
		    return false;
	    }

	    void CreateVisibleAddonShellPlaceholder(Transform shell, string placeholderName, string addonId) {
		    var phGo = new GameObject(placeholderName);
		    phGo.transform.SetParent(shell, false);
		    var phRt = phGo.AddComponent<RectTransform>();
		    phRt.anchorMin = new Vector2(0f, 1f);
		    phRt.anchorMax = new Vector2(1f, 1f);
		    phRt.pivot = new Vector2(0.5f, 1f);
		    phRt.sizeDelta = new Vector2(0f, 96f);
		    phRt.anchoredPosition = Vector2.zero;
		    var le = phGo.AddComponent<LayoutElement>();
		    le.minHeight = 72f;
		    le.preferredHeight = 96f;
		    le.flexibleWidth = 1f;
		    // Banner so the empty state is visible even if TMP has no font asset in the player.
		    var bg = phGo.AddComponent<Image>();
		    bg.color = new Color(0.12f, 0.35f, 0.55f, 0.95f);
		    bg.raycastTarget = false;

		    var textGo = new GameObject("Label");
		    textGo.transform.SetParent(phGo.transform, false);
		    var textRt = textGo.AddComponent<RectTransform>();
		    textRt.anchorMin = Vector2.zero;
		    textRt.anchorMax = Vector2.one;
		    textRt.offsetMin = new Vector2(10f, 8f);
		    textRt.offsetMax = new Vector2(-10f, -8f);
		    var tmp = textGo.AddComponent<TextMeshProUGUI>();
		    string idBit = string.IsNullOrEmpty(addonId) ? "this add-on" : addonId;
		    tmp.text = $"Waiting for {idBit} UI…\nPython create_panel has not run (HTTP :5557). Use Add-on Manager → Load addons now after FastAPI is up.";
		    tmp.fontSize = 14;
		    tmp.color = Color.white;
		    tmp.alignment = TextAlignmentOptions.TopLeft;
		    tmp.enableWordWrapping = true;
		    tmp.raycastTarget = false;
		    // Copy font from a strip tab so IL2CPP players render text (bare TMP has no default font).
		    TextMeshProUGUI fontSrc = GetRibbonStripTypographyReferenceTMP(ResolveEffectiveTabStripTransform(), null);
		    if (fontSrc == null && _tabGroup != null)
			    fontSrc = _tabGroup.GetComponentInChildren<TextMeshProUGUI>(true);
		    if (fontSrc != null && fontSrc.font != null) {
			    tmp.font = fontSrc.font;
			    tmp.fontSharedMaterial = fontSrc.fontSharedMaterial;
		    }
	    }

	    void OnArtList_Toggle(TabsGroupElem_UI tab){
	        if (_SD_ArtList_Panel == null) return;
	        ShowOnePanel( _SD_ArtList_Panel.gameObject );
	        _currentPanel = Panel.Art;
	    }

	    void OnArtBgList_Toggle(TabsGroupElem_UI tab){
	        if (_SD_ArtBgList_Panel == null) return;
	        ShowOnePanel( _SD_ArtBgList_Panel.gameObject );
	        _currentPanel = Panel.ArtBG;
	    }

	    void On_3D_Meshes_Toggle(TabsGroupElem_UI tab){
	        if (_SD_3D_Models_Panels == null) return;
	        ShowOnePanel( _SD_3D_Models_Panels.gameObject );
	        _currentPanel = Panel.Obj3D;
	    }

	    void On_ControlNets_Toggle(TabsGroupElem_UI tab){
	        if (_SD_ControlNets_List_Panel == null) return;
	        ShowOnePanel( _SD_ControlNets_List_Panel.gameObject );
	        _currentPanel = Panel.CtrlNet;
	    }

	    void On_Paint_Toggle(TabsGroupElem_UI tab){
	        if (_Paint_Panel == null) return;
	        ShowOnePanel( _Paint_Panel.gameObject );
	        _currentPanel = Panel.Paint;
	    }

	    void Update(){
		    if (isActiveAndEnabled) {
			    EnsureTabGroupResolved();
			    Transform stripPoll = ResolveEffectiveTabStripTransform();
			    var stripRtPoll = stripPoll as RectTransform;
			    if (stripRtPoll != null) {
				    float wPoll = stripRtPoll.rect.width;
				    if (_lastRibbonStripWidth < 0f)
					    _lastRibbonStripWidth = wPoll;
				    else if (Mathf.Abs(wPoll - _lastRibbonStripWidth) > 1f) {
					    _lastRibbonStripWidth = wPoll;
					    RefreshTabStripLayout();
				    }
			    } else {
				    _lastRibbonStripWidth = -1f;
			    }
			    SyncStripTabSelectionChromeIfChanged();
		    }
	        if(KeyMousePenInput.isSomeInputFieldActive()){ return;} //maybe typing some exclamation mark etc.
	        if (KeyMousePenInput.isKey_Shift_pressed() == false){ return; }
	        EnsureTabGroupResolved();
	        if (_tabGroup == null) return; // TabsGroup_UI may not be found at init; avoid NullReferenceException on Shift+1..9
	        if (Input.GetKeyDown(KeyCode.Alpha1)){ _tabGroup.SwitchTab("art list"); }
	        if (Input.GetKeyDown(KeyCode.Alpha2)){ _tabGroup.SwitchTab("art bg list"); }
	        if (Input.GetKeyDown(KeyCode.Alpha3)){ _tabGroup.SwitchTab("mesh"); }
	        if (Input.GetKeyDown(KeyCode.Alpha4)){ _tabGroup.SwitchTab("controlnet"); }
	        if (Input.GetKeyDown(KeyCode.Alpha5)){ _tabGroup.SwitchTab("paint"); }
	        // Shift+6..9: add-ons in stable order (folder id, ordinal case-insensitive sort)
	        int addonIdx = 6;
	        for (int i = 0; i < _addonIdsShortcutOrder.Count && addonIdx <= 9; i++) {
	            string addonId = _addonIdsShortcutOrder[i];
	            if (string.IsNullOrEmpty(addonId)) continue;
	            if (!_addonPanelsById.TryGetValue(addonId, out var shell) || shell == null || shell.gameObject == null) continue;
	            if (Input.GetKeyDown(KeyCode.Alpha0 + addonIdx))
		            _tabGroup.SwitchTab(AddonRibbonIntegration.TabIdForAddon(addonId));
	            addonIdx++;
	        }
	    }
    

	    void Awake(){
	        if(instance != null){  DestroyImmediate(this); return; }
	        instance = this;

	        TryResolvePanelRefs();
	        EnsureTabGroupResolved();
	        EnsurePaintTabExists();
	        PatchTabStripResponsiveLayout();
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

	        HarmonizeStripTabTypography();
	        SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
	        ApplyThemeTokens();
	    }

	    void OnDestroy() {
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	        if (instance == this)
	            instance = null;
	    }

	    string _lastStripSelectionKey = "";
	    bool _lastStripNomadChrome;

	    static void RecolorOrRestorePanelShell(RectTransform panel, bool recolorChrome) {
	        if (panel == null) return;
	        if (!recolorChrome) {
	            SpzUiThemeOps.RestoreBoundChromeUnder(panel);
	            return;
	        }
	        var img = panel.GetComponent<Image>();
	        if (img == null) return;
	        ApplyPanelShellColor(panel, SpzUiThemeOps.Active.panelBg);
	    }

	    /// <summary>
	    /// Ownership-root theme apply for ribbon strip cells, active pills, panel shells, and dividers.
	    /// Nomad: flat dark cells + subtle selected fill (no gold 0.72 pill) + strip label typography.
	    /// Builtin: full RestoreBoundChromeUnder + hide Monolith overlays.
	    /// </summary>
	    void ApplyThemeTokens() {
	        var t = SpzUiThemeOps.Active;
	        bool recolorChrome = SpzUiThemeOps.ShouldRecolorBoundChrome;
	        bool iconOnly = recolorChrome && SpzUiThemeOps.RibbonIconOnlyActive;

	        RecolorOrRestorePanelShell(_SD_ArtList_Panel, recolorChrome);
	        RecolorOrRestorePanelShell(_SD_ArtBgList_Panel, recolorChrome);
	        RecolorOrRestorePanelShell(_SD_3D_Models_Panels, recolorChrome);
	        RecolorOrRestorePanelShell(_SD_ControlNets_List_Panel, recolorChrome);
	        RecolorOrRestorePanelShell(_Paint_Panel, recolorChrome);

	        Transform strip = ResolveEffectiveTabStripTransform();
	        if (strip != null) {
	            if (!recolorChrome) {
	                SpzUiThemeOps.RestoreBoundChromeUnder(strip);
	                // Belt-and-suspenders: Nomad Monolith overlays must not remain blended over OG tabs.
	                HideMonolithOverlaysUnder(strip);
	            }

	            for (int i = 0; i < strip.childCount; i++) {
	                Transform cell = strip.GetChild(i);
	                if (cell == null) continue;
	                if (cell.name.StartsWith("StripDivider_", StringComparison.Ordinal)
	                    || cell.name.StartsWith("AddonDivider_", StringComparison.Ordinal)) {
	                    var divImg = cell.GetComponent<Image>();
	                    if (divImg != null) {
	                        if (recolorChrome) {
	                            Color c = t.border;
	                            c.a = Mathf.Max(c.a, 0.55f);
	                            SpzUiThemeOps.ApplyBoundChromeGraphic(divImg, c);
	                        }
	                        // Dividers are visual only — never steal adjacent tab clicks.
	                        divImg.raycastTarget = false;
	                    }
	                    continue;
	                }
	                if (cell.GetComponent<TabsGroupElem_UI>() == null)
	                    continue;
	                ThemeStripTabCell(cell, t, recolorChrome, iconOnly);
	            }
	        }

	        if (_addonPanelsById != null) {
	            foreach (var kvp in _addonPanelsById) {
	                if (kvp.Value != null)
	                    RecolorOrRestorePanelShell(kvp.Value, recolorChrome);
	            }
	        }

	        if (!iconOnly)
	            HarmonizeStripTabTypography();

	        if (strip != null && strip is RectTransform stripRt)
	            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(stripRt);

	        SnapshotStripTabSelectionChrome();
	    }

	    /// <summary>Flat tool fill: selected = subtle accent mix (never gold plate).</summary>
	    static Color FlatStripTabFill(bool selected, SpzUiThemeOps.ThemeTokens t) {
	        return selected
	            ? Color.Lerp(t.controlBg, t.accent, 0.14f)
	            : t.controlBg;
	    }

	    static Image FindStripTabFaceImage(Transform cell) {
	        if (cell == null) return null;
	        var tabBg = cell.Find("TabBg")?.GetComponent<Image>();
	        if (tabBg != null) return tabBg;
	        // Find() skips inactive children — runtime tabs may hide TabBg briefly.
	        Transform tabBgT = SpzUiThemeOps.FindDirectChildIncludingInactive(cell, "TabBg");
	        if (tabBgT != null) {
	            var inactiveBg = tabBgT.GetComponent<Image>();
	            if (inactiveBg != null) return inactiveBg;
	        }
	        var btn = cell.GetComponent<Button>();
	        if (btn != null && btn.targetGraphic is Image btnImg)
	            return btnImg;
	        return cell.GetComponent<Image>();
	    }

	    /// <summary>
	    /// Prefab Art/BG/Mesh/Control tabs have null <see cref="Button.targetGraphic"/> and no TabBg —
	    /// OG clicks landed on TMP labels. Nomad <see cref="SpzUiThemeOps.ApplyBoundChromeStripLabelTmp"/>
	    /// clears those label raycasts → dead strip. Ensure a stretch hit face + wire the Button.
	    /// </summary>
	    static Image EnsureStripTabHitFace(Transform cell) {
	        if (cell == null) return null;
	        var btn = cell.GetComponent<Button>();
	        Image face = FindStripTabFaceImage(cell);
	        if (face == null) {
	            var go = new GameObject("TabBg", typeof(RectTransform));
	            go.transform.SetParent(cell, false);
	            go.transform.SetAsFirstSibling();
	            var rt = go.GetComponent<RectTransform>();
	            rt.anchorMin = Vector2.zero;
	            rt.anchorMax = Vector2.one;
	            rt.pivot = new Vector2(0.5f, 0.5f);
	            rt.anchoredPosition = Vector2.zero;
	            rt.sizeDelta = Vector2.zero;
	            rt.offsetMin = Vector2.zero;
	            rt.offsetMax = Vector2.zero;
	            face = go.AddComponent<Image>();
	            go.AddComponent<SpzUiThemeSyntheticHitFace>();
	            // Invisible until BoundChrome paints fill — still receives hits (label-cleared litmus).
	            face.color = new Color(1f, 1f, 1f, 0f);
	            face.raycastTarget = true;
	            face.sprite = UiRuntimeSprites.SolidRect;
	            face.type = Image.Type.Simple;
	            face.preserveAspect = false;
	        }
	        if (btn != null && (btn.targetGraphic == null || !ReferenceEquals(btn.targetGraphic, face)))
	            btn.targetGraphic = face;
	        face.raycastTarget = true;
	        return face;
	    }

	    static void FlattenStripTabFace(Image img) {
	        if (img == null) return;
	        SpzUiThemeOps.ApplyRoundedControlSprite(img, markEligible: true);
	        img.preserveAspect = false;
	    }

	    /// <summary>Themes one strip menu item (Art / BG / Mesh / Control / Paint / add-on).</summary>
	    void ThemeStripTabCell(Transform cell, SpzUiThemeOps.ThemeTokens t, bool recolorChrome, bool iconOnly) {
	        if (cell == null) return;
	        var elem = cell.GetComponent<TabsGroupElem_UI>();
	        bool selected = elem != null && elem.IsVisuallySelectedAsActiveTab();

	        if (!recolorChrome) {
	            foreach (var label in cell.GetComponentsInChildren<TextMeshProUGUI>(true)) {
	                if (label == null) continue;
	                label.maxVisibleCharacters = int.MaxValue;
	            }
	            // Do not EnsureStripTabHitFace on leave — Restore already unwound raycasts;
	            // injecting TabBg after Restore SPZ sticks forever and can steal hits.
	            ApplyStudioTabChromeColors(cell, t, recolorChrome: false);
	            return;
	        }

	        Color fill = FlatStripTabFill(selected, t);
	        Image face = EnsureStripTabHitFace(cell);
	        if (face != null) {
	            SpzUiThemeOps.ApplyBoundChromeGraphic(face, fill);
	            FlattenStripTabFace(face);
	            face.raycastTarget = true;
	        }

	        var active = cell.Find("go active");
	        if (active != null) {
	            var pill = FindActivePillImage(active);
	            if (pill != null) {
	                // Flatten authored flared/gold slice into the same flat selected fill.
	                SpzUiThemeOps.ApplyBoundChromeGraphic(pill, fill);
	                FlattenStripTabFace(pill);
	            }
	        }

	        foreach (var label in cell.GetComponentsInChildren<TextMeshProUGUI>(true)) {
	            if (label == null) continue;
	            if (iconOnly) {
	                label.maxVisibleCharacters = 0;
	                SpzUiThemeOps.ApplyBoundChromeTmp(label,
	                    new Color(t.textPrimary.r, t.textPrimary.g, t.textPrimary.b, 0f),
	                    kRibbonStripTabLabelDefaultPt);
	            } else {
	                label.maxVisibleCharacters = int.MaxValue;
	                SpzUiThemeOps.ApplyBoundChromeStripLabelTmp(label, t.textPrimary, kRibbonStripTabLabelDefaultPt);
	            }
	        }

	        ApplyStudioTabChromeColors(cell, t, recolorChrome: true);
	        ClearStripTabNonFaceRaycasts(cell);
	    }

	    /// <summary>
	    /// Prefab divider left/right, go-active pill, and TMP labels ship with raycastTarget=1 and
	    /// steal clicks from the tab Button under Nomad icon-only (tight cells). Only the Button face hits.
	    /// Prefab Art/BG/Mesh/Control tabs often have <c>Button.targetGraphic == null</c> and no TabBg —
	    /// <see cref="EnsureStripTabHitFace"/> creates/wires the face before this runs.
	    /// </summary>
	    static void ClearStripTabNonFaceRaycasts(Transform cell) {
	        if (cell == null) return;
	        var btn = cell.GetComponent<Button>();
	        Graphic face = null;
	        if (btn != null && btn.targetGraphic != null)
	            face = btn.targetGraphic;
	        else
	            face = FindStripTabFaceImage(cell);
	        // Prefab tabs ship with null targetGraphic; wire TabBg so ColorTint + hits stay on the face.
	        if (btn != null && btn.targetGraphic == null && face != null)
	            btn.targetGraphic = face;
	        // No resolvable face → do not mass-clear (would make the whole tab unclickable under Nomad).
	        if (face == null)
	            return;
	        foreach (var g in cell.GetComponentsInChildren<Graphic>(true)) {
	            if (g == null) continue;
	            if (ReferenceEquals(g, face)) {
	                SpzUiThemeOps.SnapshotAuthoredGraphicForTheme(g);
	                g.raycastTarget = true;
	                continue;
	            }
	            SpzUiThemeOps.SnapshotAuthoredGraphicForTheme(g);
	            g.raycastTarget = false;
	        }
	    }

	    /// <summary>Force-hide Nomad MonolithLineIcon / MonolithActiveBar under a strip root (Restore SPZ).</summary>
	    static void HideMonolithOverlaysUnder(Transform root) {
	        if (root == null) return;
	        foreach (var tr in root.GetComponentsInChildren<Transform>(true)) {
	            if (tr == null) continue;
	            string n = tr.name ?? "";
	            if (n == "MonolithLineIcon" || n == "MonolithActiveBar")
	                tr.gameObject.SetActive(false);
	        }
	    }

	    string BuildStripSelectionKey() {
	        Transform strip = ResolveEffectiveTabStripTransform();
	        if (strip == null) return "";
	        var sb = new System.Text.StringBuilder(64);
	        for (int i = 0; i < strip.childCount; i++) {
	            Transform cell = strip.GetChild(i);
	            if (cell == null) continue;
	            var elem = cell.GetComponent<TabsGroupElem_UI>();
	            if (elem == null) continue;
	            sb.Append(elem.IsVisuallySelectedAsActiveTab() ? '1' : '0');
	            sb.Append(cell.GetInstanceID());
	            sb.Append(';');
	        }
	        return sb.ToString();
	    }

	    void SnapshotStripTabSelectionChrome() {
	        _lastStripNomadChrome = SpzUiThemeOps.ShouldRecolorBoundChrome;
	        _lastStripSelectionKey = BuildStripSelectionKey();
	    }

	    /// <summary>Tab clicks toggle go-active without ThemeChanged — re-tint flat fills when selection moves.</summary>
	    void SyncStripTabSelectionChromeIfChanged() {
	        bool nomad = SpzUiThemeOps.ShouldRecolorBoundChrome;
	        if (!nomad) {
	            if (_lastStripNomadChrome)
	                ApplyThemeTokens();
	            return;
	        }
	        string key = BuildStripSelectionKey();
	        if (key == _lastStripSelectionKey && _lastStripNomadChrome)
	            return;
	        var t = SpzUiThemeOps.Active;
	        bool iconOnly = SpzUiThemeOps.RibbonIconOnlyActive;
	        Transform strip = ResolveEffectiveTabStripTransform();
	        if (strip == null) return;
	        for (int i = 0; i < strip.childCount; i++) {
	            Transform cell = strip.GetChild(i);
	            if (cell == null || cell.GetComponent<TabsGroupElem_UI>() == null) continue;
	            ThemeStripTabCell(cell, t, recolorChrome: true, iconOnly);
	        }
	        SnapshotStripTabSelectionChrome();
	    }

	    /// <summary>Prefer the authored pill graphic; never retint Monolith chrome overlays.</summary>
	    static Image FindActivePillImage(Transform active) {
	        if (active == null) return null;
	        var named = active.Find("image")?.GetComponent<Image>();
	        if (named != null) return named;
	        var rootImg = active.GetComponent<Image>();
	        if (rootImg != null) return rootImg;
	        foreach (var img in active.GetComponentsInChildren<Image>(true)) {
	            if (img == null) continue;
	            string n = img.gameObject.name ?? "";
	            if (n == "MonolithActiveBar" || n == "MonolithLineIcon") continue;
	            return img;
	        }
	        return null;
	    }

	    /// <summary>
	    /// Studio chrome on strip tabs: active bar + line icon (create if missing).
	    /// Only when <paramref name="recolorChrome"/> (non-builtin theme): create/show Monolith icons and tint.
	    /// Builtin default: hide Monolith overlays so strip matches authored OG text tabs.
	    /// When <see cref="SpzUiThemeOps.RibbonIconOnlyActive"/>, centers a larger icon (Nomad-like).
	    /// </summary>
	    static void ApplyStudioTabChromeColors(Transform cell, SpzUiThemeOps.ThemeTokens t, bool recolorChrome = true) {
	        if (cell == null) return;
	        bool iconOnly = recolorChrome && SpzUiThemeOps.RibbonIconOnlyActive;
	        Transform active = cell.Find("go active");
	        Transform bar = active != null
	            ? SpzUiThemeOps.FindDirectChildIncludingInactive(active, "MonolithActiveBar")
	            : null;
	        Transform iconTransform = SpzUiThemeOps.FindDirectChildIncludingInactive(cell, "MonolithLineIcon");

	        if (!recolorChrome) {
	            if (bar != null)
	                bar.gameObject.SetActive(false);
	            if (iconTransform != null)
	                iconTransform.gameObject.SetActive(false);
	            var leBuiltin = cell.GetComponent<LayoutElement>();
	            if (leBuiltin != null) {
	                leBuiltin.flexibleWidth = 1f;
	                leBuiltin.preferredWidth = -1f;
	                // Drop icon-only lock so Harmonize / label measure can reflow.
	                leBuiltin.minWidth = 0f;
	            }
	            return;
	        }

	        // Nomad sculpt: thin accent underline under the active pill (create if missing).
	        if (active != null) {
	            if (bar == null) {
	                var barGo = new GameObject("MonolithActiveBar", typeof(RectTransform));
	                barGo.transform.SetParent(active, false);
	                bar = barGo.transform;
	                var barImgNew = barGo.AddComponent<Image>();
	                barImgNew.raycastTarget = false;
	            }
	            var barRt = bar as RectTransform;
	            if (barRt != null) {
	                barRt.anchorMin = new Vector2(0.18f, 0f);
	                barRt.anchorMax = new Vector2(0.82f, 0f);
	                barRt.pivot = new Vector2(0.5f, 0f);
	                barRt.sizeDelta = new Vector2(0f, 2f);
	                barRt.anchoredPosition = Vector2.zero;
	            }
	            bar.gameObject.SetActive(true);
	            var barImg = bar.GetComponent<Image>();
	            if (barImg != null)
	                barImg.color = t.accent;
	            var pill = FindActivePillImage(active);
	            if (pill != null)
	                pill.enabled = true;
	        }
	        if (iconTransform == null) {
	            var go = new GameObject("MonolithLineIcon", typeof(RectTransform));
	            go.transform.SetParent(cell, false);
	            iconTransform = go.transform;
	            var img = go.AddComponent<Image>();
	            img.raycastTarget = false;
	            img.preserveAspect = true;
	        }
	        iconTransform.gameObject.SetActive(true);
	        var iconRt = iconTransform as RectTransform;
	        if (iconRt != null) {
	            // Icon-only and label+icon share the same center so strip glyphs stay aligned.
	            iconRt.anchorMin = new Vector2(0.5f, 0.5f);
	            iconRt.anchorMax = new Vector2(0.5f, 0.5f);
	            iconRt.pivot = new Vector2(0.5f, 0.5f);
	            iconRt.anchoredPosition = Vector2.zero;
	            iconRt.sizeDelta = iconOnly ? new Vector2(24f, 24f) : new Vector2(18f, 18f);
	        }
	        var icon = iconTransform.GetComponent<Image>();
	        if (icon != null) {
	            var overrideMark = cell.GetComponent<SpzStripLineIconOverride>();
	            StudioLineIcon glyph = overrideMark != null
	                ? overrideMark.Icon
	                : ResolveStripTabLineIcon(cell);
	            icon.sprite = UiRuntimeSprites.GetLineIcon(glyph);
	            SpzUiThemeOps.ApplyLineIconTint(icon);
	        }
	        EnsureStripTabHoverTooltip(cell);
	        // Tighten strip cell when icon-only so the row reads like a toolbox.
	        // Leaving icon-only must clear preferredWidth/flexibleWidth locks so Harmonize can reflow.
	        var le = cell.GetComponent<LayoutElement>();
	        if (le != null) {
	            if (iconOnly) {
	                le.flexibleWidth = 0f;
	                le.preferredWidth = 44f;
	                le.minWidth = 40f;
	            } else {
	                le.flexibleWidth = 1f;
	                le.preferredWidth = -1f;
	                // Clear icon-only minWidth before Harmonize; empty labels early-out and would leave 40px locks.
	                le.minWidth = 0f;
	            }
	        }
	    }

	    /// <summary>Hover label for icon-only strip tabs (Paint, Art, Mesh, …).</summary>
	    static void EnsureStripTabHoverTooltip(Transform cell) {
	        if (cell == null) return;
	        var tip = cell.GetComponent<CanShowTooltip_UI>();
	        if (tip == null)
	            tip = cell.gameObject.AddComponent<CanShowTooltip_UI>();
	        tip.set_overrideMessage(ResolveStripTabDisplayName(cell));
	    }

	    /// <summary>User-facing name for tooltips — prefers TabsGroup title (prettified), then visible label.</summary>
	    public static string ResolveStripTabDisplayName(Transform cell) {
	        if (cell == null) return "Tab";
	        var elem = cell.GetComponent<TabsGroupElem_UI>();
	        if (elem != null && !string.IsNullOrWhiteSpace(elem.title)) {
	            string pretty = PrettifyStripTabTitle(elem.title);
	            if (!string.IsNullOrWhiteSpace(pretty))
	                return pretty;
	        }
	        foreach (var tmp in cell.GetComponentsInChildren<TextMeshProUGUI>(true)) {
	            if (tmp == null) continue;
	            string label = (tmp.text ?? "").Trim();
	            if (label.Length == 0) continue;
	            // Collapse whitespace / newlines from prefab labels like "ART  (BG)".
	            label = System.Text.RegularExpressions.Regex.Replace(label, @"\s+", " ");
	            return PrettifyStripTabLabel(label);
	        }
	        string n = cell.name ?? "";
	        if (n.StartsWith("Tab:", StringComparison.OrdinalIgnoreCase))
	            return PrettifyStripTabTitle(n.Substring(4).Trim());
	        if (n.StartsWith("AddonTab_", StringComparison.OrdinalIgnoreCase))
	            return n.Substring("AddonTab_".Length).Trim();
	        return string.IsNullOrWhiteSpace(n) ? "Tab" : n;
	    }

	    public static string PrettifyStripTabTitle(string title) {
	        string t = (title ?? "").Trim();
	        if (string.Equals(t, "paint", StringComparison.OrdinalIgnoreCase)) return "Paint";
	        if (string.Equals(t, "art list", StringComparison.OrdinalIgnoreCase)) return "Art";
	        if (string.Equals(t, "art bg list", StringComparison.OrdinalIgnoreCase)) return "Art BG";
	        if (string.Equals(t, "mesh", StringComparison.OrdinalIgnoreCase)) return "Mesh";
	        if (string.Equals(t, "3d", StringComparison.OrdinalIgnoreCase)) return "Mesh";
	        if (string.Equals(t, "controlnet", StringComparison.OrdinalIgnoreCase)) return "Control";
	        if (t.StartsWith("addon_", StringComparison.OrdinalIgnoreCase))
	            return t.Substring(6);
	        return t;
	    }

	    /// <summary>Normalize prefab TMP labels (ctrl, ART, ART (BG)) for hover tooltips.</summary>
	    public static string PrettifyStripTabLabel(string label) {
	        string t = (label ?? "").Trim();
	        if (string.Equals(t, "ctrl", StringComparison.OrdinalIgnoreCase)
	            || string.Equals(t, "control", StringComparison.OrdinalIgnoreCase)
	            || string.Equals(t, "controlnet", StringComparison.OrdinalIgnoreCase))
	            return "Control";
	        if (string.Equals(t, "ART", StringComparison.OrdinalIgnoreCase)
	            || string.Equals(t, "Art", StringComparison.Ordinal))
	            return "Art";
	        if (t.IndexOf("BG", StringComparison.OrdinalIgnoreCase) >= 0
	            && t.IndexOf("ART", StringComparison.OrdinalIgnoreCase) >= 0)
	            return "Art BG";
	        if (string.Equals(t, "mesh", StringComparison.OrdinalIgnoreCase)
	            || string.Equals(t, "3d", StringComparison.OrdinalIgnoreCase))
	            return "Mesh";
	        if (string.Equals(t, "paint", StringComparison.OrdinalIgnoreCase))
	            return "Paint";
	        return t;
	    }

	/// <summary>Map strip tab identity (name + title + label) to a descriptive line icon.</summary>
	    public static StudioLineIcon ResolveStripTabLineIcon(Transform cell) {
	        string hay = BuildStripTabMatchHaystack(cell);
	        return ResolveStripTabLineIconFromHaystack(hay);
	    }

	    /// <summary>Legacy name-only resolve kept for callers that only have a string.</summary>
	    static StudioLineIcon ResolveStripTabLineIcon(string cellName) {
	        return ResolveStripTabLineIconFromHaystack(cellName ?? "");
	    }

	    static string BuildStripTabMatchHaystack(Transform cell) {
	        if (cell == null) return "";
	        var sb = new System.Text.StringBuilder(cell.name ?? "");
	        var elem = cell.GetComponent<TabsGroupElem_UI>();
	        if (elem != null && !string.IsNullOrEmpty(elem.title)) {
	            sb.Append(' ');
	            sb.Append(elem.title);
	        }
	        foreach (var tmp in cell.GetComponentsInChildren<TextMeshProUGUI>(true)) {
	            if (tmp == null || string.IsNullOrEmpty(tmp.text)) continue;
	            sb.Append(' ');
	            sb.Append(tmp.text);
	        }
	        return sb.ToString();
	    }

	    public static StudioLineIcon ResolveStripTabLineIconFromHaystack(string haystack) {
	        string n = haystack ?? "";
	        // Order matters: Paint before Art (label "Paint" must not fall through).
	        if (n.IndexOf("Paint", StringComparison.OrdinalIgnoreCase) >= 0)
	            return StudioLineIcon.Brush;
	        if (n.IndexOf("Control", StringComparison.OrdinalIgnoreCase) >= 0
	            || n.IndexOf("CTRL", StringComparison.OrdinalIgnoreCase) >= 0
	            || n.IndexOf("controlnet", StringComparison.OrdinalIgnoreCase) >= 0)
	            return StudioLineIcon.Grid;
	        if (n.IndexOf("Mesh", StringComparison.OrdinalIgnoreCase) >= 0
	            || n.IndexOf("3D", StringComparison.OrdinalIgnoreCase) >= 0
	            || n.IndexOf("Obj", StringComparison.OrdinalIgnoreCase) >= 0)
	            return StudioLineIcon.Mesh;
	        // Art BG before Art — "art bg list" / "ART (BG)".
	        if (n.IndexOf("art bg", StringComparison.OrdinalIgnoreCase) >= 0
	            || n.IndexOf("ArtBG", StringComparison.OrdinalIgnoreCase) >= 0
	            || (n.IndexOf("BG", StringComparison.OrdinalIgnoreCase) >= 0
	                && n.IndexOf("Art", StringComparison.OrdinalIgnoreCase) >= 0))
	            return StudioLineIcon.Layers;
	        if (n.IndexOf("Art", StringComparison.OrdinalIgnoreCase) >= 0)
	            return StudioLineIcon.Image;
	        if (n.IndexOf("Addon", StringComparison.OrdinalIgnoreCase) >= 0
	            || n.IndexOf("Nomad", StringComparison.OrdinalIgnoreCase) >= 0
	            || n.IndexOf("Settings", StringComparison.OrdinalIgnoreCase) >= 0)
	            return StudioLineIcon.Settings;
	        return StudioLineIcon.Folder;
	    }

	    /// <summary>
	    /// Icon pack v1: assign a <see cref="StudioLineIcon"/> glyph on every strip tab whose name contains <paramref name="tabMatch"/>.
	    /// </summary>
	    public bool TrySetStripTabLineIcon(string tabMatch, StudioLineIcon icon, out string error) {
	        error = null;
	        if (string.IsNullOrWhiteSpace(tabMatch)) {
	            error = "tab match is empty";
	            return false;
	        }
	        Transform strip = ResolveEffectiveTabStripTransform();
	        if (strip == null) {
	            error = "tab strip not available";
	            return false;
	        }
	        string needle = tabMatch.Trim();
	        int matched = 0;
	        for (int i = 0; i < strip.childCount; i++) {
	            Transform cell = strip.GetChild(i);
	            if (cell == null) continue;
	            if (cell.GetComponent<TabsGroupElem_UI>() == null)
	                continue;
	            string n = cell.name ?? "";
	            if (n.StartsWith("StripDivider_", StringComparison.Ordinal)
	                || n.StartsWith("AddonDivider_", StringComparison.Ordinal))
	                continue;
	            // Match name + TabsGroup title + label text (e.g. "Mesh" → "Tab: 3d" / title mesh).
	            string hay = BuildStripTabMatchHaystack(cell);
	            if (hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
	                continue;
	            ApplyStudioTabChromeColors(cell, SpzUiThemeOps.Active, SpzUiThemeOps.ShouldRecolorBoundChrome);
	            Transform iconTransform = SpzUiThemeOps.FindDirectChildIncludingInactive(cell, "MonolithLineIcon");
	            if (iconTransform == null) {
	                error = $"line icon missing after ensure on '{n}'";
	                return false;
	            }
	            var img = iconTransform.GetComponent<Image>();
	            if (img == null) {
	                error = "MonolithLineIcon has no Image";
	                return false;
	            }
	            img.sprite = UiRuntimeSprites.GetLineIcon(icon);
	            SpzUiThemeOps.ApplyLineIconTint(img);
	            var mark = cell.GetComponent<SpzStripLineIconOverride>()
	                       ?? cell.gameObject.AddComponent<SpzStripLineIconOverride>();
	            mark.Icon = icon;
	            EnsureStripTabHoverTooltip(cell);
	            matched++;
	        }
	        if (matched == 0) {
	            error = $"No strip tab matching '{needle}'";
	            return false;
	        }
	        return true;
	    }

	    static void ApplyPanelShellColor(RectTransform panel, Color panelBg) {
	        if (panel == null) return;
	        var img = panel.GetComponent<Image>();
	        if (img != null)
	            SpzUiThemeOps.ApplyBoundChromeGraphic(img, SpzUiThemeOps.ResolvePanelShellColor());
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

	    /// <summary>
	    /// Runtime <see cref="TabsGroup_UI"/> strip cell (Art / ControlNet / Paint / add-ons share the row only — add-ons are not part of the paint pipeline).
	    /// Hierarchy: root <see cref="LayoutElement"/> + <see cref="Button"/>, child <c>TabBg</c> as <see cref="Button.targetGraphic"/>,
	    /// <c>go active</c> slice, <c>Input (text)</c> label — matches how the Paint tab button is built so <see cref="HorizontalLayoutGroup"/> lays out correctly.
	    /// </summary>
	    TabsGroupElem_UI CreateRibbonStripTabCell(Transform tabStrip, string tabsGroupTitleId, string headerLabelText) {
		    if (tabStrip == null || string.IsNullOrEmpty(tabsGroupTitleId)) return null;
		    var tabGo = new GameObject("Tab: " + headerLabelText);
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
		    var tabBtn = tabGo.AddComponent<Button>();

		    var goTabBg = new GameObject("TabBg");
		    goTabBg.transform.SetParent(tabGo.transform, false);
		    goTabBg.transform.SetAsFirstSibling();
		    var tabBgRect = goTabBg.AddComponent<RectTransform>();
		    tabBgRect.anchorMin = Vector2.zero;
		    tabBgRect.anchorMax = Vector2.one;
		    tabBgRect.sizeDelta = Vector2.zero;
		    tabBgRect.anchoredPosition = Vector2.zero;
		    var tabBgImg = goTabBg.AddComponent<Image>();
		    tabBgImg.sprite = null;
		    tabBgImg.type = Image.Type.Simple;
		    tabBgImg.color = Color.white;
		    tabBgImg.raycastTarget = true;
		    tabBtn.targetGraphic = tabBgImg;
		    var tabBtnColors = tabBtn.colors;
		    tabBtnColors.normalColor = new Color(1f, 1f, 1f, 0f);
		    tabBtnColors.highlightedColor = new Color(1f, 1f, 1f, 0.08f);
		    tabBtnColors.pressedColor = new Color(1f, 1f, 1f, 0.14f);
		    tabBtnColors.selectedColor = new Color(1f, 1f, 1f, 0f);
		    tabBtnColors.disabledColor = new Color(1f, 1f, 1f, 0f);
		    tabBtn.colors = tabBtnColors;

		    var goActive = new GameObject("go active");
		    goActive.transform.SetParent(tabGo.transform, false);
		    var activeRect = goActive.AddComponent<RectTransform>();
		    activeRect.anchorMin = Vector2.zero;
		    activeRect.anchorMax = Vector2.one;
		    activeRect.sizeDelta = Vector2.zero;
		    activeRect.anchoredPosition = Vector2.zero;
		    var activeInner = new GameObject("image");
		    activeInner.transform.SetParent(goActive.transform, false);
		    var activeInnerRt = activeInner.AddComponent<RectTransform>();
		    activeInnerRt.anchorMin = Vector2.zero;
		    activeInnerRt.anchorMax = Vector2.one;
		    activeInnerRt.sizeDelta = Vector2.zero;
		    activeInnerRt.anchoredPosition = Vector2.zero;
		    var activeImg = activeInner.AddComponent<Image>();
		    activeImg.color = new Color(0.32941177f, 0.32941177f, 0.32941177f, 1f);
		    activeImg.raycastTarget = false;
		    goActive.SetActive(false);

		    var tabTextGo = new GameObject("Input (text)");
		    tabTextGo.transform.SetParent(tabGo.transform, false);
		    var tabTextRect = tabTextGo.AddComponent<RectTransform>();
		    tabTextRect.anchorMin = Vector2.zero;
		    tabTextRect.anchorMax = Vector2.one;
		    tabTextRect.sizeDelta = Vector2.zero;
		    var tabText = tabTextGo.AddComponent<TextMeshProUGUI>();
		    tabText.text = headerLabelText;
		    tabText.fontSize = kRibbonStripTabLabelDefaultPt;
		    tabText.color = Color.white;
		    tabText.alignment = TextAlignmentOptions.Center;
		    tabText.verticalAlignment = TMPro.VerticalAlignmentOptions.Middle;
		    tabText.raycastTarget = false;
		    tabText.textWrappingMode = TMPro.TextWrappingModes.Normal;
		    tabText.overflowMode = TMPro.TextOverflowModes.Ellipsis;

		    ApplyRibbonStripTabVisuals(tabStrip, tabGo, tabBgImg, activeImg, tabText);
		    ApplyStripTabMinWidthForLabel(tabLE, tabText, kRibbonStripTabLabelHorizontalPad);

		    var tabElem = tabGo.AddComponent<TabsGroupElem_UI>();
		    tabElem.InitForRuntime(tabsGroupTitleId, tabBtn);
		    tabElem.SetRuntimeActiveHighlight(goActive);
		    return tabElem;
	    }

	    /// <summary>Creates the Paint tab and panel at runtime if missing, so Paint appears alongside Art list, Art BG, Mesh, ControlNet.</summary>
	    void EnsurePaintTabExists(){
	        if (_tabGroup == null) _tabGroup = GetComponentInChildren<TabsGroup_UI>(true);
	        if (_tabGroup == null) {
	            UnityEngine.Debug.LogWarning("[CommandRibbon_UI] Paint tab: no TabsGroup_UI found.");
	            return;
	        }
	        Transform tabStrip = ResolveEffectiveTabStripTransform();
	        if (tabStrip == null) return;
	        // Only skip when BOTH panel and tab exist; if panel exists but tab does not, we must create the tab below so the panel is reachable.
	        if (_Paint_Panel != null && _tabGroup.HasTab("paint")) return;

	        Transform panelsParent = GetRibbonTabBodiesParent(tabStrip);
	        RectTransform newPanelRect = null;
	        if (_Paint_Panel == null && panelsParent != null) {
	            var panelGo = new GameObject("Panel_Paint");
	            panelGo.transform.SetParent(panelsParent, false);
	            panelGo.transform.SetSiblingIndex(0);
	            var panelRect = panelGo.AddComponent<RectTransform>();
	            panelRect.anchorMin = Vector2.zero;
	            panelRect.anchorMax = Vector2.one;
	            panelRect.sizeDelta = Vector2.zero;
	            panelRect.anchoredPosition = Vector2.zero;
	            CopyRibbonTabBodyRectFromReference(panelRect);
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

	        var tabElem = CreateRibbonStripTabCell(tabStrip, "paint", "Paint");
	        if (tabElem == null) {
		        UnityEngine.Debug.LogError("[CommandRibbon_UI] Paint tab: CreateRibbonStripTabCell returned null.");
		        return;
	        }
	        _tabGroup.AddTab(tabElem);
	        if (newPanelRect != null)
	            _Paint_Panel = newPanelRect;
	        var stripRect = tabStrip as RectTransform;
	        if (stripRect != null) UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(stripRect);
	        QueueTabStripRebuildNextFrame(tabStrip);
	        UnityEngine.Debug.Log($"[CommandRibbon_UI] Paint tab created, parented to {tabElem.transform.parent.name}, siblingIndex={tabElem.transform.GetSiblingIndex()}, strip.childCount={tabStrip.childCount}");
	    }

	    /// <summary>When tabs are not direct children of the TabsGroup transform, find a child row. Never return a <see cref="TabsGroupElem_UI"/> tab cell.</summary>
	    static Transform FindTabStripFallback(Transform tabGroupTransform){
	        if (tabGroupTransform == null) return null;
	        for (int i = 0; i < tabGroupTransform.childCount; i++) {
	            Transform ch = tabGroupTransform.GetChild(i);
	            if (ch.GetComponent<TabsGroupElem_UI>() != null) continue;
	            if (ch.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>() != null)
	                return ch;
	        }
	        for (int i = 0; i < tabGroupTransform.childCount; i++) {
	            Transform ch = tabGroupTransform.GetChild(i);
	            if (ch.GetComponent<TabsGroupElem_UI>() != null) continue;
	            if (ch is RectTransform && ch.childCount >= 2)
	                return ch;
	        }
	        return null;
	    }

	    static TabsGroupElem_UI FindAddonTabElementForTabId(TabsGroup_UI tabGroup, string tabId) {
	        if (tabGroup == null || string.IsNullOrEmpty(tabId)) return null;
	        Transform strip = ResolveTabStripForGroup(tabGroup);
	        if (strip == null) return null;
	        string idLower = tabId.ToLowerInvariant();
	        for (int i = 0; i < strip.childCount; i++) {
		        var e = strip.GetChild(i).GetComponent<TabsGroupElem_UI>();
		        if (e != null && e.title != null && e.title.ToLowerInvariant() == idLower)
			        return e;
	        }
	        foreach (var e in strip.GetComponentsInChildren<TabsGroupElem_UI>(true)) {
	            if (e == null) continue;
	            if (e.title != null && e.title.ToLowerInvariant() == idLower)
	                return e;
	        }
	        return null;
	    }

	    void RegisterAddonShortcutOrder(string addonId) {
		    if (string.IsNullOrEmpty(addonId)) return;
		    int ix = _addonIdsShortcutOrder.BinarySearch(addonId, StringComparer.OrdinalIgnoreCase);
		    if (ix >= 0) return;
		    _addonIdsShortcutOrder.Insert(~ix, addonId);
	    }

	    void UnregisterAddonShortcutOrder(string addonId) {
		    if (string.IsNullOrEmpty(addonId)) return;
		    int ix = _addonIdsShortcutOrder.BinarySearch(addonId, StringComparer.OrdinalIgnoreCase);
		    if (ix >= 0) _addonIdsShortcutOrder.RemoveAt(ix);
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
	            Transform strip = ResolveEffectiveTabStripTransform();
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

	    /// <summary>
	    /// Parent transform that holds stacked tab bodies (Art, Mesh, ControlNet, Paint, …) — same for all, never the raw tab strip row.
	    /// Using <see cref="Transform.parent"/> of the tab strip alone can parent add-on shells as a sibling that covers the strip and blocks tab clicks.
	    /// </summary>
	    Transform GetRibbonTabBodiesParent(Transform tabStrip)
	    {
		    TryResolvePanelRefs();
		    if (_ribbonTabBodiesRootOverride != null)
			    return _ribbonTabBodiesRootOverride;
		    var parents = new List<Transform>();
		    void CollectParent(RectTransform body)
		    {
			    if (body == null) return;
			    Transform p = body.parent;
			    if (p != null) parents.Add(p);
		    }
		    CollectParent(_SD_ControlNets_List_Panel);
		    CollectParent(_SD_ArtList_Panel);
		    CollectParent(_SD_ArtBgList_Panel);
		    CollectParent(_SD_3D_Models_Panels);
		    CollectParent(_Paint_Panel);
		    if (parents.Count > 0)
		    {
			    Transform first = parents[0];
			    bool allSame = true;
			    for (int i = 1; i < parents.Count; i++)
			    {
				    if (parents[i] != first)
				    {
					    allSame = false;
					    break;
				    }
			    }
			    if (allSame)
				    return first;
			    UnityEngine.Debug.LogWarning("[CommandRibbon_UI] Built-in ribbon bodies use different parents; preferring Art list parent for addon shells.");
			    if (_SD_ArtList_Panel != null && _SD_ArtList_Panel.parent != null)
				    return _SD_ArtList_Panel.parent;
			    return first;
		    }
		    // No serialized bodies: stack lives under the same rect as the tab strip row (e.g. RIGHT PANEL).
		    // Do not pick "first child with childCount>=2" — that is often the Art panel root, which parents addon UI inside Art (overlay bug).
		    if (tabStrip != null && tabStrip.parent != null)
			    return tabStrip.parent;
		    return null;
	    }

	    /// <summary>Stack add-on shells with built-in tab bodies (same sibling group) so a full-rect shell does not sit above the tab strip.</summary>
	    void PlaceAddonShellAmongBuiltInPanels(Transform shell, Transform panelsParent)
	    {
		    if (shell == null || panelsParent == null || shell.parent != panelsParent) return;
		    int maxIx = -1;
		    void Consider(Transform t)
		    {
			    if (t == null || t.parent != panelsParent || t == shell) return;
			    maxIx = Mathf.Max(maxIx, t.GetSiblingIndex());
		    }
		    Consider(_SD_ControlNets_List_Panel);
		    Consider(_SD_ArtList_Panel);
		    Consider(_SD_ArtBgList_Panel);
		    Consider(_SD_3D_Models_Panels);
		    Consider(_Paint_Panel);
		    foreach (var kv in _addonPanelsById)
			    Consider(kv.Value != null ? kv.Value.transform : null);
		    if (maxIx >= 0)
			    shell.SetSiblingIndex(Mathf.Clamp(maxIx + 1, 0, panelsParent.childCount - 1));
		    else
			    shell.SetSiblingIndex(0);
	    }

	    void DestroyAddonStripDivider(string addonId)
	    {
		    if (string.IsNullOrEmpty(addonId)) return;
		    if (!_addonStripDividerById.TryGetValue(addonId, out var go) || go == null)
		    {
			    _addonStripDividerById.Remove(addonId);
			    return;
		    }
		    _addonStripDividerById.Remove(addonId);
		    Destroy(go);
	    }

	    /// <summary>Inserts a narrow non-interactive divider before the next tab (call immediately before <see cref="CreateRibbonStripTabCell"/> for add-ons).</summary>
	    void EnsureAddonStripDividerBeforeTab(Transform tabStrip, string addonId)
	    {
		    if (tabStrip == null || string.IsNullOrEmpty(addonId)) return;
		    DestroyAddonStripDivider(addonId);
		    var go = new GameObject("StripDivider_" + addonId);
		    go.transform.SetParent(tabStrip, false);
		    go.transform.SetAsLastSibling();
		    var rt = go.AddComponent<RectTransform>();
		    rt.anchorMin = Vector2.zero;
		    rt.anchorMax = Vector2.one;
		    rt.pivot = new Vector2(0.5f, 0.5f);
		    rt.sizeDelta = Vector2.zero;
		    rt.anchoredPosition = Vector2.zero;
		    var le = go.AddComponent<LayoutElement>();
		    le.minWidth = kRibbonAddonDividerLayoutWidth;
		    le.preferredWidth = kRibbonAddonDividerLayoutWidth;
		    le.flexibleWidth = 0f;
		    le.minHeight = -1f;
		    le.preferredHeight = -1f;
		    le.flexibleHeight = 1f;
		    var img = go.AddComponent<Image>();
		    img.sprite = GetAddonRibbonDividerSprite();
		    img.type = Image.Type.Simple;
		    img.preserveAspect = false;
		    img.raycastTarget = false;
		    img.color = Color.white;
		    _addonStripDividerById[addonId] = go;
	    }

	    void StripAddonTabPanelShowHandler(string addonId, TabsGroupElem_UI tabOnStrip, GameObject tabGoFromDict) {
		    if (!_addonTabPanelShowByAddonId.TryGetValue(addonId, out var ph) || ph == null) return;
		    if (tabOnStrip != null)
			    tabOnStrip.onClicked = (Action<TabsGroupElem_UI>)Delegate.Remove(tabOnStrip.onClicked, ph);
		    if (tabGoFromDict != null) {
			    var te = tabGoFromDict.GetComponent<TabsGroupElem_UI>();
			    if (te != null && te != tabOnStrip)
				    te.onClicked = (Action<TabsGroupElem_UI>)Delegate.Remove(te.onClicked, ph);
		    }
		    _addonTabPanelShowByAddonId.Remove(addonId);
	    }

	    /// <summary>
	    /// Ensures the addon tab calls <see cref="ShowOnePanel"/> for this panel (restores tab→panel link if a prior subscribe failed or state desynced).
	    /// </summary>
	    bool TryWireAddonTabPanelShow(string addonId, string tabId, RectTransform panelRect, TabsGroupElem_UI tabElem) {
		    if (panelRect == null || tabElem == null || _tabGroup == null) return false;
		    GameObject panelGo = panelRect.gameObject;
		    if (_addonTabPanelShowByAddonId.TryGetValue(addonId, out var prevHandler) && prevHandler != null)
			    tabElem.onClicked = (Action<TabsGroupElem_UI>)Delegate.Remove(tabElem.onClicked, prevHandler);
		    Action<TabsGroupElem_UI> handler = _ => {
			    UnityEngine.Debug.Log($"[CommandRibbon_UI] Switching to addon tab: {addonId}");
			    ShowOnePanel(panelGo);
			    _currentPanel = Panel.Unknown;
		    };
		    if (!_tabGroup.SubscribeForTab(tabId, handler)) {
			    UnityEngine.Debug.LogError($"[CommandRibbon_UI] SubscribeForTab failed for addon '{addonId}' tab '{tabId}'; tab→panel wiring not applied.");
			    _addonTabPanelShowByAddonId.Remove(addonId); // prev handler was stripped from delegate; drop stale map entry
			    return false;
		    }
		    _addonTabPanelShowByAddonId[addonId] = handler;
		    return true;
	    }

	    /// <summary>One tab per addon (Blender N-panel style). Returns the panel content parent for this addon.</summary>
	    public RectTransform GetOrCreatePanelForAddon(string addonId, string displayTitle){
	        UnityEngine.Debug.Log($"[CommandRibbon_UI] GetOrCreatePanelForAddon: {addonId} ({displayTitle})");
	        
	        EnsureTabGroupResolved();
	        TryResolvePanelRefs();
	        UnityEngine.Debug.Log($"[CommandRibbon_UI] _tabGroup={(_tabGroup != null ? _tabGroup.name : "null")} _SD_ControlNets_List_Panel={(_SD_ControlNets_List_Panel != null ? "set" : "null")}");
	        
	        if(_tabGroup == null) {
	            UnityEngine.Debug.LogError($"[CommandRibbon_UI] Cannot create addon tab: _tabGroup is null.");
	            return null;
	        }
	        string tabId = AddonRibbonIntegration.TabIdForAddon(addonId);
	        if (_addonPanelsById.TryGetValue(addonId, out var existing)) {
	            bool panelOk = existing != null && existing.gameObject != null;
	            bool groupHasTab = _tabGroup.HasTab(tabId);
	            TabsGroupElem_UI tabOnStrip = groupHasTab ? FindAddonTabElementForTabId(_tabGroup, tabId) : null;
	            _addonTabById.TryGetValue(addonId, out var tabGoFromDict);
	            // Connectivity: panel + tab in TabsGroup + SubscribeForTab panel-show handler (not just existence).
	            if (panelOk && groupHasTab && tabOnStrip != null) {
	                _addonTabById[addonId] = tabOnStrip.gameObject;
	                if (TryWireAddonTabPanelShow(addonId, tabId, existing, tabOnStrip)) {
		                RegisterAddonShortcutOrder(addonId);
		                UnityEngine.Debug.Log($"[CommandRibbon_UI] Returning existing panel for: {addonId}");
		                return existing;
	                }
	                UnityEngine.Debug.LogWarning($"[CommandRibbon_UI] Could not restore tab→panel subscription for {addonId}; recreating tab/panel.");
	            } else
		            UnityEngine.Debug.LogWarning($"[CommandRibbon_UI] Stale or incomplete addon ribbon state for {addonId} (panelOk={panelOk}, groupHasTab={groupHasTab}, tabOnStrip={(tabOnStrip != null)}). Recreating tab/panel.");
	            StripAddonTabPanelShowHandler(addonId, tabOnStrip, tabGoFromDict);
	            DestroyAddonStripDivider(addonId);
	            // Salvage AddonPanel_* content before destroying the shell — AddonUI_MGR still tracks those GOs.
	            var salvagedContent = new List<Transform>();
	            if (panelOk) {
	                for (int ci = existing.childCount - 1; ci >= 0; ci--) {
	                    Transform child = existing.GetChild(ci);
	                    if (child == null) continue;
	                    string cn = child.name ?? "";
	                    bool match = AddonUI_MGR.instance != null
		                    ? AddonUI_MGR.instance.IsAddonPanelOwnedBy(cn, addonId)
		                    : (cn.StartsWith("AddonPanel_" + addonId + "_", StringComparison.Ordinal)
		                       || string.Equals(cn, "AddonPanel_" + addonId, StringComparison.Ordinal));
	                    if (!match) continue;
	                    child.SetParent(null, false);
	                    salvagedContent.Add(child);
	                }
	                Destroy(existing.gameObject);
	            }
	            _addonPanelsById.Remove(addonId);
	            UnregisterAddonShortcutOrder(addonId);
	            _addonTabById.Remove(addonId);
	            bool sameGo = tabOnStrip != null && tabGoFromDict != null && tabGoFromDict == tabOnStrip.gameObject;
	            if (tabOnStrip != null) {
	                _tabGroup.RemoveTab(tabOnStrip);
	                Destroy(tabOnStrip.gameObject);
	            }
	            if (tabGoFromDict != null && !sameGo) {
	                var strayElem = tabGoFromDict.GetComponent<TabsGroupElem_UI>();
	                if (strayElem != null)
	                    _tabGroup.RemoveTab(strayElem);
	                Destroy(tabGoFromDict);
	            }
	            // Fall through to create a fresh shell, then reattach salvaged content below.
	            Transform tabStripSalvage = ResolveEffectiveTabStripTransform();
	            if (tabStripSalvage == null) {
	                ReparkSalvagedInsteadOfDestroy(addonId, displayTitle, salvagedContent);
	                UnityEngine.Debug.LogError("[CommandRibbon_UI] Cannot recreate addon tab: tab strip is null.");
	                return null;
	            }
	            Transform panelsParentSalvage = GetRibbonTabBodiesParent(tabStripSalvage);
	            if (panelsParentSalvage == null) {
	                ReparkSalvagedInsteadOfDestroy(addonId, displayTitle, salvagedContent);
	                UnityEngine.Debug.LogError("[CommandRibbon_UI] Cannot recreate addon tab: panelsParent is null");
	                return null;
	            }
	            var recreated = CreateFreshAddonShellAndTab(addonId, displayTitle, tabId, tabStripSalvage, panelsParentSalvage);
	            if (recreated == null) {
	                ReparkSalvagedInsteadOfDestroy(addonId, displayTitle, salvagedContent);
	                return null;
	            }
	            for (int s = 0; s < salvagedContent.Count; s++) {
	                Transform child = salvagedContent[s];
	                if (child == null) continue;
	                child.SetParent(recreated, false);
	                var rt = child as RectTransform;
	                if (rt != null) {
	                    rt.anchorMin = Vector2.zero;
	                    rt.anchorMax = Vector2.one;
	                    rt.sizeDelta = Vector2.zero;
	                    rt.anchoredPosition = Vector2.zero;
	                }
	            }
	            return recreated;
	        }
	        Transform tabStrip = ResolveEffectiveTabStripTransform();
	        if (tabStrip == null) {
	            UnityEngine.Debug.LogError("[CommandRibbon_UI] Cannot create addon tab: tab strip is null.");
	            return null;
	        }

	        Transform panelsParent = GetRibbonTabBodiesParent(tabStrip);
	        if(panelsParent == null) {
	            UnityEngine.Debug.LogError("[CommandRibbon_UI] Cannot create addon tab: panelsParent is null");
	            return null;
	        }

	        return CreateFreshAddonShellAndTab(addonId, displayTitle, tabId, tabStrip, panelsParent);
	    }

	    static void ReparkSalvagedInsteadOfDestroy(string addonId, string displayTitle, List<Transform> salvagedContent) {
		    if (salvagedContent == null || salvagedContent.Count == 0) return;
		    if (AddonUI_MGR.instance != null) {
			    AddonUI_MGR.instance.ReparkSalvagedAddonContent(addonId, displayTitle, salvagedContent);
			    return;
		    }
		    // Last resort: leave unparented rather than Destroy — instance IDs must stay valid.
		    UnityEngine.Debug.LogWarning($"[CommandRibbon_UI] AddonUI_MGR missing; leaving {salvagedContent.Count} salvaged panel(s) unparented for {addonId}.");
	    }

	    RectTransform CreateFreshAddonShellAndTab(string addonId, string displayTitle, string tabId, Transform tabStrip, Transform panelsParent) {
	        UnityEngine.Debug.Log($"[CommandRibbon_UI] Creating new tab and panel for: {addonId}. TabID: {tabId}");
	        
	        var panelGo = new GameObject("Panel_" + addonId);
	        panelGo.transform.SetParent(panelsParent, false);
	        PlaceAddonShellAmongBuiltInPanels(panelGo.transform, panelsParent);
	        var panelRect = panelGo.transform as RectTransform;
	        if (panelRect == null)
		        panelRect = panelGo.AddComponent<RectTransform>();
	        panelRect.anchorMin = Vector2.zero;
	        panelRect.anchorMax = Vector2.one;
	        panelRect.sizeDelta = Vector2.zero;
	        panelRect.anchoredPosition = Vector2.zero;
	        CopyRibbonTabBodyRectFromReference(panelRect);
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
	        panelGo.SetActive(false);

	        UnityEngine.Debug.Log($"[CommandRibbon_UI] Tab strip: {tabStrip.name}, childCount={tabStrip.childCount}");
	        EnsureAddonStripDividerBeforeTab(tabStrip, addonId);
	        var tabElem = CreateRibbonStripTabCell(tabStrip, tabId, displayTitle);
	        if (tabElem == null) {
		        UnityEngine.Debug.LogError($"[CommandRibbon_UI] Add-on tab creation failed for {addonId} (CreateRibbonStripTabCell returned null).");
		        DestroyAddonStripDivider(addonId);
		        Destroy(panelGo);
		        return null;
	        }
	        _tabGroup.AddTab(tabElem);
	        GameObject tabGo = tabElem.gameObject;

	        if (!TryWireAddonTabPanelShow(addonId, tabId, panelRect, tabElem)) {
	            UnityEngine.Debug.LogError($"[CommandRibbon_UI] Addon tab '{tabId}' was not registered in TabsGroup_UI; cannot wire panel show. Destroying tab and panel.");
	            _addonTabPanelShowByAddonId.Remove(addonId);
	            _tabGroup.RemoveTab(tabElem);
	            Destroy(tabGo);
	            DestroyAddonStripDivider(addonId);
	            Destroy(panelGo);
	            return null;
	        }

	        _addonPanelsById[addonId] = panelRect;
	        _addonTabById[addonId] = tabGo;
	        RegisterAddonShortcutOrder(addonId);

	        UnityEngine.Debug.Log($"[CommandRibbon_UI] Addon tab created: {displayTitle}, tabGo.activeSelf={tabGo.activeSelf}, tabStrip.childCount={tabStrip.childCount}");

	        RefreshRibbonTabStripLayout(tabStrip);
	        
	        return panelRect;
	    }

	    void ApplyRibbonStripTabVisuals(Transform tabStrip, GameObject newTabGo, Image tabBgImg, Image activeImg, TextMeshProUGUI tabText)
	    {
	        if (tabBgImg != null) {
	            tabBgImg.sprite = null;
	            tabBgImg.type = Image.Type.Simple;
	            tabBgImg.color = new Color(1f, 1f, 1f, 0f);
	            tabBgImg.raycastTarget = true;
	        }
	        Sprite slice = _paintTabSliceSprite;
	        var refElem = FindFirstOtherStripTabForStyleReference(tabStrip, newTabGo != null ? newTabGo.transform : null);
	        if (refElem != null)
	        {
	            CopyStripTabLabelStyleFromReference(refElem, tabText);
	            if (slice == null)
	                slice = FindFirstSlicedTabSpriteUnder(refElem.transform);
	        }
	        if (_paintTabFont != null && tabText != null)
	        {
	            tabText.font = _paintTabFont;
	            tabText.fontSharedMaterial = _paintTabFont.material;
	        }
	        // Active state only: same as other ribbon tabs (flared slice lives under go active, toggled off when deselected).
	        if (slice != null)
	        {
	            activeImg.sprite = slice;
	            activeImg.type = Image.Type.Sliced;
	        }
	        if (refElem != null)
	            CopySlicedTabImageTuningFromReference(refElem, activeImg);
	        else if (slice != null)
	            activeImg.pixelsPerUnitMultiplier = 6f;
	        // Same TMP auto-size band / wrap rules as HarmonizeStripTabTypography (add-on + Paint strip cells match prefab tabs).
	        if (tabText != null)
	        {
		        TextMeshProUGUI refForPts = GetRibbonStripTypographyReferenceTMP(tabStrip, null);
		        float basis = kRibbonStripTabLabelDefaultPt * SpzUiThemeOps.Active.fontScale;
		        ConfigureResponsiveRibbonTabText(tabText, refForPts, basis);
	        }
	    }

	    /// <summary>Prefab tab used to copy fonts/slice (skips the tab being styled and any tab named like the Paint strip cell).</summary>
	    static TabsGroupElem_UI FindFirstOtherStripTabForStyleReference(Transform tabStrip, Transform skipTabRoot)
	    {
	        if (tabStrip == null) return null;
	        foreach (var elem in tabStrip.GetComponentsInChildren<TabsGroupElem_UI>(true))
	        {
	            if (elem == null || skipTabRoot != null && elem.transform == skipTabRoot) continue;
	            if (elem.transform.parent != tabStrip) continue;
	            if (elem.gameObject.name.IndexOf("paint", StringComparison.OrdinalIgnoreCase) >= 0) continue;
	            return elem;
	        }
	        return null;
	    }

	    static void CopyStripTabLabelStyleFromReference(TabsGroupElem_UI reference, TextMeshProUGUI tabText)
	    {
	        if (reference == null || tabText == null) return;
	        var tmp = reference.GetComponentInChildren<TextMeshProUGUI>(true);
	        if (tmp == null) return;
	        tabText.font = tmp.font;
	        tabText.fontSharedMaterial = tmp.fontSharedMaterial;
	        tabText.fontSize = tmp.fontSize;
	        tabText.fontWeight = tmp.fontWeight;
	        tabText.alignment = tmp.alignment;
	        tabText.textWrappingMode = tmp.textWrappingMode;
	        tabText.overflowMode = tmp.overflowMode;
	    }

	    static Sprite FindFirstSlicedTabSpriteUnder(Transform tabRoot)
	    {
	        if (tabRoot == null) return null;
	        foreach (var img in tabRoot.GetComponentsInChildren<Image>(true))
	        {
	            if (img.sprite != null && img.type == Image.Type.Sliced)
	                return img.sprite;
	        }
	        return null;
	    }

	    static void CopySlicedTabImageTuningFromReference(TabsGroupElem_UI reference, Image activeImg)
	    {
	        if (reference == null || activeImg == null) return;
	        Image template = null;
	        foreach (var img in reference.GetComponentsInChildren<Image>(true))
	        {
	            if (img.sprite == null || img.type != Image.Type.Sliced) continue;
	            Transform p = img.transform.parent;
	            if (p != null && p.name.IndexOf("active", StringComparison.OrdinalIgnoreCase) >= 0)
	            {
	                template = img;
	                break;
	            }
	        }
	        if (template == null)
	        {
	            foreach (var img in reference.GetComponentsInChildren<Image>(true))
	            {
	                if (img.sprite != null && img.type == Image.Type.Sliced)
	                {
	                    template = img;
	                    break;
	                }
	            }
	        }
	        if (template == null) return;
	        float ppu = template.pixelsPerUnitMultiplier;
	        if (ppu > 0.001f)
	            activeImg.pixelsPerUnitMultiplier = ppu;
	        activeImg.color = template.color;
	    }

	    void QueueTabStripRebuildNextFrame(Transform tabStrip) {
		    if (tabStrip == null) return;
		    if (_rebuildTabStripLayout_crtn != null)
			    StopCoroutine(_rebuildTabStripLayout_crtn);
		    int seq = ++_rebuildTabStripLayoutSeq;
		    _rebuildTabStripLayout_crtn = StartCoroutine(RebuildTabStripLayoutNextFrame(tabStrip, seq));
	    }

	    IEnumerator RebuildTabStripLayoutNextFrame(Transform tabStrip, int seq){
	        try {
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
	        } finally {
		        if (seq == _rebuildTabStripLayoutSeq)
			        _rebuildTabStripLayout_crtn = null;
	        }
	    }

	    /// <summary>Removes an addon's tab and panel (e.g. when addon is disabled). Call from Addon_MGR.UnloadAddon.</summary>
	    public void RemoveAddonPanel(string addonId){
		    RemoveAddonPanelCore(addonId, preserveContent: false);
	    }

	    /// <summary>
	    /// Removes the ribbon tab/shell but reparents AddonPanel_* content into off-screen parking
	    /// (enabled add-on with host pref show_in_command_ribbon = false).
	    /// </summary>
	    public void RemoveAddonPanelPreservingContent(string addonId) {
		    RemoveAddonPanelCore(addonId, preserveContent: true);
	    }

	    void RemoveAddonPanelCore(string addonId, bool preserveContent){
	        if (string.IsNullOrEmpty(addonId)) return;
	        EnsureTabGroupResolved();
	        string tabId = AddonRibbonIntegration.TabIdForAddon(addonId);
	        _addonTabById.TryGetValue(addonId, out var tabGo);
	        TabsGroupElem_UI tabElemOnStrip = null;
	        if (tabGo != null)
		        tabElemOnStrip = tabGo.GetComponent<TabsGroupElem_UI>();
	        if (tabElemOnStrip == null && _tabGroup != null && _tabGroup.HasTab(tabId))
		        tabElemOnStrip = FindAddonTabElementForTabId(_tabGroup, tabId);
	        bool addonTabWasSelected = tabElemOnStrip != null && tabElemOnStrip.IsVisuallySelectedAsActiveTab();
	        bool hadRibbonShell = _addonPanelsById.TryGetValue(addonId, out var panelRect);

	        List<Transform> salvaged = null;
	        if (preserveContent && hadRibbonShell && panelRect != null && panelRect.gameObject != null) {
		        salvaged = new List<Transform>();
		        for (int ci = panelRect.childCount - 1; ci >= 0; ci--) {
			        Transform child = panelRect.GetChild(ci);
			        if (child == null) continue;
			        string cn = child.name ?? "";
			        bool match = AddonUI_MGR.instance != null
				        ? AddonUI_MGR.instance.IsAddonPanelOwnedBy(cn, addonId)
				        : (cn.StartsWith("AddonPanel_" + addonId + "_", StringComparison.Ordinal)
				           || string.Equals(cn, "AddonPanel_" + addonId, StringComparison.Ordinal));
			        if (!match) continue;
			        child.SetParent(null, false);
			        salvaged.Add(child);
		        }
	        }

	        StripAddonTabPanelShowHandler(addonId, tabElemOnStrip, tabGo);
	        if(tabGo != null){
	            var tabElem = tabGo.GetComponent<TabsGroupElem_UI>();
	            if(tabElem != null && _tabGroup != null) _tabGroup.RemoveTab(tabElem);
	            UnityEngine.Object.Destroy(tabGo);
	            _addonTabById.Remove(addonId);
	        } else if (_tabGroup != null && _tabGroup.HasTab(tabId)) {
	            var orphan = FindAddonTabElementForTabId(_tabGroup, tabId);
	            if (orphan != null) {
	                _tabGroup.RemoveTab(orphan);
	                UnityEngine.Object.Destroy(orphan.gameObject);
	            }
	        }
	        if (hadRibbonShell && panelRect != null && panelRect.gameObject != null)
		        UnityEngine.Object.Destroy(panelRect.gameObject);
	        if (hadRibbonShell)
		        _addonPanelsById.Remove(addonId);
	        UnregisterAddonShortcutOrder(addonId);
	        DestroyAddonStripDivider(addonId);

	        if (preserveContent && salvaged != null && salvaged.Count > 0) {
		        string title = addonId;
		        if (Addon_MGR.instance != null
		            && Addon_MGR.instance.GetAddons().TryGetValue(addonId, out var info)
		            && info != null
		            && !string.IsNullOrWhiteSpace(info.displayName))
			        title = info.displayName.Trim();
		        ReparkSalvagedInsteadOfDestroy(addonId, title, salvaged);
	        }

	        UnityEngine.Debug.Log($"[CommandRibbon_UI] Removed addon tab/panel: {addonId} (hadRibbonShell={hadRibbonShell}, preserveContent={preserveContent})");
	        if (_tabGroup != null) {
		        if (addonTabWasSelected)
			        _tabGroup.SwitchTab("art list");
		        Transform strip = ResolveEffectiveTabStripTransform();
		        if (strip != null)
			        RefreshRibbonTabStripLayout(strip);
	        }
	    }

	    /// <summary>Re-apply strip HLG rules, label auto-size, layout rebuild, and optional ScrollRect (call after add/remove runtime tabs).</summary>
	    void RefreshRibbonTabStripLayout(Transform tabStrip) {
		    if (tabStrip == null) return;
		    PatchTabStripResponsiveLayout();
		    HarmonizeStripTabTypography();
		    // Theme/icon-only widths + ForceRebuild live inside ApplyThemeTokens (must run before any prior rebuild).
		    ApplyThemeTokens();
		    QueueTabStripRebuildNextFrame(tabStrip);
	    }

	    /// <summary>Public hook to reflow the ribbon tab row (e.g. after external hierarchy changes). Uses <see cref="ResolveEffectiveTabStripTransform"/>.</summary>
	    public void RefreshTabStripLayout() {
		    RefreshRibbonTabStripLayout(ResolveEffectiveTabStripTransform());
	    }

	    /// <summary>Calls <see cref="RefreshTabStripLayout"/> when a ribbon instance is available (e.g. from add-on load after a frame).</summary>
	    public static void RefreshTabStripLayoutIfPresent(CommandRibbon_UI ribbon) {
		    if (ribbon != null)
			    ribbon.RefreshTabStripLayout();
	    }


	}
}//end namespace

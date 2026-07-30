using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace spz {

	// Owns the UI controls that affect brushing.
	// Doesn't actually deal with textures etc, only with the UI controls.
	public class BrushRibbon_UI : MonoBehaviour{
	    public static BrushRibbon_UI instance { get; private set; } = null;

	    [Space(10)]
	    [SerializeField] BrushRibbon_UI_Colors _colors;
	    [SerializeField] BrushRibbon_UI_Opacity _opacity;
	    [SerializeField] BrushRibbon_UI_Hardness _hardness;
	    [SerializeField] BrushRibbon_UI_PressureMode _pressureTabletMode;
	    [Space(10)]
	    [SerializeField] BrushRibbon_UI_Size _size;
	    [SerializeField] BrushRibbon_UI_BucketFill _bucketFill;
	    [SerializeField] BrushRibbon_UI_InvertMask _invertMask;
	    [SerializeField] BrushRibbon_UI_DeleteButton _deleteColorsButton;
	    [SerializeField] Toggle _eyeDropperToggle;

	    public BrushRibbon_UI_Hardness brushHardnessUI => _hardness;

	    /// <summary> Set brush size 0–1. Used when applying ABR suggested size; also allows AlphaPicker to work when SD_WorkflowOptionsRibbon_UI is not present. </summary>
	    public void SetBrushSize(float s) { if (_size != null) _size.SetBrushSize(s); }
	    /// <summary> Set brush spacing 0–1 (0 = continuous). Used when applying ABR suggested spacing. </summary>
	    public void SetBrushSpacing(float s) { if (_size != null) _size.SetBrushSpacing(s); }
	    public void SetBrushAngle(float deg) { if (_size != null) _size.SetBrushAngle(deg); }
	    public void SetBrushRoundness(float r) { if (_size != null) _size.SetBrushRoundness(r); }
	    public float brushSize01 => _size != null ? _size.brushSize01 : 0f;
	    public float brushSpacing01 => _size != null ? _size.brushSpacing01 : 0f;
	    public float brushAngleDeg => _size != null ? _size.brushAngleDeg : 0f;
	    public float brushRoundness01 => _size != null ? _size.brushRoundness01 : 1f;

	    public float brushOpacity01 => _opacity != null ? _opacity.Opacity01 : 1f;
	    public void SetBrushOpacity01(float opacity01) { if (_opacity != null) _opacity.SetOpacity01(opacity01); }

	    void Awake(){
	        if (instance != null){ DestroyImmediate(this); return; }
	        instance = this;

	        EventsBinder.Bind_Clickable_to_event("BrushRibbon_UI:ColorsButton", _colors);
	        EventsBinder.Bind_Clickable_to_event("BrushRibbon_UI:OpacityButton", _opacity);
	        EventsBinder.Bind_Clickable_to_event("BrushRibbon_UI:HardnessButton", _hardness);
	        EventsBinder.Bind_Clickable_to_event("BrushRibbon_UI:PressureButton", _pressureTabletMode);
	        EventsBinder.Bind_Clickable_to_event("BrushRibbon_UI:SizeSlider", _size);
	        EventsBinder.Bind_Clickable_to_event("BrushRibbon_UI:BucketFillButton", _bucketFill);
	        EventsBinder.Bind_Clickable_to_event("BrushRibbon_UI:InvertMaskButton", _invertMask);
	        EventsBinder.Bind_Clickable_to_event("BrushRibbon_UI:DeleteColorsButton", _deleteColorsButton);
	        EventsBinder.Bind_Clickable_to_event("BrushRibbon_UI:EyeDropperToggle", _eyeDropperToggle);

	        SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
	        BrushRibbon_UI_Direction.OnDirectionToggleChanged += ApplyThemeTokens;
	        DimensionMode_MGR._Act_OnDimensionChanged += OnDimensionChanged_Retheme;
	    }

	    void Start(){
	        ApplyThemeTokens();
	    }

	    void OnDestroy(){
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	        BrushRibbon_UI_Direction.OnDirectionToggleChanged -= ApplyThemeTokens;
	        DimensionMode_MGR._Act_OnDimensionChanged -= OnDimensionChanged_Retheme;
	        if (instance == this)
	            instance = null;
	    }

	    void OnDimensionChanged_Retheme(DimensionMode _) => ApplyThemeTokens();

	    /// <summary>Nomad: flat tool cells + line icons on the brush strip (gated BoundChrome).</summary>
	    public void ApplyThemeTokens() {
	        var dir = ResolveDirection();
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            // MGR host is childless; restore each wired tool root (Direction lives on the workflow strip).
	            SpzUiThemeOps.RestoreBoundChromeUnder(transform);
	            ForEachDirectionHost(d => {
	                SpzUiThemeOps.RestoreBoundChromeUnder(d.transform);
	                // Re-assert default gaps after restore (smudge inject already used them; Gen3D uses snapshot).
	                if (d.SmudgeToggle != null)
	                    BrushRibbon_UI_Direction.ApplyPaintSmudgeEraseGaps(d, nomadGaps: false);
	            });
	            RestoreSelectableChrome(_bucketFill != null ? _bucketFill.Button : null);
	            if (_bucketFill != null && _bucketFill.IconRoot != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_bucketFill.IconRoot);
	            RestoreSelectableChrome(_invertMask != null ? _invertMask.Button : null);
	            RestoreSelectableChrome(_deleteColorsButton != null ? _deleteColorsButton.Button : null);
	            if (_deleteColorsButton != null && _deleteColorsButton.IconRoot != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_deleteColorsButton.IconRoot);
	            RestoreSelectableChrome(_eyeDropperToggle);
	            if (_pressureTabletMode != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_pressureTabletMode.transform);
	            if (_size != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_size.transform);
	            if (_opacity != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_opacity.transform);
	            return;
	        }
	        var t = SpzUiThemeOps.Active;
	        if (_size != null)
	            _size.ApplyThemeTokens(t);
	        ThemeToolButton(_bucketFill != null ? _bucketFill.Button : null, StudioLineIcon.Bucket, t, applyIcon: false);
	        if (_bucketFill != null && _bucketFill.IconRoot != null)
	            SpzUiThemeOps.ApplyControlLineIcon(_bucketFill.IconRoot, StudioLineIcon.Bucket, 22f);
	        ThemeToolButton(_invertMask != null ? _invertMask.Button : null, StudioLineIcon.Drop, t, applyIcon: true);
	        ThemeToolButton(_deleteColorsButton != null ? _deleteColorsButton.Button : null, StudioLineIcon.Trash, t, applyIcon: false);
	        if (_deleteColorsButton != null && _deleteColorsButton.IconRoot != null)
	            SpzUiThemeOps.ApplyControlLineIcon(_deleteColorsButton.IconRoot, StudioLineIcon.Trash, 22f);
	        if (_eyeDropperToggle != null)
	            ThemeToolToggle(_eyeDropperToggle, StudioLineIcon.Eye, t, iconSizePx: 20f);
	        // Theme SD and Gen3D direction strips — ResolveDirection alone would leave the inactive mode SPZ.
	        ForEachDirectionHost(d => ThemeDirectionTools(d, t));
	        if (_pressureTabletMode != null)
	            ThemePressureMode(_pressureTabletMode, t);
	        if (_opacity != null)
	            ThemeOpacityReadout(_opacity, t);
	        // Labels live on cross-wired tool roots, not under this empty MGR transform.
	        ThemeTmpUnder(_size != null ? _size.transform : null, t, excludeOpacityPressure: true);
	        ForEachDirectionHost(d => ThemeTmpUnder(d.transform, t, excludeOpacityPressure: true));
	        ThemeTmpUnder(_pressureTabletMode != null ? _pressureTabletMode.transform : null, t, excludeOpacityPressure: true);
	        ThemeTmpUnder(_opacity != null ? _opacity.transform : null, t, excludeOpacityPressure: true);
	    }

	    /// <summary>
	    /// BrushRibbon_UI_MGR is an empty host with cross-hierarchy refs — Direction is not a child.
	    /// Prefer the direction strip that matches the current dimension mode (Gen3D vs SD).
	    /// </summary>
	    public static BrushRibbon_UI_Direction ResolveDirection(Transform host) {
	        if (host != null) {
	            var local = host.GetComponentInChildren<BrushRibbon_UI_Direction>(true);
	            if (local != null)
	                return local;
	        }
	        bool preferGen3d = DimensionMode_MGR.instance != null
	            && DimensionMode_MGR.instance._dimensionMode == DimensionMode.dim_gen_3d;
	        var sd = UnityEngine.Object.FindFirstObjectByType<SD_BrushRibbon_UI_Direction>(FindObjectsInactive.Include);
	        var gen3d = UnityEngine.Object.FindFirstObjectByType<Gen3D_BrushRibbon_UI_Direction>(FindObjectsInactive.Include);
	        if (preferGen3d) {
	            if (gen3d != null) return gen3d;
	            if (sd != null) return sd;
	        }
	        else {
	            if (sd != null) return sd;
	            if (gen3d != null) return gen3d;
	        }
	        return UnityEngine.Object.FindFirstObjectByType<BrushRibbon_UI_Direction>(FindObjectsInactive.Include);
	    }

	    /// <summary>Both SD and Gen3D direction hosts must be themed — ResolveDirection alone leaves the other strip SPZ.</summary>
	    static void ForEachDirectionHost(Action<BrushRibbon_UI_Direction> apply) {
	        if (apply == null) return;
	        var sd = UnityEngine.Object.FindFirstObjectByType<SD_BrushRibbon_UI_Direction>(FindObjectsInactive.Include);
	        var gen3d = UnityEngine.Object.FindFirstObjectByType<Gen3D_BrushRibbon_UI_Direction>(FindObjectsInactive.Include);
	        if (sd != null) apply(sd);
	        if (gen3d != null) apply(gen3d);
	    }

	    BrushRibbon_UI_Direction ResolveDirection() => ResolveDirection(transform);

	    static void RestoreSelectableChrome(Selectable sel) {
	        if (sel == null) return;
	        SpzUiThemeOps.RestoreBoundChromeUnder(sel.transform);
	    }

	    void ThemeTmpUnder(Transform root, SpzUiThemeOps.ThemeTokens t, bool excludeOpacityPressure) {
	        if (root == null) return;
	        var dir = ResolveDirection();
	        foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true)) {
	            if (tmp == null) continue;
	            if (excludeOpacityPressure) {
	                if (_opacity != null && ReferenceEquals(tmp, _opacity.OpacityText))
	                    continue;
	                if (_pressureTabletMode != null && _pressureTabletMode.OwnsLabel(tmp))
	                    continue;
	            }
	            // Direction tool cells: ThemeToolToggle owns TMP (hidden under Nomad square litmus).
	            if (dir != null && IsUnderDirectionToolToggle(tmp.transform, dir))
	                continue;
	            // Size dial owns DialValue + Compact "size" caption — BoundChromeTmp tracking would
	            // re-open spacing and fight the tall-holder layout (100 / size crush litmus).
	            if (_size != null && tmp.transform.IsChildOf(_size.transform))
	                continue;
	            if (tmp.GetComponentInParent<CircleSlider_Snapping_UI>(true) != null)
	                continue;
	            SpzUiThemeOps.ApplyBoundChromeTmp(tmp, t.textPrimary);
	        }
	    }

	    static bool IsUnderDirectionToolToggle(Transform label, BrushRibbon_UI_Direction dir) {
	        if (label == null || dir == null) return false;
	        Transform t = label;
	        while (t != null) {
	            if (dir.PaintToggle != null && t == dir.PaintToggle.transform) return true;
	            if (dir.SmudgeToggle != null && t == dir.SmudgeToggle.transform) return true;
	            if (dir.EraseToggle != null && t == dir.EraseToggle.transform) return true;
	            t = t.parent;
	        }
	        return false;
	    }

	    static void ThemePressureMode(BrushRibbon_UI_PressureMode pressure, SpzUiThemeOps.ThemeTokens t) {
	        if (pressure == null) return;
	        ThemePressureToggle(pressure.NoneToggle, t);
	        ThemePressureToggle(pressure.BothToggle, t);
	        ThemePressureToggle(pressure.SizeToggle, t);
	        ThemePressureToggle(pressure.OpacityToggle, t);
	    }

	    /// <summary>
	    /// N/B/S/O cells: keep dark flat fill when selected (never white plate) so letter stays reverse-out.
	    /// </summary>
	    static void ThemePressureToggle(Toggle toggle, SpzUiThemeOps.ThemeTokens t) {
	        if (toggle == null) return;
	        Color fill = FlatToolFill(toggle.isOn, t);
	        SpzUiThemeOps.ApplyBoundChromeSelectable(toggle, fill, t.accent);
	        ApplyFlatToolColorBlock(toggle);
	        if (toggle.targetGraphic is Image bg) {
	            SpzUiThemeOps.ApplyRoundedControlSprite(bg, markEligible: true);
	            SpzUiThemeOps.FlattenToolFaceImage(bg);
	        }
	        HideSecondaryChromeUnder(toggle);
	        foreach (var tmp in toggle.GetComponentsInChildren<TMP_Text>(true)) {
	            if (tmp == null) continue;
	            // Always light type on dark cell — selected used to go white-on-white.
	            SpzUiThemeOps.ApplyBoundChromeCompactToolLabelTmp(tmp, t.textPrimary, 11f);
	        }
	        SpzUiThemeOps.ClearNonFaceRaycastsForTheme(toggle);
	    }

	    static void ThemeOpacityReadout(BrushRibbon_UI_Opacity opacity, SpzUiThemeOps.ThemeTokens t) {
	        if (opacity == null || opacity.OpacityText == null) return;
	        // Opacity sits on a luminous circular face — dark ink for contrast (matches dial fix).
	        Color ink = new Color(0.10f, 0.09f, 0.10f, 1f);
	        SpzUiThemeOps.ApplyBoundChromeDialValueTmp(opacity.OpacityText, ink, 16f);
	    }

	    static void ThemeDirectionTools(BrushRibbon_UI_Direction dir, SpzUiThemeOps.ThemeTokens t) {
	        if (dir == null) return;
	        // Pack flat Paint / Smudge / Eraser squares tight (Nomad hairline, not sparse gutters).
	        BrushRibbon_UI_Direction.ApplyPaintSmudgeEraseGaps(dir, nomadGaps: true);
	        ThemeToolToggle(dir.PaintToggle, StudioLineIcon.Brush, t);
	        ThemeToolToggle(dir.SmudgeToggle, StudioLineIcon.Smudge, t);
	        ThemeToolToggle(dir.EraseToggle, StudioLineIcon.Eraser, t);
	        // Re-assert gaps after theming — Flatten used to stretch root faces and wipe anchors.
	        BrushRibbon_UI_Direction.ApplyPaintSmudgeEraseGaps(dir, nomadGaps: true);
	    }

	    /// <summary>
	    /// Flat square cell (no beveled plate / 9-slice corner chevrons) + centered Nomad line icon.
	    /// Matches bucket/trash square litmus — no stacked label/+ band that elongates Paint/Smudge/Erase.
	    /// </summary>
	    static void ThemeToolToggle(Toggle toggle, StudioLineIcon glyph, SpzUiThemeOps.ThemeTokens t, float iconSizePx = 24f) {
	        if (toggle == null || !SpzUiThemeOps.ShouldRecolorBoundChrome) return;
	        Color normal = FlatToolFill(toggle.isOn, t);
	        SpzUiThemeOps.ApplyBoundChromeSelectable(toggle, normal, t.accent);
	        ApplyFlatToolColorBlock(toggle);
	        if (toggle.targetGraphic is Image bg) {
	            SpzUiThemeOps.ApplyRoundedControlSprite(bg, markEligible: true);
	            // Root-face Images share the Toggle RT — FlattenToolFaceImage skips RT rewrite for Selectables.
	            SpzUiThemeOps.FlattenToolFaceImage(bg);
	            bg.raycastTarget = true;
	        }
	        HideSecondaryChromeUnder(toggle);
	        // Centered icon only (bucket/trash litmus). Stacked label band made cells tall rectangles.
	        SpzUiThemeOps.ApplyControlLineIcon(toggle.transform, glyph, iconSizePx);
	        foreach (var tmp in toggle.GetComponentsInChildren<TMP_Text>(true)) {
	            if (tmp == null) continue;
	            SpzUiThemeOps.ApplyBoundChromeTmp(tmp, t.textPrimary, 11f);
	            SpzUiThemeOps.HideAuthoredGraphicForTheme(tmp);
	        }
	        // Ensure the Monolith glyph is on top and tinted (authored "icon" children stay hidden).
	        var iconT = SpzUiThemeOps.FindDirectChildIncludingInactive(toggle.transform, "MonolithLineIcon");
	        if (iconT != null) {
	            iconT.gameObject.SetActive(true);
	            iconT.SetAsLastSibling();
	            var iconImg = iconT.GetComponent<Image>();
	            if (iconImg != null) {
	                iconImg.enabled = true;
	                iconImg.sprite = UiRuntimeSprites.GetLineIcon(glyph);
	                iconImg.preserveAspect = true;
	                iconImg.raycastTarget = false;
	                SpzUiThemeOps.ApplyLineIconTint(iconImg);
	            }
	        }
	        SpzUiThemeOps.ClearNonFaceRaycastsForTheme(toggle);
	    }

	    static void ThemeToolButton(Button btn, StudioLineIcon glyph, SpzUiThemeOps.ThemeTokens t, bool applyIcon = true) {
	        if (btn == null || !SpzUiThemeOps.ShouldRecolorBoundChrome) return;
	        SpzUiThemeOps.ApplyBoundChromeSelectable(btn, FlatToolFill(false, t), t.accent);
	        ApplyFlatToolColorBlock(btn);
	        if (btn.targetGraphic is Image bg) {
	            SpzUiThemeOps.ApplyRoundedControlSprite(bg, markEligible: true);
	            SpzUiThemeOps.FlattenToolFaceImage(bg);
	        }
	        HideSecondaryChromeUnder(btn);
	        if (applyIcon)
	            SpzUiThemeOps.ApplyControlLineIcon(btn.transform, glyph, 22f);
	        SpzUiThemeOps.ClearNonFaceRaycastsForTheme(btn);
	    }

	    /// <summary>ColorTint must not multiply the sliced bevel when selected.</summary>
	    static void ApplyFlatToolColorBlock(Selectable sel) {
	        if (sel == null) return;
	        var cb = sel.colors;
	        cb.normalColor = Color.white;
	        cb.highlightedColor = Color.white;
	        cb.pressedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
	        cb.selectedColor = Color.white;
	        cb.disabledColor = new Color(1f, 1f, 1f, 0.4f);
	        cb.colorMultiplier = 1f;
	        sel.colors = cb;
	    }

	    /// <summary>Hide tick / check / corner-triangle chrome; keep Monolith overlays.</summary>
	    static void HideSecondaryChromeUnder(Selectable sel) {
	        if (sel == null) return;
	        if (sel is Toggle toggle && toggle.graphic is Image tick && tick != sel.targetGraphic)
	            SpzUiThemeOps.HideAuthoredGraphicForTheme(tick);
	        foreach (var img in sel.GetComponentsInChildren<Image>(true)) {
	            if (img == null || img == sel.targetGraphic) continue;
	            string n = img.gameObject.name ?? "";
	            if (n == "MonolithActiveBar" || n == "MonolithLineIcon" || n == "LineIcon")
	                continue;
	            if (n.IndexOf("triangle", StringComparison.OrdinalIgnoreCase) >= 0
	                || n.Equals("Checkmark", StringComparison.OrdinalIgnoreCase)
	                || n.IndexOf("pressed", StringComparison.OrdinalIgnoreCase) >= 0
	                || n.Equals("tick", StringComparison.OrdinalIgnoreCase))
	                SpzUiThemeOps.HideAuthoredGraphicForTheme(img);
	        }
	    }

	    static Color FlatToolFill(bool selected, SpzUiThemeOps.ThemeTokens t) {
	        return selected
	            ? Color.Lerp(t.controlBg, t.accent, 0.14f)
	            : t.controlBg;
	    }

	    /// <summary>Re-apply pressure N/B/S/O cell fills after selection changes.</summary>
	    public void NotifyPressureModeChromeChanged() {
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) return;
	        if (_pressureTabletMode == null) return;
	        ThemePressureMode(_pressureTabletMode, SpzUiThemeOps.Active);
	    }

	    public void Save( StableProjectorz_SL spz){
	        var trSL = new BrushRibbon_UI_SL();
	        _hardness.Save(trSL);
	        _colors.Save(trSL);
	        _size.Save(trSL);
	        _opacity.Save(trSL);
	        spz.brush_MGR = trSL;
	    }

	    public void Load(StableProjectorz_SL spz){
	        BrushRibbon_UI_SL trSL = spz.brush_MGR;
	        if (trSL == null) return;
	        _hardness.Load(trSL);
	        _colors.Load(trSL);
	        _size.Load(trSL);
	        _opacity.Load(trSL);
	    }
	}
}//end namespace

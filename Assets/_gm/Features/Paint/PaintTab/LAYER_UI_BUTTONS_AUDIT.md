# Layer UI – Line-by-Line Button & Flow Audit

This audit verifies that every layer UI button and control works as designed and that data/display flows are correct.

---

## 1. Add Layer button

| Location | Line(s) | Design | Implementation | Status |
|----------|---------|--------|----------------|--------|
| **CollectPaintUI** | 443–466 | Single “+ Layer” button at bottom of layers section | `CreateLayersPanelRuntime` creates `AddLayerBtn` with Button, Text “+ Layer”; `panel.SetAddLayerButton(addBtn)` | OK |
| **LayersPanel_UI** | 69, 141–146, 161–164 | Add button calls AddLayer on stack | `Start()`: `_addLayerButton.onClick.RemoveAllListeners(); AddListener(OnAddLayer)`. `OnAddLayer()`: `_layerStack?.AddLayer()` | OK |
| **PaintLayerStack_MGR** | 91–110 | AddLayer: new layer visible, active, empty; others unchanged | New layer `Visible = true`, appended to `_layers`, `_activeIndex = Count - 1`, `OnLayersChanged` + `OnActiveLayerChanged` | OK |

**Flow:** Click “+ Layer” → OnAddLayer → AddLayer() → new layer created and made active → OnLayersChanged → RebuildList → new row appears. Other layers remain; visibility unchanged. **Working as designed.**

---

## 2. Blue button (Select / Active layer)

| Location | Line(s) | Design | Implementation | Status |
|----------|---------|--------|----------------|--------|
| **BuildMinimalRow** | 408–420 | Blue 28×28 button “SelectLayer”, no tint | GameObject “SelectLayer”, Image blue, Button, `transition = None`, targetGraphic = Image | OK |
| **CreateRow** | 227–242 | Click sets this layer active; dark blue = active, light = inactive | Find “SelectLayer”, transition None, onClick → `SetActiveLayer(index)` (with bounds check), Image color = isActive ? dark : light | OK |
| **PaintLayerStack_MGR** | 182–186 | SetActiveLayer updates index, fires event | Bounds check, `_activeIndex = index`, `OnActiveLayerChanged?.Invoke()` | OK |
| **LayersPanel_UI** | 139–140, 165–181 | Rebuild on active change; row built with isActive | OnActiveLayerChanged += RebuildList; RebuildList passes `index == active` to CreateRow | OK |

**Flow:** Click blue → SetActiveLayer(index) → OnActiveLayerChanged → RebuildList → active row gets dark blue + “Active layer” label. **Working as designed.**

---

## 3. Red button (Delete layer)

| Location | Line(s) | Design | Implementation | Status |
|----------|---------|--------|----------------|--------|
| **BuildMinimalRow** | 422–435 | Red 28×28 “Delete” button | GameObject “Delete”, Image red, Button, transition None, targetGraphic = Image | OK |
| **CreateRow** | 245–281 | Find Delete (direct Find + child name fallback), wire Button + LayerRowDeleteTrigger | Find “Delete” or iterate children by name; deleteGo.SetActive(true); Button interactable, transition None, targetGraphic/raycastTarget set; onClick → RemoveLayer + RequestReRender; LayerRowDeleteTrigger.Setup(stack, index) | OK |
| **LayerRowDeleteTrigger** | 35–52 | IPointerClickHandler so delete works if Button/ScrollRect swallows click | OnPointerClick: bounds check → RemoveLayer(layerIndex) → RequestReRender() | OK |
| **PaintLayerStack_MGR** | 154–166 | RemoveLayer disposes layer, removes from list, fixes active index | Bounds check, Dispose, RemoveAt, adjust _activeIndex, OnLayersChanged + OnActiveLayerChanged | OK |

**Flow:** Click red → (Button onClick or LayerRowDeleteTrigger.OnPointerClick) → RemoveLayer(index) → OnLayersChanged → RebuildList → row removed; RequestReRender refreshes view. **Working as designed.**

---

## 4. Circle button (Visibility)

| Location | Line(s) | Design | Implementation | Status |
|----------|---------|--------|----------------|--------|
| **BuildMinimalRow** | 436–455 | “Visibility” 28×28, circle icon, light gray default | GameObject “Visibility”, Image (light gray), Button transition None, child “Icon” with GetVisibilityIconSprite() | OK |
| **CreateRow** | 284–321 | Toggle or Button path; circle = sole control for visibility | If Toggle: onValueChanged → SetLayerVisible(index, visible). Else Find “Visibility”: transition None, UpdateVisibilityButtonLabel(visible), onClick → toggle Visible, UpdateVisibilityButtonLabel, RequestReRender | OK |
| **UpdateVisibilityButtonLabel** | 103–124 | On = light gray bg + white icon; off = dark gray + dim icon | bgImg.color by visible; Icon color by visible | OK |
| **PaintLayerStack_MGR** | 188–195 | SetLayerVisible sets Visible, fires OnLayersChanged | Bounds check, `_layers[index].Visible = visible`, OnLayersChanged | OK |
| **Inpaint_MaskPainter** | ApplyColorLayer | Viewport uses composite of visible layers | CompositeToWithActiveOnTop uses only visible layers; hidden layers excluded | OK |

**Flow:** Click circle → SetLayerVisible(index, !visible) → OnLayersChanged → UpdateVisibilityButtonLabel → RequestReRender; viewport composite excludes hidden layers. **Working as designed.**

---

## 5. Opacity slider

| Location | Line(s) | Design | Implementation | Status |
|----------|---------|--------|----------------|--------|
| **BuildMinimalRow** | 457–494 | Slider 0–1 in row | GameObject “Opacity”, Slider min/max 0–1, Fill + Handle, LayoutElement 52px | OK |
| **CreateRow** | 323–338 | Slider value = layer.Opacity; change → SetLayerOpacity | SetValueWithoutNotify(Clamp01(layer.Opacity)); onValueChanged: bounds check → SetLayerOpacity(opacityIndex, value), RequestReRender | OK |
| **PaintLayerStack_MGR** | 197–204 | SetLayerOpacity clamps 0–1, fires OnLayersChanged | Bounds check, Opacity = Clamp01(opacity), OnLayersChanged | OK |

**Flow:** Drag slider → SetLayerOpacity(index, value) → OnLayersChanged → RequestReRender; composite uses per-layer opacity. **Working as designed.** (Bounds check and captured index added in audit.)

---

## 6. Click layer name (select layer)

| Location | Line(s) | Design | Implementation | Status |
|----------|---------|--------|----------------|--------|
| **CreateRow** | 184–226 | Row does not receive raycasts; Name area is click-to-select | rowImg.raycastTarget = false; row Button cleared (targetGraphic = null, listeners removed); nameGo gets Button, targetGraphic = Name’s Graphic, onClick → SetActiveLayer(index) | OK |

**Flow:** Click on layer name → SetActiveLayer(index) → same as blue button. **Working as designed.**

---

## 7. Panel lifecycle and wiring (CollectPaintUI)

| Location | Line(s) | Design | Implementation | Status |
|----------|---------|--------|----------------|--------|
| **CollectPaintUI** | 190–227 | Layers section: resolve scroll content, find or create panel, reparent into scroll | GetLayersScrollContentAndRoot; FindObjectOfType PaintTab_LayersPanel_UI; if null and scrollContent != null → CreateLayersPanelRuntime(scrollContent, sectionRoot); reparent panel (and LayerButtonsRow) into layersScrollContent; ForceRebuildLayoutImmediate | OK |
| **CreateLayersPanelRuntime** | 389–470 | Create stack if missing; panel GO in scroll content; list root = panel; Add button in LayerButtonsRow | PaintLayerStack_MGR.instance created if null; panel GO parent = scrollContent, first sibling; panel has VLG + ContentSizeFitter; panel.SetLayerStack(instance); LayerButtonsRow as second child of scrollContent; Add Layer button in row; panel.SetAddLayerButton(addBtn) | OK |
| **LayersPanel_UI Start** | 132–148 | Resolve stack/list root, subscribe to events, wire Add button, RebuildList | _layerStack ?? FindObjectOfType; _listRoot ?? transform; OnLayersChanged += RebuildList; OnActiveLayerChanged += RebuildList; _addLayerButton onClick = OnAddLayer; RebuildList() | OK |

**Flow:** CollectNow runs → panel created or found → SetLayerStack + SetAddLayerButton → Start subscribes and builds list. **Working as designed.**

---

## 8. RebuildList and CreateRow

| Item | Design | Implementation | Status |
|------|--------|----------------|--------|
| RebuildList | Clear old rows, create one row per layer with correct active | Destroy all _rowInstances; for each layer CreateRow(layer, i, i == active); add to _rowInstances | OK |
| CreateRow template vs minimal | Prefab row or BuildMinimalRow | _layerRowTemplate ? Instantiate : BuildMinimalRow(index) | OK |
| Row order (minimal) | SelectLayer, Delete, Visibility, Opacity, Name | BuildMinimalRow adds in that order | OK |
| Active indicator | “Active layer” label when isActive | ActiveIndicator created or updated, text “Active layer”; hidden when !isActive | OK |

---

## 9. Display and paint (Inpaint_MaskPainter)

| Item | Design | Implementation | Status |
|------|--------|----------------|--------|
| Paint target | Strokes go to active layer Data (or fallback) | GetPaintTarget() → ActiveLayerDataRenderUdims ?? ActiveLayerRenderUdims ?? _ObjectUV_brushedColorRGBA | OK |
| Viewport | All visible layers composited; active on top; fallback as base | ApplyColorLayer_To_UV_Textures: CompositeToWithActiveOnTop(_layerStackCompositeTemp, baseUnderneath); source = composite | OK |
| RequestReRender | After delete/visibility/opacity so view updates | Delete and Visibility and Opacity handlers call RequestReRender(); LayerRowDeleteTrigger calls RequestReRender() | OK |

---

## 10. Summary

| Button / Control | Intended behavior | Verified |
|------------------|-------------------|----------|
| **Add Layer** | New empty layer, active; others unchanged and visible | Yes |
| **Blue (Select)** | Set active layer; dark = active, light = inactive | Yes |
| **Red (Delete)** | Remove that layer; list and view update | Yes (Button + LayerRowDeleteTrigger) |
| **Circle (Visibility)** | Toggle layer visible in viewport; on/off styling | Yes |
| **Opacity slider** | Set layer opacity 0–1; viewport composite uses it | Yes (bounds check added) |
| **Layer name click** | Set active layer (same as blue) | Yes |

**Fixes applied during audit**

- Opacity slider: added bounds check and captured `opacityIndex` in listener for safety.

All buttons and flows are implemented and wired as designed; the layer UI is ready for use.

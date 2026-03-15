# Layer system scaffolding audit

Line-by-line verification that the layer system is wired and implemented as intended (Photoshop-like: siloed data, ordering, visibility, opacity, scrollable UI, clear active selection).

---

## 1. Data layer (PaintLayer, PaintLayerStack_MGR)

| Check | Location | Status |
|-------|----------|--------|
| Each layer has own Content + Data | PaintLayer.cs: Content, Data | OK – siloed; only active receives paint |
| Visibility and opacity per layer | PaintLayer.cs: Visible, Opacity | OK – used in display and composite |
| Stack ordering: index 0 = bottom, last = top | PaintLayerStack_MGR: _layers list, MoveLayer | OK |
| Only active layer receives new paint | Inpaint_MaskPainter.GetPaintTarget → ActiveLayerDataRenderUdims | OK |
| Display: only active layer injected (when visible) + opacity | Inpaint_MaskPainter.ApplyColorLayer_To_UV_Textures | OK – source = active.Content, _TotalOpacity01 = active.Opacity |
| Hidden active = scene only | ApplyColorLayer_To_UV_Textures: if !active.Visible source = null path | OK |
| SetLayerVisible / SetLayerOpacity fire OnLayersChanged | PaintLayerStack_MGR | OK |
| Save/Load layer stack | PaintLayerStack_MGR.Save/Load | OK |

---

## 2. Paint target and bake

| Check | Location | Status |
|-------|----------|--------|
| Strokes write to ActiveLayerDataRenderUdims (or fallback) | GetPaintTarget() | OK |
| After stroke, active layer Bake() then re-render | Inpaint_MaskPainter after Apply_into_ColorBrushTex | OK |
| EnsureResolution when active has no content | OnActiveLayerChanged_EnsureContent, GetPaintTarget | OK |
| OnLayerAdded: migrate previous layer or fallback into new layer | OnLayerAdded_MigrateFallback | OK |

---

## 3. UI: Layers panel (PaintTab_LayersPanel_UI)

| Check | Location | Status |
|-------|----------|--------|
| _listRoot defaults to panel transform (rows = children) | Start: _listRoot ?? transform | OK |
| _layerStack resolved at runtime if null | Start: FindObjectOfType; SetLayerStack() from CollectNow | OK |
| RebuildList on OnLayersChanged + OnActiveLayerChanged | Start / OnDestroy | OK |
| Add Layer button wired | SetAddLayerButton, OnAddLayer → AddLayer | OK |
| Row click sets active layer | selectBtn/btn.onClick → SetActiveLayer(index) | OK |
| Visibility toggle/button and opacity slider per row | CreateRow: Toggle / Visibility button, Slider | OK |
| Active row visual: background + outline | CreateRow: img.color + Outline (effectColor, effectDistance) | OK – clear “selected” outline |
| Drag reorder: LayerRowDragHandle, LayerRowDropTarget → MoveLayer | CreateRow | OK |
| RequestReRender after visibility/opacity change | SetLayerVisible/SetLayerOpacity path → OnLayersChanged; panel uses RequestReRender in handlers | OK |

---

## 4. Scaffolding: CollectNow and scroll

| Check | Location | Status |
|-------|----------|--------|
| Resolve scroll content vs section root for Layers | GetLayersScrollContentAndRoot(LayersSection) | OK – prefab: section has Content→ScrollRect.content; runtime: section ref may be ScrollContent |
| Panel parent = scroll content (list scrolls) | CreateLayersPanelRuntime(scrollContent, sectionRoot); panel.SetParent(scrollContent) | OK |
| Add Layer button parent = section root (always visible) | addBtnGo.SetParent(sectionRoot), SetAsLastSibling | OK |
| Panel has ContentSizeFitter (vertical PreferredSize) | CreateLayersPanelRuntime: csf on panel GO | OK – scroll content height grows with rows |
| Panel VLG childControlHeight = false | CreateLayersPanelRuntime: vlg.childControlHeight = false | OK – height from ContentSizeFitter |
| PaintLayerStack_MGR created if missing | CreateLayersPanelRuntime | OK |
| Panel gets _layerStack assigned | SetLayerStack(instance) | OK |

---

## 5. Export and SD mask (full composite)

| Check | Location | Status |
|-------|----------|--------|
| ExtractColorLayer / GetDisposable_ScreenMask use CompositeTo (all visible + opacity) | Inpaint_MaskPainter | OK – full composite for export/SD |

---

## 6. Intended behaviour summary

- **Ordering:** Bottom (0) to top (last); reorder via drag-drop or MoveLayer.
- **Siloed data:** Each layer has Content + Data; only the active layer receives brush strokes and scene injection.
- **Display:** Viewport shows scene + only the active layer’s Content (if visible), with that layer’s Opacity.
- **Visibility:** Hidden layers are not shown in viewport; export/SD use full composite.
- **UI:** Layer list lives inside the section’s scroll content (scrollable); “+ Layer” is in section root (fixed at bottom); active row has background + outline.

---

*Last audit: after fixing scroll content/section root wiring, Add button placement, and active row outline.*

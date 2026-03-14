# Layer UI – Line-by-Line Audit

Audit date: current. Verifies every button and wiring path so nothing is missing or broken.

---

## A. PaintTab_CollectPaintUI.cs – Layers section (lines 189–258)

| Lines | Code / purpose | Audit |
|-------|----------------|--------|
| 190 | `if (_layout.LayersSection != null)` | Layers block only runs when layout has a Layers section. |
| 192 | `GetLayersScrollContentAndRoot(_layout.LayersSection, out var layersScrollContent, out var layersSectionRoot)` | Resolves scroll content and section root for prefab vs runtime. |
| 194–198 | Create `PaintLayerStack_MGR` if `instance == null` | Stack always exists before we find/create panel; panel can safely wire to it. |
| 199 | `var layersPanel = FindObjectOfType<PaintTab_LayersPanel_UI>(true)` | Finds existing panel (including inactive). |
| 200–201 | If panel null and scrollContent not null → `CreateLayersPanelRuntime(layersScrollContent, layersSectionRoot)` | Panel is created only when needed and only when we have scroll content. |
| 203–225 | **Wire panel (found or created)** | |
| 204 | `if (layersPanel != null)` | All wiring guarded by panel existence. |
| 205 | `layersPanel.SetLayerStack(PaintLayerStack_MGR.instance)` | Stack set every time; SetLayerStack subscribes and calls RebuildList (no early return). |
| 206–219 | Find Add button: `searchRoot` = scrollContent or panel.parent; loop children for `name == "LayerButtonsRow"`; inside row get `Find("AddLayerBtn")` or `GetComponentInChildren<Button>` | Correctly finds existing Add Layer button when present. |
| 221–222 | If `addBtn == null` and `layersScrollContent != null` → `addBtn = EnsureLayersAddButtonRow(layersScrollContent)` | **Add Layer is never missing:** if no button found, we create the row and button. |
| 223–224 | If `addBtn != null` → `layersPanel.SetAddLayerButton(addBtn)` | Add button (found or created) is always assigned and wired when we have a stack. |
| 226–251 | Reparent panel into `layersScrollContent` if needed; move `LayerButtonsRow` with panel so it stays sibling index 1 | Keeps list and button row in correct scroll content. |
| 253–256 | `did = true`; `LayoutRebuilder.ForceRebuildLayoutImmediate(layersScrollContent)` | Layout refreshes so list and Add button are laid out. |

**Verdict:** CollectNow ensures stack exists, panel exists (found or created), SetLayerStack and SetAddLayerButton are always called, and Add Layer button is created when missing. No feature removed.

---

## B. EnsureLayersAddButtonRow (CollectPaintUI 420–465)

| Lines | Code / purpose | Audit |
|-------|----------------|--------|
| 422–423 | `if (scrollContent == null) return null` | Safe. |
| 424–438 | Create `LayerButtonsRow` GameObject; SetParent(scrollContent); SetAsLastSibling; RectTransform, LayoutElement (h 26), HorizontalLayoutGroup | Row is a proper layout child under scroll content. |
| 440–464 | Create `AddLayerBtn` child: LayoutElement (80×24), **Image** (green, raycastTarget true), **Button** with **targetGraphic = addImg**, Text child with "+ Layer" (TMP), text raycastTarget false | **targetGraphic set** so the Button receives clicks. Label does not block. |
| 464 | `return addBtn` | Returns the Button so caller can SetAddLayerButton. |

**Verdict:** Add Layer button is created with correct targetGraphic and label; no removal of the button.

---

## C. CreateLayersPanelRuntime (CollectPaintUI 467–508)

| Lines | Code / purpose | Audit |
|-------|----------------|--------|
| 469–473 | Create stack if null | Redundant with CollectNow but safe. |
| 476–498 | Panel GO: parent = scrollContent, SetAsFirstSibling; RectTransform, LayoutElement, VerticalLayoutGroup, ContentSizeFitter (PreferredSize vertical) | Panel is first child of scroll content; list will be built inside it. |
| 500 | `var panel = go.AddComponent<PaintTab_LayersPanel_UI>()` | Panel component added. |
| 501 | `panel.SetLayerStack(PaintLayerStack_MGR.instance)` | Stack set immediately; subscribes and RebuildList. |
| 504–505 | `addBtn = EnsureLayersAddButtonRow(scrollContent)`; `panel.SetAddLayerButton(addBtn)` | **Add Layer row and button always created** when panel is created; button wired. |
| 507 | `return panel` | Panel returned to CollectNow for further wiring/reparenting. |

**Verdict:** New panel always gets stack and Add Layer button; both wired.

---

## D. PaintTab_LayersPanel_UI – SetAddLayerButton (69–77)

| Lines | Code / purpose | Audit |
|-------|----------------|--------|
| 71 | `_addLayerButton = btn` | Stores reference. |
| 72–76 | If `_addLayerButton != null && _layerStack != null`: RemoveAllListeners, AddListener(OnAddLayer) | When stack already set (e.g. SetLayerStack called first), Add Layer is wired here so order of calls does not matter. |

**Verdict:** Add button is wired when both button and stack are set.

---

## E. PaintTab_LayersPanel_UI – SetLayerStack (80–101)

| Lines | Code / purpose | Audit |
|-------|----------------|--------|
| 83–86 | If `_layerStack != null`: unsubscribe OnLayersChanged and OnActiveLayerChanged | Prevents double subscription when stack is replaced or set again. |
| 87 | `_layerStack = stack` | Assigns current stack. |
| 88–99 | If `_layerStack != null`: subscribe RebuildList to both events; if `_listRoot == null` set to transform; if `_addLayerButton != null` wire OnAddLayer; **RebuildList()** | Every time stack is set we subscribe, ensure list root, wire Add button if present, and build the list. No early return that would skip this. |

**Verdict:** SetLayerStack always re-wires and rebuilds when stack is non-null.

---

## F. PaintTab_LayersPanel_UI – Start (158–174)

| Lines | Code / purpose | Audit |
|-------|----------------|--------|
| 161–162 | Resolve `_layerStack` (FindObjectOfType if null), `_listRoot` (transform if null) | Fallbacks for prefab/serialized refs. |
| 163 | `if (_layerStack == null) return` | No wiring without stack. |
| 164 | Template deactivate if present | Prevents template showing as a row. |
| 165–168 | Unsubscribe then subscribe OnLayersChanged / OnActiveLayerChanged | Idempotent; safe if SetLayerStack already ran. |
| 169–173 | If `_addLayerButton != null`: RemoveAllListeners, AddListener(OnAddLayer) | Add Layer wired in Start when refs are set at load. |
| 174 | RebuildList() | List built when panel first runs with stack. |

**Verdict:** Start wires events and Add button and builds list when stack is available.

---

## G. PaintTab_LayersPanel_UI – OnAddLayer, RebuildList (186–209)

| Lines | Code / purpose | Audit |
|-------|----------------|--------|
| 188–191 | OnAddLayer: `_layerStack?.AddLayer()` | Add Layer click calls stack AddLayer; null-safe. |
| 193–194 | RebuildList: if stack or listRoot null return | Safe. |
| 195–200 | Destroy all _rowInstances, Clear list | Clean slate. |
| 199–207 | For each layer: index = i, CreateRow(layer, index, index == active), add to _rowInstances | One row per layer; active row passed correctly. |

**Verdict:** Add Layer triggers AddLayer; list is rebuilt from stack and active index.

---

## H. PaintTab_LayersPanel_UI – CreateRow (211–419)

### H1. Row creation and basics (212–254)

| Lines | Code / purpose | Audit |
|-------|----------------|--------|
| 215–222 | Row from template (Instantiate to _listRoot) or BuildMinimalRow(index) | Template or minimal row. |
| 224–229 | LayoutElement on row (height, flex width) | Layout correct. |
| 231–235 | Name label: TMP or legacy Text; set layer.Name | Label shows layer name. |
| 237–238 | Row Image raycastTarget = false | **Row does not steal clicks** from buttons. |
| 241–250 | nameGo = Find("Name") or name label/legacy gameObject; Button on nameGo; targetGraphic = name Graphic; onClick → SetActiveLayer(index) with bounds check | **Click layer name** = set active; button has targetGraphic. |
| 252–253 | Row-level Button: clear listeners and targetGraphic | Row does not intercept. |

### H2. Blue Select button (255–270)

| Lines | Code / purpose | Audit |
|-------|----------------|--------|
| 256 | Find("SelectLayer")?.GetComponent<Button>() | Direct find. |
| 258–265 | transition None; onClick → SetActiveLayer(index) with bounds check | Blue click sets active. |
| 266–269 | Select button Image color: isActive dark blue, else light blue | Visual state correct. |

### H3. Red Delete button (272–308)

| Lines | Code / purpose | Audit |
|-------|----------------|--------|
| 273–281 | Find("Delete"); fallback loop over row children by name "Delete" | Find with fallback. |
| 284–303 | deleteGo.SetActive(true); Button interactable, transition None; targetGraphic/raycastTarget set if needed; onClick: bounds check → RemoveLayer(layerIndexToRemove), RequestReRender | Button path wired. |
| 304–307 | LayerRowDeleteTrigger: get or add; Setup(_layerStack, index) | **IPointerClickHandler** path so delete works even if Button/ScrollRect swallows. |

### H4. Visibility (310–344)

| Lines | Code / purpose | Audit |
|-------|----------------|--------|
| 311–324 | If Toggle present: SetIsOnWithoutNotify(layer.Visible); onValueChanged → SetLayerVisible(index, visible), RequestReRender | Toggle path. |
| 328–343 | Else Find("Visibility") Button: transition None; UpdateVisibilityButtonLabel(visible); onClick: bounds check, toggle Visible, UpdateVisibilityButtonLabel, RequestReRender | **Circle button** path; state and API correct. |

### H5. Opacity (346–362)

| Lines | Code / purpose | Audit |
|-------|----------------|--------|
| 351–361 | Slider SetValueWithoutNotify(layer.Opacity); onValueChanged: bounds check → SetLayerOpacity(opacityIndex, value), RequestReRender | Opacity wired with captured index and bounds check. |

### H6. Visuals and drop target (364–418)

| Lines | Code / purpose | Audit |
|-------|----------------|--------|
| 366–381 | Row Image color and Outline for active row | Active row highlighted. |
| 382–413 | ActiveIndicator: "Active layer" label when isActive; created or updated; raycastTarget false | Label does not block. |
| 416–418 | LayerRowDropTarget: layerIndex, stack set | Reorder drop target present. |

**Verdict:** All per-row controls (name click, Select, Delete, Visibility, Opacity) are found or built, have targetGraphic/raycast where needed, and call the correct stack APIs and RequestReRender. Delete has both Button and LayerRowDeleteTrigger.

---

## I. PaintTab_LayersPanel_UI – BuildMinimalRow (421–539)

| Lines | Code / purpose | Audit |
|-------|----------------|--------|
| 423–438 | Row GO; parent _listRoot; RectTransform, LayoutElement, HorizontalLayoutGroup; row Image; row Button (for compatibility; cleared in CreateRow) | Row structure. |
| 440–451 | **SelectLayer**: Image blue, raycastTarget true; Button targetGraphic = Image, transition None | Blue button clickable. |
| 454–466 | **Delete**: Image red, raycastTarget true; Button targetGraphic = delImg, transition None | Red button clickable; LayerRowDeleteTrigger added in CreateRow. |
| 468–487 | **Visibility**: Image (light gray), Button targetGraphic = visBg, transition None; Icon child with GetVisibilityIconSprite(), raycastTarget false | Circle button clickable. |
| 489–524 | **Opacity**: Slider 0–1, fillRect, handleRect, targetGraphic on handle | Slider usable. |
| 526–538 | **Name**: LayoutElement flexibleWidth 1; Text (legacy) | Name area for click-to-select; CreateRow adds Button. |

**Verdict:** BuildMinimalRow creates all controls with correct targetGraphic and raycastTarget; no controls removed.

---

## J. LayerRowDeleteTrigger (34–50)

| Lines | Code / purpose | Audit |
|-------|----------------|--------|
| 39–43 | Setup(stack, index) | Stores refs. |
| 45–49 | OnPointerClick: bounds check → RemoveLayer(layerIndex), RequestReRender | Delete works even if Button does not fire. |

**Verdict:** Backup delete path correct.

---

## K. Summary checklist

| Item | Location | Status |
|------|----------|--------|
| Stack created when missing | CollectNow 194–198; CreateLayersPanelRuntime 469–473 | OK |
| Panel found or created | CollectNow 199–201 | OK |
| SetLayerStack always called when panel != null | CollectNow 205 | OK |
| SetLayerStack subscribes and RebuildList (no early return) | LayersPanel_UI 80–101 | OK |
| Add Layer button found or created | CollectNow 206–224; EnsureLayersAddButtonRow | OK |
| Add Layer targetGraphic set | EnsureLayersAddButtonRow 449–450 | OK |
| SetAddLayerButton wires OnAddLayer when stack set | LayersPanel_UI 69–77; also in SetLayerStack 94–97 | OK |
| RebuildList builds rows with correct index/active | LayersPanel_UI 193–209 | OK |
| Row does not steal clicks (raycastTarget false) | CreateRow 237–238, 252–253 | OK |
| Name click → SetActiveLayer | CreateRow 241–250 | OK |
| Blue Select → SetActiveLayer, targetGraphic, transition None | CreateRow 255–270; BuildMinimalRow 440–451 | OK |
| Red Delete → RemoveLayer + RequestReRender (Button + Trigger) | CreateRow 272–307; BuildMinimalRow 454–466 | OK |
| Visibility → SetLayerVisible + RequestReRender | CreateRow 310–344; BuildMinimalRow 468–487 | OK |
| Opacity → SetLayerOpacity + RequestReRender | CreateRow 346–362; BuildMinimalRow 489–524 | OK |

**Conclusion:** No features were removed. Add Layer button is always present (created when missing) and has targetGraphic. All per-row buttons and the Add Layer button are wired to the layer stack and RequestReRender. This audit matches the current code line-by-line.

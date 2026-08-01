using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace spz {

	// The catalogue the bridge exposes. Deliberately curated rather than auto-derived
	// from every StaticEvents id: most of those ids are internal UI plumbing
	// ("SetButtonsInteractable"), and handing an agent 87 undocumented switches is a
	// good way to get unpredictable behaviour. Broad access is still available through
	// 'list_events' + 'invoke_event' for anyone who wants it.
	//
	// Every tool must answer exactly once, via ok(...) or fail(...). Answering from a
	// later frame is fine and expected (see get_viewport_screenshot).
	public static class SPZ_Agent_Tools {

	    static readonly Dictionary<string, AgentTool> _tools = new Dictionary<string, AgentTool>(StringComparer.Ordinal);
	    static bool _registered = false;

	    public static AgentTool Find(string name){
	        if (name == null){ return null; }
	        return _tools.TryGetValue(name, out AgentTool t) ? t : null;
	    }


	    public static void RegisterAll(){
	        // Upsert catalogue each boot (safe if bridge re-inits after domain reload).
	        _registered = true;

	        Add("describe", "Describe the tool catalogue",
	            "Protocol version and the full catalogue of available tools. Call this first.",
	            null, Tool_Describe,
	            readOnly: true, idempotent: true);

	        Add("get_app_state", "Read app state",
	            "Snapshot of the app: version, WebUI connections, loaded 3D model, UDIM tiles, selection, and whether a generation is running.",
	            null, Tool_GetAppState,
	            readOnly: true, idempotent: true);

	        // Read-only, but not idempotent: the viewport changes as the user navigates,
	        // so two identical calls legitimately return different pixels.
	        Add("get_viewport_screenshot", "Capture the viewport",
	            "PNG capture of a region of the main 3D viewport, as base64. Coordinates are viewport-normalized, (0,0) bottom-left to (1,1) top-right.",
	            new List<AgentParamDesc>{
	                new AgentParamDesc("min_x", "number", false, "Left edge, 0..1. Default 0."),
	                new AgentParamDesc("min_y", "number", false, "Bottom edge, 0..1. Default 0."),
	                new AgentParamDesc("max_x", "number", false, "Right edge, 0..1. Default 1."),
	                new AgentParamDesc("max_y", "number", false, "Top edge, 0..1. Default 1."),
	            },
	            Tool_Screenshot,
	            readOnly: true, idempotent: false, returnsImage: true);

	        Add("list_generations", "List stored generations",
	            "How many stored generations exist per kind, plus the GUID of the most recent one. Each generation keeps its camera POV, prompts, result textures and masks.",
	            null, Tool_ListGenerations,
	            readOnly: true, idempotent: true);

	        Add("list_events", "List UI event ids",
	            "Every StaticEvents id currently registered by the running UI, with the parameter types each one expects. This is the raw action surface.",
	            new List<AgentParamDesc>{
	                new AgentParamDesc("filter", "string", false, "Case-insensitive substring to narrow the list, e.g. 'Settings:'."),
	            },
	            Tool_ListEvents,
	            readOnly: true, idempotent: true);

	        // The only writing tool here. What it does depends entirely on the id, and
	        // some ids delete work (clearing generations, resetting settings), so it is
	        // flagged destructive so that a client can ask the user before firing it.
	        Add("invoke_event", "Fire a UI event",
	            "Fire a StaticEvents id, the same way the corresponding UI control would. Use list_events to discover ids and their argument types. Reports an error if the id is unknown or the arguments don't match.",
	            new List<AgentParamDesc>{
	                new AgentParamDesc("id",   "string", true,  "Event id, e.g. 'Settings:OpenSettingsPanel'."),
	                new AgentParamDesc("args", "array",  false, "Arguments, in order. Omit for a no-argument event."),
	            },
	            Tool_InvokeEvent,
	            readOnly: false, idempotent: false, destructive: true);

	        Add("get_sd_gen_settings", "Read SD Gen Art settings",
	            "Current Stable Diffusion UI: checkpoint, VAE, sampler, scheduler, steps, CFG, seed, resolution, prompts, ControlNet models, can_gen flags.",
	            null, Tool_GetSdGenSettings,
	            readOnly: true, idempotent: true);

	        Add("list_sd_options", "List SD dropdown options",
	            "Available checkpoint / sampler / scheduler / VAE / ControlNet model names from the live UI dropdowns.",
	            null, Tool_ListSdOptions,
	            readOnly: true, idempotent: true);

	        Add("set_sd_gen_settings", "Set SD Gen Art settings",
	            "Update SD UI fields used for Gen Art (checkpoint, VAE, sampler, scheduler, steps, CFG, seed, size, prompts). Optional clear_controlnet_models. Klein checkpoints auto-attach TE+VAE modules on options sync.",
	            new List<AgentParamDesc>{
	                new AgentParamDesc("checkpoint", "string", false, "SD checkpoint dropdown name, e.g. flux-2-klein-4b."),
	                new AgentParamDesc("vae", "string", false, "VAE dropdown name (e.g. None or flux2_klein_4b_vae)."),
	                new AgentParamDesc("sampler", "string", false, "Sampler name, e.g. Euler."),
	                new AgentParamDesc("scheduler", "string", false, "Scheduler name, e.g. automatic / simple."),
	                new AgentParamDesc("steps", "number", false, "Sampling steps."),
	                new AgentParamDesc("cfg_scale", "number", false, "CFG scale."),
	                new AgentParamDesc("seed", "number", false, "Seed integer (-1 for random if UI allows)."),
	                new AgentParamDesc("width", "number", false, "Generation width."),
	                new AgentParamDesc("height", "number", false, "Generation height."),
	                new AgentParamDesc("positive_prompt", "string", false, "Replace positive prompt text."),
	                new AgentParamDesc("negative_prompt", "string", false, "Replace negative prompt text."),
	                new AgentParamDesc("clear_controlnet_models", "boolean", false, "If true, set every ControlNet unit model to None."),
	            },
	            Tool_SetSdGenSettings,
	            readOnly: false, idempotent: true, destructive: false);

	        Add("set_controlnet_unit", "Set one ControlNet unit",
	            "Change a ControlNet unit's model, weight, what-to-send, and/or activation by index (0-based). For Klein img2img co-opt: model None, what_to_send ContentCam or CustomFile, activated true.",
	            new List<AgentParamDesc>{
	                new AgentParamDesc("unit", "number", true, "ControlNet unit index (0-based)."),
	                new AgentParamDesc("model", "string", false, "ControlNet model name, or None to clear."),
	                new AgentParamDesc("weight", "number", false, "Control weight."),
	                new AgentParamDesc("what_to_send", "string", false, "None|Depth|Normals|VertexColors|ContentCam|CustomFile. CustomFile does not open a file picker."),
	                new AgentParamDesc("activated", "boolean", false, "Expand (true) or collapse (false) the unit."),
	            },
	            Tool_SetControlNetUnit,
	            readOnly: false, idempotent: true, destructive: false);

	        Add("generate", "Start Gen Art or Gen BG",
	            "Same as pressing GEN ART / GEN BG. Respects Klein ControlNet bypass and normal depth/normals gates.",
	            new List<AgentParamDesc>{
	                new AgentParamDesc("backgrounds", "boolean", false, "If true, Gen BG; else Gen Art. Default false."),
	            },
	            Tool_Generate,
	            readOnly: false, idempotent: false, destructive: true);

	        Add("prepare_flux_klein_test", "Preset Flux.2 Klein Gen Art test",
	            "Convenience: flux-2-klein-4b, Euler, 4 steps, CFG 1.0, 512x512, clears ControlNet models.",
	            new List<AgentParamDesc>{
	                new AgentParamDesc("checkpoint", "string", false, "Override checkpoint name. Default flux-2-klein-4b."),
	                new AgentParamDesc("width", "number", false, "Width. Default 512."),
	                new AgentParamDesc("height", "number", false, "Height. Default 512."),
	            },
	            Tool_PrepareFluxKleinTest,
	            readOnly: false, idempotent: true, destructive: false);

	        // Full autonomy: same surface as add-on JSON-RPC (FastPath + UI chrome).
	        Add("list_spz_commands", "List all spz.cmd / spz.ui methods",
	            "Returns the full Addon_SocketServer API catalogue (camera, mesh, workflow, paint, project, export, UI). Use with spz_cmd.",
	            null, Tool_ListSpzCommands,
	            readOnly: true, idempotent: true);

	        Add("spz_cmd", "Call any spz.cmd or spz.ui method",
	            "Dispatches through Addon_SocketServer.ProcessRequestDirect (same as TCP JSON-RPC on :5555). Pass method (e.g. spz.cmd.set_camera_pos) and params object. Prefer this for full autonomy; curated tools remain for common Gen Art loops.",
	            new List<AgentParamDesc>{
	                new AgentParamDesc("method", "string", true, "e.g. spz.cmd.select_all_meshes, spz.cmd.set_workflow_mode, spz.ui.get_theme."),
	                new AgentParamDesc("params", "object", false, "JSON-RPC params object for that method."),
	            },
	            Tool_SpzCmd,
	            readOnly: false, idempotent: false, destructive: true);

	        Add("get_generation_status", "Generation / can-gen status",
	            "Whether Gen Art/BG can run, if a generation is in flight, kind, cooldown, Klein bypass flags.",
	            null, Tool_GetGenerationStatus,
	            readOnly: true, idempotent: true);

	        Add("stop_generation", "Interrupt current generation",
	            "Same as pressing Stop / cancel on Gen Art.",
	            null, Tool_StopGeneration,
	            readOnly: false, idempotent: true, destructive: true);

	        Add("focus_camera", "Focus view camera on selection",
	            "F-key equivalent: frame selected meshes in a view camera.",
	            new List<AgentParamDesc>{
	                new AgentParamDesc("camera_index", "number", false, "View camera index. Default: current view camera."),
	            },
	            Tool_FocusCamera,
	            readOnly: false, idempotent: true, destructive: false);

	        Add("show_status", "Show viewport status text",
	            "HUD message in the main viewport (agent feedback).",
	            new List<AgentParamDesc>{
	                new AgentParamDesc("text", "string", true, "Message to show."),
	                new AgentParamDesc("duration", "number", false, "Seconds visible. Default 3."),
	            },
	            Tool_ShowStatus,
	            readOnly: false, idempotent: true, destructive: false);
	    }


	    static void Add(string name, string title, string description, List<AgentParamDesc> prms,
	                    AgentToolHandler handler,
	                    bool readOnly = false, bool idempotent = false,
	                    bool destructive = false, bool returnsImage = false){
	        var desc = new AgentToolDesc{
	            name = name,
	            title = title,
	            description = description,
	            returnsImage = returnsImage,
	            readOnly = readOnly,
	            destructive = destructive,
	            idempotent = idempotent,
	            prms = prms ?? new List<AgentParamDesc>()
	        };
	        _tools[name] = new AgentTool(desc, handler);
	    }


	    // ---------------- tools ----------------

	    static void Tool_Describe(JObject prms, Action<object> ok, Action<string> fail){
	        var catalogue = new List<AgentToolDesc>();
	        foreach (var kv in _tools){ catalogue.Add(kv.Value.desc); }
	        catalogue.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

	        ok(new Dictionary<string, object>{
	            { "protocol_version", SPZ_Agent_Protocol.PROTOCOL_VERSION },
	            { "app",              SPZ_Agent_Protocol.APP_NAME },
	            { "app_version",      SP_Version.currVersion },
	            { "tools",            catalogue },
	        });
	    }


	    static void Tool_GetAppState(JObject prms, Action<object> ok, Action<string> fail){
	        var state = new Dictionary<string, object>{
	            { "app_version", SP_Version.currVersion },
	            { "sd_connected",  Connection_MGR.is_sd_connected },
	            { "gen3d_connected", Connection_MGR.is_3d_connected },
	            { "sd_url",    Connection_MGR.A1111_IP_AND_PORT },
	            { "gen3d_url", Connection_MGR.GEN3D_URL },
	        };

	        var models = ModelsHandler_3D.instance;
	        if (models == null){
	            state["model_loaded"] = false;
	            state["note"] = "ModelsHandler_3D not ready yet (scenes still loading).";
	        }else{
	            state["model_loaded"]   = models.hasModelRootGO;
	            state["model_name"]     = models.currModelRootGO_name();
	            state["is_importing"]   = models._isImportingModel;
	            state["mesh_count"]     = models.meshes?.Count ?? 0;
	            state["selected_count"] = models.selectedMeshes?.Count ?? 0;
	            state["udim_count"]     = models._allKnownUdims?.Count ?? 0;
	        }

	        var hub = StableDiffusion_Hub.instance;
	        state["is_generating"] = hub != null && hub._generating;

	        try {
	            var sd = CollectSdGenSettings();
	            if (sd != null){
	                foreach (var kv in sd){ state[kv.Key] = kv.Value; }
	            }
	        } catch { /* SD panel may still be loading */ }

	        ok(state);
	    }


	    // Screenshot_MGR calls StopAllCoroutines() when it starts a capture, so a second
	    // request would silently kill the first one's callback and leave that command
	    // hanging until it times out. Serialise here instead.
	    static bool _screenshotInFlight = false;

	    static void Tool_Screenshot(JObject prms, Action<object> ok, Action<string> fail){
	        var mgr = Screenshot_MGR.instance;
	        if (mgr == null){ fail("Screenshot_MGR is not ready yet (scenes still loading)."); return; }
	        if (_screenshotInFlight){ fail("A screenshot is already in progress; retry shortly."); return; }

	        float minX = Mathf.Clamp01(ReadFloat(prms, "min_x", 0f));
	        float minY = Mathf.Clamp01(ReadFloat(prms, "min_y", 0f));
	        float maxX = Mathf.Clamp01(ReadFloat(prms, "max_x", 1f));
	        float maxY = Mathf.Clamp01(ReadFloat(prms, "max_y", 1f));
	        if (maxX <= minX || maxY <= minY){ fail("Empty region: max_x/max_y must be greater than min_x/min_y."); return; }

	        _screenshotInFlight = true;
	        try{
	            mgr.ScreenshotViewport_viaScript(new Vector2(minX, minY), new Vector2(maxX, maxY),
	                (min, max, tex) => {
	                    _screenshotInFlight = false;
	                    if (tex == null){ fail("Capture returned no texture."); return; }
	                    try{
	                        byte[] png = tex.EncodeToPNG();
	                        if (png == null){ fail("Could not encode the capture to PNG."); return; }
	                        ok(new Dictionary<string, object>{
	                            { "image_png_base64", Convert.ToBase64String(png) },
	                            { "width",  tex.width },
	                            { "height", tex.height },
	                        });
	                    }catch (Exception ex){
	                        fail($"{ex.GetType().Name} while encoding the capture: {ex.Message}");
	                    }finally{
	                        // The callback owns this texture ("plzDeleteLater").
	                        UnityEngine.Object.Destroy(tex);
	                    }
	                });
	        }catch (Exception ex){
	            _screenshotInFlight = false;
	            fail($"{ex.GetType().Name}: {ex.Message}");
	        }
	    }


	    static void Tool_ListGenerations(JObject prms, Action<object> ok, Action<string> fail){
	        var archive = GenData2D_Archive.instance;
	        if (archive == null){ fail("GenData2D_Archive is not ready yet (scenes still loading)."); return; }

	        var byKind = new Dictionary<string, int>();
	        foreach (GenerationData_Kind kind in Enum.GetValues(typeof(GenerationData_Kind))){
	            var found = archive.FindAll_GenData_ofKind(kind);
	            byKind[kind.ToString()] = found?.Count ?? 0;
	        }

	        Guid latest = archive.latestGeneration_GUID;
	        ok(new Dictionary<string, object>{
	            { "count_by_kind", byKind },
	            { "latest_guid", latest == default ? null : latest.ToString() },
	        });
	    }


	    static void Tool_ListEvents(JObject prms, Action<object> ok, Action<string> fail){
	        string filter = prms?.Value<string>("filter");
	        var listed = new List<object>();

	        foreach (string id in StaticEvents.GetRegisteredIds()){
	            if (string.IsNullOrEmpty(filter) == false &&
	                id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0){ continue; }

	            Type[] types = StaticEvents.GetParameterTypes(id);
	            var names = new List<string>();
	            if (types != null){ foreach (Type t in types){ names.Add(t.Name); } }
	            listed.Add(new Dictionary<string, object>{
	                { "id", id },
	                { "param_types", names },
	            });
	        }
	        ok(new Dictionary<string, object>{ { "events", listed }, { "count", listed.Count } });
	    }


	    static void Tool_InvokeEvent(JObject prms, Action<object> ok, Action<string> fail){
	        string id = prms?.Value<string>("id");
	        if (string.IsNullOrEmpty(id)){ fail("Missing required parameter 'id'."); return; }

	        var args = new List<object>();
	        if (prms["args"] is JArray arr){
	            foreach (JToken tok in arr){
	                if (tok.Type == JTokenType.Null){ args.Add(null); continue; }
	                // Only scalars can be mapped onto an Action<...> parameter.
	                if (tok is JValue val){ args.Add(val.Value); continue; }
	                fail($"Argument of type '{tok.Type}' is not supported; pass strings, numbers or booleans.");
	                return;
	            }
	        }

	        if (StaticEvents.TryInvokeDynamic(id, args.ToArray(), out string error) == false){
	            fail(error);
	            return;
	        }
	        ok(new Dictionary<string, object>{ { "invoked", id } });
	    }


	    static void Tool_GetSdGenSettings(JObject prms, Action<object> ok, Action<string> fail){
	        var sd = CollectSdGenSettings();
	        if (sd == null){ fail("SD_InputPanel_UI is not ready yet."); return; }
	        ok(sd);
	    }


	    static void Tool_ListSdOptions(JObject prms, Action<object> ok, Action<string> fail){
	        var cnModels = new List<string>();
	        var cn = SD_ControlNetsList_UI.instance;
	        if (cn?._models?.model_list != null){
	            foreach (string m in cn._models.model_list) cnModels.Add(m);
	        }
	        ok(new Dictionary<string, object>{
	            { "checkpoints", SD_Neural_Models.instance?.ListCheckpointNames() ?? new List<string>() },
	            { "samplers", SD_Samplers.instance?.ListSamplerNames() ?? new List<string>() },
	            { "schedulers", SD_Scheduler.instance?.ListSchedulerNames() ?? new List<string>() },
	            { "vaes", SD_VAE.instance?.ListVAENames() ?? new List<string>() },
	            { "controlnet_models", cnModels },
	            { "controlnet_unit_count", cn != null ? cn.numTotalUnitsExisting() : 0 },
	        });
	    }


	    static void Tool_SetSdGenSettings(JObject prms, Action<object> ok, Action<string> fail){
	        if (prms == null){ fail("Missing parameters."); return; }
	        var applied = new Dictionary<string, object>();
	        var errors = new List<string>();

	        ApplyNamedSelect(prms, "checkpoint", applied, errors,
	            () => SD_Neural_Models.instance,
	            (SD_Neural_Models m, string n, out string r, out string e) => m.TrySelectModelByName(n, out r, out e));
	        ApplyNamedSelect(prms, "vae", applied, errors,
	            () => SD_VAE.instance,
	            (SD_VAE m, string n, out string r, out string e) => m.TrySelectVAEByName(n, out r, out e));
	        ApplyNamedSelect(prms, "sampler", applied, errors,
	            () => SD_Samplers.instance,
	            (SD_Samplers m, string n, out string r, out string e) => m.TrySelectSamplerByName(n, out r, out e));
	        ApplyNamedSelect(prms, "scheduler", applied, errors,
	            () => SD_Scheduler.instance,
	            (SD_Scheduler m, string n, out string r, out string e) => m.TrySelectSchedulerByName(n, out r, out e));

	        var panel = SD_InputPanel_UI.instance;
	        if (panel == null){ errors.Add("SD_InputPanel_UI not ready"); }
	        else {
	            if (HasNum(prms, "steps")){
	                float steps = prms["steps"].Value<float>();
	                panel.sampleSteps_slider?.SetSliderValue(steps, invokeCallback: true);
	                applied["steps"] = steps;
	            }
	            if (HasNum(prms, "cfg_scale")){
	                float cfg = prms["cfg_scale"].Value<float>();
	                panel.CFG_scale_slider?.SetSliderValue(cfg, invokeCallback: true);
	                applied["cfg_scale"] = cfg;
	            }
	            if (HasNum(prms, "seed")){
	                int seed = Mathf.RoundToInt(prms["seed"].Value<float>());
	                panel.seed_intField?.SetValue(seed.ToString());
	                applied["seed"] = seed;
	            }
	            bool hasW = HasNum(prms, "width");
	            bool hasH = HasNum(prms, "height");
	            if (hasW || hasH){
	                int w = hasW ? Mathf.RoundToInt(prms["width"].Value<float>()) : panel.width;
	                int h = hasH ? Mathf.RoundToInt(prms["height"].Value<float>()) : panel.height;
	                panel.SetWidthHeight(w, h);
	                applied["width"] = w;
	                applied["height"] = h;
	            }
	        }

	        var prompts = StableDiffusion_Prompts_UI.instance;
	        if (prms["positive_prompt"] != null && prms["positive_prompt"].Type != JTokenType.Null){
	            if (prompts == null) errors.Add("StableDiffusion_Prompts_UI not ready");
	            else {
	                string p = prms.Value<string>("positive_prompt") ?? "";
	                prompts.SetPositivePrompt(p);
	                applied["positive_prompt"] = p;
	            }
	        }
	        if (prms["negative_prompt"] != null && prms["negative_prompt"].Type != JTokenType.Null){
	            if (prompts == null) errors.Add("StableDiffusion_Prompts_UI not ready");
	            else {
	                string n = prms.Value<string>("negative_prompt") ?? "";
	                prompts.SetNegativePrompt(n);
	                applied["negative_prompt"] = n;
	            }
	        }

	        if (HasBool(prms, "clear_controlnet_models") && prms["clear_controlnet_models"].Value<bool>()){
	            var cn = SD_ControlNetsList_UI.instance;
	            if (cn == null){ errors.Add("SD_ControlNetsList_UI not ready"); }
	            else {
	                applied["controlnet_models_cleared"] = cn.ClearAllUnitModelsToNone();
	            }
	        }

	        if (errors.Count > 0 && applied.Count == 0){
	            fail(string.Join("; ", errors));
	            return;
	        }

	        var result = CollectSdGenSettings() ?? new Dictionary<string, object>();
	        result["applied"] = applied;
	        if (errors.Count > 0) result["warnings"] = errors;
	        ok(result);
	    }


	    static void Tool_SetControlNetUnit(JObject prms, Action<object> ok, Action<string> fail){
	        if (prms == null || !HasNum(prms, "unit")){ fail("Missing required parameter 'unit'."); return; }
	        int unitIx = Mathf.RoundToInt(prms["unit"].Value<float>());
	        var cn = SD_ControlNetsList_UI.instance;
	        if (cn == null){ fail("SD_ControlNetsList_UI not ready."); return; }
	        var unit = cn.GetUnit(unitIx);
	        if (unit == null){ fail($"ControlNet unit {unitIx} not found (count={cn.numTotalUnitsExisting()})."); return; }

	        var applied = new Dictionary<string, object>{ { "unit", unitIx } };
	        string model = prms.Value<string>("model");
	        if (!string.IsNullOrEmpty(model)){
	            if (unit.dropdowns == null){
	                fail("ControlNet model dropdowns not ready");
	                return;
	            }
	            if (!unit.dropdowns.TrySelectModelByName(model, out string resolved, out string err)){
	                fail(err ?? "ControlNet model select failed");
	                return;
	            }
	            applied["model"] = resolved;
	        }
	        if (HasNum(prms, "weight")){
	            float w = prms["weight"].Value<float>();
	            unit.SetControlWeight(w);
	            applied["weight"] = w;
	        }
	        string whatToSend = prms.Value<string>("what_to_send");
	        if (!string.IsNullOrEmpty(whatToSend)){
	            if (!Enum.TryParse(typeof(WhatImageToSend_CTRLNET), whatToSend, ignoreCase: true, out object parsed)
	                || parsed == null){
	                fail("what_to_send must be None|Depth|Normals|VertexColors|ContentCam|CustomFile");
	                return;
	            }
	            var want = (WhatImageToSend_CTRLNET)parsed;
	            if (!unit.TrySetWhatImageToSend(want, allowOpenFileDialog: false)){
	                fail("Failed to set what_to_send=" + whatToSend);
	                return;
	            }
	            applied["what_to_send"] = unit._whatImageToSend.ToString();
	            if (want == WhatImageToSend_CTRLNET.CustomFile
	                && unit.isActivated
	                && unit.is_currModel_none
	                && !unit.IsKleinImg2ImgInitSource()){
	                fail("what_to_send=CustomFile but no image is loaded on unit " + unitIx
	                     + ". Load a CustomFile or use ContentCam.");
	                return;
	            }
	        }
	        if (HasBool(prms, "activated")){
	            bool wantOpen = prms["activated"].Value<bool>();
	            if (!unit.TrySetActivated(wantOpen)){
	                fail("Failed to set activated=" + wantOpen);
	                return;
	            }
	            applied["activated"] = unit.isActivated;
	        }
	        ok(new Dictionary<string, object>{
	            { "applied", applied },
	            { "model", unit.currModelName() },
	            { "weight", unit.GetControlWeight() },
	            { "what_to_send", unit._whatImageToSend.ToString() },
	            { "activated", unit.isActivated },
	            { "klein_img2img_eligible", unit.IsKleinImg2ImgInitSource() },
	        });
	    }


	    static void Tool_Generate(JObject prms, Action<object> ok, Action<string> fail){
	        var hub = StableDiffusion_Hub.instance;
	        if (hub == null){ fail("StableDiffusion_Hub not ready."); return; }
	        bool backgrounds = HasBool(prms, "backgrounds") && prms["backgrounds"].Value<bool>();
	        hub.isCanGenerate(out bool canArt, out bool canBg);
	        if (backgrounds && !canBg){ fail("Cannot Gen BG right now (cooldown, disconnected, or busy)."); return; }
	        if (!backgrounds && !canArt){ fail("Cannot Gen Art right now (need depth/normals CN, or Klein bypass; or busy/disconnected)."); return; }
	        // Match UI DenyWithMessage gates the agent can_* snapshot misses (empty CustomFile, CN download, import…).
	        if (hub.DenyWithMessage_ifCantGenerate(allow_without_controlnets: backgrounds)){
	            fail("Generation denied (see viewport status: ControlNet/CustomFile/import/download/busy).");
	            return;
	        }
	        hub.Generate(backgrounds);
	        // Confirm the request was actually POSTed — empty init / start failures abort asynchronously.
	        if (Coroutines_MGR.instance == null){
	            ok(new Dictionary<string, object>{
	                { "started", backgrounds ? "gen_bg" : "gen_art" },
	                { "is_generating", hub._generating },
	                { "confirmed", false },
	                { "warning", "Coroutines_MGR missing; could not confirm request was sent." },
	            });
	            return;
	        }
	        Coroutines_MGR.instance.StartCoroutine(ConfirmGenerateStarted_crtn(hub, backgrounds, ok, fail));
	    }


	    static IEnumerator ConfirmGenerateStarted_crtn(
	        StableDiffusion_Hub hub, bool backgrounds, Action<object> ok, Action<string> fail){
	        bool sawBusy = false;
	        for (int i = 0; i < 120; i++){
	            yield return null;
	            if (hub == null){
	                fail("StableDiffusion_Hub disappeared while confirming generate.");
	                yield break;
	            }
	            if (hub._generating || hub._finalPreparations_beforeGen)
	                sawBusy = true;
	            // Finalize_GenerationRequest clears prep and leaves _generating true until Neo finishes.
	            if (hub._generating && !hub._finalPreparations_beforeGen){
	                ok(new Dictionary<string, object>{
	                    { "started", backgrounds ? "gen_bg" : "gen_art" },
	                    { "is_generating", true },
	                    { "confirmed", true },
	                    { "generating_what", hub._isGeneratingWhat.ToString() },
	                });
	                yield break;
	            }
	            // Only treat as abort after we actually observed prep/busy — avoids racing Start_GenerationRequest.
	            if (sawBusy && !hub._generating && !hub._finalPreparations_beforeGen){
	                fail("Generation aborted before the request was sent (missing init image or start failed).");
	                yield break;
	            }
	            // DenyWithMessage / no coroutine: never enters prep — fail soon instead of waiting ~120 frames.
	            if (!sawBusy && i >= 12){
	                fail("Generation did not start (denied or never entered prep).");
	                yield break;
	            }
	        }
	        if (hub._generating){
	            ok(new Dictionary<string, object>{
	                { "started", backgrounds ? "gen_bg" : "gen_art" },
	                { "is_generating", true },
	                { "confirmed", true },
	                { "generating_what", hub._isGeneratingWhat.ToString() },
	                { "note", "still_preparing_after_wait" },
	            });
	        } else {
	            fail(sawBusy
	                ? "Generation aborted before the request was sent (missing init image or start failed)."
	                : "Generation did not start (denied or never entered prep).");
	        }
	    }


	    static void Tool_PrepareFluxKleinTest(JObject prms, Action<object> ok, Action<string> fail){
	        string ckpt = prms?.Value<string>("checkpoint");
	        if (string.IsNullOrEmpty(ckpt)) ckpt = "flux-2-klein-4b";
	        int w = 512, h = 512;
	        if (prms != null){
	            if (HasNum(prms, "width")) w = Mathf.RoundToInt(prms["width"].Value<float>());
	            if (HasNum(prms, "height")) h = Mathf.RoundToInt(prms["height"].Value<float>());
	        }
	        Tool_SetSdGenSettings(new JObject{
	            ["checkpoint"] = ckpt,
	            ["sampler"] = "Euler",
	            ["steps"] = 4,
	            ["cfg_scale"] = 1.0,
	            ["width"] = w,
	            ["height"] = h,
	            ["clear_controlnet_models"] = true,
	        }, result => {
	            // Do not force ContentCam — CustomFile is the preferred Klein img2img ref when present.
	            // Only arm ContentCam as fallback when no valid CustomFile co-opt is available.
	            var cn = SD_ControlNetsList_UI.instance;
	            bool armedContentCam = false;
	            bool hasCustom = cn != null && cn.TryPeekKleinImg2ImgInitSource(out _, out string src)
	                && string.Equals(src, "CustomFile", System.StringComparison.Ordinal);
	            if (!hasCustom){
	                var unit = cn != null ? cn.GetUnit(0) : null;
	                if (unit != null){
	                    armedContentCam = unit.TrySetWhatImageToSend(
	                        WhatImageToSend_CTRLNET.ContentCam, allowOpenFileDialog: false);
	                    if (armedContentCam) unit.TrySetActivated(true);
	                }
	            }
	            if (result is Dictionary<string, object> dict){
	                dict["klein_customfile_preferred"] = hasCustom;
	                dict["klein_contentcam_armed"] = armedContentCam;
	                if (cn != null && cn.TryPeekKleinImg2ImgInitSource(out _, out string label))
	                    dict["klein_init_source"] = label;
	            }
	            ok(result);
	        }, fail);
	    }


	    static void Tool_ListSpzCommands(JObject prms, Action<object> ok, Action<string> fail){
	        DispatchSpzCmd("spz.cmd.get_api_capabilities", new JObject(), ok, fail);
	    }


	    static void Tool_SpzCmd(JObject prms, Action<object> ok, Action<string> fail){
	        string method = prms?.Value<string>("method");
	        if (string.IsNullOrEmpty(method)){ fail("Missing required parameter 'method'."); return; }
	        JObject callParams = prms["params"] as JObject;
	        if (callParams == null && prms["params"] != null && prms["params"].Type == JTokenType.Object)
	            callParams = (JObject)prms["params"];
	        callParams ??= new JObject();
	        // Allow flat params alongside method (convenience): copy sibling keys except method/params.
	        if (prms != null){
	            foreach (var prop in prms.Properties()){
	                if (prop.Name == "method" || prop.Name == "params") continue;
	                if (callParams[prop.Name] == null)
	                    callParams[prop.Name] = prop.Value;
	            }
	        }
	        DispatchSpzCmd(method, callParams, ok, fail);
	    }


	    static void Tool_GetGenerationStatus(JObject prms, Action<object> ok, Action<string> fail){
	        var hub = StableDiffusion_Hub.instance;
	        bool canArt = false, canBg = false;
	        hub?.isCanGenerate(out canArt, out canBg);
	        float cooldownLeft = 0f;
	        if (hub != null && Time.unscaledTime < hub._generationCooldownUntil)
	            cooldownLeft = hub._generationCooldownUntil - Time.unscaledTime;
	        string ckpt = SD_InputPanel_UI.instance?.models?.selectedModel_name ?? "";
	        ok(new Dictionary<string, object>{
	            { "is_generating", hub != null && hub._generating },
	            { "generating_what", hub != null ? hub._isGeneratingWhat.ToString() : "nothing" },
	            { "final_preparations", hub != null && hub._finalPreparations_beforeGen },
	            { "cooldown_seconds_left", cooldownLeft },
	            { "can_gen_art", canArt },
	            { "can_gen_bg", canBg },
	            { "sd_connected", Connection_MGR.is_sd_connected },
	            { "sd_checkpoint", ckpt },
	            { "klein_checkpoint", SD_OptionsPacket.CheckpointNeedsKleinModules(ckpt) },
	            { "klein_gen_art_bypass", StableDiffusion_Hub.IsActiveCheckpointKlein() },
	            { "is_importing", ModelsHandler_3D.instance != null && ModelsHandler_3D.instance._isImportingModel },
	            { "is_project_busy", Save_MGR.instance != null && Save_MGR.instance._isSaving },
	        });
	    }


	    static void Tool_StopGeneration(JObject prms, Action<object> ok, Action<string> fail){
	        var hub = StableDiffusion_Hub.instance;
	        if (hub == null){ fail("StableDiffusion_Hub not ready."); return; }
	        bool was = hub._generating;
	        hub.OnStopGenerate_Button();
	        ok(new Dictionary<string, object>{
	            { "was_generating", was },
	            { "is_generating", hub._generating },
	        });
	    }


	    static void Tool_FocusCamera(JObject prms, Action<object> ok, Action<string> fail){
	        var cams = UserCameras_MGR.instance;
	        if (cams == null){ fail("UserCameras_MGR not ready."); return; }
	        int count = cams.GetViewCameraCount();
	        int ix;
	        if (HasNum(prms, "camera_index"))
	            ix = Mathf.RoundToInt(prms["camera_index"].Value<float>());
	        else
	            ix = cams.CurrentViewCameraIndex;
	        if (count <= 0 || ix < 0 || ix >= count){
	            fail($"camera_index {ix} out of range (0..{Mathf.Max(0, count - 1)}).");
	            return;
	        }
	        cams.FocusViewCamera(ix);
	        ok(new Dictionary<string, object>{ { "focused_camera_index", ix } });
	    }


	    static void Tool_ShowStatus(JObject prms, Action<object> ok, Action<string> fail){
	        string text = prms?.Value<string>("text");
	        if (string.IsNullOrEmpty(text)){ fail("Missing required parameter 'text'."); return; }
	        float dur = HasNum(prms, "duration") ? prms["duration"].Value<float>() : 3f;
	        if (Viewport_StatusText.instance == null){ fail("Viewport_StatusText not ready."); return; }
	        Viewport_StatusText.instance.ShowStatusText(text, false, dur, false);
	        ok(new Dictionary<string, object>{ { "shown", text }, { "duration", dur } });
	    }


	    // ---------------- helpers ----------------

	    static void DispatchSpzCmd(string method, JObject callParams, Action<object> ok, Action<string> fail){
	        var server = Addon_SocketServer.instance;
	        if (server == null){ fail("Addon_SocketServer not ready."); return; }
	        try {
	            var request = new JObject{
	                ["method"] = method,
	                ["params"] = callParams ?? new JObject(),
	            };
	            JObject envelope = server.ProcessRequestDirect(request);
	            if (envelope == null){ fail("ProcessRequestDirect returned null."); return; }
	            if (envelope["error"] != null){
	                string msg = envelope["error"]?["message"]?.ToString()
	                             ?? envelope["error"]?.ToString()
	                             ?? "spz_cmd error";
	                fail(msg);
	                return;
	            }
	            object result = envelope["result"];
	            ok(result != null ? result : new Dictionary<string, object>{ { "success", true } });
	        } catch (Exception ex){
	            fail($"{ex.GetType().Name}: {ex.Message}");
	        }
	    }

	    delegate bool NamedSelectFn<T>(T inst, string name, out string resolved, out string error);

	    static void ApplyNamedSelect<T>(JObject prms, string key, Dictionary<string, object> applied, List<string> errors,
	                                    System.Func<T> get, NamedSelectFn<T> select) where T : class {
	        string name = prms.Value<string>(key);
	        if (string.IsNullOrEmpty(name)) return;
	        T inst = get();
	        if (inst == null){ errors.Add(typeof(T).Name + " not ready"); return; }
	        if (!select(inst, name, out string resolved, out string err)){
	            errors.Add(err ?? (key + " select failed"));
	            return;
	        }
	        applied[key] = resolved;
	    }

	    static bool HasNum(JObject prms, string key){
	        return prms != null && prms[key] != null && prms[key].Type != JTokenType.Null;
	    }

	    static bool HasBool(JObject prms, string key){
	        return HasNum(prms, key); // JSON bool tokens are non-null here
	    }

	    static Dictionary<string, object> CollectSdGenSettings(){
	        var panel = SD_InputPanel_UI.instance;
	        if (panel == null) return null;

	        var cnModels = new List<string>();
	        var cnList = SD_ControlNetsList_UI.instance;
	        if (cnList != null){
	            for (int i = 0; i < cnList.numTotalUnitsExisting(); i++){
	                var u = cnList.GetUnit(i);
	                if (u == null) continue;
	                cnModels.Add(u.currModelName() ?? "None");
	            }
	        }

	        string ckpt = panel.models != null ? panel.models.selectedModel_name : "";
	        var prompts = StableDiffusion_Prompts_UI.instance;
	        bool canArt = false, canBg = false;
	        StableDiffusion_Hub.instance?.isCanGenerate(out canArt, out canBg);

	        return new Dictionary<string, object>{
	            { "sd_checkpoint", ckpt },
	            { "sd_vae", panel.sd_vae != null ? panel.sd_vae.selectedVAE_name : "" },
	            { "sd_sampler", panel.samplers != null ? panel.samplers.selectedSampler_name : "" },
	            { "sd_scheduler", panel.scheduler != null ? panel.scheduler.selectedScheduler_name : "" },
	            { "sd_steps", panel.sampleSteps_slider != null ? panel.sampleSteps_slider.value : 0f },
	            { "sd_cfg_scale", panel.CFG_scale_slider != null ? panel.CFG_scale_slider.value : 0f },
	            { "sd_seed", panel.seed_intField != null ? panel.seed_intField.recentVal : 0 },
	            { "sd_width", panel.width },
	            { "sd_height", panel.height },
	            { "positive_prompt", prompts != null ? prompts.positivePrompt : "" },
	            { "negative_prompt", prompts != null ? prompts.negativePrompt : "" },
	            { "controlnet_models", cnModels },
	            { "can_gen_art", canArt },
	            { "can_gen_bg", canBg },
	            { "klein_checkpoint", SD_OptionsPacket.CheckpointNeedsKleinModules(ckpt) },
	            { "klein_gen_art_bypass", StableDiffusion_Hub.IsActiveCheckpointKlein() },
	            { "klein_img2img_from_cn",
	                StableDiffusion_Hub.IsActiveCheckpointKlein()
	                && SD_ControlNetsList_UI.instance != null
	                && SD_ControlNetsList_UI.instance.HasKleinImg2ImgInitSource() },
	        };
	    }

	    static float ReadFloat(JObject prms, string key, float fallback){
	        if (prms == null){ return fallback; }
	        JToken tok = prms[key];
	        if (tok == null || tok.Type == JTokenType.Null){ return fallback; }
	        try{ return tok.Value<float>(); }catch (Exception){ return fallback; }
	    }
	}
}//end namespace

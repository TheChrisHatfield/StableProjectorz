using System.Collections.Generic;
using UnityEngine;

namespace spz {

	/// <summary>
	/// Neo Flux.2 Klein structure channel:
	/// ImageStitch refs = [mesh depth, ContentCam/style] + RefControl Depth LoRA.
	/// Depth alone as a stitch ref makes Klein reproduce a depth plate — LoRA + second
	/// ref teaches depth-as-structure (see thedeoxen/refcontrol-FLUX.2-klein-4B-...).
	/// Never Fun-Union CN; never Depth as img2img init_images.
	/// </summary>
	public static class SD_KleinStructureChannel {
	    public const string AlwaysOnScriptName = "imagestitch integrated";
	    public const string GeometrySourceId = "mesh_depth_content_frustum";
	    /// <summary>Neo models/Lora file stem (no extension).</summary>
	    public const string RefControlLoraName = "flux2_klein_4b_refcontrol_depth";
	    public const string RefControlTrigger = "refcontrol";
	    public const float RefControlLoraWeight = 0.9f;
	    const int DefaultMaxSide = 1024;

	    /// <summary>
	    /// Cheap readiness: depth contrast RT already allocated. Safe for UI polls (isCanGenerate).
	    /// Does not render — use <see cref="EnsureDepthRendered"/> / <see cref="CanCaptureMeshDepth"/> at payload time.
	    /// </summary>
	    public static bool HasMeshDepthRt(){
	        var cams = UserCameras_MGR.instance != null ? UserCameras_MGR.instance.camTextures : null;
	        return cams != null && cams._SD_depthCam_RT_R32_contrast != null;
	    }

	    /// <summary>
	    /// True when mesh depth can be captured. Forces one depth render so RT is allocated/fresh.
	    /// Checks RT while the depth lock is still held — unlocking can Destroy the RT immediately.
	    /// Do not call from per-frame Gen Art interactable polls — use <see cref="HasMeshDepthRt"/> there.
	    /// </summary>
	    public static bool CanCaptureMeshDepth(){
	        object lockOwner = typeof(SD_KleinStructureChannel);
	        UserCameras_Permissions.LockOrUnlock_ByType(CameraTexType.DepthUserCamera, lockOwner, isLock: true);
	        try {
	            Update_callbacks_MGR.content_depthRender?.Invoke();
	            return HasMeshDepthRt();
	        } finally {
	            UserCameras_Permissions.LockOrUnlock_ByType(CameraTexType.DepthUserCamera, lockOwner, isLock: false);
	        }
	    }

	    /// <summary>
	    /// Lock depth cams, allocate/render, return CPU RGBA copy. Safe after unlock (copy owns pixels).
	    /// </summary>
	    public static bool TryCaptureMeshDepthDisposable(out Texture2D depthRgba, out string failReason){
	        depthRgba = null;
	        failReason = "";
	        object lockOwner = typeof(SD_KleinStructureChannel);
	        UserCameras_Permissions.LockOrUnlock_ByType(CameraTexType.DepthUserCamera, lockOwner, isLock: true);
	        try {
	            Update_callbacks_MGR.content_depthRender?.Invoke();
	            var cams = UserCameras_MGR.instance != null ? UserCameras_MGR.instance.camTextures : null;
	            if (cams == null || cams._SD_depthCam_RT_R32_contrast == null){
	                failReason = "depth_rt_missing";
	                return false;
	            }
	            depthRgba = cams.GetDisposable_DepthTexture();
	            if (depthRgba == null){
	                failReason = "depth_capture_null";
	                return false;
	            }
	            return true;
	        } finally {
	            UserCameras_Permissions.LockOrUnlock_ByType(CameraTexType.DepthUserCamera, lockOwner, isLock: false);
	        }
	    }

	    public static void EnsureDepthRendered(){
	        object lockOwner = typeof(SD_KleinStructureChannel);
	        UserCameras_Permissions.LockOrUnlock_ByType(CameraTexType.DepthUserCamera, lockOwner, isLock: true);
	        try {
	            Update_callbacks_MGR.content_depthRender?.Invoke();
	        } finally {
	            UserCameras_Permissions.LockOrUnlock_ByType(CameraTexType.DepthUserCamera, lockOwner, isLock: false);
	        }
	    }

	    /// <summary>
	    /// Inject RefControl LoRA + trigger so Neo treats ImageStitch depth as structure, not content.
	    /// </summary>
	    public static void AppendRefControlToPrompt(ref string positive){
	        if (positive == null) positive = "";
	        string loraTag = $"<lora:{RefControlLoraName}:{RefControlLoraWeight:0.##}>";
	        bool hasLora = positive.IndexOf("<lora:" + RefControlLoraName, System.StringComparison.OrdinalIgnoreCase) >= 0;
	        bool hasTrigger = positive.IndexOf(RefControlTrigger, System.StringComparison.OrdinalIgnoreCase) >= 0;
	        if (!hasLora && !hasTrigger)
	            positive = $"{loraTag} {RefControlTrigger}, {positive}".Trim();
	        else if (!hasLora)
	            positive = $"{loraTag} {positive}".Trim();
	        else if (!hasTrigger)
	            positive = $"{RefControlTrigger}, {positive}".Trim();
	        KleinStructureTrace.Set("refcontrol_lora", RefControlLoraName);
	        KleinStructureTrace.Set("refcontrol_in_prompt", true);
	    }

	    /// <summary>
	    /// Capture mesh depth (+ ContentCam/CustomFile style ref), attach ImageStitch alwayson.
	    /// Ref order: [depth, style] per RefControl Depth LoRA. Returns false if depth missing.
	    /// </summary>
	    public static bool TryAttachMeshDepthStructure(
	        Dictionary<string, AlwaysOn_Value> alwayson,
	        SD_GenRequestArgs_byproducts intermediates,
	        string neoEndpoint,
	        string pixelInitKind){
	        KleinStructureTrace.BeginRequest();
	        KleinStructureTrace.Set("geometry_source", GeometrySourceId);
	        KleinStructureTrace.Set("neo_endpoint", neoEndpoint ?? "");
	        KleinStructureTrace.Set("pixel_init_kind", pixelInitKind ?? "none");
	        KleinStructureTrace.Set("controlnet_alwayson", false);
	        KleinStructureTrace.Set("structure_channel", AlwaysOnScriptName);

	        if (alwayson == null){
	            KleinStructureTrace.Set("structure_attached", false);
	            KleinStructureTrace.Set("reject_reason", "no_alwayson_dict");
	            return false;
	        }

	        if (!TryCaptureMeshDepthDisposable(out Texture2D depth, out string failReason)){
	            KleinStructureTrace.Set("depth_rt_present", failReason != "depth_rt_missing");
	            KleinStructureTrace.Set("structure_attached", false);
	            KleinStructureTrace.Set("reject_reason", string.IsNullOrEmpty(failReason) ? "depth_capture_null" : failReason);
	            return false;
	        }

	        KleinStructureTrace.Set("depth_rt_present", true);
	        KleinStructureTrace.Set("depth_w", depth.width);
	        KleinStructureTrace.Set("depth_h", depth.height);

	        if (intermediates != null){
	            if (intermediates.depth_disposableTex != null)
	                Object.DestroyImmediate(intermediates.depth_disposableTex);
	            intermediates.depth_disposableTex = depth;
	        }

	        string depthB64 = TextureTools_SPZ.TextureToBase64(depth);
	        if (string.IsNullOrEmpty(depthB64)){
	            KleinStructureTrace.Set("structure_attached", false);
	            KleinStructureTrace.Set("reject_reason", "depth_encode_failed");
	            if (intermediates == null || !ReferenceEquals(intermediates.depth_disposableTex, depth))
	                Object.DestroyImmediate(depth);
	            else {
	                intermediates.depth_disposableTex = null;
	                Object.DestroyImmediate(depth);
	            }
	            return false;
	        }

	        // RefControl: image1 = depth (structure), image2 = RGB identity/style.
	        string styleB64 = null;
	        string styleKind = "none";
	        TryCaptureStyleRefBase64(intermediates, out styleB64, out styleKind);
	        KleinStructureTrace.Set("style_ref_kind", styleKind);

	        var refs = new List<string> { depthB64 };
	        if (!string.IsNullOrEmpty(styleB64))
	            refs.Add(styleB64);

	        if (intermediates == null)
	            Object.DestroyImmediate(depth);

	        alwayson[AlwaysOnScriptName] = ImageStitch_AlwaysOnArgs.FromReferenceBase64List(refs, DefaultMaxSide);
	        KleinStructureTrace.Set("structure_attached", true);
	        KleinStructureTrace.Set("structure_ref_count", refs.Count);
	        KleinStructureTrace.Set("reject_reason", "");
	        if (refs.Count < 2 && Viewport_StatusText.instance != null){
	            Viewport_StatusText.instance.ShowStatusText(
	                "Klein structure: depth only (no ContentCam/CustomFile style ref). RefControl works best with two refs.",
	                false, 4f, false);
	        }
	        return true;
	    }

	    /// <summary>
	    /// Prefer any loaded CustomFile bitmap (even if unit deactivated), else ContentCam RGB
	    /// as RefControl image2. Do not use IsKleinImg2ImgInitSource — prepare leaves CustomFile off.
	    /// </summary>
	    static bool TryCaptureStyleRefBase64(SD_GenRequestArgs_byproducts intermediates, out string b64, out string kind){
	        b64 = null;
	        kind = "none";
	        Texture2D style = null;
	        bool destroyStyle = false;
	        object contentLock = typeof(SD_KleinStructureChannel);
	        try {
	            if (SD_ControlNetsList_UI.instance != null
	                && SD_ControlNetsList_UI.instance.TryGetDisposableLoadedCustomFileBitmap(out style, out _)){
	                kind = "CustomFile";
	                destroyStyle = true;
	            } else if (intermediates != null && intermediates.usualView_disposableTexture != null){
	                // Reuse ContentCam already captured for img2img when present.
	                style = intermediates.usualView_disposableTexture;
	                kind = "ContentCam_reuse";
	                destroyStyle = false;
	            } else if (UserCameras_MGR.instance != null && UserCameras_MGR.instance.camTextures != null){
	                // Ensure content RT exists (same unlock-destroys pattern as depth — capture under lock).
	                UserCameras_Permissions.LockOrUnlock_ByType(CameraTexType.ContentUserCam, contentLock, isLock: true);
	                try {
	                    if (Objects_Renderer_MGR.instance != null)
	                        Objects_Renderer_MGR.instance.EnsureInpaintColorLayerAppliedForCapture();
	                    style = UserCameras_MGR.instance.camTextures.GetDisposable_ContentCamTexture();
	                } finally {
	                    UserCameras_Permissions.LockOrUnlock_ByType(CameraTexType.ContentUserCam, contentLock, isLock: false);
	                }
	                kind = style != null ? "ContentCam" : "none";
	                destroyStyle = style != null;
	            }
	            if (style == null) return false;
	            b64 = TextureTools_SPZ.TextureToBase64(style);
	            return !string.IsNullOrEmpty(b64);
	        } finally {
	            if (destroyStyle && style != null)
	                Object.DestroyImmediate(style);
	        }
	    }

	    /// <summary>
	    /// Mean absolute luma difference in [0,1]. Higher = more different from depth.
	    /// Returns -1 if compare impossible.
	    /// </summary>
	    public static float MeanAbsLumaDiff01(Texture2D a, Texture2D b){
	        if (a == null || b == null) return -1f;
	        int w = Mathf.Min(a.width, b.width, 64);
	        int h = Mathf.Min(a.height, b.height, 64);
	        if (w < 4 || h < 4) return -1f;

	        try {
	            Color[] ca = a.GetPixels();
	            Color[] cb = b.GetPixels();
	            if (ca == null || cb == null || ca.Length == 0 || cb.Length == 0) return -1f;

	            float sum = 0f;
	            int n = 0;
	            for (int y = 0; y < h; y++){
	                int ya = (y * (a.height - 1)) / Mathf.Max(1, h - 1);
	                int yb = (y * (b.height - 1)) / Mathf.Max(1, h - 1);
	                for (int x = 0; x < w; x++){
	                    int xa = (x * (a.width - 1)) / Mathf.Max(1, w - 1);
	                    int xb = (x * (b.width - 1)) / Mathf.Max(1, w - 1);
	                    Color pa = ca[ya * a.width + xa];
	                    Color pb = cb[yb * b.width + xb];
	                    float la = 0.299f * pa.r + 0.587f * pa.g + 0.114f * pa.b;
	                    float lb = 0.299f * pb.r + 0.587f * pb.g + 0.114f * pb.b;
	                    sum += Mathf.Abs(la - lb);
	                    n++;
	                }
	            }
	            return n > 0 ? sum / n : -1f;
	        } catch (System.Exception){
	            return -1f;
	        }
	    }

	    /// <summary>True when result looks like the depth plate (too similar).</summary>
	    public static bool LooksLikeDepthPlate(Texture2D result, Texture2D depthStructure, out float diff01){
	        diff01 = MeanAbsLumaDiff01(result, depthStructure);
	        if (diff01 < 0f) return false;
	        return diff01 < 0.08f;
	    }
	}

	/// <summary>Neo Gradio positional args for ImageStitch Integrated alwayson.</summary>
	public class ImageStitch_AlwaysOnArgs : AlwaysOn_Value {
	    public object[] args;

	    public override AlwaysOn_Value Clone(){
	        var clone = (ImageStitch_AlwaysOnArgs)MemberwiseClone();
	        clone.args = args != null ? (object[])args.Clone() : null;
	        return clone;
	    }

	    public static ImageStitch_AlwaysOnArgs FromReferenceBase64(string base64, int maxSide){
	        return FromReferenceBase64List(new List<string> { base64 }, maxSide);
	    }

	    public static ImageStitch_AlwaysOnArgs FromReferenceBase64List(IList<string> base64Refs, int maxSide){
	        var uris = new List<object>();
	        if (base64Refs != null){
	            for (int i = 0; i < base64Refs.Count; i++){
	                string base64 = base64Refs[i];
	                if (string.IsNullOrEmpty(base64)) continue;
	                if (!base64.StartsWith("data:image/", System.StringComparison.OrdinalIgnoreCase))
	                    base64 = "data:image/png;base64," + base64;
	                uris.Add(base64);
	            }
	        }
	        return new ImageStitch_AlwaysOnArgs {
	            args = new object[] {
	                true,
	                uris.ToArray(),
	                maxSide,
	            }
	        };
	    }
	}
}

using System.Collections.Generic;
using UnityEngine;

namespace spz {

	/// <summary>
	/// Neo Flux.2 Klein structure channel: mesh depth via alwayson "imagestitch integrated"
	/// (reference latents) — not ControlNet Fun-Union and never as img2img init_images.
	/// Geometry source: UserCameras content-frustum depth RT (actual 3D mesh).
	/// </summary>
	public static class SD_KleinStructureChannel {
	    public const string AlwaysOnScriptName = "imagestitch integrated";
	    public const string GeometrySourceId = "mesh_depth_content_frustum";
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
	    /// Does not allocate a CPU Texture2D (Deny/heal/prepare); attach uses TryCaptureMeshDepthDisposable.
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
	        // Prefer TryCaptureMeshDepthDisposable for attach — unlock can destroy the RT.
	        object lockOwner = typeof(SD_KleinStructureChannel);
	        UserCameras_Permissions.LockOrUnlock_ByType(CameraTexType.DepthUserCamera, lockOwner, isLock: true);
	        try {
	            Update_callbacks_MGR.content_depthRender?.Invoke();
	        } finally {
	            UserCameras_Permissions.LockOrUnlock_ByType(CameraTexType.DepthUserCamera, lockOwner, isLock: false);
	        }
	    }

	    /// <summary>
	    /// Capture mesh depth, store on intermediates, attach ImageStitch alwayson args.
	    /// Returns false if depth missing (fail closed for Klein Gen Art).
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

	        string b64 = TextureTools_SPZ.TextureToBase64(depth);
	        if (string.IsNullOrEmpty(b64)){
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

	        // Encode succeeded; if caller passed no intermediates, we still need to free the CPU copy.
	        if (intermediates == null)
	            Object.DestroyImmediate(depth);

	        var stitch = ImageStitch_AlwaysOnArgs.FromReferenceBase64(b64, DefaultMaxSide);
	        alwayson[AlwaysOnScriptName] = stitch;
	        KleinStructureTrace.Set("structure_attached", true);
	        KleinStructureTrace.Set("reject_reason", "");
	        return true;
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
	            // Sample a coarse grid; GetPixels throws if either tex is non-readable.
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
	        // Depth plate copies stay very close; albedo Gen Art should diverge more.
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
	        // extract_images accepts list[str] of base64 (see Neo image_stitch.py).
	        return new ImageStitch_AlwaysOnArgs {
	            args = new object[] {
	                true,
	                new object[] { base64 },
	                maxSide,
	            }
	        };
	    }
	}
}

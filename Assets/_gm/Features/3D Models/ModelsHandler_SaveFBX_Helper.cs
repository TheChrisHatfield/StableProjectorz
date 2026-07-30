using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Autodesk.Fbx;
using System.IO;

namespace spz {

	// Helps the 'ModelsHandler_3D' to store models onto disk as an FBX format.
	// Useful when saving the project.
	public class ModelsHandler_SaveFBX_Helper : MonoBehaviour
	{
		static readonly string[] s_fbxBinaryWriterDescriptionHints = {
			"FBX binary (*.fbx)",
			"Autodesk FBX (binary) (*.fbx)", "Autodesk FBX 2014 (binary) (*.fbx)", "Autodesk FBX 2010 (binary) (*.fbx)",
			"FBX 6.0 binary (*.fbx)", "FBX 7.0 binary (*.fbx)", "FBX 7.4.0 binary", "FBX 6.0 binary", "Autodesk Binary FBX",
		};

		static readonly string[] s_fbxAsciiOffIoKeys = {
			"Export|FBX|Ascii", "Export|ExportInASCII", "Export|FBX|ExportInASCII", "Export|FBX|FileFormat|eASCII", "Export|FBX|ExportInAscii",
		};

		/// <summary>Blender 4+ / io_scene_fbx does not import ASCII FBX. Avoid depending on a single
		/// <see cref="FbxIOPluginRegistry.FindWriterIDByDescription"/> string (varies by SDK) — see <see cref="ResolveFbxBinaryWriterFormatId"/>.
		/// Returns true only when Export succeeds and the file exists (not a pre-existing stale FBX).</summary>
		public bool SaveModels(string finalFilepath_with_exten, GameObject saveMe){
			if (string.IsNullOrEmpty(finalFilepath_with_exten) || saveMe == null) {
				return false;
			}
			// Remove any prior file so File.Exists after a failed Initialize cannot look like success.
			try {
				if (File.Exists(finalFilepath_with_exten)) {
					File.Delete(finalFilepath_with_exten);
				}
			} catch (Exception ex) {
				UnityEngine.Debug.LogWarning("[SPZ] FBX: could not clear prior file before export: " + ex.Message);
			}
			using (FbxManager fbxManager = FbxManager.Create()) {
				fbxManager.SetIOSettings(FbxIOSettings.Create(fbxManager, Globals.IOSROOT));
				ApplyFbxExportPreferBinary(fbxManager.GetIOSettings());
				int fileFormat = ResolveFbxBinaryWriterFormatId(fbxManager.GetIOPluginRegistry());
				if (fileFormat < 0) {
					UnityEngine.Debug.LogWarning(
						"[SPZ] FBX: no explicit binary writer id; SDK will pick default — if Blender reports ASCII, update ResolveFbxBinaryWriterFormatId."
					);
				}
				using (FbxExporter exporter = FbxExporter.Create(fbxManager, "myExporter")) {
					if (!exporter.Initialize(finalFilepath_with_exten, fileFormat, fbxManager.GetIOSettings())) {
						UnityEngine.Debug.LogError(
							"[SPZ] FBX Export Initialize failed: " + exporter.GetStatus().GetErrorString()
						);
						return false;
					}
					FbxScene scene = FbxScene.Create(fbxManager, "StableProjectorz Scene");
					FbxNode fbxRootNode = scene.GetRootNode();
					Vector3 rootLocalRot_adj = new Vector3(90, -90, 180);
					FBX_ExportComponents(saveMe, rootLocalRot_adj, scene, fbxRootNode);
					if (!exporter.Export(scene)) {
						UnityEngine.Debug.LogError(
							"[SPZ] FBX Export() failed: " + exporter.GetStatus().GetErrorString()
						);
						return false;
					}
				}
			}
			return File.Exists(finalFilepath_with_exten);
		}

		static int TryInvokeIntNoArgs(object target, string methodName) {
			if (target == null) {
				return -1;
			}
			try {
				MethodInfo m = target.GetType().GetMethod(
					methodName, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null
				);
				if (m == null) {
					return -1;
				}
				object o = m.Invoke(target, null);
				if (o == null) {
					return -1;
				}
				if (o is int vi) {
					return vi >= 0 ? vi : -1;
				}
				try {
					int c = System.Convert.ToInt32(o);
					return c >= 0 ? c : -1;
				} catch {
					return -1;
				}
			} catch {
				return -1;
			}
		}

		/// <summary>Best effort: native binary writer, scan writers, or known <c>FindWriterIDByDescription</c> labels.</summary>
		static int ResolveFbxBinaryWriterFormatId(FbxIOPluginRegistry reg) {
			if (reg == null) {
				return -1;
			}
			int id = TryInvokeIntNoArgs(reg, "GetNativeWriterFormat");
			if (id >= 0) {
				return id;
			}
			int n = TryInvokeIntNoArgs(reg, "GetWriterFormatCount");
			if (n > 0) {
				try {
					MethodInfo getDesc = reg.GetType().GetMethod("GetWriterFormatDescription", new[] { typeof(int) });
					if (getDesc != null) {
						int cap = Math.Min(n, 256);
						for (int i = 0; i < cap; i++) {
							var o = getDesc.Invoke(reg, new object[] { i });
							if (!(o is string desc) || string.IsNullOrEmpty(desc)) {
								continue;
							}
							if (desc.IndexOf("fbx", StringComparison.OrdinalIgnoreCase) < 0) {
								continue;
							}
							if (desc.IndexOf("binary", StringComparison.OrdinalIgnoreCase) < 0) {
								continue;
							}
							if (desc.IndexOf("ascii", StringComparison.OrdinalIgnoreCase) >= 0) {
								continue;
							}
							return i;
						}
					}
				} catch { /* ignore, fall back */ }
			}
			foreach (string h in s_fbxBinaryWriterDescriptionHints) {
				if (string.IsNullOrEmpty(h)) {
					continue;
				}
				int k = reg.FindWriterIDByDescription(h);
				if (k >= 0) {
					return k;
				}
			}
			return -1;
		}

		/// <summary>Try common SDK property paths to prefer binary over ASCII in IO settings (no-op if a path is unknown).</summary>
		static void ApplyFbxExportPreferBinary(FbxIOSettings io) {
			if (io == null) {
				return;
			}
			foreach (string k in s_fbxAsciiOffIoKeys) {
				try {
					io.SetBoolProp(k, false);
				} catch { /* some SDKs omit a path; ignore */ }
			}
		}


	    // https://github.com/Unity-Technologies/com.autodesk.fbx/blob/master/examples/export/Assets/Editor/FbxExporter02.cs
	    // root_rotAdjust: local rotation of go gets adjusted by these angles. But children have no such adjustments.
	    protected void FBX_ExportComponents(GameObject go, Vector3 root_rotAdjust, FbxScene fbxScene, FbxNode fbxNodeParent){
	        // create an node and add it as a child of parent
	        FbxNode fbxNode = FbxNode.Create(fbxScene,  go.name);
	        fbxNodeParent.AddChild(fbxNode);

	        ExportTransform(go, root_rotAdjust, fbxNode);
	        ExportMesh( GetMeshInfo(go), fbxNode, fbxScene);

	        root_rotAdjust = Vector3.zero;//consecutive children won't get rotation adjustment (only first obj could).

	        // now  unityGo  through our children and recurse
	        foreach (Transform childT in  go.transform){
	            FBX_ExportComponents(childT.gameObject, root_rotAdjust, fbxScene, fbxNode);
	        }
	    }

    
	    void ExportTransform( GameObject go, Vector3 rotAdjust, FbxNode fbxNode){
	        Transform tr = go.transform;
	        var fbxTranslate = new FbxDouble3(-tr.localPosition.x, tr.localPosition.y, tr.localPosition.z);
	        var fbxRotate = new FbxDouble3( tr.localRotation.x+rotAdjust.x, 
	                                       -tr.localRotation.y+rotAdjust.y, 
	                                       -tr.localRotation.z+ rotAdjust.z );
	        var fbxScale = new FbxDouble3(tr.localScale.x, tr.localScale.y, tr.localScale.z);
	        fbxNode.LclTranslation.Set(fbxTranslate);
	        fbxNode.LclRotation.Set(fbxRotate);
	        fbxNode.LclScaling.Set(fbxScale);
	    }


	    /// <summary>
	    /// Export the mesh's normals, binormals and tangents using 
	    /// layer 0.
	    /// </summary>
	    /// 
	    public void ExportNormalsEtc (MeshInfo mesh, FbxMesh fbxMesh){
	        /// Set the Normals on Layer 0.
	        FbxLayer fbxLayer = fbxMesh.GetLayer(0 /* default layer */);
	        if (fbxLayer == null){
	            fbxMesh.CreateLayer();
	            fbxLayer = fbxMesh.GetLayer(0 /* default layer */);
	        }

	        var normals   = mesh.Normals;
	        var binormals = mesh.Binormals;
	        var tangents  = mesh.Tangents;

	        using (var fbxLayerElement = FbxLayerElementNormal.Create (fbxMesh, "Normals")){
	            fbxLayerElement.SetMappingMode (FbxLayerElement.EMappingMode.eByControlPoint);

	            // TODO: normals for each triangle vertex instead of averaged per control point
	            //fbxNormalLayer.SetMappingMode (FbxLayerElement.eByPolygonVertex);

	            fbxLayerElement.SetReferenceMode (FbxLayerElement.EReferenceMode.eDirect);

	            // Add one normal per each vertex face index (3 per triangle)
	            FbxLayerElementArray fbxElementArray = fbxLayerElement.GetDirectArray ();

	            for (int n=0; n<normals.Length; n++){
	                fbxElementArray.Add (new FbxVector4( -normals[n][0],//NOTICE -x otherwise normals are point in another dir along Y in 3ds max.
	                                                      normals[n][1],
	                                                      normals[n][2]) );
	            }
	            fbxLayer.SetNormals(fbxLayerElement);
	        }

	        /// Set the binormals on Layer 0. 
	        using (var fbxLayerElement = FbxLayerElementBinormal.Create(fbxMesh, "Binormals")){
	            fbxLayerElement.SetMappingMode (FbxLayerElement.EMappingMode.eByControlPoint);

	            // TODO: normals for each triangle vertex instead of averaged per control point
	            //fbxBinormalLayer.SetMappingMode (FbxLayerElement.eByPolygonVertex);

	            fbxLayerElement.SetReferenceMode (FbxLayerElement.EReferenceMode.eDirect);

	            // Add one normal per each vertex face index (3 per triangle)
	            FbxLayerElementArray fbxElementArray = fbxLayerElement.GetDirectArray ();

	            for (int n=0; n<binormals.Length; n++){
	                fbxElementArray.Add (new FbxVector4 (binormals[n][0], 
	                                                     binormals[n][1],
	                                                     binormals[n][2]));
	            }
	            fbxLayer.SetBinormals (fbxLayerElement);
	        }

	        // Set the tangents on Layer 0.
	        using (var fbxLayerElement = FbxLayerElementTangent.Create (fbxMesh, "Tangents")) 
	        {
	            fbxLayerElement.SetMappingMode (FbxLayerElement.EMappingMode.eByControlPoint);

	            // TODO: normals for each triangle vertex instead of averaged per control point
	            //fbxBinormalLayer.SetMappingMode (FbxLayerElement.eByPolygonVertex);

	            fbxLayerElement.SetReferenceMode (FbxLayerElement.EReferenceMode.eDirect);

	            // Add one normal per each vertex face index (3 per triangle)
	            FbxLayerElementArray fbxElementArray = fbxLayerElement.GetDirectArray ();

	            for (int n = 0; n<tangents.Length; n++) {
	                fbxElementArray.Add (new FbxVector4 (tangents[n][0],
	                                                     tangents[n][1],
	                                                     tangents[n][2]));
	            }
	            fbxLayer.SetTangents (fbxLayerElement);
	        }
	    }

	    /// <summary>
	    /// Export the mesh's vertex color using layer 0.
	    /// </summary>
	    /// 
	    public void ExportVertexColors (MeshInfo mesh, FbxMesh fbxMesh)
	    {
	        // Set the normals on Layer 0.
	        FbxLayer fbxLayer = fbxMesh.GetLayer (0 /* default layer */);
	        if (fbxLayer == null) 
	        {
	            fbxMesh.CreateLayer ();
	            fbxLayer = fbxMesh.GetLayer (0 /* default layer */);
	        }

	        using (var fbxLayerElement = FbxLayerElementVertexColor.Create (fbxMesh, "VertexColors")) 
	        {
	            fbxLayerElement.SetMappingMode (FbxLayerElement.EMappingMode.eByControlPoint);

	            // TODO: normals for each triangle vertex instead of averaged per control point
	            //fbxNormalLayer.SetMappingMode (FbxLayerElement.eByPolygonVertex);

	            fbxLayerElement.SetReferenceMode (FbxLayerElement.EReferenceMode.eDirect);

	            // Add one normal per each vertex face index (3 per triangle)
	            FbxLayerElementArray fbxElementArray = fbxLayerElement.GetDirectArray ();

	            for (int n = 0; n < mesh.VertexColors.Length; n++) 
	            {
	                // Converting to Color from Color32, as Color32 stores the colors
	                // as ints between 0-255, while FbxColor and Color
	                // use doubles between 0-1
	                Color color = mesh.VertexColors [n];
	                fbxElementArray.Add (new FbxColor (color.r,
	                                                    color.g,
	                                                    color.b,
	                                                    color.a));
	            }
	            fbxLayer.SetVertexColors (fbxLayerElement);
	        }
	    }

	    public void ExportUVs(MeshInfo mesh, FbxMesh fbxMesh)
	    {
	        FbxLayer fbxLayer = fbxMesh.GetLayer(0);
	        if (fbxLayer == null)
	        {
	            fbxMesh.CreateLayer();
	            fbxLayer = fbxMesh.GetLayer(0);
	        }

	        using (var fbxLayerElement = FbxLayerElementUV.Create(fbxMesh, "UVSet"))
	        {
	            fbxLayerElement.SetMappingMode(FbxLayerElement.EMappingMode.eByControlPoint);
	            fbxLayerElement.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);

	            FbxLayerElementArray fbxElementArray = fbxLayerElement.GetDirectArray();

	            for (int i = 0; i < mesh.UV.Length; i++)
	            {
	                fbxElementArray.Add(new FbxVector2(mesh.UV[i].x, mesh.UV[i].y));
	            }

	            fbxLayer.SetUVs(fbxLayerElement, FbxLayerElement.EType.eTextureDiffuse);
	        }
	    }

	    public void ExportMesh(MeshInfo mesh, FbxNode fbxNode, FbxScene fbxScene){
	        if (!mesh.IsValid) return;

	        FbxMesh fbxMesh = FbxMesh.Create(fbxScene, "Mesh");

	        int NumControlPoints = mesh.VertexCount;
	        fbxMesh.InitControlPoints(NumControlPoints);

	        for (int v = 0; v < NumControlPoints; v++){
	            fbxMesh.SetControlPointAt(new FbxVector4(-mesh.Vertices[v].x, mesh.Vertices[v].y, mesh.Vertices[v].z), v);
	        }

	        ExportNormalsEtc(mesh, fbxMesh);
	        ExportVertexColors(mesh, fbxMesh);
	        ExportUVs(mesh, fbxMesh);

	        for (int f=0; f<mesh.Triangles.Length; f+=3){
	            fbxMesh.BeginPolygon();
	            fbxMesh.AddPolygon(mesh.Triangles[f + 2]);
	            fbxMesh.AddPolygon(mesh.Triangles[f + 1]);
	            fbxMesh.AddPolygon(mesh.Triangles[f + 0]);
	            fbxMesh.EndPolygon();
	        }

	        fbxNode.SetNodeAttribute(fbxMesh);
	        fbxNode.SetShadingMode(FbxNode.EShadingMode.eWireFrame);
	    }

	    MeshInfo GetMeshInfo(GameObject fromGO, bool requireRenderer = true){
	        if (requireRenderer){// Verify that we are rendering. Otherwise, don't export:
	            var renderer = fromGO.gameObject.GetComponent<MeshRenderer> ();
	            if (!renderer || !renderer.enabled){
	                return new MeshInfo();
	            }
	        }
	        var meshFilter = fromGO.GetComponent<MeshFilter> ();
	        if(!meshFilter){  return new MeshInfo (); }
        
	        var mesh = meshFilter.sharedMesh;
	        if (!mesh){  return new MeshInfo(); }

	        return new MeshInfo(fromGO, mesh);
	    }


	    // export mesh info from Unity
	    ///Information about the mesh that is important for exporting. 
	    public class MeshInfo{
        
	        public Mesh mesh = null;

	        // The gameobject in the scene to which this mesh is attached.
	        // This can be null: don't rely on it existing!
	        public GameObject unityObject = null;

	        public bool IsValid => mesh != null;
	        public int VertexCount => mesh.vertexCount;

	        // Gets the triangles. Each triangle is represented as 3 indices from the vertices array.
	        // Ex: if triangles = [3,4,2], then we have one triangle with vertices vertices[3], vertices[4], and vertices[2]
	        public int[] Triangles { get { return mesh.triangles; } }

	        // Gets the vertices, represented in local coordinates.
	        public Vector3[] Vertices { get { return mesh.vertices; } }

	        // Gets the normals for the vertices.
	        public Vector3[] Normals { get { return mesh.normals; } }

	        Vector3[] _Binormals;
	        public Vector3[] Binormals { 
	            get {
	                // NOTE: LINQ
	                //    return mesh.normals.Zip (mesh.tangents, (first, second)
	                //    => Math.cross (normal, tangent.xyz) * tangent.w
	                Vector3[] normals = mesh.normals;
	                Vector4[] tangents = mesh.tangents;

	                if(normals.Length == 0) { return _Binormals; }
	                if(tangents.Length == 0) { return _Binormals; }
	                if(tangents.Length != normals.Length){ return _Binormals; }

	                _Binormals = new Vector3 [normals.Length];

	                for (int i=0; i<tangents.Length; i++){
	                    _Binormals [i] =  Vector3.Cross(normals[i], tangents[i])  *  tangents[i].w;
	                }
	                return _Binormals;
	            }
	        }

	        public int[] Indices { get { return mesh.triangles; } }// Gets the triangle vertex indices
	        public Vector4[] Tangents { get { return mesh.tangents; } }
	        public Color32[] VertexColors { get { return mesh.colors32; } }
	        public Vector2[] UV { get { return mesh.uv; } }//texture coords

	        public MeshInfo(){ }

	        // Initializes a new instance of the 'MeshInfo' struct.
	        // mesh: A mesh we want to export.
	        public MeshInfo(Mesh mesh){
	            this.mesh = mesh;
	            this.unityObject = null;
	            this._Binormals = new Vector3[0];
	        }

	        // Initializes a new instance of the 'MeshInfo' struct.
	        // gameObject: The GameObject the mesh is attached to.
	        // mesh: A mesh we want to export.
	        public MeshInfo(GameObject gameObject, Mesh mesh){
	            this.mesh = mesh;
	            this.unityObject = gameObject;
	            this._Binormals = new Vector3[0];
	        }
	    }
	}
}//end namespace

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Assimp; // From AssimpNetter
using Assimp.Configs;

// Aliases to resolve ambiguity between System.Numerics and UnityEngine
using UnityMat = UnityEngine.Material;
using UnityMesh = UnityEngine.Mesh;
using UnityQuat = UnityEngine.Quaternion;
using UnityVec3 = UnityEngine.Vector3;
using UnityVec2 = UnityEngine.Vector2;

// System.Numerics types used by AssimpNetter
using SysMatrix4x4 = System.Numerics.Matrix4x4;
using SysVector3 = System.Numerics.Vector3;
using SysQuaternion = System.Numerics.Quaternion;

namespace spz {

	public class AssimpLoader
	{
	    private Dictionary<string, UnityMat> _loadedMaterials;
	    private Dictionary<string, Texture2D> _loadedTextures;
	    private string _baseDirectory;

	    // The user's axis basis describes their external tool's convention, so it applies in BOTH
	    // directions: the writers map outgoing geometry into it, and import maps incoming geometry back
	    // out of it. Without this the documented export/import inverse pair only holds for the default
	    // basis, and an SPZ GO round-trip would return the mesh permuted (and inside-out whenever the
	    // basis flips handedness). Snapshotted once per load, never read per vertex.
	    private ExportAxisSettings.Basis _axisBasis;

	    public GameObject Load(string filePath)
	    {
	        if (!File.Exists(filePath)) return null;

	        _axisBasis = ExportAxisSettings.Snapshot();
	        _baseDirectory = Path.GetDirectoryName(filePath);
	        _loadedMaterials = new Dictionary<string, UnityMat>();
	        _loadedTextures = new Dictionary<string, Texture2D>();

	        using (var importer = new AssimpContext())
	        {
	            // Configure for Unity
	            importer.SetConfig(new NormalSmoothingAngleConfig(66.0f));

	            // Critical flags for Unity compatibility
	            var steps = PostProcessSteps.Triangulate |
	                        PostProcessSteps.GenerateSmoothNormals |
	                        PostProcessSteps.CalculateTangentSpace |
	                        PostProcessSteps.MakeLeftHanded |  // Converts to Unity coords
	                        // PostProcessSteps.FlipUVs |         // REMOVED: Unity UV is already bottom-left. Flipping makes them top-left (upside down).
	                        PostProcessSteps.FlipWindingOrder |// prevents inverted normals
	                        PostProcessSteps.JoinIdenticalVertices;

	            Scene scene = null;
	            try
	            {
	                scene = importer.ImportFile(filePath, steps);
	            }
	            catch (Exception e)
	            {
	                Debug.LogError($"Assimp Import Error: {e.Message}");
	                return null;
	            }

	            if (scene == null || !scene.HasMeshes) return null;

	            GameObject rootObject = new GameObject(Path.GetFileNameWithoutExtension(filePath));
	            ProcessNode(scene.RootNode, scene, rootObject.transform);

	            return rootObject;
	        }
	    }

	    private void ProcessNode(Node node, Scene scene, Transform parentTransform)
	    {
	        GameObject nodeObject = new GameObject(node.Name);
	        nodeObject.transform.SetParent(parentTransform, false);

	        // Handle System.Numerics Matrix Decomposition
	        SysMatrix4x4 sysMat = node.Transform;
	        sysMat = SysMatrix4x4.Transpose(sysMat); // Fix for memory layout mismatch (Row-Major vs Column-Vector)
    
	        SysVector3 sysScale;
	        SysQuaternion sysRot;
	        SysVector3 sysPos;

	        // System.Numerics.Matrix4x4.Decompose is a STATIC method
	        if (SysMatrix4x4.Decompose(sysMat, out sysScale, out sysRot, out sysPos))
	        {
	            nodeObject.transform.localPosition = new UnityVec3(sysPos.X, sysPos.Y, sysPos.Z);
	            nodeObject.transform.localRotation = new UnityQuat(sysRot.X, sysRot.Y, sysRot.Z, sysRot.W);
	            nodeObject.transform.localScale    = new UnityVec3(sysScale.X, sysScale.Y, sysScale.Z);
	        }

	        if (node.HasMeshes)
	        {
	            foreach (int meshIndex in node.MeshIndices)
	            {
	                var assimpMesh = scene.Meshes[meshIndex];
	                GameObject meshObj = new GameObject(assimpMesh.Name ?? $"Mesh_{meshIndex}");
	                meshObj.transform.SetParent(nodeObject.transform, false);

	                var mf = meshObj.AddComponent<MeshFilter>();
	                var mr = meshObj.AddComponent<MeshRenderer>();

	                mf.sharedMesh = ConvertMesh(assimpMesh);
            
	                if (assimpMesh.MaterialIndex >= 0 && assimpMesh.MaterialIndex < scene.MaterialCount)
	                {
	                    mr.sharedMaterial = ConvertMaterial(scene.Materials[assimpMesh.MaterialIndex], scene);
	                }
	            }
	        }

	        if (node.HasChildren)
	        {
	            foreach (Node child in node.Children)
	                ProcessNode(child, scene, nodeObject.transform);
	        }
	    }

	    private UnityMesh ConvertMesh(Assimp.Mesh aMesh)
	    {
	        UnityMesh uMesh = new UnityMesh();
	        uMesh.name = aMesh.Name;

	        // Support large meshes
	        if (aMesh.VertexCount > 65000) 
	            uMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

	        bool remapAxes = !_axisBasis.IsDefault;

	        List<UnityVec3> verts = new List<UnityVec3>();
	        // AssimpNetter Vertices are likely System.Numerics.Vector3 or similar struct with XYZ
	        foreach (var v in aMesh.Vertices) 
	            verts.Add(remapAxes
	                ? _axisBasis.MapImportedUnityVertex(new UnityVec3(v.X, v.Y, v.Z))
	                : new UnityVec3(v.X, v.Y, v.Z));
        
	        uMesh.SetVertices(verts);

	        if (aMesh.HasNormals)
	        {
	            List<UnityVec3> norms = new List<UnityVec3>();
	            // Signed axis permutations are orthogonal, so directions remap exactly like positions.
	            foreach (var n in aMesh.Normals) 
	                norms.Add(remapAxes
	                    ? _axisBasis.MapImportedUnityVertex(new UnityVec3(n.X, n.Y, n.Z))
	                    : new UnityVec3(n.X, n.Y, n.Z));
            
	            uMesh.SetNormals(norms);
	        }

	        if (aMesh.HasTextureCoords(0))
	        {
	            List<UnityVec2> uvs = new List<UnityVec2>();
	            foreach (var uv in aMesh.TextureCoordinateChannels[0]) 
	                uvs.Add(new UnityVec2(uv.X, uv.Y));
            
	            uMesh.SetUVs(0, uvs);
	        }

	        // A handedness-flipping basis mirrors the geometry, so winding must flip back or every
	        // imported face ends up inside-out. Mirrors the same parity rule the writers apply.
	        bool reverseWinding = remapAxes && _axisBasis.FlipsHandedness;
	        List<int> indices = new List<int>();
	        foreach (var face in aMesh.Faces)
	        {
	            if (face.IndexCount == 3)
	            {
	                indices.Add(face.Indices[reverseWinding ? 2 : 0]);
	                indices.Add(face.Indices[1]);
	                indices.Add(face.Indices[reverseWinding ? 0 : 2]);
	            }
	        }
	        uMesh.SetTriangles(indices, 0);

	        uMesh.RecalculateBounds();
	        uMesh.RecalculateTangents();
	        return uMesh;
	    }

	    /// <summary>Get a shader suitable for imported materials. "Standard" is built-in only; use fallbacks for URP or when stripped.</summary>
	    private static Shader GetDefaultLitShader()
	    {
	        Shader s = Shader.Find("Standard");
	        if (s != null) return s;
	        s = Shader.Find("Universal Render Pipeline/Lit");
	        if (s != null) return s;
	        s = Shader.Find("Shader Graphs/Lit");
	        if (s != null) return s;
	        s = Shader.Find("Unlit/Texture");
	        if (s != null) return s;
	        return Shader.Find("Hidden/InternalErrorShader"); // Unity built-in, always present
	    }

	    private UnityMat ConvertMaterial(Assimp.Material aMat, Scene scene)
	    {
	        if (_loadedMaterials.ContainsKey(aMat.Name)) return _loadedMaterials[aMat.Name];

	        Shader shader = GetDefaultLitShader();
	        UnityMat mat = new UnityMat(shader);
	        mat.name = aMat.Name;

	        // Albedo
	        if (aMat.HasTextureDiffuse)
	            AssignTexture(mat, "_MainTex", aMat.TextureDiffuse, scene);

	        // Bump/Normal
	        if (aMat.HasTextureNormal)
	        {
	            AssignTexture(mat, "_BumpMap", aMat.TextureNormal, scene);
	            mat.EnableKeyword("_NORMALMAP");
	        }

	        _loadedMaterials[aMat.Name] = mat;
	        return mat;
	    }

	    private void AssignTexture(UnityMat mat, string property, TextureSlot texSlot, Scene scene)
	    {
	        string path = texSlot.FilePath;
	        Texture2D tex = null;

	        // Handle Embedded textures (Files named "*0", "*1", etc)
	        if (path.StartsWith("*"))
	        {
	            if (int.TryParse(path.Substring(1), out int index) && index < scene.TextureCount)
	            {
	                var embeddedTex = scene.Textures[index];
	                if (embeddedTex.IsCompressed)
	                {
	                    tex = new Texture2D(2, 2);
	                    tex.LoadImage(embeddedTex.CompressedData); // PNG/JPG bytes
	                }
	                else
	                {
	                    Debug.LogWarning("Uncompressed embedded texture handling not fully implemented in this snippet.");
	                }
	            }
	        }
	        else
	        {
	            // Handle External textures
	            tex = LoadExternalTexture(path);
	        }

	        if (tex != null)
	        {
	            tex.name = Path.GetFileName(path);
	            mat.SetTexture(property, tex);
	            if(!string.IsNullOrEmpty(path)) _loadedTextures[path] = tex; 
	        }
	    }

	    private Texture2D LoadExternalTexture(string relativePath)
	    {
	        if (_loadedTextures.ContainsKey(relativePath)) return _loadedTextures[relativePath];

	        string fullPath = Path.Combine(_baseDirectory, relativePath);
        
	        // Try to fix path separators
	        if (!File.Exists(fullPath)) 
	            fullPath = Path.Combine(_baseDirectory, Path.GetFileName(relativePath));

	        if (File.Exists(fullPath))
	        {
	            byte[] bytes = File.ReadAllBytes(fullPath);
	            Texture2D t = new Texture2D(2, 2);
	            t.LoadImage(bytes);
	            return t;
	        }
	        return null;
	    }
	}
}//end namespace

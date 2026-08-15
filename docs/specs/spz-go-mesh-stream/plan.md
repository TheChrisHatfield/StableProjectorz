# Plan: SPZ GO Mesh Stream

**Hook:** `spz.go.mesh_stream`

1. Add a bounded V1 serializer/sender in Unity with explicit coordinate conversion and fast gzip.
2. Expose it through JSON-RPC and both HTTP facades.
3. Add a loopback Blender receiver, packet decoder, and main-thread `foreach_set` materializer.
4. Prefer streaming in Blender Pull and the in-app SPZ GO Export; retain FBX fallback.
5. Keep installed Blender bridge files synchronized and update installer packaging.
6. Validate protocol contracts, Python syntax/tests, C# compilation, and end-to-end wiring.

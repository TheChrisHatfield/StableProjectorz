# Specification: SPZ GO Mesh Stream

**Hook:** `spz.go.mesh_stream`

## Requirements

### R1 — Direct geometry transport

SPZ can send the current visible model to a Blender SPZ GO listener without creating an FBX. The V1 packet is versioned, framed, little-endian, compressed with a declared codec, and rejected when declared sizes exceed protocol limits.

### R2 — Equivalent Blender geometry

Blender creates one object per streamed Unity mesh through bulk collection writes, preserving mesh names, triangle topology, UVs, authoring scale, upright orientation, and facing.

### R3 — Thread-safe receive

Socket receive and decompression may run off Blender's main thread. All `bpy` mutation runs from a registered main-thread timer.

### R4 — Compatibility fallback

If the listener is unavailable, the protocol is unsupported, the packet is invalid, or mesh creation fails, existing FBX export/import remains available and no success is reported for the failed stream.

### R5 — Progressive one-click export

The in-app SPZ GO Export attempts geometry streaming before its existing FBX/texture export. Blender suppresses the next corresponding FBX geometry auto-import after successful stream materialization while still applying the completed exchange texture.

## Acceptance criteria

- Contract tests verify framing constants, limits, RPC/HTTP wiring, Blender bulk mesh writes, ship-copy parity, fallback, and stream-before-FBX ordering.
- `dotnet build Assembly-CSharp.csproj` succeeds.
- Python bridge contract tests succeed.

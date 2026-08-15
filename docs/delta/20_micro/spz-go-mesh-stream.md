# SPZ GO Mesh Stream

**Hook:** `spz.go.mesh_stream`

## Intent

Make SPZ → Blender geometry visible without writing and reparsing an intermediary FBX. SPZ sends a versioned binary packet to the Blender add-on over localhost TCP; Blender materializes mesh arrays with bulk `foreach_set`.

## Architecture

- Unity remains the geometry source and performs the single Unity-left-handed/Y-up → Blender-right-handed/Z-up conversion.
- Blender owns a loopback-only receiver thread and queues complete packets for main-thread mesh creation.
- The packet is framed, little-endian, size-bounded, and codec-versioned. V1 carries named triangle meshes, positions, indices, and optional per-vertex UVs.
- Fast gzip is the portable V1 codec. Codec identifiers leave room for dictionary-backed compression later without changing framing.
- Existing FBX + texture export remains the compatibility and texture-completion path. A successful stream suppresses only the matching next auto-import, not texture assignment.

## Constraints

- No `bpy` access from the receiver thread.
- Never deserialize unbounded lengths.
- A failed or unsupported stream must leave the FBX path usable.
- Preserve authoring scale by removing SPZ fit-to-volume during capture, then restoring it.

## Spec

[`docs/specs/spz-go-mesh-stream/`](../../specs/spz-go-mesh-stream/)

"""Run with Blender --background --factory-startup --python to smoke-test V1 receive/materialize."""

from pathlib import Path
import gzip
import socket
import struct
import sys
import time

import bpy


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "External"))
from Blender_SpzBridge import mesh_stream  # noqa: E402


def main():
    name = b"StreamTriangle"
    positions = struct.pack("<9f", 1, 3, 2, 4, 6, 5, 7, 9, 8)
    indices = struct.pack("<3I", 2, 1, 0)
    uv = struct.pack("<6f", 0, 0, 1, 0, 0, 1)
    raw = struct.pack("<HHII", len(name), mesh_stream.MESH_FLAG_UV0, 3, 3) + name + positions + indices + uv
    wire = gzip.compress(raw, compresslevel=1)
    packet = struct.pack(
        "<8sHHIII",
        mesh_stream.PACKET_MAGIC,
        mesh_stream.PROTOCOL_VERSION,
        mesh_stream.CODEC_GZIP_FAST,
        len(raw),
        len(wire),
        1,
    ) + wire

    port = 5561
    assert mesh_stream.ensure_listener(port)
    conn = None
    for _ in range(30):
        try:
            conn = socket.create_connection((mesh_stream.DEFAULT_HOST, port), timeout=1.0)
            break
        except OSError:
            time.sleep(0.05)
    assert conn is not None
    with conn:
        conn.sendall(packet)
        ack = conn.recv(12)
    magic, status = struct.unpack("<8sI", ack)
    assert magic == mesh_stream.ACK_MAGIC and status == 0

    created = mesh_stream.materialize_next()
    assert created is not None and len(created) == 1
    obj = created[0]
    assert len(obj.data.vertices) == 3
    assert len(obj.data.polygons) == 1
    assert len(obj.data.uv_layers) == 1
    assert tuple(round(v, 5) for v in obj.data.vertices[0].co) == (1.0, 3.0, 2.0)
    assert list(obj.data.polygons[0].vertices) == [2, 1, 0]
    mesh_stream.stop_listener()
    bpy.data.objects.remove(obj, do_unlink=True)
    print("SPZ_MESH_STREAM_SMOKE_OK")


if __name__ == "__main__":
    main()

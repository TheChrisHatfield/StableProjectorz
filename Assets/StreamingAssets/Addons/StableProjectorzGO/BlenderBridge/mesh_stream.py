"""Bounded SPZ GO mesh stream V1 receiver and Blender bulk materializer."""

from __future__ import annotations

import gzip
import io
import queue
import socket
import struct
import threading
from dataclasses import dataclass
from typing import Optional

import bpy
import numpy as np


PACKET_MAGIC = b"SPZMSH\x00\x00"
ACK_MAGIC = b"SPZACK\x00\x00"
PROTOCOL_VERSION = 1
CODEC_NONE = 0
CODEC_GZIP_FAST = 1
DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 5560
HEADER_BYTES = 24
MAX_MESH_COUNT = 4096
MAX_NAME_BYTES = 1024
MAX_RAW_BYTES = 512 * 1024 * 1024
MAX_WIRE_BYTES = 256 * 1024 * 1024
MESH_FLAG_UV0 = 1


@dataclass
class MeshArrays:
    name: str
    positions: np.ndarray
    indices: np.ndarray
    uv0: Optional[np.ndarray]


@dataclass
class Transfer:
    meshes: list[MeshArrays]
    codec: int


_pending: queue.Queue[Transfer] = queue.Queue(maxsize=4)
_listener_socket: Optional[socket.socket] = None
_listener_thread: Optional[threading.Thread] = None
_stop = threading.Event()
_ready = threading.Event()
_last_error = ""
_bound_port = 0


def _recv_exact(conn: socket.socket, size: int) -> bytes:
    chunks = bytearray(size)
    view = memoryview(chunks)
    offset = 0
    while offset < size:
        got = conn.recv_into(view[offset:], size - offset)
        if got <= 0:
            raise ValueError("connection closed before framed payload completed")
        offset += got
    return bytes(chunks)


def _decompress_bounded(wire: bytes, codec: int, raw_size: int) -> bytes:
    if codec == CODEC_NONE:
        raw = wire
    elif codec == CODEC_GZIP_FAST:
        with gzip.GzipFile(fileobj=io.BytesIO(wire), mode="rb") as source:
            raw = source.read(raw_size + 1)
    else:
        raise ValueError(f"unsupported mesh stream codec {codec}")
    if len(raw) != raw_size:
        raise ValueError(f"raw payload length mismatch: declared {raw_size}, received {len(raw)}")
    return raw


def _parse_payload(raw: bytes, mesh_count: int) -> Transfer:
    offset = 0
    total = len(raw)
    meshes: list[MeshArrays] = []

    def take(size: int) -> int:
        nonlocal offset
        if size < 0 or offset + size > total:
            raise ValueError("mesh payload section exceeds frame")
        start = offset
        offset += size
        return start

    for _ in range(mesh_count):
        header_at = take(12)
        name_len, flags, vertex_count, index_count = struct.unpack_from("<HHII", raw, header_at)
        if name_len > MAX_NAME_BYTES:
            raise ValueError("mesh name exceeds protocol limit")
        if vertex_count == 0 or index_count == 0 or index_count % 3:
            raise ValueError("invalid vertex/index count")
        name_at = take(name_len)
        name = raw[name_at : name_at + name_len].decode("utf-8", errors="replace") or "SPZ_Mesh"

        positions_at = take(vertex_count * 3 * 4)
        positions = np.frombuffer(raw, dtype="<f4", count=vertex_count * 3, offset=positions_at).reshape((-1, 3))
        indices_at = take(index_count * 4)
        indices = np.frombuffer(raw, dtype="<u4", count=index_count, offset=indices_at)
        if indices.size and int(indices.max()) >= vertex_count:
            raise ValueError(f"mesh '{name}' contains an out-of-range index")

        uv0 = None
        if flags & MESH_FLAG_UV0:
            uv_at = take(vertex_count * 2 * 4)
            uv0 = np.frombuffer(raw, dtype="<f4", count=vertex_count * 2, offset=uv_at).reshape((-1, 2))
        unknown_flags = flags & ~MESH_FLAG_UV0
        if unknown_flags:
            raise ValueError(f"unsupported mesh flags 0x{unknown_flags:x}")
        meshes.append(MeshArrays(name=name, positions=positions, indices=indices, uv0=uv0))

    if offset != total:
        raise ValueError(f"mesh payload has {total - offset} trailing bytes")
    return Transfer(meshes=meshes, codec=CODEC_NONE)


def _handle_connection(conn: socket.socket) -> None:
    status = 1
    try:
        conn.settimeout(10.0)
        header = _recv_exact(conn, HEADER_BYTES)
        magic, version, codec, raw_size, wire_size, mesh_count = struct.unpack("<8sHHIII", header)
        if magic != PACKET_MAGIC:
            raise ValueError("invalid mesh stream magic")
        if version != PROTOCOL_VERSION:
            status = 2
            raise ValueError(f"unsupported mesh stream version {version}")
        if mesh_count < 1 or mesh_count > MAX_MESH_COUNT:
            status = 3
            raise ValueError("mesh count exceeds protocol limit")
        if raw_size < 1 or raw_size > MAX_RAW_BYTES or wire_size < 1 or wire_size > MAX_WIRE_BYTES:
            status = 4
            raise ValueError("mesh stream frame exceeds size limit")
        wire = _recv_exact(conn, wire_size)
        raw = _decompress_bounded(wire, codec, raw_size)
        transfer = _parse_payload(raw, mesh_count)
        transfer.codec = codec
        try:
            _pending.put_nowait(transfer)
        except queue.Full as exc:
            status = 6
            raise ValueError("mesh stream queue is full") from exc
        status = 0
    except Exception as exc:
        global _last_error
        _last_error = str(exc)
        print("SPZ GO mesh stream rejected:", exc)
    finally:
        try:
            conn.sendall(struct.pack("<8sI", ACK_MAGIC, status))
        except OSError:
            pass


def _listen() -> None:
    global _listener_socket, _last_error, _bound_port
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    _listener_socket = sock
    try:
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        sock.bind((DEFAULT_HOST, _bound_port or DEFAULT_PORT))
        _bound_port = int(sock.getsockname()[1])
        sock.listen(2)
        sock.settimeout(0.5)
        _last_error = ""
        print(f"SPZ GO mesh stream: listening on {DEFAULT_HOST}:{_bound_port}")
        _ready.set()
        while not _stop.is_set():
            try:
                conn, _addr = sock.accept()
            except socket.timeout:
                continue
            with conn:
                _handle_connection(conn)
    except OSError as exc:
        if not _stop.is_set():
            _last_error = str(exc)
            print("SPZ GO mesh stream listener:", exc)
        try:
            sock.close()
        except OSError:
            pass
        if _listener_socket is sock:
            _listener_socket = None
        _ready.set()
        return
    finally:
        try:
            sock.close()
        except OSError:
            pass
        if _listener_socket is sock:
            _listener_socket = None


def start_listener(port: int = DEFAULT_PORT) -> bool:
    global _listener_thread, _bound_port, _last_error
    if _listener_thread is not None and _listener_thread.is_alive():
        return _listener_socket is not None and _bound_port == int(port)
    if port < 1 or port > 65535:
        _last_error = "invalid listener port"
        return False
    _bound_port = int(port)
    _last_error = ""
    _stop.clear()
    _ready.clear()
    _listener_thread = threading.Thread(target=_listen, name="SPZ GO Mesh Stream", daemon=True)
    _listener_thread.start()
    # Bind runs asynchronously; do not report success until listen() is ready or failed.
    if not _ready.wait(2.0):
        _last_error = _last_error or "listener bind timed out"
        stop_listener()
        return False
    if _listener_socket is None or _last_error:
        stop_listener()
        return False
    return True


def ensure_listener(port: int = DEFAULT_PORT) -> bool:
    if _listener_thread is not None and _listener_thread.is_alive() and _bound_port != int(port):
        stop_listener()
    return start_listener(port)


def stop_listener() -> None:
    global _listener_thread, _listener_socket
    _stop.set()
    sock = _listener_socket
    if sock is not None:
        try:
            sock.close()
        except OSError:
            pass
    thread = _listener_thread
    if thread is not None and thread.is_alive():
        thread.join(timeout=1.5)
    _listener_thread = None
    _listener_socket = None
    while True:
        try:
            _pending.get_nowait()
        except queue.Empty:
            break


def listener_port() -> int:
    return _bound_port or DEFAULT_PORT


def last_error() -> str:
    return _last_error


def _remove_previous_stream_objects() -> None:
    # Both markers count as "the SPZ model currently in the scene": a stream that only cleared its
    # own objects would stack a duplicate on top of a model that arrived via FBX import.
    for obj in list(bpy.data.objects):
        try:
            if not (obj.get("spz_mesh_stream") or obj.get("spz_go_import")):
                continue
        except (ReferenceError, TypeError):
            continue
        mesh = obj.data if getattr(obj, "type", None) == "MESH" else None
        bpy.data.objects.remove(obj, do_unlink=True)
        if mesh is not None and mesh.users == 0:
            bpy.data.meshes.remove(mesh)


def materialize_next(context=None) -> Optional[list]:
    """Create one queued transfer. Must run on Blender's main thread."""
    try:
        transfer = _pending.get_nowait()
    except queue.Empty:
        return None

    ctx = context if context is not None else bpy.context
    created = []
    created_meshes = []
    try:
        for item in transfer.meshes:
            mesh = bpy.data.meshes.new(item.name)
            created_meshes.append(mesh)
            mesh.vertices.add(len(item.positions))
            mesh.vertices.foreach_set("co", item.positions.ravel())

            loop_count = len(item.indices)
            poly_count = loop_count // 3
            mesh.loops.add(loop_count)
            mesh.loops.foreach_set("vertex_index", item.indices)
            mesh.polygons.add(poly_count)
            starts = np.arange(0, loop_count, 3, dtype=np.int32)
            totals = np.full(poly_count, 3, dtype=np.int32)
            mesh.polygons.foreach_set("loop_start", starts)
            mesh.polygons.foreach_set("loop_total", totals)
            mesh.polygons.foreach_set("use_smooth", np.ones(poly_count, dtype=np.bool_))

            if item.uv0 is not None:
                uv_layer = mesh.uv_layers.new(name="UVMap")
                loop_uv = item.uv0[item.indices]
                uv_layer.data.foreach_set("uv", loop_uv.ravel())

            mesh.update(calc_edges=True)
            obj = bpy.data.objects.new(item.name, mesh)
            created.append(obj)
            ctx.collection.objects.link(obj)
    except Exception:
        for obj in created:
            bpy.data.objects.remove(obj, do_unlink=True)
        for mesh in created_meshes:
            if mesh.users == 0:
                bpy.data.meshes.remove(mesh)
        raise

    # Commit replacement only after every new mesh was built and linked successfully.
    _remove_previous_stream_objects()
    for obj in created:
        obj["spz_mesh_stream"] = True

    try:
        bpy.ops.object.select_all(action="DESELECT")
    except Exception:
        pass
    for obj in created:
        try:
            obj.select_set(True)
        except Exception:
            pass
    if created:
        try:
            ctx.view_layer.objects.active = created[0]
        except Exception:
            pass
    print(f"SPZ GO: materialized {len(created)} streamed mesh object(s).")
    return created

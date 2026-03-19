# API Endpoints & WebSocket Analysis

## Current Architecture

### Existing Communication Methods

1. **Add-on System (Current):**
   - **Protocol:** TCP Sockets
   - **Format:** JSON-RPC 2.0
   - **Port:** 5555 (configurable)
   - **Connection:** Persistent TCP connection
   - **Threading:** Background thread for listening, main thread for execution

2. **External Services (StableProjectorz → A1111/Trellis):**
   - **Protocol:** HTTP REST API
   - **Library:** `UnityWebRequest` (Unity's built-in HTTP client)
   - **Endpoints:** 
     - `http://{ip}:{port}/sdapi/v1/*` (Stable Diffusion)
     - `http://{ip}:{port}/controlnet/*` (ControlNet)
     - `http://{ip}:{port}/*` (3D Generation)

## Proposed Enhancements

### Option 1: HTTP REST API Endpoints
**Status:** ✅ **IMPLEMENTED** (FastAPI in Python)  
**Priority:** Medium  
**Complexity:** Medium

**Benefits:**
- ✅ Standard HTTP protocol (easier for web clients, curl, Postman)
- ✅ Stateless requests (no connection management)
- ✅ Works through firewalls/proxies
- ✅ Easy to test and debug
- ✅ Can use HTTPS for security
- ✅ Works with any HTTP client library

**Drawbacks:**
- ❌ Higher overhead per request (HTTP headers)
- ❌ No persistent connection (reconnect overhead)
- ❌ Less efficient for high-frequency operations
- ❌ Requires HTTP server implementation

**Use Cases:**
- Web-based add-ons
- Remote control from other applications
- Integration with web services
- Testing and debugging

**Implementation:**
✅ **Implemented using FastAPI (Python)**
- `http_server.py` - FastAPI HTTP REST API server
- Automatically started by Python add-on server
- Interactive docs at `http://localhost:5557/docs`
- Maps REST endpoints to JSON-RPC methods via `spz` client
- Legacy C# `Addon_HttpServer.cs` still available but deprecated

**Example Endpoints:**
```
GET  /api/v1/cameras/{index}/position
POST /api/v1/cameras/{index}/position
GET  /api/v1/meshes
POST /api/v1/meshes/{id}/position
GET  /api/v1/scene/info
POST /api/v1/sd/generate
```

### Option 2: WebSocket Support
**Status:** Not implemented  
**Priority:** Low-Medium  
**Complexity:** High

**Benefits:**
- ✅ Real-time bidirectional communication
- ✅ Lower latency than HTTP polling
- ✅ Persistent connection (like TCP, but standardized)
- ✅ Built-in reconnection handling
- ✅ Works through proxies (with HTTP upgrade)
- ✅ Can send events from Unity to clients

**Drawbacks:**
- ❌ More complex implementation
- ❌ Requires WebSocket library (not built into Unity)
- ❌ Overhead for simple request/response
- ❌ Less standard than REST for APIs

**Use Cases:**
- Real-time monitoring dashboards
- Live parameter updates
- Event-driven add-ons
- Multi-client scenarios

**Implementation:**
```csharp
// Would need WebSocket library (e.g., WebSocketSharp-NetStandard)
public class Addon_WebSocketServer : MonoBehaviour {
    // WebSocket server on port 5556
    // Supports JSON-RPC over WebSocket
}
```

### Option 3: Hybrid Approach
**Status:** Recommended  
**Priority:** High  
**Complexity:** Medium-High

**Strategy:**
- Keep TCP for Python add-ons (current, efficient)
- Add HTTP REST for web/remote clients
- Optional WebSocket for real-time features

**Architecture:**
```
┌─────────────────┐
│  Python Addons │──TCP (5555)──┐
└─────────────────┘              │
                                 ├──► Addon_SocketServer
┌─────────────────┐              │   (JSON-RPC Dispatcher)
│  Web Clients    │──HTTP (5557)─┤
└─────────────────┘              │
                                 │
┌─────────────────┐              │
│  Real-time Apps │──WS (5558)───┘
└─────────────────┘
```

## Implementation Plan

### Phase 1: HTTP REST API (Recommended First)

**Why First:**
- Most requested feature
- Easier to implement than WebSocket
- Broad compatibility
- Can reuse existing JSON-RPC handlers

**Implementation (Completed):**

1. ✅ **FastAPI HTTP Server** (`http_server.py`)
   - FastAPI framework in Python
   - Automatically started by `addon_server.py`
   - Port 5557 (configurable)
   - CORS middleware enabled

2. ✅ **REST Endpoint Mapping**
   - REST endpoints map to JSON-RPC methods via `spz` client
   - Example: `POST /api/v1/cameras/0/position` → `spz.cmd.set_camera_pos`
   - Path parameters and request bodies handled by Pydantic models

3. ✅ **Response Format**
   - JSON responses (consistent with JSON-RPC)
   - HTTP status codes (200, 400, 404, 500)
   - Error messages in JSON
   - Interactive API documentation at `/docs` (Swagger UI)

4. **CORS Support** (for web clients)
   - Add CORS headers for cross-origin requests
   - Configurable allowed origins

**Example API Design:**
```http
# Get camera position
GET /api/v1/cameras/0/position
Response: {"x": 0.0, "y": 0.0, "z": 0.0}

# Set camera position
POST /api/v1/cameras/0/position
Body: {"x": 1.0, "y": 2.0, "z": 3.0}
Response: {"success": true}

# Get all meshes
GET /api/v1/meshes
Response: {"meshes": [{"id": 1, "name": "Mesh1", ...}]}

# Batch operations
POST /api/v1/meshes/batch/position
Body: {"mesh_ids": [1, 2, 3], "positions": [...]}
Response: {"success": true, "count": 3}
```

### Phase 2: WebSocket Support (Optional)

**Implementation Steps:**

1. **Add WebSocket Library**
   - Use `WebSocketSharp-NetStandard` or similar
   - Unity-compatible WebSocket server

2. **WebSocket Server**
   ```csharp
   public class Addon_WebSocketServer : MonoBehaviour {
       // WebSocket server on port 5558
       // JSON-RPC over WebSocket
       // Event broadcasting
   }
   ```

3. **Event Broadcasting**
   - Send events to all connected clients
   - Example: Camera moved, mesh selected, generation complete

**Use Cases:**
- Real-time dashboards
- Live parameter monitoring
- Multi-client scenarios

## Comparison Table

| Feature | TCP (Current) | HTTP REST | WebSocket |
|---------|--------------|-----------|-----------|
| **Protocol** | TCP Socket | HTTP/HTTPS | WebSocket |
| **Connection** | Persistent | Stateless | Persistent |
| **Latency** | Low | Medium | Low |
| **Overhead** | Low | Medium | Medium |
| **Firewall** | May block | Usually works | Usually works |
| **Ease of Use** | Medium | Easy | Medium |
| **Real-time Events** | Possible | Polling only | Native |
| **Multi-client** | One per connection | Unlimited | Unlimited |
| **Web Support** | No | Yes | Yes |
| **Implementation** | ✅ Done | Not done | Not done |

## Recommendations

### Immediate (Phase 1): HTTP REST API
**Why:**
- Most versatile and widely supported
- Easy to test and debug
- Works with any HTTP client
- Can reuse existing JSON-RPC handlers

**Implementation Effort:** Medium (2-3 days)

### Future (Phase 2): WebSocket Support
**Why:**
- Useful for real-time features
- Better than HTTP polling for events
- Not critical for most use cases

**Implementation Effort:** High (4-5 days)

### Keep TCP for Python
**Why:**
- Already working well
- Efficient for Python add-ons
- No need to change existing system

## Security Considerations

### HTTP REST API:
- Add authentication (API keys, tokens)
- Rate limiting
- HTTPS support (TLS)
- CORS configuration

### WebSocket:
- WSS (secure WebSocket)
- Authentication on connection
- Rate limiting per connection

### TCP (Current):
- Localhost only (IPAddress.Loopback)
- Consider adding authentication

## Example Use Cases

### HTTP REST API:
1. **Web Dashboard:** Browser-based control panel
2. **Remote Control:** Control from another machine
3. **Integration:** Connect from other applications
4. **Testing:** Easy API testing with Postman/curl

### WebSocket:
1. **Live Monitoring:** Real-time parameter display
2. **Event Streaming:** Push events to clients
3. **Multi-user:** Multiple clients watching same session

## Next Steps

1. **Decision:** Choose HTTP REST, WebSocket, or both
2. **Implementation:** Start with HTTP REST (recommended)
3. **Testing:** Create test clients for each protocol
4. **Documentation:** API documentation for REST endpoints

## Conclusion

**Recommended Approach:**
1. ✅ **Keep TCP** for Python add-ons (current system)
2. ➕ **Add HTTP REST API** for web/remote clients (Phase 1)
3. ➕ **Add WebSocket** for real-time features (Phase 2, optional)

This provides maximum flexibility while maintaining backward compatibility.

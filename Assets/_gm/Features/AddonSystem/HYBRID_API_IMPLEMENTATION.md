# Hybrid API Implementation Summary

## Overview
The hybrid approach has been implemented, providing multiple communication protocols for different use cases.

## Implemented Components

### 1. TCP JSON-RPC Server ✅
**Status:** Already implemented  
**Port:** 5555 (default)  
**Protocol:** TCP Socket with JSON-RPC 2.0  
**Use Case:** Python add-ons (efficient, persistent connections)

**Files:**
- `Addon_SocketServer.cs` - TCP server implementation
- `spz.py` - Python client library

### 2. HTTP REST API Server (FastAPI) ✅
**Status:** ✅ **IMPLEMENTED**  
**Port:** 5557 (default)  
**Protocol:** HTTP/HTTPS with REST endpoints  
**Use Case:** Web clients, remote control, integration with other applications

**Files:**
- `http_server.py` - FastAPI HTTP REST API server (Python)
- `REST_API_DOCUMENTATION.md` - Complete API documentation
- `Addon_HttpServer.cs` - Legacy C# HTTP server (deprecated, optional)

**Features:**
- ✅ RESTful endpoints (`/api/v1/{resource}/{id}/{action}`)
- ✅ CORS support for web browsers (via FastAPI middleware)
- ✅ Maps REST endpoints to existing JSON-RPC methods via `spz` client
- ✅ JSON request/response format with Pydantic validation
- ✅ Standard HTTP status codes (200, 400, 404, 500)
- ✅ All existing API methods exposed via REST
- ✅ **Interactive API documentation** at `/docs` (Swagger UI) and `/redoc` (ReDoc)
- ✅ **High performance** - FastAPI is one of the fastest Python frameworks
- ✅ **Type safety** - Automatic request/response validation

**Example Endpoints:**
```
GET  /api/v1/cameras/0/position
POST /api/v1/cameras/0/position
GET  /api/v1/meshes
POST /api/v1/meshes/1/position
GET  /api/v1/scene/info
POST /api/v1/sd/generate
```

### 3. WebSocket Server ⏳
**Status:** Planned (requires WebSocket library)  
**Port:** 5558 (default, configurable)  
**Protocol:** WebSocket (WSS for secure)  
**Use Case:** Real-time bidirectional communication, event streaming

**Implementation Notes:**
- Requires WebSocket library (e.g., `WebSocketSharp-NetStandard`)
- Would enable real-time event broadcasting
- Useful for dashboards and live monitoring
- Can be added later if needed

## Architecture

```
┌─────────────────┐
│  Python Addons │──TCP (5555)──┐
└─────────────────┘              │
                                 ├──► Addon_SocketServer
┌─────────────────┐              │   (JSON-RPC Dispatcher)
│  Web Clients    │──HTTP (5557)─┤   └──► FastPath_API
│  Remote Apps    │              │       (Unity API)
│  (FastAPI)      │              │
└─────────────────┘              │
                                 │
┌─────────────────┐              │
│  Real-time Apps │──WS (5558)───┘  (Future)
└─────────────────┘
```

## Configuration

### Addon_MGR Settings
```csharp
[SerializeField] int _serverPort = 5555;          // TCP JSON-RPC
[SerializeField] int _httpServerPort = 5557;     // HTTP REST API (FastAPI)
[SerializeField] int _webSocketPort = 5558;      // WebSocket (future)
[SerializeField] bool _enableHttpServer = true;   // Enable FastAPI HTTP server (Python)
[SerializeField] bool _enableCSharpHttpServer = false; // Legacy C# HTTP server (deprecated)
[SerializeField] bool _enableWebSocketServer = false; // Enable WebSocket (future)
```

### FastAPI HTTP Server
The FastAPI server is automatically started by the Python add-on server. Install dependencies:
```bash
pip install -r StreamingAssets/AddonSystem/requirements.txt
```

Or manually:
```bash
pip install fastapi uvicorn
```

The server includes CORS middleware configured for all origins by default. Access interactive docs at:
- Swagger UI: `http://localhost:5557/docs`
- ReDoc: `http://localhost:5557/redoc`

## Usage Examples

### Python (TCP)
```python
import spz
api = spz.get_api()
api.cameras.set_pos(0, 1.0, 2.0, 3.0)
```

### HTTP REST (cURL)
```bash
curl -X POST http://localhost:5557/api/v1/cameras/0/position \
  -H "Content-Type: application/json" \
  -d '{"x": 1.0, "y": 2.0, "z": 3.0}'
```

### HTTP REST (JavaScript)
```javascript
fetch('http://localhost:5557/api/v1/cameras/0/position', {
  method: 'POST',
  headers: {'Content-Type': 'application/json'},
  body: JSON.stringify({x: 1.0, y: 2.0, z: 3.0})
});
```

### HTTP REST (Python)
```python
import requests
response = requests.post(
    'http://localhost:5557/api/v1/cameras/0/position',
    json={'x': 1.0, 'y': 2.0, 'z': 3.0}
)
```

## Benefits of Hybrid Approach

1. **Flexibility:** Choose the right protocol for each use case
2. **Compatibility:** HTTP works with any HTTP client
3. **Efficiency:** TCP for Python add-ons (low overhead)
4. **Web Support:** HTTP enables browser-based add-ons
5. **Future-Proof:** WebSocket can be added when needed

## Security Considerations

### Current (Localhost Only)
- TCP: `IPAddress.Loopback` (127.0.0.1 only)
- HTTP: `localhost` and `127.0.0.1` only
- No authentication required

### Future Enhancements
- API key authentication
- HTTPS support
- IP whitelisting
- Rate limiting
- Request signing

## Testing

### Test HTTP REST API
```bash
# Test camera position
curl http://localhost:5557/api/v1/cameras/0/position

# Test scene info
curl http://localhost:5557/api/v1/scene/info

# Test mesh list
curl http://localhost:5557/api/v1/meshes
```

### Test from Browser
Open browser console and run:
```javascript
fetch('http://localhost:5557/api/v1/scene/info')
  .then(r => r.json())
  .then(console.log);
```

## Next Steps

1. ✅ **HTTP REST API** - Complete
2. ⏳ **WebSocket Server** - Optional, requires library
3. ⏳ **Authentication** - API keys/tokens
4. ⏳ **HTTPS Support** - TLS encryption
5. ⏳ **Rate Limiting** - Prevent abuse

## Files Created/Modified

**New Files:**
- `http_server.py` - FastAPI HTTP REST API server (Python)
- `requirements.txt` - Python dependencies (FastAPI, uvicorn)
- `REST_API_DOCUMENTATION.md` - API documentation
- `HYBRID_API_IMPLEMENTATION.md` - This file

**Modified Files:**
- `addon_server.py` - Added FastAPI server startup and HTTP port configuration
- `Addon_MGR.cs` - Added HTTP server port configuration, disabled C# HTTP server by default
- `Addon_HttpServer.cs` - Legacy C# HTTP server (deprecated, optional)

**Deprecated:**
- `Addon_HttpServer.cs` - Still available but not recommended. Use FastAPI instead.

## Summary

✅ **Hybrid approach implemented!**

- **TCP JSON-RPC** - For Python add-ons (existing)
- **HTTP REST API** - For web/remote clients (new)
- **WebSocket** - Planned for future (optional)

The system now supports multiple communication protocols, providing maximum flexibility for different use cases while maintaining backward compatibility with existing Python add-ons.

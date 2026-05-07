"""
Optional FastAPI routes for GPU pacing. Mounted by ``AddonSystem/http_server.py``
if this file exists (same pattern as other REST surface).
"""

from __future__ import annotations

import asyncio
import sys
from pathlib import Path
from typing import Any, Optional

from fastapi import HTTPException
from pydantic import BaseModel, Field

# Ensure sibling modules resolve when loaded via importlib from http_server.
_addon_dir = Path(__file__).resolve().parent
_d = str(_addon_dir)
if _d not in sys.path:
    sys.path.insert(0, _d)

try:
    import gpu_flow_runtime as gfr
except ImportError:
    gfr = None  # type: ignore


class PaceBody(BaseModel):
    max_wait_ms: int = Field(12000, ge=50, le=120000)
    source: str = Field("http")
    phase: str = Field("manual")
    run_id: Optional[str] = None


def register_routes(app: Any) -> bool:
    if gfr is None:
        return False

    @app.get("/api/v1/gpu-flow/status", tags=["gpu-flow"])
    async def gpu_flow_status():
        rt = gfr.get_runtime()
        return await asyncio.to_thread(rt.status)

    @app.post("/api/v1/gpu-flow/pace", tags=["gpu-flow"])
    async def gpu_flow_pace(body: PaceBody):
        rt = gfr.get_runtime()
        try:
            return await asyncio.to_thread(
                rt.pace,
                body.max_wait_ms,
                body.source,
                body.phase,
                body.run_id,
            )
        except Exception as e:
            raise HTTPException(status_code=500, detail=str(e))

    return True

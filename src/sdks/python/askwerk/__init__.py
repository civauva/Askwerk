# SPDX-License-Identifier: Apache-2.0
"""The Askwerk Public API for Python, with no dependencies (standard library only)."""
from __future__ import annotations

import hashlib
import hmac
import time

from ._generated import *  # noqa: F403
from ._generated import ExportsApi, ParticipantsApi, PublicExport, ResponsesApi, SurveysApi, UsersApi, WebhooksApi
from ._transport import AskwerkError, Transport

__version__ = "1.0.0"


class Exports(ExportsApi):
    """Exports, with waiting until a file is ready."""

    def wait_until_ready(self, export_id: str, *, interval: float = 2, timeout: float = 600) -> PublicExport:
        """Polls the export until it is ready; raises AskwerkError when it failed or expired, or after the timeout."""
        until = time.monotonic() + timeout
        while True:
            current = self.get(export_id)
            if current["status"] == "ready":
                return current
            if current["status"] in ("failed", "expired"):
                error = current.get("error") or {}
                raise AskwerkError(409, {"type": error.get("type") or current["status"], "title": error.get("title") or f"The export {current['status']}.", "detail": error.get("detail")})
            if time.monotonic() > until:
                raise AskwerkError(408, {"title": "The export was not ready in time."})
            time.sleep(interval)


class Askwerk:
    """A client of the Askwerk Public API: pass an API key, or an OAuth client's id and secret."""

    def __init__(self, base_url: str, *, api_key: str | None = None, client_id: str | None = None, client_secret: str | None = None, scope: str | None = None, timeout: float = 60) -> None:
        transport = Transport(base_url, api_key=api_key, client_id=client_id, client_secret=client_secret, scope=scope, timeout=timeout)
        self.surveys = SurveysApi(transport)
        self.responses = ResponsesApi(transport)
        self.exports = Exports(transport)
        self.webhooks = WebhooksApi(transport)
        self.users = UsersApi(transport)
        self.participants = ParticipantsApi(transport)


def verify_webhook(secret: str, header: str, body: str | bytes, tolerance_seconds: int = 300) -> bool:
    """Checks a delivery's Askwerk-Signature header: HMAC-SHA256 of "{t}.{body}" with the webhook's secret, at most
    tolerance_seconds old. Pass the raw request body."""
    parts = dict(part.split("=", 1) for part in header.split(",") if "=" in part)
    if "t" not in parts or "v1" not in parts or not parts["t"].lstrip("-").isdigit():
        return False
    if abs(time.time() - int(parts["t"])) > tolerance_seconds:
        return False
    raw = body if isinstance(body, bytes) else body.encode()
    expected = hmac.new(secret.encode(), parts["t"].encode() + b"." + raw, hashlib.sha256).hexdigest()
    return hmac.compare_digest(expected, parts["v1"])


__all__ = ["Askwerk", "AskwerkError", "Exports", "verify_webhook"]

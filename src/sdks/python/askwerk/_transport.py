# SPDX-License-Identifier: Apache-2.0
"""The hand-written part of the Python SDK: authentication, requests, errors, retries and paging."""
from __future__ import annotations

import json
import time
import urllib.error
import urllib.parse
import urllib.request
from collections.abc import Callable, Iterator
from typing import Any

MAX_RETRIES = 3


class AskwerkError(Exception):
    """Every error answer of the Public API, with its problem details."""

    def __init__(self, status: int, problem: dict[str, Any]) -> None:
        self.status = status
        self.type: str | None = problem.get("type") or problem.get("error")
        """The stable problem type, e.g. https://askwerk.org/problems/not-published."""
        self.title: str | None = problem.get("title") or problem.get("error_description")
        self.detail: str | None = problem.get("detail")
        self.trace_id: str | None = problem.get("traceId")
        """Helps an administrator find the call in the logs."""
        super().__init__(self.title or f"HTTP {status}")


class Transport:
    def __init__(self, base_url: str, *, api_key: str | None = None, client_id: str | None = None, client_secret: str | None = None, scope: str | None = None, timeout: float = 60) -> None:
        if api_key is None and (client_id is None or client_secret is None):
            raise ValueError("Pass api_key, or client_id and client_secret.")
        self._base = base_url.rstrip("/")
        self._api_key = api_key
        self._client = (client_id, client_secret, scope)
        self._timeout = timeout
        self._token: tuple[str, float] | None = None

    def request(self, method: str, path: str, *, query: dict[str, Any] | None = None, body: Any = None, idempotency_key: str | None = None, binary: bool = False) -> Any:
        url = self._base + path
        params = {name: _text(value) for name, value in (query or {}).items() if value is not None}
        if params:
            url += "?" + urllib.parse.urlencode(params)
        data = None if body is None else json.dumps({key: value for key, value in body.items() if value is not None}).encode()

        for attempt in range(MAX_RETRIES + 1):
            headers = {"Authorization": f"Bearer {self._bearer(renew=attempt > 0)}", "Accept": "application/json"}
            if data is not None:
                headers["Content-Type"] = "application/json"
            if idempotency_key is not None:
                headers["Idempotency-Key"] = idempotency_key
            try:
                with urllib.request.urlopen(urllib.request.Request(url, data=data, method=method, headers=headers), timeout=self._timeout) as response:
                    content = response.read()
                    if binary:
                        return content
                    return json.loads(content) if content else None
            except urllib.error.HTTPError as error:
                if error.code == 429 and attempt < MAX_RETRIES:
                    time.sleep(float(error.headers.get("Retry-After") or 1))
                    continue
                if error.code == 401 and self._api_key is None and attempt == 0:
                    self._token = None
                    continue
                raise AskwerkError(error.code, _problem(error)) from None
        raise AssertionError("unreachable")

    def by_cursor(self, page: Callable[[str | None], Any]) -> Iterator[Any]:
        cursor: str | None = None
        while True:
            current = page(cursor)
            yield from current["items"]
            cursor = current.get("nextCursor")
            if cursor is None:
                return

    def by_sequence(self, since: int, page: Callable[[int], Any]) -> Iterator[Any]:
        while True:
            current = page(since)
            if not current["items"]:
                return
            yield from current["items"]
            since = current["nextSince"]

    def _bearer(self, *, renew: bool) -> str:
        if self._api_key is not None:
            return self._api_key
        if not renew and self._token is not None and self._token[1] > time.time():
            return self._token[0]
        client_id, client_secret, scope = self._client
        form = {"grant_type": "client_credentials", "client_id": client_id, "client_secret": client_secret}
        if scope is not None:
            form["scope"] = scope
        request = urllib.request.Request(self._base + "/api/v1/oauth/token", data=urllib.parse.urlencode(form).encode(), method="POST", headers={"Content-Type": "application/x-www-form-urlencoded"})
        try:
            with urllib.request.urlopen(request, timeout=self._timeout) as response:
                token = json.loads(response.read())
        except urllib.error.HTTPError as error:
            raise AskwerkError(error.code, _problem(error)) from None
        self._token = (token["access_token"], time.time() + token["expires_in"] - 60)
        return self._token[0]


def _text(value: Any) -> str:
    if isinstance(value, bool):
        return "true" if value else "false"
    return str(value)


def _problem(error: urllib.error.HTTPError) -> dict[str, Any]:
    try:
        problem = json.loads(error.read())
        return problem if isinstance(problem, dict) else {}
    except ValueError:
        return {}

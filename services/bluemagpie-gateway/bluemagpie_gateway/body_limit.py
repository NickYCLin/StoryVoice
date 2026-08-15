from __future__ import annotations

from starlette.responses import JSONResponse
from starlette.types import ASGIApp, Message, Receive, Scope, Send


class RequestBodyLimitMiddleware:
    """Reject oversized speech bodies before routing or Pydantic sees them."""

    def __init__(
        self,
        app: ASGIApp,
        *,
        max_bytes: int,
        path: str,
    ) -> None:
        if max_bytes <= 0:
            raise ValueError("max_bytes must be positive")
        self._app = app
        self._max_bytes = max_bytes
        self._path = path

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        if (
            scope["type"] != "http"
            or scope.get("path") != self._path
            or scope.get("method") != "POST"
        ):
            await self._app(scope, receive, send)
            return

        content_lengths = [
            value
            for name, value in scope.get("headers", [])
            if name.lower() == b"content-length"
        ]
        if content_lengths:
            try:
                declared_lengths = {int(value) for value in content_lengths}
            except ValueError:
                await self._reject_bad_length(scope, receive, send)
                return

            if len(declared_lengths) != 1 or next(iter(declared_lengths)) < 0:
                await self._reject_bad_length(scope, receive, send)
                return
            if next(iter(declared_lengths)) > self._max_bytes:
                await self._reject_too_large(scope, receive, send)
                return

        chunks: list[bytes] = []
        received_bytes = 0
        while True:
            message = await receive()
            if message["type"] == "http.disconnect":
                return
            if message["type"] != "http.request":
                continue

            chunk = message.get("body", b"")
            received_bytes += len(chunk)
            if received_bytes > self._max_bytes:
                await self._reject_too_large(scope, receive, send)
                return
            if chunk:
                chunks.append(chunk)
            if not message.get("more_body", False):
                break

        body = b"".join(chunks)
        replayed = False

        async def replay_receive() -> Message:
            nonlocal replayed
            if not replayed:
                replayed = True
                return {
                    "type": "http.request",
                    "body": body,
                    "more_body": False,
                }
            return await receive()

        await self._app(scope, replay_receive, send)

    @staticmethod
    async def _reject_bad_length(
        scope: Scope,
        receive: Receive,
        send: Send,
    ) -> None:
        response = JSONResponse(
            status_code=400,
            content={"detail": "invalid content length"},
        )
        await response(scope, receive, send)

    @staticmethod
    async def _reject_too_large(
        scope: Scope,
        receive: Receive,
        send: Send,
    ) -> None:
        response = JSONResponse(
            status_code=413,
            content={"detail": "request body too large"},
        )
        await response(scope, receive, send)

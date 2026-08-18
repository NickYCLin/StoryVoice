from __future__ import annotations

import hashlib
import logging
import secrets

from starlette.responses import JSONResponse
from starlette.types import ASGIApp, Message, Receive, Scope, Send


logger = logging.getLogger("local_clone_gateway")
SYNTHESIS_STAGE_HEADER = "X-StoryVoice-Local-Clone-Stage"
SYNTHESIS_REJECTION_STAGES = frozenset(
    {
        "auth",
        "content_length",
        "multipart",
        "request_contract",
        "upstream_readiness",
        "admission",
        "queue",
        "upstream_terminal",
        "upstream_contract",
    }
)


class RequestBodyLimitMiddleware:
    """Bound synthesis bodies before multipart parsing allocates resources."""

    def __init__(
        self,
        app: ASGIApp,
        *,
        max_bytes: int,
        path: str,
        token_header: str,
        internal_token: str,
    ) -> None:
        if max_bytes <= 0:
            raise ValueError("max_bytes must be positive")
        self._app = app
        self._max_bytes = max_bytes
        self._path = path
        self._token_header = token_header.lower().encode("ascii")
        self._token_digest = hashlib.sha256(
            internal_token.encode("ascii")
        ).digest()

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        if (
            scope["type"] != "http"
            or scope.get("path") != self._path
            or scope.get("method") != "POST"
        ):
            await self._app(scope, receive, send)
            return

        token_values = [
            value
            for name, value in scope.get("headers", [])
            if name.lower() == self._token_header
        ]
        candidate = token_values[0] if len(token_values) == 1 else b""
        candidate_digest = hashlib.sha256(candidate).digest()
        token_matches = secrets.compare_digest(
            candidate_digest,
            self._token_digest,
        )
        if len(token_values) != 1 or not token_matches:
            await self._reject(scope, receive, send, 401, "unauthorized", "auth")
            return

        content_lengths = [
            value
            for name, value in scope.get("headers", [])
            if name.lower() == b"content-length"
        ]
        if content_lengths:
            try:
                declared = {int(value) for value in content_lengths}
            except ValueError:
                await self._reject(
                    scope, receive, send, 400, "invalid content length", "content_length"
                )
                return
            if len(declared) != 1 or next(iter(declared)) < 0:
                await self._reject(
                    scope, receive, send, 400, "invalid content length", "content_length"
                )
                return
            if next(iter(declared)) > self._max_bytes:
                await self._reject(
                    scope, receive, send, 413, "request body too large", "content_length"
                )
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
                await self._reject(
                    scope, receive, send, 413, "request body too large", "content_length"
                )
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
    async def _reject(
        scope: Scope,
        receive: Receive,
        send: Send,
        status_code: int,
        detail: str,
        stage: str,
    ) -> None:
        if stage not in SYNTHESIS_REJECTION_STAGES:
            raise RuntimeError("invalid synthesis rejection stage")
        logger.warning(
            "synthesis_rejected stage=%s status=%d",
            stage,
            status_code,
        )
        response = JSONResponse(
            status_code=status_code,
            content={"detail": detail},
            headers={SYNTHESIS_STAGE_HEADER: stage},
        )
        await response(scope, receive, send)

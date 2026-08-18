from __future__ import annotations

import json
from dataclasses import dataclass
from typing import Protocol

import httpx

from .constants import (
    COSYVOICE_SOURCE_REVISION,
    MAX_HEALTH_RESPONSE_BYTES,
    MAX_OUTPUT_BYTES,
    MODEL_ID,
    MODEL_ID_HEADER,
    MODEL_REVISION,
    MODEL_REVISION_HEADER,
    SOURCE_REVISION_HEADER,
    UPSTREAM_HEALTH_PATH,
    UPSTREAM_ORIGIN,
    UPSTREAM_SYNTHESIS_PATH,
)


class UpstreamRejected(RuntimeError):
    """The fixed upstream returned a terminal non-success response."""


class UpstreamProtocolError(RuntimeError):
    """The fixed upstream returned a terminal but invalid response."""


class UpstreamTransportUncertain(RuntimeError):
    """The request ended without proof that upstream inference is terminal."""


@dataclass(frozen=True, slots=True)
class UpstreamAttestation:
    source_revision: str
    model_id: str
    model_revision: str


@dataclass(frozen=True, slots=True)
class UpstreamSynthesis:
    content: bytes
    attestation: UpstreamAttestation


class CloneUpstream(Protocol):
    async def readiness(self) -> UpstreamAttestation | None: ...

    async def synthesize(
        self,
        text: str,
        reference_text: str,
        reference_audio: bytes,
    ) -> UpstreamSynthesis: ...

    async def close(self) -> None: ...


class FaceSpeakUpstream:
    """Fixed-origin FaceSpeak adapter with proxies and redirects disabled."""

    def __init__(
        self,
        *,
        transport: httpx.AsyncBaseTransport | None = None,
    ) -> None:
        self._client = httpx.AsyncClient(
            base_url=UPSTREAM_ORIGIN,
            follow_redirects=False,
            trust_env=False,
            timeout=None,
            transport=transport,
            headers={
                "Accept-Encoding": "identity",
                "User-Agent": "storyvoice-local-clone-gateway/1",
            },
        )

    async def readiness(self) -> UpstreamAttestation | None:
        try:
            async with self._client.stream(
                "GET",
                UPSTREAM_HEALTH_PATH,
            ) as response:
                if response.status_code != 200:
                    return None
                media_type = response.headers.get("content-type", "").split(
                    ";",
                    1,
                )[0].strip().lower()
                if media_type != "application/json":
                    return None
                body = await self._read_bounded(
                    response,
                    MAX_HEALTH_RESPONSE_BYTES,
                )
            payload = json.loads(body)
        except (
            httpx.HTTPError,
            UnicodeDecodeError,
            json.JSONDecodeError,
            ValueError,
            UpstreamProtocolError,
        ):
            return None

        exact = (
            isinstance(payload, dict)
            and set(payload)
            == {"ready", "sourceRevision", "modelId", "modelRevision"}
            and payload["ready"] is True
            and payload["sourceRevision"] == COSYVOICE_SOURCE_REVISION
            and payload["modelId"] == MODEL_ID
            and payload["modelRevision"] == MODEL_REVISION
        )
        if not exact:
            return None
        return UpstreamAttestation(
            source_revision=payload["sourceRevision"],
            model_id=payload["modelId"],
            model_revision=payload["modelRevision"],
        )

    async def synthesize(
        self,
        text: str,
        reference_text: str,
        reference_audio: bytes,
    ) -> UpstreamSynthesis:
        try:
            async with self._client.stream(
                "POST",
                UPSTREAM_SYNTHESIS_PATH,
                data={"text": text, "reference_text": reference_text},
                files={
                    "reference_audio": (
                        "reference.wav",
                        reference_audio,
                        "audio/wav",
                    )
                },
            ) as response:
                # Redirects are terminal failures; the gateway never follows a
                # Location header to a second origin (or even another path).
                if response.status_code != 200:
                    raise UpstreamRejected("upstream rejected synthesis")
                media_type = response.headers.get("content-type", "").split(
                    ";",
                    1,
                )[0].strip().lower()
                if media_type != "audio/wav":
                    raise UpstreamProtocolError("upstream returned invalid media")
                attestation = self._validated_attestation(response.headers)
                content = await self._read_bounded(response, MAX_OUTPUT_BYTES)
                return UpstreamSynthesis(
                    content=content,
                    attestation=attestation,
                )
        except (UpstreamRejected, UpstreamProtocolError):
            raise
        except httpx.HTTPError as exc:
            # A transport failure can lose a response while the separate
            # FaceSpeak process is still running inference. The gateway exits
            # fail-closed; the executor owns its GPU lock and termination.
            raise UpstreamTransportUncertain(
                "upstream inference state is uncertain"
            ) from exc

    async def close(self) -> None:
        await self._client.aclose()

    @staticmethod
    def _validated_attestation(headers: httpx.Headers) -> UpstreamAttestation:
        expected = (
            (SOURCE_REVISION_HEADER, COSYVOICE_SOURCE_REVISION),
            (MODEL_ID_HEADER, MODEL_ID),
            (MODEL_REVISION_HEADER, MODEL_REVISION),
        )
        observed: dict[str, str] = {}
        for name, exact_value in expected:
            values = headers.get_list(name)
            if values != [exact_value]:
                raise UpstreamProtocolError("invalid upstream attestation")
            observed[name] = values[0]
        return UpstreamAttestation(
            source_revision=observed[SOURCE_REVISION_HEADER],
            model_id=observed[MODEL_ID_HEADER],
            model_revision=observed[MODEL_REVISION_HEADER],
        )

    @staticmethod
    async def _read_bounded(response: httpx.Response, max_bytes: int) -> bytes:
        content_encoding = response.headers.get("content-encoding", "identity")
        if content_encoding.lower().strip() not in {"", "identity"}:
            raise UpstreamProtocolError("encoded upstream responses are forbidden")
        declared = response.headers.get("content-length")
        if declared is not None:
            try:
                declared_bytes = int(declared)
            except ValueError as exc:
                raise UpstreamProtocolError("invalid upstream content length") from exc
            if declared_bytes < 0 or declared_bytes > max_bytes:
                raise UpstreamProtocolError("upstream response is too large")

        chunks: list[bytes] = []
        received = 0
        async for chunk in response.aiter_bytes():
            received += len(chunk)
            if received > max_bytes:
                raise UpstreamProtocolError("upstream response is too large")
            chunks.append(chunk)
        return b"".join(chunks)

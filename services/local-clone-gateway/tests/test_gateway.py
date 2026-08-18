from __future__ import annotations

import asyncio
import io
import logging
import wave
from pathlib import Path

import httpx
import pytest
from fastapi.testclient import TestClient

import local_clone_gateway.body_limit as body_limit_module
from local_clone_gateway.app import create_app
from local_clone_gateway.body_limit import RequestBodyLimitMiddleware
from local_clone_gateway.constants import (
    COSYVOICE_SOURCE_REVISION,
    MAX_OUTPUT_BYTES,
    MAX_REFERENCE_AUDIO_BYTES,
    MAX_REQUEST_BODY_BYTES,
    MODEL_ID,
    MODEL_ID_HEADER,
    MODEL_REVISION,
    MODEL_REVISION_HEADER,
    SOURCE_REVISION_HEADER,
    TOKEN_HEADER,
    UPSTREAM_ORIGIN,
    UPSTREAM_SYNTHESIS_PATH,
)
from local_clone_gateway.settings import GatewaySettings
from local_clone_gateway.upstream import (
    FaceSpeakUpstream,
    UpstreamAttestation,
    UpstreamProtocolError,
    UpstreamRejected,
    UpstreamSynthesis,
    UpstreamTransportUncertain,
)


TOKEN = "offline-contract-token-at-least-32-characters"
AUTH_HEADERS = {TOKEN_HEADER: TOKEN}
PRIVATE_TEXT = "尚未公開的小說正文"
PRIVATE_TRANSCRIPT = "本人授權但不得寫進記錄的逐字稿"


def pcm_wave(
    *,
    sample_rate: int = 24_000,
    channels: int = 1,
    sample_width: int = 2,
    frames: int = 240,
) -> bytes:
    output = io.BytesIO()
    with wave.open(output, "wb") as stream:
        stream.setnchannels(channels)
        stream.setsampwidth(sample_width)
        stream.setframerate(sample_rate)
        stream.writeframes(b"\x00" * frames * channels * sample_width)
    return output.getvalue()


def add_junk_chunk(content: bytes, payload_bytes: int) -> bytes:
    padding = b"\x00" if payload_bytes % 2 else b""
    chunk = (
        b"JUNK"
        + payload_bytes.to_bytes(4, "little")
        + (b"\x00" * payload_bytes)
        + padding
    )
    result = content[:12] + chunk + content[12:]
    return result[:4] + (len(result) - 8).to_bytes(4, "little") + result[8:]


REFERENCE_AUDIO = pcm_wave(sample_rate=48_000, frames=48_000 * 10)
ATTESTATION = UpstreamAttestation(
    source_revision=COSYVOICE_SOURCE_REVISION,
    model_id=MODEL_ID,
    model_revision=MODEL_REVISION,
)
ATTESTATION_HEADERS = {
    SOURCE_REVISION_HEADER: COSYVOICE_SOURCE_REVISION,
    MODEL_ID_HEADER: MODEL_ID,
    MODEL_REVISION_HEADER: MODEL_REVISION,
}


class FakeUpstream:
    def __init__(
        self,
        content: bytes | None = None,
        *,
        ready: bool = True,
        attestation: UpstreamAttestation = ATTESTATION,
        synthesis_attestation: UpstreamAttestation | None = None,
    ) -> None:
        self.content = content if content is not None else pcm_wave()
        self.ready = ready
        self.attestation = attestation
        self.synthesis_attestation = synthesis_attestation or attestation
        self.ready_calls = 0
        self.calls: list[tuple[str, str, bytes]] = []
        self.closed = 0

    async def readiness(self) -> UpstreamAttestation | None:
        self.ready_calls += 1
        return self.attestation if self.ready else None

    async def synthesize(
        self,
        text: str,
        reference_text: str,
        reference_audio: bytes,
    ) -> UpstreamSynthesis:
        self.calls.append((text, reference_text, reference_audio))
        return UpstreamSynthesis(
            content=self.content,
            attestation=self.synthesis_attestation,
        )

    async def close(self) -> None:
        self.closed += 1


def build_app(
    upstream: FakeUpstream,
    *,
    queue_timeout_seconds: float = 0.1,
    watchdog_seconds: float = 2.0,
    fatal_exit=None,
):
    return create_app(
        GatewaySettings(
            internal_token=TOKEN,
            queue_timeout_seconds=queue_timeout_seconds,
            synthesis_watchdog_seconds=watchdog_seconds,
        ),
        lambda: upstream,
        fatal_exit=fatal_exit,
    )


def post_synthesis(
    client: TestClient,
    *,
    headers: dict[str, str] | None = None,
    text: str = "台灣華語試音。",
    reference_text: str = "這是逐字相符的參考錄音。",
    audio: bytes = REFERENCE_AUDIO,
    content_type: str = "audio/wav",
    filename: str = "reference.wav",
):
    return client.post(
        UPSTREAM_SYNTHESIS_PATH,
        headers=headers,
        data={"text": text, "reference_text": reference_text},
        files={"reference_audio": (filename, audio, content_type)},
    )


def test_lifecycle_and_exact_health_pins() -> None:
    upstream = FakeUpstream()
    app = build_app(upstream)

    with TestClient(app) as client:
        assert client.get("/health/live").json() == {"status": "live"}
        ready = client.get("/health/ready")
        assert ready.status_code == 200
        assert ready.json() == {
            "status": "ready",
            "source_revision": COSYVOICE_SOURCE_REVISION,
            "model_id": MODEL_ID,
            "model_revision": MODEL_REVISION,
        }

    assert upstream.closed == 1


def test_readiness_fails_closed_when_upstream_is_unready() -> None:
    upstream = FakeUpstream(ready=False)
    app = build_app(upstream)

    with TestClient(app) as client:
        response = client.get("/health/ready")

    assert response.status_code == 503
    assert response.json() == {"status": "not_ready"}


def test_gateway_never_self_attests_an_unverified_upstream() -> None:
    wrong = UpstreamAttestation(
        source_revision="wrong",
        model_id=MODEL_ID,
        model_revision=MODEL_REVISION,
    )
    health_app = build_app(FakeUpstream(attestation=wrong))
    speech_app = build_app(
        FakeUpstream(
            attestation=ATTESTATION,
            synthesis_attestation=wrong,
        )
    )

    with TestClient(health_app) as client:
        health = client.get("/health/ready")
    with TestClient(speech_app) as client:
        speech = post_synthesis(client, headers=AUTH_HEADERS)

    assert health.status_code == 503
    assert speech.status_code == 503
    assert SOURCE_REVISION_HEADER not in speech.headers
    assert MODEL_ID_HEADER not in speech.headers
    assert MODEL_REVISION_HEADER not in speech.headers


def test_synthesis_returns_only_pinned_pcm_contract_and_no_store() -> None:
    upstream = FakeUpstream()
    app = build_app(upstream)

    with TestClient(app) as client:
        response = post_synthesis(
            client,
            headers=AUTH_HEADERS,
            audio=REFERENCE_AUDIO,
        )

    assert response.status_code == 200
    assert response.headers["content-type"] == "audio/wav"
    assert response.headers[SOURCE_REVISION_HEADER] == COSYVOICE_SOURCE_REVISION
    assert response.headers[MODEL_ID_HEADER] == MODEL_ID
    assert response.headers[MODEL_REVISION_HEADER] == MODEL_REVISION
    assert response.headers["cache-control"] == "no-store"
    assert response.headers["pragma"] == "no-cache"
    assert response.headers["x-content-type-options"] == "nosniff"
    assert int(response.headers["content-length"]) == len(response.content)
    assert response.content == pcm_wave()
    assert upstream.calls == [
        (
            "台灣華語試音。",
            "這是逐字相符的參考錄音。",
            REFERENCE_AUDIO,
        )
    ]


def test_internal_token_is_required() -> None:
    upstream = FakeUpstream()
    app = build_app(upstream)

    with TestClient(app) as client:
        missing = post_synthesis(client)
        wrong = post_synthesis(client, headers={TOKEN_HEADER: "wrong"})

    assert missing.status_code == 401
    assert wrong.status_code == 401
    assert missing.json() == wrong.json() == {"detail": "unauthorized"}
    assert upstream.calls == []


@pytest.mark.parametrize(
    ("token", "message"),
    [
        ("x" * 31, "between 32 and 512"),
        ("x" * 31 + "\n", "only printable ASCII"),
        ("x" * 31 + " ", "only printable ASCII"),
        ("密" * 32, "only printable ASCII"),
        ("x" * 513, "between 32 and 512"),
    ],
    ids=("short", "newline", "space", "unicode", "too-long"),
)
def test_internal_token_configuration_contract(
    token: str,
    message: str,
    monkeypatch,
) -> None:
    monkeypatch.setenv("LOCAL_CLONE_INTERNAL_TOKEN", token)

    with pytest.raises(RuntimeError, match=message):
        GatewaySettings.from_env()


@pytest.mark.parametrize("length", [32, 512])
def test_internal_token_accepts_both_length_boundaries(length: int) -> None:
    settings = GatewaySettings(internal_token="!" * length)

    assert settings.internal_token == "!" * length


@pytest.mark.parametrize(
    "headers",
    [
        [
            (TOKEN_HEADER.lower().encode("ascii"), b"wrong-token"),
            (b"content-length", str(MAX_REQUEST_BODY_BYTES + 1).encode("ascii")),
        ],
        [
            (TOKEN_HEADER.lower().encode("ascii"), b"wrong-token"),
            (b"transfer-encoding", b"chunked"),
        ],
        [
            (b"content-length", str(MAX_REQUEST_BODY_BYTES + 1).encode("ascii")),
        ],
    ],
    ids=("declared-wrong", "streamed-wrong", "declared-missing"),
)
def test_asgi_token_precheck_never_reads_unauthorized_body(
    headers: list[tuple[bytes, bytes]],
    monkeypatch,
) -> None:
    async def exercise() -> None:
        downstream_called = False
        receive_called = False
        sent: list[dict] = []
        compare_calls: list[tuple[bytes, bytes]] = []
        original_compare = body_limit_module.secrets.compare_digest

        def recording_compare(left: bytes, right: bytes) -> bool:
            compare_calls.append((left, right))
            return original_compare(left, right)

        monkeypatch.setattr(
            body_limit_module.secrets,
            "compare_digest",
            recording_compare,
        )

        async def downstream(_scope, _receive, _send) -> None:
            nonlocal downstream_called
            downstream_called = True

        async def receive() -> dict:
            nonlocal receive_called
            receive_called = True
            raise AssertionError("unauthorized request body was read")

        async def send(message: dict) -> None:
            sent.append(message)

        middleware = RequestBodyLimitMiddleware(
            downstream,
            max_bytes=MAX_REQUEST_BODY_BYTES,
            path=UPSTREAM_SYNTHESIS_PATH,
            token_header=TOKEN_HEADER,
            internal_token=TOKEN,
        )
        scope = {
            "type": "http",
            "asgi": {"version": "3.0"},
            "http_version": "1.1",
            "method": "POST",
            "scheme": "http",
            "path": UPSTREAM_SYNTHESIS_PATH,
            "raw_path": UPSTREAM_SYNTHESIS_PATH.encode("ascii"),
            "query_string": b"",
            "root_path": "",
            "headers": headers,
            "client": ("127.0.0.1", 12345),
            "server": ("test", 80),
        }

        await middleware(scope, receive, send)

        assert downstream_called is False
        assert receive_called is False
        assert sent[0]["type"] == "http.response.start"
        assert sent[0]["status"] == 401
        assert sent[1]["body"] == b'{"detail":"unauthorized"}'
        assert len(compare_calls) == 1
        assert len(compare_calls[0][0]) == 32
        assert len(compare_calls[0][1]) == 32

    asyncio.run(exercise())


def test_gateway_has_no_redis_runtime_dependency_or_setting() -> None:
    service_root = Path(__file__).resolve().parents[1]
    runtime_files = (
        service_root / "requirements.txt",
        service_root / "local_clone_gateway" / "app.py",
        service_root / "local_clone_gateway" / "constants.py",
        service_root / "local_clone_gateway" / "settings.py",
    )

    for path in runtime_files:
        assert "redis" not in path.read_text(encoding="utf-8").lower()
    assert not (service_root / "local_clone_gateway" / "gpu_lock.py").exists()


def test_private_fields_and_filename_are_neither_logged_nor_reflected(caplog) -> None:
    class RejectingUpstream(FakeUpstream):
        async def synthesize(self, text, reference_text, reference_audio):
            raise UpstreamRejected(f"{text}:{reference_text}")

    upstream = RejectingUpstream()
    private_filename = "C:\\private\\voice-owner-name.wav"
    app = build_app(upstream)

    with caplog.at_level(logging.INFO, logger="local_clone_gateway"):
        with TestClient(app) as client:
            response = post_synthesis(
                client,
                headers=AUTH_HEADERS,
                text=PRIVATE_TEXT,
                reference_text=PRIVATE_TRANSCRIPT,
                filename=private_filename,
            )

    assert response.status_code == 503
    assert response.json() == {"detail": "synthesis unavailable"}
    for secret in (PRIVATE_TEXT, PRIVATE_TRANSCRIPT, private_filename):
        assert secret not in response.text
        assert secret not in caplog.text


def test_schema_is_exact_and_never_reflects_rejected_values() -> None:
    upstream = FakeUpstream()
    app = build_app(upstream)
    invalid_cases = (
        ({"text": " ", "reference_text": "逐字稿"}, {}),
        ({"text": "內容", "reference_text": " "}, {}),
        ({"text": "字" * 1_001, "reference_text": "逐字稿"}, {}),
        (
            {
                "text": "內容",
                "reference_text": "逐字稿",
                "private_extra_field": "不可反射",
            },
            {},
        ),
    )

    with TestClient(app) as client:
        responses = [
            client.post(
                UPSTREAM_SYNTHESIS_PATH,
                headers=AUTH_HEADERS,
                data=data,
                files={
                    "reference_audio": (
                        "private-name.wav",
                        REFERENCE_AUDIO,
                        "audio/wav",
                    )
                },
                **extra,
            )
            for data, extra in invalid_cases
        ]

    assert [item.status_code for item in responses] == [422, 422, 422, 400]
    assert all(item.json() == {"detail": "invalid request"} for item in responses)
    assert "不可反射" not in responses[-1].text
    assert upstream.calls == []


def test_reference_audio_type_and_ten_mib_limit_are_enforced() -> None:
    upstream = FakeUpstream()
    app = build_app(upstream)

    with TestClient(app) as client:
        wrong_type = post_synthesis(
            client,
            headers=AUTH_HEADERS,
            content_type="application/octet-stream",
        )
        too_large = post_synthesis(
            client,
            headers=AUTH_HEADERS,
            audio=b"x" * (MAX_REFERENCE_AUDIO_BYTES + 1),
        )

    assert wrong_type.status_code == 415
    assert wrong_type.json() == {"detail": "unsupported media type"}
    assert too_large.status_code == 413
    assert too_large.json() == {"detail": "request body too large"}
    assert upstream.calls == []


def test_existing_2_4_mb_reference_wav_fits_the_gateway_contract() -> None:
    upstream = FakeUpstream()
    app = build_app(upstream)
    reference = add_junk_chunk(
        pcm_wave(sample_rate=48_000, frames=48_000 * 25),
        26,
    )
    assert len(reference) == 2_400_078

    with TestClient(app) as client:
        response = post_synthesis(
            client,
            headers=AUTH_HEADERS,
            audio=reference,
        )

    assert response.status_code == 200
    assert len(upstream.calls) == 1
    assert upstream.calls[0][2] == reference


@pytest.mark.parametrize(
    "invalid_reference",
    [
        b"not a wav",
        pcm_wave(sample_rate=24_000, frames=24_000 * 10),
        pcm_wave(sample_rate=48_000, channels=2, frames=48_000 * 10),
        pcm_wave(sample_rate=48_000, sample_width=1, frames=48_000 * 10),
        pcm_wave(sample_rate=48_000, frames=48_000 * 10 - 1),
        pcm_wave(sample_rate=48_000, frames=48_000 * 45 + 1),
    ],
    ids=(
        "not-wav",
        "wrong-rate",
        "stereo",
        "wrong-width",
        "too-short",
        "too-long",
    ),
)
def test_reference_must_be_pcm16_48khz_mono_for_ten_to_45_seconds(
    invalid_reference: bytes,
) -> None:
    upstream = FakeUpstream()
    app = build_app(upstream)

    with TestClient(app) as client:
        response = post_synthesis(
            client,
            headers=AUTH_HEADERS,
            audio=invalid_reference,
        )

    assert response.status_code == 422
    assert response.json() == {"detail": "invalid request"}
    assert upstream.calls == []


def test_asgi_body_cap_rejects_declared_and_streamed_oversize_before_parser() -> None:
    upstream = FakeUpstream()
    app = build_app(upstream)

    def oversized_stream():
        yield b"a" * (MAX_REQUEST_BODY_BYTES // 2)
        yield b"b" * (MAX_REQUEST_BODY_BYTES // 2 + 2)

    with TestClient(app) as client:
        declared = client.post(
            UPSTREAM_SYNTHESIS_PATH,
            headers={
                **AUTH_HEADERS,
                "Content-Type": "multipart/form-data; boundary=fixed",
                "Content-Length": str(MAX_REQUEST_BODY_BYTES + 1),
            },
            content=b"small",
        )
        streamed = client.post(
            UPSTREAM_SYNTHESIS_PATH,
            headers={
                **AUTH_HEADERS,
                "Content-Type": "multipart/form-data; boundary=fixed",
            },
            content=oversized_stream(),
        )

    assert declared.status_code == 413
    assert streamed.status_code == 413
    assert declared.json() == streamed.json() == {
        "detail": "request body too large"
    }
    assert upstream.calls == []


@pytest.mark.parametrize(
    "invalid_audio",
    [
        b"not a wav",
        pcm_wave(sample_rate=48_000),
        pcm_wave(channels=2),
        pcm_wave(sample_width=1),
        b"x" * (MAX_OUTPUT_BYTES + 1),
    ],
    ids=("not-wav", "wrong-rate", "stereo", "wrong-width", "oversized"),
)
def test_output_must_be_bounded_pcm16_24khz_mono(invalid_audio: bytes) -> None:
    upstream = FakeUpstream(invalid_audio)
    app = build_app(upstream)

    with TestClient(app) as client:
        response = post_synthesis(client, headers=AUTH_HEADERS)

    assert response.status_code == 503
    assert response.json() == {"detail": "synthesis unavailable"}


class BlockingUpstream(FakeUpstream):
    def __init__(self) -> None:
        super().__init__()
        self.started = asyncio.Event()
        self.release = asyncio.Event()

    async def synthesize(self, text, reference_text, reference_audio):
        self.calls.append((text, reference_text, reference_audio))
        self.started.set()
        await self.release.wait()
        return UpstreamSynthesis(
            content=self.content,
            attestation=self.attestation,
        )


def test_process_local_single_flight_has_a_bounded_queue() -> None:
    async def exercise() -> None:
        upstream = BlockingUpstream()
        app = build_app(upstream, queue_timeout_seconds=0.1)

        async def wait_for_ready_calls(expected: int) -> None:
            while upstream.ready_calls < expected:
                await asyncio.sleep(0)

        async with app.router.lifespan_context(app):
            transport = httpx.ASGITransport(app=app)
            async with httpx.AsyncClient(
                transport=transport,
                base_url="http://test",
            ) as client:
                first = asyncio.create_task(
                    client.post(
                        UPSTREAM_SYNTHESIS_PATH,
                        headers=AUTH_HEADERS,
                        data={"text": "第一句", "reference_text": PRIVATE_TRANSCRIPT},
                        files={
                            "reference_audio": (
                                "reference.wav",
                                REFERENCE_AUDIO,
                                "audio/wav",
                            )
                        },
                    )
                )
                await asyncio.wait_for(upstream.started.wait(), timeout=1)
                second = asyncio.create_task(
                    client.post(
                        UPSTREAM_SYNTHESIS_PATH,
                        headers=AUTH_HEADERS,
                        data={"text": "第二句", "reference_text": PRIVATE_TRANSCRIPT},
                        files={
                            "reference_audio": (
                                "reference.wav",
                                REFERENCE_AUDIO,
                                "audio/wav",
                            )
                        },
                    )
                )
                await asyncio.wait_for(wait_for_ready_calls(2), timeout=1)
                third = await client.post(
                    UPSTREAM_SYNTHESIS_PATH,
                    headers=AUTH_HEADERS,
                    data={"text": "第三句", "reference_text": PRIVATE_TRANSCRIPT},
                    files={
                        "reference_audio": (
                            "reference.wav",
                            REFERENCE_AUDIO,
                            "audio/wav",
                        )
                    },
                )
                second_response = await second

                assert third.status_code == 503
                assert third.headers["retry-after"] == "1"
                assert second_response.status_code == 503
                assert second_response.headers["retry-after"] == "1"
                assert len(upstream.calls) == 1

                upstream.release.set()
                assert (await first).status_code == 200

    asyncio.run(exercise())


def test_client_cancellation_waits_for_upstream_terminal() -> None:
    async def exercise() -> None:
        upstream = BlockingUpstream()
        app = build_app(upstream)
        async with app.router.lifespan_context(app):
            transport = httpx.ASGITransport(app=app)
            async with httpx.AsyncClient(
                transport=transport,
                base_url="http://test",
            ) as client:
                request = asyncio.create_task(
                    client.post(
                        UPSTREAM_SYNTHESIS_PATH,
                        headers=AUTH_HEADERS,
                        data={"text": PRIVATE_TEXT, "reference_text": PRIVATE_TRANSCRIPT},
                        files={
                            "reference_audio": (
                                "reference.wav",
                                REFERENCE_AUDIO,
                                "audio/wav",
                            )
                        },
                    )
                )
                await asyncio.wait_for(upstream.started.wait(), timeout=1)
                request.cancel()
                await asyncio.sleep(0.03)
                assert not request.done()

                upstream.release.set()
                with pytest.raises(asyncio.CancelledError):
                    await request

    asyncio.run(exercise())


def test_watchdog_fatally_exits_gateway_for_executor_owned_cleanup() -> None:
    async def exercise() -> None:
        upstream = BlockingUpstream()
        exit_codes: list[int] = []

        def fake_fatal_exit(code: int):
            exit_codes.append(code)

        app = build_app(
            upstream,
            watchdog_seconds=0.03,
            fatal_exit=fake_fatal_exit,
        )
        async with app.router.lifespan_context(app):
            transport = httpx.ASGITransport(app=app)
            async with httpx.AsyncClient(
                transport=transport,
                base_url="http://test",
            ) as client:
                with pytest.raises(SystemExit, match="70"):
                    await client.post(
                        UPSTREAM_SYNTHESIS_PATH,
                        headers=AUTH_HEADERS,
                        data={"text": PRIVATE_TEXT, "reference_text": PRIVATE_TRANSCRIPT},
                        files={
                            "reference_audio": (
                                "reference.wav",
                                REFERENCE_AUDIO,
                                "audio/wav",
                            )
                        },
                    )
                assert exit_codes == [70]
                upstream.release.set()
                await asyncio.sleep(0)

    asyncio.run(exercise())


def test_transport_uncertainty_fatally_exits_without_logging_private_text(caplog) -> None:
    class UncertainUpstream(FakeUpstream):
        async def synthesize(self, text, reference_text, reference_audio):
            raise UpstreamTransportUncertain(f"uncertain: {text}: {reference_text}")

    async def exercise() -> list[int]:
        upstream = UncertainUpstream()
        exit_codes: list[int] = []

        def fake_fatal_exit(code: int):
            exit_codes.append(code)

        app = build_app(
            upstream,
            fatal_exit=fake_fatal_exit,
        )
        async with app.router.lifespan_context(app):
            transport = httpx.ASGITransport(app=app)
            async with httpx.AsyncClient(
                transport=transport,
                base_url="http://test",
            ) as client:
                with pytest.raises(SystemExit, match="70"):
                    await client.post(
                        UPSTREAM_SYNTHESIS_PATH,
                        headers=AUTH_HEADERS,
                        data={"text": PRIVATE_TEXT, "reference_text": PRIVATE_TRANSCRIPT},
                        files={
                            "reference_audio": (
                                "reference.wav",
                                REFERENCE_AUDIO,
                                "audio/wav",
                            )
                        },
                    )
        return exit_codes

    with caplog.at_level(logging.INFO, logger="local_clone_gateway"):
        exit_codes = asyncio.run(exercise())

    assert exit_codes == [70]
    assert PRIVATE_TEXT not in caplog.text
    assert PRIVATE_TRANSCRIPT not in caplog.text


def test_fixed_upstream_ignores_proxy_environment_and_never_redirects(
    monkeypatch,
) -> None:
    async def exercise() -> None:
        requests: list[httpx.Request] = []

        async def handler(request: httpx.Request) -> httpx.Response:
            requests.append(request)
            return httpx.Response(
                302,
                headers={"Location": "http://attacker.invalid/steal"},
            )

        monkeypatch.setenv("HTTP_PROXY", "http://attacker.invalid:9999")
        upstream = FaceSpeakUpstream(transport=httpx.MockTransport(handler))
        try:
            assert upstream._client.follow_redirects is False
            assert upstream._client._trust_env is False
            with pytest.raises(UpstreamRejected):
                await upstream.synthesize("內容", "逐字稿", b"authorized")
        finally:
            await upstream.close()

        assert len(requests) == 1
        assert str(requests[0].url) == (
            f"{UPSTREAM_ORIGIN}{UPSTREAM_SYNTHESIS_PATH}"
        )

    asyncio.run(exercise())


def test_real_adapter_requires_exact_health_and_sanitizes_multipart_filename() -> None:
    async def exercise() -> None:
        received_body = b""

        async def handler(request: httpx.Request) -> httpx.Response:
            nonlocal received_body
            if request.method == "GET":
                return httpx.Response(
                    200,
                    json={
                        "ready": True,
                        "sourceRevision": COSYVOICE_SOURCE_REVISION,
                        "modelId": MODEL_ID,
                        "modelRevision": MODEL_REVISION,
                    },
                )
            received_body = await request.aread()
            return httpx.Response(
                200,
                content=pcm_wave(),
                headers={
                    "Content-Type": "audio/wav",
                    **ATTESTATION_HEADERS,
                },
            )

        upstream = FaceSpeakUpstream(transport=httpx.MockTransport(handler))
        try:
            assert await upstream.readiness() == ATTESTATION
            result = await upstream.synthesize(
                PRIVATE_TEXT,
                PRIVATE_TRANSCRIPT,
                b"authorized",
            )
        finally:
            await upstream.close()

        assert result.content == pcm_wave()
        assert result.attestation == ATTESTATION
        assert b'name="text"' in received_body
        assert b'name="reference_text"' in received_body
        assert b'name="reference_audio"; filename="reference.wav"' in received_body
        assert b"private" not in received_body.lower()

    asyncio.run(exercise())


@pytest.mark.parametrize(
    "payload",
    [
        {"ready": True, "modelId": MODEL_ID, "modelRevision": MODEL_REVISION},
        {
            "ready": True,
            "sourceRevision": "wrong",
            "modelId": MODEL_ID,
            "modelRevision": MODEL_REVISION,
        },
        {
            "ready": True,
            "sourceRevision": COSYVOICE_SOURCE_REVISION,
            "modelId": "wrong",
            "modelRevision": MODEL_REVISION,
        },
        {
            "ready": True,
            "sourceRevision": COSYVOICE_SOURCE_REVISION,
            "modelId": MODEL_ID,
            "modelRevision": "wrong",
        },
        {
            "ready": True,
            "sourceRevision": COSYVOICE_SOURCE_REVISION,
            "modelId": MODEL_ID,
            "modelRevision": MODEL_REVISION,
            "unexpected": True,
        },
    ],
)
def test_real_adapter_rejects_non_exact_health_payload(payload: dict) -> None:
    async def exercise() -> None:
        async def handler(_request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, json=payload)

        upstream = FaceSpeakUpstream(transport=httpx.MockTransport(handler))
        try:
            assert await upstream.readiness() is None
        finally:
            await upstream.close()

    asyncio.run(exercise())


@pytest.mark.parametrize(
    "headers",
    [
        {
            "Content-Type": "audio/wav",
            MODEL_ID_HEADER: MODEL_ID,
            MODEL_REVISION_HEADER: MODEL_REVISION,
        },
        {
            "Content-Type": "audio/wav",
            **ATTESTATION_HEADERS,
            SOURCE_REVISION_HEADER: "wrong",
        },
        [
            ("Content-Type", "audio/wav"),
            (SOURCE_REVISION_HEADER, COSYVOICE_SOURCE_REVISION),
            (SOURCE_REVISION_HEADER, COSYVOICE_SOURCE_REVISION),
            (MODEL_ID_HEADER, MODEL_ID),
            (MODEL_REVISION_HEADER, MODEL_REVISION),
        ],
    ],
    ids=("missing", "wrong", "duplicate"),
)
def test_real_adapter_rejects_missing_wrong_or_duplicate_speech_attestation(
    headers,
) -> None:
    async def exercise() -> None:
        async def handler(_request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, content=pcm_wave(), headers=headers)

        upstream = FaceSpeakUpstream(transport=httpx.MockTransport(handler))
        try:
            with pytest.raises(UpstreamProtocolError):
                await upstream.synthesize("內容", "逐字稿", b"authorized")
        finally:
            await upstream.close()

    asyncio.run(exercise())

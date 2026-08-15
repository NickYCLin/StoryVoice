from __future__ import annotations

import asyncio
import io
import logging
import threading
import wave

import httpx
import pytest
from fastapi.testclient import TestClient

from bluemagpie_gateway.app import create_app
from bluemagpie_gateway.constants import (
    GPU_LOCK_KEY,
    MODEL_REVISION,
    PROVIDER_VERSION,
    PROVIDER_VERSION_HEADER,
    REVISION_HEADER,
    TOKEN_HEADER,
    VOICE_HEADER,
)
from bluemagpie_gateway.gpu_lock import RedisGpuExecutionLock
from bluemagpie_gateway.settings import GatewaySettings


TOKEN = "contract-test-token-at-least-32-chars"
AUTH_HEADERS = {TOKEN_HEADER: TOKEN}


def pcm_wave() -> bytes:
    output = io.BytesIO()
    with wave.open(output, "wb") as stream:
        stream.setnchannels(1)
        stream.setsampwidth(2)
        stream.setframerate(48_000)
        stream.writeframes(b"\x00\x00" * 480)
    return output.getvalue()


class FakeSynthesizer:
    def __init__(self, content: bytes | None = None) -> None:
        self.content = content if content is not None else pcm_wave()
        self.started = 0
        self.closed = 0
        self.calls: list[tuple[str, str]] = []

    def startup(self) -> None:
        self.started += 1

    def synthesize(self, text: str, voice: str) -> bytes:
        self.calls.append((text, voice))
        return self.content

    def close(self) -> None:
        self.closed += 1


class FakeGpuLock:
    def __init__(
        self,
        acquire_results: list[str | None] | None = None,
    ) -> None:
        self.acquire_results = list(acquire_results or [])
        self.acquire_timeouts: list[float] = []
        self.released_tokens: list[str] = []
        self.closed = 0

    async def acquire(self, timeout_seconds: float) -> str | None:
        self.acquire_timeouts.append(timeout_seconds)
        if self.acquire_results:
            return self.acquire_results.pop(0)
        return f"fake-lock-token-{len(self.acquire_timeouts)}"

    async def release(self, token: str) -> None:
        self.released_tokens.append(token)

    async def close(self) -> None:
        self.closed += 1


def build_app(
    fake: FakeSynthesizer,
    *,
    queue_timeout_seconds: float = 0.1,
    watchdog_seconds: float = 120.0,
    gpu_lock: FakeGpuLock | None = None,
    fatal_exit=None,
):
    resolved_gpu_lock = gpu_lock or FakeGpuLock()
    return create_app(
        GatewaySettings(
            internal_token=TOKEN,
            queue_timeout_seconds=queue_timeout_seconds,
            synthesis_watchdog_seconds=watchdog_seconds,
        ),
        lambda: fake,
        lambda _settings: resolved_gpu_lock,
        fatal_exit,
    )


def test_lifecycle_and_health_contract() -> None:
    fake = FakeSynthesizer()
    gpu_lock = FakeGpuLock()
    app = build_app(fake, gpu_lock=gpu_lock)

    with TestClient(app) as client:
        live = client.get("/health/live")
        ready = client.get("/health/ready")

        assert live.status_code == 200
        assert live.json() == {"status": "live"}
        assert ready.status_code == 200
        assert ready.json() == {
            "status": "ready",
            "model_revision": MODEL_REVISION,
            "provider_version": PROVIDER_VERSION,
        }
        assert fake.started == 1

    assert fake.closed == 1
    assert gpu_lock.closed == 1
    assert gpu_lock.released_tokens == [
        "fake-lock-token-1",
        "fake-lock-token-2",
    ]


def test_speech_returns_pinned_pcm_wave_contract() -> None:
    fake = FakeSynthesizer()
    gpu_lock = FakeGpuLock()
    app = build_app(fake, gpu_lock=gpu_lock)

    with TestClient(app) as client:
        response = client.post(
            "/v1/audio/speech",
            headers=AUTH_HEADERS,
            json={"text": "台灣華語試音。", "voice": "female_voice"},
        )

    assert response.status_code == 200
    assert response.headers["content-type"] == "audio/wav"
    assert response.headers[REVISION_HEADER] == MODEL_REVISION
    assert response.headers[PROVIDER_VERSION_HEADER] == PROVIDER_VERSION
    assert response.headers[VOICE_HEADER] == "female_voice"
    assert response.headers["cache-control"] == "no-store"
    assert int(response.headers["content-length"]) == len(response.content)
    assert response.content == pcm_wave()
    assert fake.calls == [("台灣華語試音。", "female_voice")]
    assert len(gpu_lock.acquire_timeouts) == 3
    assert gpu_lock.acquire_timeouts[0] >= 210
    assert 0 < gpu_lock.acquire_timeouts[1] <= 0.1
    assert gpu_lock.released_tokens == [
        "fake-lock-token-1",
        "fake-lock-token-2",
        "fake-lock-token-3",
    ]


def test_speech_requires_constant_time_internal_token_check() -> None:
    fake = FakeSynthesizer()
    app = build_app(fake)

    with TestClient(app) as client:
        missing = client.post(
            "/v1/audio/speech",
            json={"text": "不應合成", "voice": "hung_yi_lee"},
        )
        wrong = client.post(
            "/v1/audio/speech",
            headers={TOKEN_HEADER: "wrong"},
            json={"text": "也不應合成", "voice": "hung_yi_lee"},
        )

    assert missing.status_code == 401
    assert wrong.status_code == 401
    assert fake.calls == []


def test_short_internal_token_is_rejected_at_startup(monkeypatch) -> None:
    monkeypatch.setenv("BLUEMAGPIE_INTERNAL_TOKEN", "too-short")

    with pytest.raises(RuntimeError, match="at least 32 characters"):
        GatewaySettings.from_env()


def test_gpu_lock_lease_is_at_least_180_seconds_and_outlives_watchdog() -> None:
    defaults = GatewaySettings(internal_token=TOKEN)
    long_watchdog = GatewaySettings(
        internal_token=TOKEN,
        synthesis_watchdog_seconds=300,
    )

    assert defaults.redis_url == "redis://redis:6379/0"
    assert defaults.gpu_lock_lease_milliseconds == 180_000
    assert defaults.startup_gpu_lock_timeout_seconds == 210.0
    assert long_watchdog.gpu_lock_lease_milliseconds == 360_000


def test_content_length_body_cap_runs_before_json_parsing(caplog) -> None:
    fake = FakeSynthesizer()
    app = build_app(fake)
    private_text = "不應被解析或記錄的正文"
    oversized_body = (
        b'{"text":"'
        + private_text.encode("utf-8")
        + b'x' * 5000
        + b'","voice":"female_voice"}'
    )

    with caplog.at_level(logging.INFO, logger="bluemagpie_gateway"):
        with TestClient(app) as client:
            response = client.post(
                "/v1/audio/speech",
                headers={
                    **AUTH_HEADERS,
                    "Content-Type": "application/json",
                    "Content-Length": str(len(oversized_body)),
                },
                content=oversized_body,
            )

    assert response.status_code == 413
    assert response.json() == {"detail": "request body too large"}
    assert private_text not in response.text
    assert private_text not in caplog.text
    assert fake.calls == []


def test_streamed_body_without_content_length_is_capped(caplog) -> None:
    fake = FakeSynthesizer()
    app = build_app(fake)
    private_text = "串流中的未公開正文"

    def body_chunks():
        yield b'{"text":"' + private_text.encode("utf-8")
        yield b"y" * 5000
        yield b'","voice":"hung_yi_lee"}'

    with caplog.at_level(logging.INFO, logger="bluemagpie_gateway"):
        with TestClient(app) as client:
            response = client.post(
                "/v1/audio/speech",
                headers={
                    **AUTH_HEADERS,
                    "Content-Type": "application/json",
                },
                content=body_chunks(),
            )

    assert response.request.headers.get("content-length") is None
    assert response.status_code == 413
    assert response.json() == {"detail": "request body too large"}
    assert private_text not in response.text
    assert private_text not in caplog.text
    assert fake.calls == []


def test_schema_allows_only_text_and_two_pinned_voices() -> None:
    fake = FakeSynthesizer()
    app = build_app(fake)

    invalid_payloads = (
        {"text": " ", "voice": "hung_yi_lee"},
        {"text": "字" * 201, "voice": "hung_yi_lee"},
        {"text": "內容", "voice": "custom_voice"},
        {
            "text": "內容",
            "voice": "female_voice",
            "private_extra_field_name": "other",
        },
    )
    with TestClient(app) as client:
        responses = [
            client.post(
                "/v1/audio/speech",
                headers=AUTH_HEADERS,
                json=payload,
            )
            for payload in invalid_payloads
        ]

    assert [response.status_code for response in responses] == [422] * 4
    assert all(
        response.json() == {"detail": "invalid request"}
        for response in responses
    )
    assert all("字" * 201 not in response.text for response in responses)
    assert "private_extra_field_name" not in responses[-1].text
    assert fake.calls == []


def test_busy_cross_process_gpu_lock_fails_before_executor() -> None:
    fake = FakeSynthesizer()
    gpu_lock = FakeGpuLock(
        acquire_results=["startup-token", None, "close-token"]
    )
    app = build_app(fake, gpu_lock=gpu_lock)

    with TestClient(app) as client:
        response = client.post(
            "/v1/audio/speech",
            headers=AUTH_HEADERS,
            json={"text": "不應進入執行器", "voice": "female_voice"},
        )

    assert response.status_code == 503
    assert response.json() == {"detail": "synthesis queue timeout"}
    assert response.headers["retry-after"] == "1"
    assert len(gpu_lock.acquire_timeouts) == 3
    assert fake.calls == []
    assert gpu_lock.released_tokens == ["startup-token", "close-token"]


class DelayedRedisClient:
    def __init__(self) -> None:
        self.values: dict[str, str] = {}
        self.release_tokens: list[str] = []

    async def set(
        self,
        key: str,
        token: str,
        *,
        nx: bool,
        px: int,
    ) -> bool:
        assert nx is True
        assert px >= 180_000
        await asyncio.sleep(0.03)
        if key in self.values:
            return False
        self.values[key] = token
        return True

    async def eval(
        self,
        _script: str,
        _key_count: int,
        key: str,
        token: str,
    ) -> int:
        self.release_tokens.append(token)
        if self.values.get(key) == token:
            del self.values[key]
            return 1
        return 0

    async def aclose(self) -> None:
        return None


def test_late_redis_set_after_queue_timeout_is_owner_cleaned() -> None:
    async def exercise() -> None:
        client = DelayedRedisClient()
        gpu_lock = RedisGpuExecutionLock(
            "redis://unused:6379/0",
            180_000,
            client=client,
        )

        token = await gpu_lock.acquire(0.005)

        assert token is None
        assert client.values == {}
        assert len(client.release_tokens) == 1
        assert GPU_LOCK_KEY not in client.values

    asyncio.run(exercise())


class FailingSynthesizer(FakeSynthesizer):
    def synthesize(self, text: str, voice: str) -> bytes:
        raise RuntimeError(f"backend included sensitive text: {text}")


def test_backend_errors_neither_log_nor_reflect_novel_text(caplog) -> None:
    async def exercise() -> None:
        fake = FailingSynthesizer()
        gpu_lock = FakeGpuLock()
        exit_codes: list[int] = []
        novel_text = "尚未公開的小說正文"

        def fake_fatal_exit(exit_code: int):
            exit_codes.append(exit_code)

        app = build_app(
            fake,
            gpu_lock=gpu_lock,
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
                        "/v1/audio/speech",
                        headers=AUTH_HEADERS,
                        json={"text": novel_text, "voice": "female_voice"},
                    )

            assert exit_codes == [70]
            assert gpu_lock.released_tokens == ["fake-lock-token-1"]

    with caplog.at_level(logging.INFO, logger="bluemagpie_gateway"):
        asyncio.run(exercise())

    assert "尚未公開的小說正文" not in caplog.text


def test_invalid_audio_is_fail_closed() -> None:
    fake = FakeSynthesizer(b"not a wav")
    app = build_app(fake)

    with TestClient(app) as client:
        response = client.post(
            "/v1/audio/speech",
            headers=AUTH_HEADERS,
            json={"text": "內容", "voice": "hung_yi_lee"},
        )

    assert response.status_code == 503
    assert response.json() == {"detail": "synthesis unavailable"}


class BlockingSynthesizer(FakeSynthesizer):
    def __init__(self) -> None:
        super().__init__()
        self.first_started = threading.Event()
        self.release_first = threading.Event()

    def synthesize(self, text: str, voice: str) -> bytes:
        self.calls.append((text, voice))
        if len(self.calls) == 1:
            self.first_started.set()
            if not self.release_first.wait(timeout=2):
                raise RuntimeError("test release timed out")
        return self.content


def test_only_one_generation_runs_and_queue_wait_is_bounded() -> None:
    async def exercise() -> None:
        fake = BlockingSynthesizer()
        app = build_app(fake, queue_timeout_seconds=0.03)

        async with app.router.lifespan_context(app):
            transport = httpx.ASGITransport(app=app)
            async with httpx.AsyncClient(
                transport=transport,
                base_url="http://test",
            ) as client:
                first = asyncio.create_task(
                    client.post(
                        "/v1/audio/speech",
                        headers=AUTH_HEADERS,
                        json={"text": "第一句", "voice": "hung_yi_lee"},
                    )
                )
                assert await asyncio.to_thread(fake.first_started.wait, 1)

                second = await client.post(
                    "/v1/audio/speech",
                    headers=AUTH_HEADERS,
                    json={"text": "第二句", "voice": "female_voice"},
                )
                assert second.status_code == 503
                assert second.headers["retry-after"] == "1"

                fake.release_first.set()
                first_response = await first
                assert first_response.status_code == 200
                assert fake.calls == [("第一句", "hung_yi_lee")]

    asyncio.run(exercise())


def test_client_cancellation_keeps_both_locks_until_executor_finishes() -> None:
    async def exercise() -> None:
        fake = WatchdogSynthesizer()
        gpu_lock = FakeGpuLock()
        app = build_app(fake, gpu_lock=gpu_lock)

        async with app.router.lifespan_context(app):
            transport = httpx.ASGITransport(app=app)
            async with httpx.AsyncClient(
                transport=transport,
                base_url="http://test",
            ) as client:
                request = asyncio.create_task(
                    client.post(
                        "/v1/audio/speech",
                        headers=AUTH_HEADERS,
                        json={"text": "取消中的工作", "voice": "female_voice"},
                    )
                )
                assert await asyncio.to_thread(fake.running.wait, 1)
                request.cancel()
                await asyncio.sleep(0.02)

                assert not request.done()
                assert gpu_lock.released_tokens == ["fake-lock-token-1"]

                fake.release_worker.set()
                with pytest.raises(asyncio.CancelledError):
                    await request

                assert gpu_lock.released_tokens == [
                    "fake-lock-token-1",
                    "fake-lock-token-2",
                ]

    asyncio.run(exercise())


class WatchdogSynthesizer(FakeSynthesizer):
    def __init__(self) -> None:
        super().__init__()
        self.running = threading.Event()
        self.release_worker = threading.Event()

    def synthesize(self, text: str, voice: str) -> bytes:
        self.calls.append((text, voice))
        self.running.set()
        self.release_worker.wait(timeout=2)
        return self.content

    def close(self) -> None:
        self.release_worker.set()
        super().close()


def test_watchdog_fatally_exits_without_releasing_gpu_lock() -> None:
    async def exercise() -> None:
        fake = WatchdogSynthesizer()
        gpu_lock = FakeGpuLock()
        exit_codes: list[int] = []

        def fake_fatal_exit(exit_code: int):
            exit_codes.append(exit_code)

        app = build_app(
            fake,
            watchdog_seconds=0.02,
            gpu_lock=gpu_lock,
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
                        "/v1/audio/speech",
                        headers=AUTH_HEADERS,
                        json={
                            "text": "會觸發 watchdog",
                            "voice": "hung_yi_lee",
                        },
                    )

            assert fake.running.is_set()
            assert exit_codes == [70]
            assert gpu_lock.released_tokens == ["fake-lock-token-1"]
            fake.release_worker.set()

    asyncio.run(exercise())


def test_busy_stale_gpu_lock_blocks_cuda_startup_and_readiness() -> None:
    async def exercise() -> None:
        fake = FakeSynthesizer()
        gpu_lock = FakeGpuLock(acquire_results=[None])
        app = build_app(fake, gpu_lock=gpu_lock)

        with pytest.raises(
            RuntimeError,
            match="shared GPU lock unavailable during startup",
        ):
            async with app.router.lifespan_context(app):
                raise AssertionError("startup unexpectedly became ready")

        assert app.state.ready is False
        assert fake.started == 0
        assert fake.closed == 0
        assert gpu_lock.acquire_timeouts == [210.0]
        assert gpu_lock.released_tokens == []
        assert gpu_lock.closed == 1

    asyncio.run(exercise())


def test_busy_gpu_lock_during_close_fatally_exits_without_cuda_close() -> None:
    async def exercise() -> None:
        fake = FakeSynthesizer()
        gpu_lock = FakeGpuLock(acquire_results=["startup-token", None])
        exit_codes: list[int] = []

        def fake_fatal_exit(exit_code: int):
            exit_codes.append(exit_code)

        app = build_app(
            fake,
            gpu_lock=gpu_lock,
            fatal_exit=fake_fatal_exit,
        )

        with pytest.raises(SystemExit, match="70"):
            async with app.router.lifespan_context(app):
                assert app.state.ready is True

        assert app.state.ready is False
        assert fake.started == 1
        assert fake.closed == 0
        assert exit_codes == [70]
        assert gpu_lock.released_tokens == ["startup-token"]
        assert gpu_lock.closed == 1

    asyncio.run(exercise())


class StartupCudaFailureSynthesizer(FakeSynthesizer):
    def __init__(self) -> None:
        super().__init__()
        self.touched_cuda = False

    def startup(self) -> None:
        self.started += 1
        self.touched_cuda = True
        raise RuntimeError("CUDA startup failed after initialization")


def test_cuda_startup_exception_preserves_owner_lease_and_never_readies() -> None:
    async def exercise() -> None:
        fake = StartupCudaFailureSynthesizer()
        gpu_lock = FakeGpuLock()
        app = build_app(fake, gpu_lock=gpu_lock)

        with pytest.raises(RuntimeError, match="CUDA startup failed"):
            async with app.router.lifespan_context(app):
                raise AssertionError("startup unexpectedly became ready")

        assert fake.touched_cuda is True
        assert app.state.ready is False
        assert gpu_lock.released_tokens == []
        assert gpu_lock.closed == 1

    asyncio.run(exercise())


class CloseCudaFailureSynthesizer(FakeSynthesizer):
    def __init__(self) -> None:
        super().__init__()
        self.touched_cuda_during_close = False

    def close(self) -> None:
        self.closed += 1
        self.touched_cuda_during_close = True
        raise RuntimeError("CUDA close failed after cleanup began")


def test_cuda_close_exception_fatally_exits_and_preserves_owner_lease() -> None:
    async def exercise() -> None:
        fake = CloseCudaFailureSynthesizer()
        gpu_lock = FakeGpuLock()
        exit_codes: list[int] = []

        def fake_fatal_exit(exit_code: int):
            exit_codes.append(exit_code)

        app = build_app(
            fake,
            gpu_lock=gpu_lock,
            fatal_exit=fake_fatal_exit,
        )

        with pytest.raises(SystemExit, match="70"):
            async with app.router.lifespan_context(app):
                assert app.state.ready is True

        assert fake.touched_cuda_during_close is True
        assert exit_codes == [70]
        assert gpu_lock.released_tokens == ["fake-lock-token-1"]
        assert gpu_lock.closed == 1

    asyncio.run(exercise())

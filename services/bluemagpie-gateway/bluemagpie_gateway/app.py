from __future__ import annotations

import asyncio
import logging
import os
import secrets
from collections.abc import Callable
from contextlib import asynccontextmanager
from typing import Annotated, Literal, NoReturn, TypeVar

from fastapi import Depends, FastAPI, Header, HTTPException, Request, status
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse, Response
from pydantic import BaseModel, ConfigDict, Field, field_validator

from .audio import validate_pcm_wave
from .body_limit import RequestBodyLimitMiddleware
from .constants import (
    FEMALE_VOICE,
    FATAL_WATCHDOG_EXIT_CODE,
    MALE_VOICE,
    MAX_TEXT_LENGTH,
    MAX_REQUEST_BODY_BYTES,
    MODEL_REVISION,
    REVISION_HEADER,
    TOKEN_HEADER,
    VOICE_HEADER,
)
from .gpu_lock import GpuExecutionLock, RedisGpuExecutionLock
from .settings import GatewaySettings
from .synthesizer import BlueMagpieSynthesizer, SpeechSynthesizer


logger = logging.getLogger("bluemagpie_gateway")
SynthesizerFactory = Callable[[], SpeechSynthesizer]
GpuLockFactory = Callable[[GatewaySettings], GpuExecutionLock]
FatalExit = Callable[[int], NoReturn]
WorkerResult = TypeVar("WorkerResult")


def _fatal_process_exit(exit_code: int) -> NoReturn:
    os._exit(exit_code)


def _invoke_fatal_exit(fatal_exit: FatalExit) -> NoReturn:
    try:
        fatal_exit(FATAL_WATCHDOG_EXIT_CODE)
    finally:
        # Production os._exit never reaches this block. It guarantees a fake or
        # broken exit seam can never return control to a CUDA-unsafe process.
        raise SystemExit(FATAL_WATCHDOG_EXIT_CODE)


async def _await_cuda_worker(
    worker: asyncio.Task[WorkerResult],
    timeout_seconds: float,
    terminate_for_watchdog: Callable[[], NoReturn],
) -> WorkerResult:
    """Await an uncancellable CUDA thread or terminate at its hard deadline."""

    watchdog_deadline = asyncio.get_running_loop().time() + timeout_seconds
    try:
        return await asyncio.wait_for(
            asyncio.shield(worker),
            timeout=timeout_seconds,
        )
    except TimeoutError:
        terminate_for_watchdog()
    except asyncio.CancelledError:
        # to_thread cannot cancel CUDA. Even repeated shutdown cancellation
        # must not let callers release a lock around a still-running worker.
        while not worker.done():
            remaining_seconds = (
                watchdog_deadline - asyncio.get_running_loop().time()
            )
            if remaining_seconds <= 0:
                terminate_for_watchdog()
            try:
                await asyncio.wait_for(
                    asyncio.shield(worker),
                    timeout=remaining_seconds,
                )
            except TimeoutError:
                terminate_for_watchdog()
            except asyncio.CancelledError:
                continue
        worker.result()
        raise

    # terminate_for_watchdog is typed NoReturn, but retain a hard guard for an
    # incorrectly implemented test seam.
    raise SystemExit(FATAL_WATCHDOG_EXIT_CODE)


class SpeechRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    text: str = Field(min_length=1, max_length=MAX_TEXT_LENGTH)
    voice: Literal[MALE_VOICE, FEMALE_VOICE]

    @field_validator("text")
    @classmethod
    def reject_blank_text(cls, value: str) -> str:
        if not value.strip():
            raise ValueError("text must not be blank")
        return value


def create_app(
    settings: GatewaySettings | None = None,
    synthesizer_factory: SynthesizerFactory | None = None,
    gpu_lock_factory: GpuLockFactory | None = None,
    fatal_exit: FatalExit | None = None,
) -> FastAPI:
    resolved_settings = settings or GatewaySettings.from_env()
    resolved_factory = synthesizer_factory or BlueMagpieSynthesizer
    resolved_gpu_lock_factory = gpu_lock_factory or (
        lambda value: RedisGpuExecutionLock(
            value.redis_url,
            value.gpu_lock_lease_milliseconds,
        )
    )
    resolved_fatal_exit = fatal_exit or _fatal_process_exit
    gate = asyncio.Semaphore(1)

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        synthesizer = resolved_factory()
        gpu_lock = resolved_gpu_lock_factory(resolved_settings)
        app.state.ready = False
        app.state.synthesizer = synthesizer
        app.state.gpu_lock = gpu_lock
        startup_lock_token: str | None = None
        preserve_startup_lock = False
        try:
            try:
                startup_lock_token = await gpu_lock.acquire(
                    resolved_settings.startup_gpu_lock_timeout_seconds
                )
                if startup_lock_token is None:
                    raise RuntimeError(
                        "shared GPU lock unavailable during startup"
                    )

                startup_worker = asyncio.create_task(
                    asyncio.to_thread(synthesizer.startup)
                )

                def terminate_startup_for_watchdog() -> NoReturn:
                    nonlocal preserve_startup_lock
                    preserve_startup_lock = True
                    logger.critical(
                        "model_startup_watchdog_expired timeout_seconds=%.3f",
                        resolved_settings.model_lifecycle_watchdog_seconds,
                    )
                    _invoke_fatal_exit(resolved_fatal_exit)

                try:
                    await _await_cuda_worker(
                        startup_worker,
                        resolved_settings.model_lifecycle_watchdog_seconds,
                        terminate_startup_for_watchdog,
                    )
                except BaseException:
                    # A lifecycle worker may have submitted CUDA work before
                    # failing. Preserve the lease through process teardown.
                    preserve_startup_lock = True
                    raise
            finally:
                if startup_lock_token is not None and not preserve_startup_lock:
                    try:
                        await gpu_lock.release(startup_lock_token)
                    except Exception as exc:
                        logger.error(
                            "startup_gpu_lock_release_failed error_type=%s",
                            type(exc).__name__,
                        )
                        raise
        except Exception as exc:
            # Exception messages and tracebacks from model libraries may contain
            # request material. Log only a stable type, never request text.
            logger.error("model_startup_failed error_type=%s", type(exc).__name__)
            await gpu_lock.close()
            raise

        app.state.ready = True
        logger.info("model_ready revision=%s", MODEL_REVISION)
        try:
            yield
        finally:
            app.state.ready = False
            close_lock_token: str | None = None
            preserve_close_lock = False
            try:
                close_lock_token = await gpu_lock.acquire(
                    resolved_settings.queue_timeout_seconds
                )
                if close_lock_token is None:
                    logger.critical("model_close_gpu_lock_unavailable")
                    _invoke_fatal_exit(resolved_fatal_exit)

                close_worker = asyncio.create_task(
                    asyncio.to_thread(synthesizer.close)
                )

                def terminate_close_for_watchdog() -> NoReturn:
                    nonlocal preserve_close_lock
                    preserve_close_lock = True
                    logger.critical(
                        "model_close_watchdog_expired timeout_seconds=%.3f",
                        resolved_settings.model_lifecycle_watchdog_seconds,
                    )
                    _invoke_fatal_exit(resolved_fatal_exit)

                try:
                    await _await_cuda_worker(
                        close_worker,
                        resolved_settings.model_lifecycle_watchdog_seconds,
                        terminate_close_for_watchdog,
                    )
                except (Exception, asyncio.CancelledError) as exc:
                    preserve_close_lock = True
                    logger.critical(
                        "model_close_failed error_type=%s",
                        type(exc).__name__,
                    )
                    _invoke_fatal_exit(resolved_fatal_exit)
            finally:
                if close_lock_token is not None and not preserve_close_lock:
                    try:
                        await gpu_lock.release(close_lock_token)
                    except Exception as exc:
                        logger.error(
                            "close_gpu_lock_release_failed error_type=%s",
                            type(exc).__name__,
                        )
                await gpu_lock.close()

    app = FastAPI(
        title="StoryVoice BlueMagpie Gateway",
        version="1.0.0",
        docs_url=None,
        redoc_url=None,
        openapi_url=None,
        lifespan=lifespan,
    )
    app.add_middleware(
        RequestBodyLimitMiddleware,
        max_bytes=MAX_REQUEST_BODY_BYTES,
        path="/v1/audio/speech",
    )
    app.state.ready = False

    async def authorize(
        token: Annotated[
            str | None,
            Header(alias=TOKEN_HEADER),
        ] = None,
    ) -> None:
        if token is None or not secrets.compare_digest(
            token,
            resolved_settings.internal_token,
        ):
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail="unauthorized",
            )

    @app.exception_handler(RequestValidationError)
    async def validation_error_handler(
        _request: Request,
        _exception: RequestValidationError,
    ) -> JSONResponse:
        # Do not expose Pydantic's loc, rejected input, or extra-field key.
        return JSONResponse(
            status_code=422,
            content={"detail": "invalid request"},
        )

    @app.get("/health/live")
    async def live() -> dict[str, str]:
        return {"status": "live"}

    @app.get("/health/ready")
    async def ready() -> JSONResponse:
        if not app.state.ready:
            return JSONResponse(
                status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                content={"status": "not_ready"},
            )
        return JSONResponse(
            content={
                "status": "ready",
                "model_revision": MODEL_REVISION,
            }
        )

    @app.post(
        "/v1/audio/speech",
        dependencies=[Depends(authorize)],
        response_class=Response,
    )
    async def synthesize(payload: SpeechRequest) -> Response:
        if not app.state.ready:
            raise HTTPException(
                status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                detail="model unavailable",
            )

        queue_deadline = (
            asyncio.get_running_loop().time()
            + resolved_settings.queue_timeout_seconds
        )
        acquired = False
        gpu_lock_token: str | None = None
        preserve_gpu_lock = False
        worker: asyncio.Task[bytes] | None = None
        try:
            try:
                await asyncio.wait_for(
                    gate.acquire(),
                    timeout=max(
                        0,
                        queue_deadline - asyncio.get_running_loop().time(),
                    ),
                )
                acquired = True
            except TimeoutError as exc:
                logger.warning("synthesis_queue_timeout voice=%s", payload.voice)
                raise HTTPException(
                    status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                    detail="synthesis queue timeout",
                    headers={"Retry-After": "1"},
                ) from exc

            remaining_queue_seconds = (
                queue_deadline - asyncio.get_running_loop().time()
            )
            if remaining_queue_seconds > 0:
                gpu_lock_token = await app.state.gpu_lock.acquire(
                    remaining_queue_seconds
                )
            if gpu_lock_token is None:
                logger.warning("gpu_lock_queue_timeout voice=%s", payload.voice)
                raise HTTPException(
                    status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                    detail="synthesis queue timeout",
                    headers={"Retry-After": "1"},
                )

            worker = asyncio.create_task(
                asyncio.to_thread(
                    app.state.synthesizer.synthesize,
                    payload.text,
                    payload.voice,
                )
            )
            def terminate_for_watchdog() -> NoReturn:
                nonlocal preserve_gpu_lock
                preserve_gpu_lock = True
                logger.critical(
                    "synthesis_watchdog_expired voice=%s timeout_seconds=%.3f",
                    payload.voice,
                    resolved_settings.synthesis_watchdog_seconds,
                )
                _invoke_fatal_exit(resolved_fatal_exit)

            try:
                content = await _await_cuda_worker(
                    worker,
                    resolved_settings.synthesis_watchdog_seconds,
                    terminate_for_watchdog,
                )
            except Exception as exc:
                preserve_gpu_lock = True
                logger.critical(
                    "synthesis_executor_failed voice=%s error_type=%s",
                    payload.voice,
                    type(exc).__name__,
                )
                _invoke_fatal_exit(resolved_fatal_exit)

            metadata = validate_pcm_wave(content)
            logger.info(
                "synthesis_complete voice=%s duration_seconds=%.3f",
                payload.voice,
                metadata.duration_seconds,
            )
            return Response(
                content=content,
                media_type="audio/wav",
                headers={
                    REVISION_HEADER: MODEL_REVISION,
                    VOICE_HEADER: payload.voice,
                    "Cache-Control": "no-store",
                    "X-Content-Type-Options": "nosniff",
                },
            )
        except HTTPException:
            raise
        except asyncio.CancelledError:
            raise
        except Exception as exc:
            logger.error(
                "synthesis_failed voice=%s error_type=%s",
                payload.voice,
                type(exc).__name__,
            )
            raise HTTPException(
                status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                detail="synthesis unavailable",
            ) from exc
        finally:
            if gpu_lock_token is not None and not preserve_gpu_lock:
                try:
                    await app.state.gpu_lock.release(gpu_lock_token)
                except Exception as exc:
                    logger.error(
                        "gpu_lock_release_failed error_type=%s",
                        type(exc).__name__,
                    )
            if acquired:
                gate.release()

    return app

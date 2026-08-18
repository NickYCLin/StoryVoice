from __future__ import annotations

import asyncio
import logging
import os
import secrets
from collections.abc import Callable
from contextlib import asynccontextmanager
from typing import Annotated, NoReturn, TypeVar

from fastapi import Depends, FastAPI, Header, HTTPException, Request, status
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse, Response
from python_multipart.exceptions import MultipartParseError
from starlette.datastructures import UploadFile
from starlette.exceptions import HTTPException as StarletteHTTPException

from .audio import validate_pcm_wave, validate_reference_wave
from .body_limit import (
    SYNTHESIS_REJECTION_STAGES,
    SYNTHESIS_STAGE_HEADER,
    RequestBodyLimitMiddleware,
)
from .constants import (
    COSYVOICE_SOURCE_REVISION,
    FATAL_WATCHDOG_EXIT_CODE,
    MAX_QUEUED_SYNTHESIS_REQUESTS,
    MAX_REFERENCE_AUDIO_BYTES,
    MAX_REFERENCE_TEXT_LENGTH,
    MAX_REQUEST_BODY_BYTES,
    MAX_TEXT_LENGTH,
    MODEL_ID,
    MODEL_ID_HEADER,
    MODEL_REVISION,
    MODEL_REVISION_HEADER,
    SOURCE_REVISION_HEADER,
    SYNTHESIS_PATH,
    TOKEN_HEADER,
)
from .settings import GatewaySettings
from .upstream import (
    CloneUpstream,
    FaceSpeakUpstream,
    UpstreamAttestation,
    UpstreamProtocolError,
    UpstreamRejected,
    UpstreamTransportUncertain,
)


logger = logging.getLogger("local_clone_gateway")
UpstreamFactory = Callable[[], CloneUpstream]
FatalExit = Callable[[int], NoReturn]
WorkerResult = TypeVar("WorkerResult")


def _synthesis_http_error(
    *,
    status_code: int,
    detail: str,
    stage: str,
    retry_after: str | None = None,
) -> HTTPException:
    if stage not in SYNTHESIS_REJECTION_STAGES:
        raise RuntimeError("invalid synthesis rejection stage")
    headers = {SYNTHESIS_STAGE_HEADER: stage}
    if retry_after is not None:
        headers["Retry-After"] = retry_after
    return HTTPException(
        status_code=status_code,
        detail=detail,
        headers=headers,
    )


def _request_contract_error(
    status_code: int = 422,
    detail: str = "invalid request",
) -> HTTPException:
    return _synthesis_http_error(
        status_code=status_code,
        detail=detail,
        stage="request_contract",
    )


def _fatal_process_exit(exit_code: int) -> NoReturn:
    os._exit(exit_code)


def _invoke_fatal_exit(fatal_exit: FatalExit) -> NoReturn:
    try:
        fatal_exit(FATAL_WATCHDOG_EXIT_CODE)
    finally:
        # os._exit never returns. This guard makes a broken or fake exit seam
        # fail closed instead of letting an uncertain process serve traffic.
        raise SystemExit(FATAL_WATCHDOG_EXIT_CODE)


async def _await_upstream_terminal(
    worker: asyncio.Task[WorkerResult],
    timeout_seconds: float,
    terminate_for_uncertainty: Callable[[str], NoReturn],
) -> WorkerResult:
    """Do not abandon FaceSpeak inference when the gateway caller cancels."""

    deadline = asyncio.get_running_loop().time() + timeout_seconds
    cancellation_requested = False
    while True:
        remaining = deadline - asyncio.get_running_loop().time()
        if remaining <= 0:
            terminate_for_uncertainty("watchdog")
        try:
            done, _ = await asyncio.wait(
                {worker},
                timeout=remaining,
                return_when=asyncio.FIRST_COMPLETED,
            )
        except asyncio.CancelledError:
            # Cancelling the downstream HTTP request must not cancel the
            # separate FaceSpeak inference request. Its executor owns the GPU
            # lock, and the gateway must await a terminal response.
            cancellation_requested = True
            continue

        if not done:
            terminate_for_uncertainty("watchdog")
        if worker in done:
            if worker.cancelled():
                terminate_for_uncertainty("upstream_cancelled")
            result = worker.result()
            if cancellation_requested:
                raise asyncio.CancelledError
            return result

    raise SystemExit(FATAL_WATCHDOG_EXIT_CODE)


async def _parse_synthesis_form(request: Request) -> tuple[str, str, bytes]:
    content_type = request.headers.get("content-type", "")
    if not content_type.lower().startswith("multipart/form-data;"):
        raise _synthesis_http_error(
            status_code=415,
            detail="unsupported media type",
            stage="multipart",
        )

    try:
        async with request.form(
            max_files=1,
            max_fields=2,
            max_part_size=MAX_REFERENCE_TEXT_LENGTH * 4,
        ) as form:
            items = list(form.multi_items())
            allowed_names = {"text", "reference_text", "reference_audio"}
            if (
                len(items) != 3
                or {name for name, _ in items} != allowed_names
                or any(len(form.getlist(name)) != 1 for name in allowed_names)
            ):
                raise _request_contract_error()

            text = form.get("text")
            reference_text = form.get("reference_text")
            reference_audio = form.get("reference_audio")
            if not isinstance(text, str) or not isinstance(reference_text, str):
                raise _request_contract_error()
            if not isinstance(reference_audio, UploadFile):
                raise _request_contract_error()

            normalized_text = text.strip()
            normalized_reference_text = reference_text.strip()
            if not normalized_text or len(normalized_text) > MAX_TEXT_LENGTH:
                raise _request_contract_error()
            if (
                not normalized_reference_text
                or len(normalized_reference_text) > MAX_REFERENCE_TEXT_LENGTH
            ):
                raise _request_contract_error()
            if reference_audio.content_type not in {
                "audio/wav",
                "audio/x-wav",
                "audio/wave",
                "audio/vnd.wave",
            }:
                raise _request_contract_error(
                    status_code=415,
                    detail="unsupported media type",
                )

            audio = await reference_audio.read(MAX_REFERENCE_AUDIO_BYTES + 1)
            if not audio or len(audio) > MAX_REFERENCE_AUDIO_BYTES:
                raise _request_contract_error(
                    status_code=413,
                    detail="reference audio too large",
                )
            try:
                validate_reference_wave(audio)
            except ValueError as exc:
                raise _request_contract_error() from exc
            return normalized_text, normalized_reference_text, audio
    except HTTPException:
        raise
    except (StarletteHTTPException, MultipartParseError) as exc:
        raise _synthesis_http_error(
            status_code=getattr(exc, "status_code", 400),
            detail="invalid request",
            stage="multipart",
        ) from exc


def create_app(
    settings: GatewaySettings | None = None,
    upstream_factory: UpstreamFactory | None = None,
    fatal_exit: FatalExit | None = None,
) -> FastAPI:
    resolved_settings = settings or GatewaySettings.from_env()
    resolved_upstream_factory = upstream_factory or FaceSpeakUpstream
    resolved_fatal_exit = fatal_exit or _fatal_process_exit
    gate = asyncio.Semaphore(1)
    admission_slots: asyncio.Queue[None] = asyncio.Queue(
        maxsize=1 + MAX_QUEUED_SYNTHESIS_REQUESTS
    )

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        upstream = resolved_upstream_factory()
        app.state.upstream = upstream
        app.state.active = True
        try:
            yield
        finally:
            app.state.active = False
            try:
                await upstream.close()
            except Exception as exc:
                logger.error(
                    "upstream_close_failed error_type=%s",
                    type(exc).__name__,
                )

    app = FastAPI(
        title="StoryVoice Local Clone Gateway",
        version="1.0.0",
        docs_url=None,
        redoc_url=None,
        openapi_url=None,
        lifespan=lifespan,
    )
    app.add_middleware(
        RequestBodyLimitMiddleware,
        max_bytes=MAX_REQUEST_BODY_BYTES,
        path=SYNTHESIS_PATH,
        token_header=TOKEN_HEADER,
        internal_token=resolved_settings.internal_token,
    )
    app.state.active = False

    async def authorize(
        token: Annotated[str | None, Header(alias=TOKEN_HEADER)] = None,
    ) -> None:
        if token is None or not secrets.compare_digest(
            token,
            resolved_settings.internal_token,
        ):
            raise _synthesis_http_error(
                status_code=401,
                detail="unauthorized",
                stage="auth",
            )

    @app.exception_handler(RequestValidationError)
    async def validation_error_handler(
        request: Request,
        _exception: RequestValidationError,
    ) -> JSONResponse:
        headers = None
        if request.url.path == SYNTHESIS_PATH:
            logger.warning(
                "synthesis_rejected stage=request_contract status=422"
            )
            headers = {SYNTHESIS_STAGE_HEADER: "request_contract"}
        return JSONResponse(
            status_code=422,
            content={"detail": "invalid request"},
            headers=headers,
        )

    @app.exception_handler(StarletteHTTPException)
    async def http_error_handler(
        request: Request,
        exception: StarletteHTTPException,
    ) -> JSONResponse:
        details = {
            400: "invalid request",
            401: "unauthorized",
            404: "not found",
            413: "request body too large",
            415: "unsupported media type",
            422: "invalid request",
            503: "synthesis unavailable",
        }
        headers: dict[str, str] = {}
        if exception.status_code == 503 and exception.headers:
            retry_after = exception.headers.get("Retry-After")
            if retry_after == "1":
                headers["Retry-After"] = "1"
        stage = None
        if exception.headers:
            stage_values = [
                value
                for name, value in exception.headers.items()
                if name.lower() == SYNTHESIS_STAGE_HEADER.lower()
            ]
            if len(stage_values) == 1:
                stage = stage_values[0]
        if (
            request.url.path == SYNTHESIS_PATH
            and stage in SYNTHESIS_REJECTION_STAGES
        ):
            logger.warning(
                "synthesis_rejected stage=%s status=%d",
                stage,
                exception.status_code,
            )
            headers[SYNTHESIS_STAGE_HEADER] = stage
        return JSONResponse(
            status_code=exception.status_code,
            content={"detail": details.get(exception.status_code, "request rejected")},
            headers=headers or None,
        )

    async def dependency_attestation() -> UpstreamAttestation | None:
        if not app.state.active:
            return None
        try:
            async with asyncio.timeout(resolved_settings.ready_timeout_seconds):
                attestation = await app.state.upstream.readiness()
                if attestation is None or not (
                    attestation.source_revision == COSYVOICE_SOURCE_REVISION
                    and attestation.model_id == MODEL_ID
                    and attestation.model_revision == MODEL_REVISION
                ):
                    return None
                return attestation
        except Exception:
            return None

    @app.get("/health/live")
    async def live() -> dict[str, str]:
        return {"status": "live"}

    @app.get("/health/ready")
    async def ready() -> JSONResponse:
        attestation = await dependency_attestation()
        if attestation is None:
            return JSONResponse(status_code=503, content={"status": "not_ready"})
        return JSONResponse(
            content={
                "status": "ready",
                "source_revision": attestation.source_revision,
                "model_id": attestation.model_id,
                "model_revision": attestation.model_revision,
            }
        )

    async def synthesize_admitted(request: Request) -> Response:
        text, reference_text, reference_audio = await _parse_synthesis_form(request)
        if await dependency_attestation() is None:
            raise _synthesis_http_error(
                status_code=503,
                detail="synthesis unavailable",
                stage="upstream_readiness",
            )

        queue_deadline = (
            asyncio.get_running_loop().time()
            + resolved_settings.queue_timeout_seconds
        )
        gate_acquired = False

        def terminate_for_uncertainty(reason: str) -> NoReturn:
            logger.critical(
                "synthesis_state_uncertain reason=%s",
                reason,
            )
            _invoke_fatal_exit(resolved_fatal_exit)

        try:
            try:
                await asyncio.wait_for(
                    gate.acquire(),
                    timeout=max(
                        0,
                        queue_deadline - asyncio.get_running_loop().time(),
                    ),
                )
                gate_acquired = True
            except TimeoutError as exc:
                raise _synthesis_http_error(
                    status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                    detail="synthesis unavailable",
                    stage="queue",
                    retry_after="1",
                ) from exc

            worker = asyncio.create_task(
                app.state.upstream.synthesize(
                    text,
                    reference_text,
                    reference_audio,
                )
            )
            try:
                content = await _await_upstream_terminal(
                    worker,
                    resolved_settings.synthesis_watchdog_seconds,
                    terminate_for_uncertainty,
                )
            except (UpstreamTransportUncertain,) as exc:
                logger.critical(
                    "upstream_transport_uncertain error_type=%s",
                    type(exc).__name__,
                )
                terminate_for_uncertainty("upstream_transport")
            except UpstreamRejected as exc:
                raise _synthesis_http_error(
                    status_code=503,
                    detail="synthesis unavailable",
                    stage="upstream_terminal",
                ) from exc
            except UpstreamProtocolError as exc:
                raise _synthesis_http_error(
                    status_code=503,
                    detail="synthesis unavailable",
                    stage="upstream_contract",
                ) from exc
            except asyncio.CancelledError:
                raise
            except Exception as exc:
                logger.critical(
                    "upstream_state_uncertain error_type=%s",
                    type(exc).__name__,
                )
                terminate_for_uncertainty("upstream_exception")

            if not (
                content.attestation.source_revision == COSYVOICE_SOURCE_REVISION
                and content.attestation.model_id == MODEL_ID
                and content.attestation.model_revision == MODEL_REVISION
            ):
                raise UpstreamProtocolError("invalid upstream attestation")
            metadata = validate_pcm_wave(content.content)
            logger.info(
                "synthesis_complete duration_seconds=%.3f bytes=%d",
                metadata.duration_seconds,
                len(content.content),
            )
            return Response(
                content=content.content,
                media_type="audio/wav",
                headers={
                    SOURCE_REVISION_HEADER: content.attestation.source_revision,
                    MODEL_ID_HEADER: content.attestation.model_id,
                    MODEL_REVISION_HEADER: content.attestation.model_revision,
                    "Cache-Control": "no-store",
                    "Pragma": "no-cache",
                    "X-Content-Type-Options": "nosniff",
                },
            )
        except HTTPException:
            raise
        except asyncio.CancelledError:
            raise
        except (ValueError, UpstreamProtocolError) as exc:
            raise _synthesis_http_error(
                status_code=503,
                detail="synthesis unavailable",
                stage="upstream_contract",
            ) from exc
        except UpstreamRejected as exc:
            raise _synthesis_http_error(
                status_code=503,
                detail="synthesis unavailable",
                stage="upstream_terminal",
            ) from exc
        except Exception as exc:
            raise _synthesis_http_error(
                status_code=503,
                detail="synthesis unavailable",
                stage="upstream_terminal",
            ) from exc
        finally:
            if gate_acquired:
                gate.release()

    @app.post(
        SYNTHESIS_PATH,
        dependencies=[Depends(authorize)],
        response_class=Response,
    )
    async def synthesize(request: Request) -> Response:
        try:
            admission_slots.put_nowait(None)
        except asyncio.QueueFull as exc:
            raise _synthesis_http_error(
                status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                detail="synthesis unavailable",
                stage="admission",
                retry_after="1",
            ) from exc
        try:
            return await synthesize_admitted(request)
        finally:
            admission_slots.get_nowait()
            admission_slots.task_done()

    return app

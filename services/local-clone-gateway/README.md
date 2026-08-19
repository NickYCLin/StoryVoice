# StoryVoice local clone gateway

Private, single-flight gateway between StoryVoice and the existing FaceSpeak
CosyVoice service. It performs no model download or third-party API call.

The upstream origin is compiled as
`http://facespeak-voice-clone:8093`; it cannot be changed by a request or an
environment variable. The HTTP client ignores proxy environment variables and
does not follow redirects. Readiness requires FaceSpeak to return an exact
four-field health document (`ready`, `sourceRevision`, `modelId`, and
`modelRevision`) containing:

- source revision: `074ca6dc9e80a2f424f1f74b48bdd7d3fea531cc`
- model: `FunAudioLLM/Fun-CosyVoice3-0.5B-2512`
- model revision: `29e01c4e8d000f4bcd70751be16fa94bf3d85a18`

Every successful speech response must independently contain exactly one copy
of each pinned FaceSpeak attestation header. The gateway validates and forwards
those observed values; it never manufactures attestation headers from its own
constants. Expected pins are also recorded in the image labels.

## Runtime contract

- listen port: `8082`
- liveness: `GET /health/live`
- dependency readiness: `GET /health/ready`
- synthesis: `POST /v1/voice-clone/speech`
- authentication: `X-StoryVoice-Internal-Token` (32–512 printable ASCII
  characters from `!` through `~`; no spaces or control characters)
- multipart fields: `text`, `reference_text`, `reference_audio`
- reference audio: PCM16 WAV, 48 kHz, mono, 10–30 seconds, at most 10 MiB
- response: bounded PCM16 WAV, 24 kHz, mono, with `Cache-Control: no-store`

The gateway does not log or return the synthesis text, reference transcript,
uploaded filename, temporary path, upstream response body, or exception text.
The ASGI middleware validates a fixed-length digest of the internal token with
constant-time comparison before reading or buffering any synthesis body;
FastAPI repeats authorization as defense in depth. Uvicorn access logging is
disabled in the image.

## Environment

Only these settings are accepted; there is intentionally no upstream URL
setting.

- `LOCAL_CLONE_INTERNAL_TOKEN` (required; same 32–512 printable ASCII contract)
- `LOCAL_CLONE_QUEUE_TIMEOUT_SECONDS` (default `15`)
- `LOCAL_CLONE_SYNTHESIS_WATCHDOG_SECONDS` (default `180`)
- `LOCAL_CLONE_READY_TIMEOUT_SECONDS` (default `3`)

The gateway has one process-local synthesis slot, admits at most one queued
synthesis request, and applies a server-wide concurrency ceiling. It does not
connect to Redis and does not own a GPU lock. GPU admission, lock lifetime,
and fatal inference cleanup belong to the FaceSpeak executor that actually
runs CosyVoice. A gateway client cancellation is held until the upstream
request is terminal. If the watchdog or transport makes upstream inference
state uncertain, the gateway exits with code 70 while the executor retains
responsibility for its lock and self-termination policy.

Compose keeps the API-facing and FaceSpeak-facing links on separate internal
networks. Redis is reachable only from its dedicated GPU-lock network; this
gateway is intentionally not attached to that network.

## Offline contract tests

From this directory, after installing `requirements-test.txt`:

```console
python -m pytest -q
python -m compileall -q local_clone_gateway tests
```

All tests use injected fakes or `httpx.MockTransport`; they do not resolve the
FaceSpeak hostname, invoke a model, or access the internet.

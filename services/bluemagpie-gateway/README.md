# StoryVoice BlueMagpie gateway

Private FastAPI adapter for the two BlueMagpie Taiwan-Mandarin voices used by
StoryVoice. It loads the already downloaded model during application startup
and exposes one authenticated synthesis endpoint:

```http
POST /v1/audio/speech
X-StoryVoice-Internal-Token: <shared secret>
Content-Type: application/json

{"text":"這是一段台灣華語試音。","voice":"female_voice"}
```

The only accepted voices are `hung_yi_lee` and `female_voice`; text is limited
to 200 Unicode characters. The raw request body is capped at 4 KiB before JSON
or Pydantic parsing, including streamed requests without `Content-Length`. A
successful response is 48 kHz, mono, PCM16 WAV
and includes `X-BlueMagpie-Model-Revision` and `X-BlueMagpie-Voice` headers.
`GET /health/live` and `GET /health/ready` do not require the shared secret.

## Offline and concurrency guarantees

- Source, model, and the official female embedding SHA-256 are fixed in code
  and as image labels.
- The exact Hugging Face snapshot is resolved directly below `/cache`; the
  runtime does not call `snapshot_download` or contain any model-download path.
- Mount `/cache` read-only. All library scratch paths point at `/tmp`.
- Uvicorn has one worker and the app has a one-permit semaphore. A queued call
  fails with 503 after `BLUEMAGPIE_QUEUE_TIMEOUT_SECONDS` (15 seconds by
  default).
- The executor also owns Redis lock `storyvoice:local-gpu:exclusive:v1` while
  CUDA generation runs. `BLUEMAGPIE_REDIS_URL` defaults to
  `redis://redis:6379/0`; acquisition shares the same queue-timeout budget.
  Its unique token is released with compare-and-delete Lua, never a plain
  `DEL`.
- CUDA model startup and close use that same Redis lock. Startup waits for the
  full lease plus 30 seconds, so a replacement container cannot bypass the
  deliberately retained lock from a watchdog-killed predecessor. It never
  becomes ready without the lock. Close uses a bounded acquisition and exits
  the process without touching CUDA if another owner still holds the GPU.
- The Redis lease is at least 180 seconds. The process synthesis watchdog is
  controlled by `BLUEMAGPIE_SYNTHESIS_WATCHDOG_SECONDS` (120 seconds by
  default). If CUDA does not return in time, the gateway immediately exits the
  process without releasing the Redis lease; cancelling a CUDA thread would be
  unsafe, and continuing to serve could overlap two generations. Startup and
  close have a separately configurable
  `BLUEMAGPIE_MODEL_LIFECYCLE_WATCHDOG_SECONDS` (also 120 seconds by default);
  increasing either watchdog automatically extends the Redis lease.
- Access logging is disabled. Application errors log voice and exception type,
  never request text, exception messages, or tracebacks.
- `BLUEMAGPIE_INTERNAL_TOKEN` must contain at least 32 characters.

Build on the ARM64 host:

```sh
docker build --pull=false -t storyvoice/bluemagpie-gateway:20260815 \
  services/bluemagpie-gateway
```

Run on an internal Docker network with the PoC cache mounted read-only:

```sh
docker run --rm --gpus all --read-only --tmpfs /tmp:rw,size=1g \
  --network storyvoice-internal -p 127.0.0.1:8081:8081 \
  -e BLUEMAGPIE_INTERNAL_TOKEN='<long-random-shared-secret>' \
  -v /home/admin/projects/bluemagpie-poc/hf-cache:/cache:ro \
  storyvoice/bluemagpie-gateway:20260815
```

The cache must contain this exact model snapshot before the container starts:

`OpenFormosa/BlueMagpie-TTS@6f7cab914a1e27c56b504ec663c0144dc25cc0a3`

## Contract tests (no GPU or torch)

```sh
python -m pip install -r services/bluemagpie-gateway/requirements-test.txt
python -m pytest -c services/bluemagpie-gateway/pytest.ini \
  services/bluemagpie-gateway/tests
```

These tests use an injected fake synthesizer. They exercise authentication,
schema limits, headers, PCM WAV validation, lifecycle readiness, error text
privacy, single-flight queue timeout, Redis-lock ownership, cancellation, and
fatal-watchdog behavior without importing BlueMagpie, Barbet, redis-py, torch,
or CUDA.

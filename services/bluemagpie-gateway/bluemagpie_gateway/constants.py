from pathlib import Path


MODEL_ID = "OpenFormosa/BlueMagpie-TTS"
MODEL_REVISION = "6f7cab914a1e27c56b504ec663c0144dc25cc0a3"
BLUEMAGPIE_SOURCE_REVISION = "ce384c8cc54efea1aaba7b9f1d7ded6c1c99aa9a"
BARBET_SOURCE_REVISION = "592e36ed77e91c5aed6f054f631df63bd4007fec"
# SHA-256/160 of the frozen BM1 synthesis contract: the three source/model revisions above,
# seed 2026081501, cfg 2.0, 10 inference steps, retry_badcase=false, PCM16 mono 48 kHz and the
# 200-scalar gateway ceiling. Cast revisions store this compact value so a rebuilt gateway cannot
# silently change the sound produced by an immutable cast snapshot.
PROVIDER_VERSION = "bm1-d2d7ef3e81456915eb7a3cfe2446a9f19417c21b"
FEMALE_EMBEDDING_SHA256 = (
    "e9556e14723c140985a104c1659d1ff8a5078d2fa28ce2fb756f04906641a8a7"
)

MALE_VOICE = "hung_yi_lee"
FEMALE_VOICE = "female_voice"
ALLOWED_VOICES = (MALE_VOICE, FEMALE_VOICE)

SAMPLE_RATE = 48_000
CHANNELS = 1
SAMPLE_WIDTH_BYTES = 2
MAX_TEXT_LENGTH = 200
MAX_REQUEST_BODY_BYTES = 4 * 1024
TOKEN_HEADER = "X-StoryVoice-Internal-Token"
REVISION_HEADER = "X-BlueMagpie-Model-Revision"
PROVIDER_VERSION_HEADER = "X-BlueMagpie-Provider-Version"
VOICE_HEADER = "X-BlueMagpie-Voice"
GPU_LOCK_KEY = "storyvoice:local-gpu:exclusive:v1"
MINIMUM_GPU_LOCK_LEASE_SECONDS = 180
FATAL_WATCHDOG_EXIT_CODE = 70

# The service never asks Hugging Face to resolve this revision. The exact
# snapshot must already exist in the read-only cache populated by the PoC.
CACHE_ROOT = Path("/cache/huggingface")
MODEL_SNAPSHOT = (
    CACHE_ROOT
    / "hub"
    / "models--OpenFormosa--BlueMagpie-TTS"
    / "snapshots"
    / MODEL_REVISION
)

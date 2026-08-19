COSYVOICE_SOURCE_REVISION = "074ca6dc9e80a2f424f1f74b48bdd7d3fea531cc"
MODEL_ID = "FunAudioLLM/Fun-CosyVoice3-0.5B-2512"
MODEL_REVISION = "29e01c4e8d000f4bcd70751be16fa94bf3d85a18"

# This is deliberately not configurable. User-controlled URLs, redirects, and
# proxy environment variables must never turn the gateway into an SSRF relay.
UPSTREAM_ORIGIN = "http://facespeak-voice-clone:8093"
UPSTREAM_HEALTH_PATH = "/health"
SYNTHESIS_PATH = "/v1/voice-clone/speech"
UPSTREAM_SYNTHESIS_PATH = SYNTHESIS_PATH

TOKEN_HEADER = "X-StoryVoice-Internal-Token"
SOURCE_REVISION_HEADER = "X-CosyVoice-Source-Revision"
MODEL_ID_HEADER = "X-CosyVoice-Model-Id"
MODEL_REVISION_HEADER = "X-CosyVoice-Model-Revision"

FATAL_WATCHDOG_EXIT_CODE = 70

MAX_TEXT_LENGTH = 1_000
MAX_REFERENCE_TEXT_LENGTH = 4_000
MAX_QUEUED_SYNTHESIS_REQUESTS = 1
MAX_REFERENCE_AUDIO_BYTES = 10 * 1024 * 1024
REFERENCE_SAMPLE_RATE = 48_000
MIN_REFERENCE_DURATION_SECONDS = 10
# Pinned CosyVoice `_extract_speech_token` rejects decoded prompts over 30s.
# Keep this provider boundary exact so invalid biometric audio never reaches it.
MAX_REFERENCE_DURATION_SECONDS = 30
# Allow multipart framing and the two bounded text fields in addition to the
# maximum audio part. The audio field is independently checked below.
MAX_REQUEST_BODY_BYTES = MAX_REFERENCE_AUDIO_BYTES + (128 * 1024)

SAMPLE_RATE = 24_000
CHANNELS = 1
SAMPLE_WIDTH_BYTES = 2
MAX_OUTPUT_DURATION_SECONDS = 300
MAX_OUTPUT_BYTES = 16 * 1024 * 1024
MAX_HEALTH_RESPONSE_BYTES = 4 * 1024

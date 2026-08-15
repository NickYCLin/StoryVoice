from __future__ import annotations

import io
import wave
from dataclasses import dataclass

from .constants import CHANNELS, SAMPLE_RATE, SAMPLE_WIDTH_BYTES


@dataclass(frozen=True, slots=True)
class WaveMetadata:
    frames: int
    duration_seconds: float


def validate_pcm_wave(content: bytes) -> WaveMetadata:
    """Validate the exact wire format accepted by the StoryVoice client."""

    if not content:
        raise ValueError("empty audio")

    try:
        with wave.open(io.BytesIO(content), "rb") as stream:
            if stream.getnchannels() != CHANNELS:
                raise ValueError("audio must be mono")
            if stream.getsampwidth() != SAMPLE_WIDTH_BYTES:
                raise ValueError("audio must be 16-bit PCM")
            if stream.getframerate() != SAMPLE_RATE:
                raise ValueError("audio must use a 48 kHz sample rate")
            if stream.getcomptype() != "NONE":
                raise ValueError("audio must be uncompressed PCM")

            frames = stream.getnframes()
            if frames <= 0:
                raise ValueError("audio has no frames")
    except (EOFError, wave.Error) as exc:
        raise ValueError("invalid WAV container") from exc

    return WaveMetadata(
        frames=frames,
        duration_seconds=frames / SAMPLE_RATE,
    )

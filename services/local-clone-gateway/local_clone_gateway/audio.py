from __future__ import annotations

import io
import wave
from dataclasses import dataclass

from .constants import (
    CHANNELS,
    MAX_REFERENCE_AUDIO_BYTES,
    MAX_REFERENCE_DURATION_SECONDS,
    MAX_OUTPUT_BYTES,
    MAX_OUTPUT_DURATION_SECONDS,
    MIN_REFERENCE_DURATION_SECONDS,
    REFERENCE_SAMPLE_RATE,
    SAMPLE_RATE,
    SAMPLE_WIDTH_BYTES,
)


@dataclass(frozen=True, slots=True)
class WaveMetadata:
    frames: int
    duration_seconds: float


def _validate_riff_envelope(content: bytes, *, max_bytes: int) -> None:
    if not content or len(content) > max_bytes:
        raise ValueError("invalid audio size")
    if len(content) < 12 or content[:4] != b"RIFF" or content[8:12] != b"WAVE":
        raise ValueError("invalid WAV container")
    if int.from_bytes(content[4:8], "little") + 8 != len(content):
        raise ValueError("invalid RIFF length")


def validate_reference_wave(content: bytes) -> WaveMetadata:
    """Validate the only voice-biometric sample contract sent upstream."""

    _validate_riff_envelope(content, max_bytes=MAX_REFERENCE_AUDIO_BYTES)
    try:
        with wave.open(io.BytesIO(content), "rb") as stream:
            if stream.getnchannels() != CHANNELS:
                raise ValueError("reference audio must be mono")
            if stream.getsampwidth() != SAMPLE_WIDTH_BYTES:
                raise ValueError("reference audio must be 16-bit PCM")
            if stream.getframerate() != REFERENCE_SAMPLE_RATE:
                raise ValueError("reference audio must use a 48 kHz sample rate")
            if stream.getcomptype() != "NONE":
                raise ValueError("reference audio must be uncompressed PCM")
            frames = stream.getnframes()
            duration_seconds = frames / REFERENCE_SAMPLE_RATE
            if not (
                MIN_REFERENCE_DURATION_SECONDS
                <= duration_seconds
                <= MAX_REFERENCE_DURATION_SECONDS
            ):
                raise ValueError("reference audio duration is outside the contract")
            if len(stream.readframes(frames)) != frames * CHANNELS * SAMPLE_WIDTH_BYTES:
                raise ValueError("reference audio data is truncated")
    except (EOFError, wave.Error) as exc:
        raise ValueError("invalid WAV container") from exc

    return WaveMetadata(frames=frames, duration_seconds=duration_seconds)


def validate_pcm_wave(content: bytes) -> WaveMetadata:
    """Accept only a bounded RIFF PCM16, mono, 24 kHz response."""

    _validate_riff_envelope(content, max_bytes=MAX_OUTPUT_BYTES)

    try:
        with wave.open(io.BytesIO(content), "rb") as stream:
            if stream.getnchannels() != CHANNELS:
                raise ValueError("audio must be mono")
            if stream.getsampwidth() != SAMPLE_WIDTH_BYTES:
                raise ValueError("audio must be 16-bit PCM")
            if stream.getframerate() != SAMPLE_RATE:
                raise ValueError("audio must use a 24 kHz sample rate")
            if stream.getcomptype() != "NONE":
                raise ValueError("audio must be uncompressed PCM")
            frames = stream.getnframes()
            if frames <= 0:
                raise ValueError("audio has no frames")
            duration_seconds = frames / SAMPLE_RATE
            if duration_seconds > MAX_OUTPUT_DURATION_SECONDS:
                raise ValueError("audio duration exceeds the limit")
            if len(stream.readframes(frames)) != frames * CHANNELS * SAMPLE_WIDTH_BYTES:
                raise ValueError("audio data is truncated")
    except (EOFError, wave.Error) as exc:
        raise ValueError("invalid WAV container") from exc

    return WaveMetadata(frames=frames, duration_seconds=duration_seconds)

from __future__ import annotations

import hashlib
import io
import platform
from pathlib import Path
from typing import Any, Protocol

from .constants import (
    ALLOWED_VOICES,
    FEMALE_EMBEDDING_SHA256,
    FEMALE_VOICE,
    MALE_VOICE,
    MODEL_SNAPSHOT,
    SAMPLE_RATE,
)


class SpeechSynthesizer(Protocol):
    def startup(self) -> None: ...

    def synthesize(self, text: str, voice: str) -> bytes: ...

    def close(self) -> None: ...


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


class BlueMagpieSynthesizer:
    """GPU model adapter whose heavyweight imports happen only at startup."""

    def __init__(self) -> None:
        self._torch: Any | None = None
        self._numpy: Any | None = None
        self._soundfile: Any | None = None
        self._model: Any | None = None
        self._speaker_centroids: dict[str, Any] = {}

    def startup(self) -> None:
        if self._model is not None:
            return

        if platform.machine().lower() not in {"aarch64", "arm64"}:
            raise RuntimeError("BlueMagpie gateway requires ARM64")
        if not MODEL_SNAPSHOT.is_dir():
            raise RuntimeError("the pinned model snapshot is absent from /cache")

        # Imported here so contract/HTTP tests need neither CUDA nor torch.
        import numpy as np
        import soundfile as sf
        import torch
        from bluemagpie import BlueMagpieModel
        from transformers import PreTrainedTokenizerFast

        if not torch.cuda.is_available():
            raise RuntimeError("CUDA is unavailable")

        tokenizer_path = MODEL_SNAPSHOT / "tokenizer.json"
        speaker_table_path = (
            MODEL_SNAPSHOT / "checkpoints" / "speaker_centroids.pt"
        )
        female_embedding_path = (
            MODEL_SNAPSHOT / "checkpoints" / "speaker_b_embedding.pt"
        )
        for required_path in (
            tokenizer_path,
            speaker_table_path,
            female_embedding_path,
        ):
            if not required_path.is_file():
                raise RuntimeError("the pinned model snapshot is incomplete")

        if _sha256(female_embedding_path) != FEMALE_EMBEDDING_SHA256:
            raise RuntimeError("the female embedding does not match its official SHA-256")

        tokenizer = PreTrainedTokenizerFast(
            tokenizer_file=str(tokenizer_path)
        )
        model = BlueMagpieModel.from_local(
            str(MODEL_SNAPSHOT),
            tokenizer=tokenizer,
            training=False,
            device="cuda",
        )
        if int(model.sample_rate) != SAMPLE_RATE:
            raise RuntimeError("the pinned model does not produce 48 kHz audio")

        speaker_table = torch.load(
            speaker_table_path,
            map_location="cpu",
            weights_only=True,
        )
        speaker_ids = list(speaker_table["speaker_ids"])
        if MALE_VOICE not in speaker_ids:
            raise RuntimeError("the pinned male voice is missing")

        female_payload = torch.load(
            female_embedding_path,
            map_location="cpu",
            weights_only=True,
        )
        if female_payload.get("speaker_id") != FEMALE_VOICE:
            raise RuntimeError("the pinned female voice has an unexpected id")

        self._torch = torch
        self._numpy = np
        self._soundfile = sf
        self._model = model
        self._speaker_centroids = {
            MALE_VOICE: speaker_table["centroids"][speaker_ids.index(MALE_VOICE)],
            FEMALE_VOICE: torch.nn.functional.normalize(
                female_payload["embedding"].float(),
                dim=0,
            ),
        }

    def synthesize(self, text: str, voice: str) -> bytes:
        if self._model is None:
            raise RuntimeError("the model is not loaded")
        if voice not in ALLOWED_VOICES:
            raise ValueError("unsupported voice")

        torch = self._torch
        np = self._numpy
        sf = self._soundfile
        if torch is None or np is None or sf is None:
            raise RuntimeError("the runtime is not initialized")

        # The fixed seed makes previews reproducible. The gateway is deliberately
        # single-flight, so no concurrent request can race the global CUDA RNG.
        torch.manual_seed(2026081501)
        torch.cuda.manual_seed_all(2026081501)
        with torch.inference_mode():
            audio = self._model.generate(
                target_text=text,
                speaker_centroid=self._speaker_centroids[voice],
                cfg_value=2.0,
                inference_timesteps=10,
                retry_badcase=False,
            )

        waveform = (
            audio.detach()
            .to(dtype=torch.float32, device="cpu")
            .squeeze()
            .numpy()
        )
        waveform = np.asarray(waveform, dtype=np.float32).reshape(-1)
        if waveform.size == 0 or not np.isfinite(waveform).all():
            raise RuntimeError("the model returned invalid audio")

        output = io.BytesIO()
        sf.write(
            output,
            np.clip(waveform, -1.0, 1.0),
            SAMPLE_RATE,
            subtype="PCM_16",
            format="WAV",
        )
        return output.getvalue()

    def close(self) -> None:
        model = self._model
        torch = self._torch
        self._model = None
        self._speaker_centroids = {}
        if model is not None:
            del model
        if torch is not None and torch.cuda.is_available():
            torch.cuda.empty_cache()

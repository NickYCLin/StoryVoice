#!/usr/bin/env python3
import argparse
import asyncio
import os
import shutil
import sys
import tempfile
from collections.abc import Awaitable, Callable
from pathlib import Path
from typing import Any

import edge_tts


DEFAULT_MAX_CHARS = 5_000
DEFAULT_MAX_ATTEMPTS = 3
BREAK_CHARACTERS = "\n。！？；，、,.!?:："


def split_text(text: str, max_chars: int = DEFAULT_MAX_CHARS) -> list[str]:
    if max_chars < 1:
        raise ValueError("max_chars must be positive")

    remaining = text.strip()
    chunks: list[str] = []
    while remaining:
        if len(remaining) <= max_chars:
            chunks.append(remaining)
            break

        window = remaining[:max_chars]
        boundary = max(window.rfind(character) for character in BREAK_CHARACTERS)
        cut = boundary + 1 if boundary >= max_chars // 2 else max_chars
        chunk = remaining[:cut].strip()
        if chunk:
            chunks.append(chunk)
        remaining = remaining[cut:].lstrip()

    return chunks


async def synthesize_text(
    text: str,
    output_path: str,
    voice: str,
    rate: str,
    *,
    max_chars: int = DEFAULT_MAX_CHARS,
    max_attempts: int = DEFAULT_MAX_ATTEMPTS,
    communicator_factory: Callable[..., Any] | None = None,
    delay: Callable[[float], Awaitable[None]] | None = None,
) -> None:
    if not text.strip():
        raise ValueError("narration text is empty")
    if max_attempts < 1:
        raise ValueError("max_attempts must be positive")

    output = Path(output_path).resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    chunks = split_text(text, max_chars)
    factory = communicator_factory or edge_tts.Communicate
    wait = delay or asyncio.sleep

    with tempfile.TemporaryDirectory(prefix="edge-tts-", dir=output.parent) as directory:
        work = Path(directory)
        audio_parts: list[Path] = []
        for index, chunk in enumerate(chunks):
            part = work / f"{index:05d}.mp3"
            for attempt in range(1, max_attempts + 1):
                part.unlink(missing_ok=True)
                try:
                    communicate = factory(chunk, voice, rate=rate)
                    await communicate.save(str(part))
                    if not part.exists() or part.stat().st_size < 1:
                        raise RuntimeError("edge-tts returned empty audio")
                    break
                except Exception as error:
                    if attempt == max_attempts:
                        raise RuntimeError(
                            f"edge-tts chunk {index + 1}/{len(chunks)} failed "
                            f"after {max_attempts} attempts"
                        ) from error
                    await wait(float(2 ** (attempt - 1)))
            audio_parts.append(part)

        candidate = work / "complete.mp3"
        with candidate.open("wb") as destination:
            for part in audio_parts:
                with part.open("rb") as source:
                    shutil.copyfileobj(source, destination)
        if candidate.stat().st_size < 1:
            raise RuntimeError("edge-tts returned empty audio")
        os.replace(candidate, output)


async def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True)
    parser.add_argument("--voice", required=True)
    parser.add_argument("--rate", required=True)
    args = parser.parse_args()

    text = sys.stdin.read()
    await synthesize_text(text, args.output, args.voice, args.rate)


if __name__ == "__main__":
    asyncio.run(main())

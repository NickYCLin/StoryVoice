#!/usr/bin/env python3
import argparse
import asyncio
import os
import sys

import edge_tts


async def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True)
    parser.add_argument("--voice", required=True)
    parser.add_argument("--rate", required=True)
    args = parser.parse_args()

    text = sys.stdin.read()
    if not text.strip():
        raise ValueError("narration text is empty")

    output = os.path.abspath(args.output)
    os.makedirs(os.path.dirname(output), exist_ok=True)
    communicate = edge_tts.Communicate(text, args.voice, rate=args.rate)
    await communicate.save(output)


if __name__ == "__main__":
    asyncio.run(main())

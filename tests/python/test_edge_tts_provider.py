import asyncio
import importlib.util
from pathlib import Path
import sys
import tempfile
import types
import unittest


fake_edge_tts = types.ModuleType("edge_tts")
fake_edge_tts.Communicate = object
sys.modules.setdefault("edge_tts", fake_edge_tts)
module_path = Path(__file__).parents[2] / "src" / "StoryVoice.Worker" / "edge_tts_provider.py"
spec = importlib.util.spec_from_file_location("edge_tts_provider", module_path)
provider = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(provider)


class EdgeTtsProviderTests(unittest.IsolatedAsyncioTestCase):
    def test_split_text_limits_chunks_and_preserves_spoken_characters(self):
        text = ("甲" * 3_000) + "。\n" + ("乙" * 3_000) + "！"

        chunks = provider.split_text(text, max_chars=4_000)

        self.assertGreater(len(chunks), 1)
        self.assertTrue(all(0 < len(chunk) <= 4_000 for chunk in chunks))
        expected = "".join(text.split())
        actual = "".join("".join(chunks).split())
        self.assertEqual(expected, actual)

    def test_split_text_never_exceeds_limit_when_break_is_next_character(self):
        chunks = provider.split_text(("甲" * 5_000) + "。乙", max_chars=5_000)

        self.assertEqual([5_000, 2], [len(chunk) for chunk in chunks])

    async def test_synthesize_text_retries_failed_chunk_and_concatenates_audio(self):
        attempts = {}

        class FakeCommunicate:
            def __init__(self, text, voice, rate):
                self.text = text

            async def save(self, path):
                attempts[self.text] = attempts.get(self.text, 0) + 1
                if "第二段" in self.text and attempts[self.text] == 1:
                    raise RuntimeError("transient")
                Path(path).write_bytes(f"audio:{self.text}|".encode())

        async def no_delay(_seconds):
            await asyncio.sleep(0)

        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "book.mp3"
            await provider.synthesize_text(
                "第一段。第二段。",
                str(output),
                "voice",
                "+0%",
                max_chars=4,
                max_attempts=3,
                communicator_factory=FakeCommunicate,
                delay=no_delay,
            )

            self.assertEqual(2, attempts["第二段。"])
            self.assertEqual(
                "audio:第一段。|audio:第二段。|".encode(),
                output.read_bytes(),
            )
            self.assertEqual([output], list(Path(directory).iterdir()))

    async def test_synthesize_text_does_not_publish_partial_audio_after_failure(self):
        class FailingCommunicate:
            def __init__(self, text, voice, rate):
                pass

            async def save(self, path):
                raise RuntimeError("permanent")

        async def no_delay(_seconds):
            await asyncio.sleep(0)

        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "book.mp3"
            with self.assertRaises(RuntimeError):
                await provider.synthesize_text(
                    "不會成功。",
                    str(output),
                    "voice",
                    "+0%",
                    max_chars=4_000,
                    max_attempts=2,
                    communicator_factory=FailingCommunicate,
                    delay=no_delay,
                )

            self.assertFalse(output.exists())
            self.assertEqual([], list(Path(directory).iterdir()))


if __name__ == "__main__":
    unittest.main()

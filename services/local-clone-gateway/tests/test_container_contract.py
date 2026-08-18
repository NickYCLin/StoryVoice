import json
from pathlib import Path


def test_uvicorn_concurrency_headroom_remains_explicit_and_bounded() -> None:
    dockerfile = Path(__file__).resolve().parents[1] / "Dockerfile"
    cmd_line = next(
        line for line in dockerfile.read_text(encoding="utf-8").splitlines()
        if line.startswith("CMD ")
    )
    command = json.loads(cmd_line.removeprefix("CMD "))

    limit_index = command.index("--limit-concurrency")
    assert command[limit_index + 1] == "8"
    assert command[command.index("--workers") + 1] == "1"
    assert command[command.index("--timeout-keep-alive") + 1] == "5"

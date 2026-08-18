from __future__ import annotations

import os
from dataclasses import dataclass, field


@dataclass(frozen=True, slots=True)
class GatewaySettings:
    internal_token: str = field(repr=False)
    queue_timeout_seconds: float = 15.0
    synthesis_watchdog_seconds: float = 180.0
    ready_timeout_seconds: float = 3.0

    def __post_init__(self) -> None:
        if not 32 <= len(self.internal_token) <= 512:
            raise RuntimeError(
                "LOCAL_CLONE_INTERNAL_TOKEN must contain between 32 and 512 characters"
            )
        if any(not "!" <= character <= "~" for character in self.internal_token):
            raise RuntimeError(
                "LOCAL_CLONE_INTERNAL_TOKEN must contain only printable ASCII characters without spaces"
            )
        if not 0 < self.queue_timeout_seconds <= 300:
            raise RuntimeError(
                "LOCAL_CLONE_QUEUE_TIMEOUT_SECONDS must be between 0 and 300"
            )
        if not 0 < self.synthesis_watchdog_seconds <= 3_600:
            raise RuntimeError(
                "LOCAL_CLONE_SYNTHESIS_WATCHDOG_SECONDS must be between 0 and 3600"
            )
        if not 0 < self.ready_timeout_seconds <= 30:
            raise RuntimeError(
                "LOCAL_CLONE_READY_TIMEOUT_SECONDS must be between 0 and 30"
            )

    @classmethod
    def from_env(cls) -> "GatewaySettings":
        def number(name: str, default: str) -> float:
            raw_value = os.environ.get(name, default)
            try:
                return float(raw_value)
            except ValueError as exc:
                raise RuntimeError(f"{name} must be a number") from exc

        return cls(
            internal_token=os.environ.get("LOCAL_CLONE_INTERNAL_TOKEN", ""),
            queue_timeout_seconds=number(
                "LOCAL_CLONE_QUEUE_TIMEOUT_SECONDS",
                "15",
            ),
            synthesis_watchdog_seconds=number(
                "LOCAL_CLONE_SYNTHESIS_WATCHDOG_SECONDS",
                "180",
            ),
            ready_timeout_seconds=number(
                "LOCAL_CLONE_READY_TIMEOUT_SECONDS",
                "3",
            ),
        )

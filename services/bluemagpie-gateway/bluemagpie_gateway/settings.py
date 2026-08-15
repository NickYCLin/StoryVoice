from __future__ import annotations

import math
import os
from dataclasses import dataclass, field


@dataclass(frozen=True, slots=True)
class GatewaySettings:
    internal_token: str = field(repr=False)
    queue_timeout_seconds: float = 15.0
    redis_url: str = field(
        default="redis://redis:6379/0",
        repr=False,
    )
    synthesis_watchdog_seconds: float = 120.0
    model_lifecycle_watchdog_seconds: float = 120.0

    def __post_init__(self) -> None:
        if len(self.internal_token) < 32:
            raise RuntimeError(
                "BLUEMAGPIE_INTERNAL_TOKEN must contain at least 32 characters"
            )
        if not 0 < self.queue_timeout_seconds <= 300:
            raise RuntimeError(
                "BLUEMAGPIE_QUEUE_TIMEOUT_SECONDS must be between 0 and 300"
            )
        if not self.redis_url.startswith(("redis://", "rediss://")):
            raise RuntimeError("BLUEMAGPIE_REDIS_URL must be a Redis URL")
        if not 0 < self.synthesis_watchdog_seconds <= 3600:
            raise RuntimeError(
                "BLUEMAGPIE_SYNTHESIS_WATCHDOG_SECONDS must be between 0 and 3600"
            )
        if not 0 < self.model_lifecycle_watchdog_seconds <= 3600:
            raise RuntimeError(
                "BLUEMAGPIE_MODEL_LIFECYCLE_WATCHDOG_SECONDS must be between 0 and 3600"
            )

    @property
    def gpu_lock_lease_milliseconds(self) -> int:
        # The lease must outlive a watchdog-triggered container exit. This gives
        # the CUDA driver time to tear the dead process down before another
        # StoryVoice process can acquire the shared GPU.
        lease_seconds = max(
            180,
            math.ceil(self.synthesis_watchdog_seconds) + 60,
            math.ceil(self.model_lifecycle_watchdog_seconds) + 60,
        )
        return lease_seconds * 1000

    @property
    def startup_gpu_lock_timeout_seconds(self) -> float:
        # A watchdog-killed predecessor deliberately leaves its lease behind.
        # A replacement must wait longer than the maximum remaining lease
        # before deciding startup cannot proceed.
        return (self.gpu_lock_lease_milliseconds / 1000) + 30

    @classmethod
    def from_env(cls) -> "GatewaySettings":
        token = os.environ.get("BLUEMAGPIE_INTERNAL_TOKEN", "")
        redis_url = os.environ.get(
            "BLUEMAGPIE_REDIS_URL",
            "redis://redis:6379/0",
        )
        raw_timeout = os.environ.get("BLUEMAGPIE_QUEUE_TIMEOUT_SECONDS", "15")
        raw_watchdog = os.environ.get(
            "BLUEMAGPIE_SYNTHESIS_WATCHDOG_SECONDS",
            "120",
        )
        raw_lifecycle_watchdog = os.environ.get(
            "BLUEMAGPIE_MODEL_LIFECYCLE_WATCHDOG_SECONDS",
            "120",
        )
        try:
            queue_timeout = float(raw_timeout)
        except ValueError as exc:
            raise RuntimeError(
                "BLUEMAGPIE_QUEUE_TIMEOUT_SECONDS must be a number"
            ) from exc
        try:
            watchdog_timeout = float(raw_watchdog)
        except ValueError as exc:
            raise RuntimeError(
                "BLUEMAGPIE_SYNTHESIS_WATCHDOG_SECONDS must be a number"
            ) from exc
        try:
            lifecycle_watchdog_timeout = float(raw_lifecycle_watchdog)
        except ValueError as exc:
            raise RuntimeError(
                "BLUEMAGPIE_MODEL_LIFECYCLE_WATCHDOG_SECONDS must be a number"
            ) from exc

        return cls(
            internal_token=token,
            queue_timeout_seconds=queue_timeout,
            redis_url=redis_url,
            synthesis_watchdog_seconds=watchdog_timeout,
            model_lifecycle_watchdog_seconds=lifecycle_watchdog_timeout,
        )

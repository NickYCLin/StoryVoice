from __future__ import annotations

import asyncio
import secrets
from typing import Any, Protocol

from .constants import GPU_LOCK_KEY, MINIMUM_GPU_LOCK_LEASE_SECONDS


class GpuExecutionLock(Protocol):
    async def acquire(self, timeout_seconds: float) -> str | None: ...

    async def release(self, token: str) -> None: ...

    async def close(self) -> None: ...


class RedisGpuExecutionLock:
    """Token-owned Redis lease shared by every local GPU executor."""

    _RELEASE_SCRIPT = """
if redis.call('get', KEYS[1]) == ARGV[1] then
    return redis.call('del', KEYS[1])
end
return 0
""".strip()

    def __init__(
        self,
        redis_url: str,
        lease_milliseconds: int,
        *,
        client: Any | None = None,
    ) -> None:
        if lease_milliseconds < MINIMUM_GPU_LOCK_LEASE_SECONDS * 1000:
            raise ValueError("GPU lock lease must be at least 180 seconds")

        # Imported only for the production factory. Contract tests inject a
        # fake lock and therefore do not need redis-py or a Redis server.
        if client is None:
            from redis.asyncio import Redis

            client = Redis.from_url(
                redis_url,
                decode_responses=True,
                socket_connect_timeout=2,
                socket_timeout=2,
            )
        self._client: Any = client
        self._lease_milliseconds = lease_milliseconds

    async def acquire(self, timeout_seconds: float) -> str | None:
        if timeout_seconds <= 0:
            return None

        loop = asyncio.get_running_loop()
        deadline = loop.time() + timeout_seconds
        token = secrets.token_urlsafe(32)

        while True:
            remaining = deadline - loop.time()
            if remaining <= 0:
                return None
            set_task = asyncio.create_task(
                self._client.set(
                    GPU_LOCK_KEY,
                    token,
                    nx=True,
                    px=self._lease_milliseconds,
                )
            )
            try:
                done, _pending = await asyncio.wait(
                    {set_task},
                    timeout=remaining,
                )
            except asyncio.CancelledError:
                await self._settle_and_cleanup(set_task, token)
                raise

            if not done:
                # Do not cancel an in-flight SET: it may already have reached
                # Redis. redis-py's two-second socket timeout bounds this wait.
                # Only after the command is terminal can compare-and-delete be
                # ordered without a late SET recreating an orphan lease.
                await self._settle_and_cleanup(set_task, token)
                return None

            try:
                acquired = set_task.result()
            except BaseException:
                # A lost response is acquisition-uncertain even though the task
                # is terminal. The owner token makes cleanup safe.
                await self._owner_cleanup(token)
                raise

            if acquired:
                return token

            remaining = deadline - loop.time()
            if remaining <= 0:
                return None
            await asyncio.sleep(min(0.1, remaining))

    async def _settle_and_cleanup(
        self,
        set_task: asyncio.Task[Any],
        token: str,
    ) -> None:
        while not set_task.done():
            try:
                await asyncio.shield(set_task)
            except asyncio.CancelledError:
                # Caller cancellation cannot reintroduce the late-SET race.
                continue
            except BaseException:
                break

        try:
            set_task.result()
        except BaseException:
            pass
        await self._owner_cleanup(token)

    async def _owner_cleanup(self, token: str) -> None:
        release_task = asyncio.create_task(self.release(token))
        while not release_task.done():
            try:
                await asyncio.shield(release_task)
            except asyncio.CancelledError:
                # Once uncertainty cleanup starts it must reach a terminal
                # compare-and-delete result before caller cancellation escapes.
                continue
        release_task.result()

    async def release(self, token: str) -> None:
        await self._client.eval(
            self._RELEASE_SCRIPT,
            1,
            GPU_LOCK_KEY,
            token,
        )

    async def close(self) -> None:
        await self._client.aclose()

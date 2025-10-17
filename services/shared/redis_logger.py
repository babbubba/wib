import os
import json
from datetime import datetime, timezone
from typing import Optional, Dict, Any
from enum import Enum
import redis.asyncio as aioredis


class LogSeverity(str, Enum):
    VERBOSE = "VERBOSE"
    DEBUG = "DEBUG"
    INFO = "INFO"
    WARNING = "WARNING"
    ERROR = "ERROR"


class RedisLogger:
    """
    Async Redis logger for Python services
    Publishes structured logs to Redis stream for centralized monitoring
    """

    def __init__(
        self,
        source: str,
        redis_url: str,
        stream_key: str = "app_logs",
        max_stream_length: int = 10000,
        min_log_level: LogSeverity = LogSeverity.INFO,
    ):
        self.source = source
        self.stream_key = stream_key
        self.max_stream_length = max_stream_length
        self.min_log_level = min_log_level
        self.enabled = True

        try:
            self.redis = aioredis.from_url(redis_url, decode_responses=False)
        except Exception as e:
            print(f"[RedisLogger] Failed to connect to Redis: {e}", flush=True)
            print("[RedisLogger] Falling back to console-only logging", flush=True)
            self.enabled = False
            self.redis = None

    async def log(
        self,
        severity: LogSeverity,
        title: str,
        message: str,
        metadata: Optional[Dict[str, Any]] = None,
    ):
        """Log a message with specified severity"""
        # Check if log level is enabled
        severity_order = {
            LogSeverity.VERBOSE: 0,
            LogSeverity.DEBUG: 1,
            LogSeverity.INFO: 2,
            LogSeverity.WARNING: 3,
            LogSeverity.ERROR: 4,
        }

        if severity_order.get(severity, 0) < severity_order.get(self.min_log_level, 0):
            return

        log_entry = {
            "timestamp": datetime.now(timezone.utc).isoformat(),
            "level": severity.value,
            "source": self.source,
            "title": title,
            "message": message,
            "metadata": metadata or {},
        }

        # Always log to console as fallback
        color_codes = {
            LogSeverity.ERROR: "\033[91m",  # Red
            LogSeverity.WARNING: "\033[93m",  # Yellow
            LogSeverity.INFO: "\033[96m",  # Cyan
            LogSeverity.DEBUG: "\033[90m",  # Gray
            LogSeverity.VERBOSE: "\033[37m",  # White
        }
        reset_code = "\033[0m"
        color = color_codes.get(severity, reset_code)

        print(
            f"{color}[{log_entry['timestamp']}] [{log_entry['level']}] [{log_entry['source']}] {log_entry['title']}: {log_entry['message']}{reset_code}",
            flush=True,
        )

        if not self.enabled or self.redis is None:
            return

        try:
            json_str = json.dumps(log_entry)
            # Use XADD with MAXLEN to automatically trim the stream
            await self.redis.execute_command(
                "XADD",
                self.stream_key,
                "MAXLEN",
                "~",
                str(self.max_stream_length),
                "*",
                "json",
                json_str,
            )
        except Exception as e:
            # NEVER throw from logger - just log to console
            print(f"[RedisLogger] Failed to publish log: {e}", flush=True)

    async def verbose(
        self, title: str, message: str, metadata: Optional[Dict[str, Any]] = None
    ):
        await self.log(LogSeverity.VERBOSE, title, message, metadata)

    async def debug(
        self, title: str, message: str, metadata: Optional[Dict[str, Any]] = None
    ):
        await self.log(LogSeverity.DEBUG, title, message, metadata)

    async def info(
        self, title: str, message: str, metadata: Optional[Dict[str, Any]] = None
    ):
        await self.log(LogSeverity.INFO, title, message, metadata)

    async def warning(
        self, title: str, message: str, metadata: Optional[Dict[str, Any]] = None
    ):
        await self.log(LogSeverity.WARNING, title, message, metadata)

    async def error(
        self,
        title: str,
        message: str,
        exception: Optional[Exception] = None,
        metadata: Optional[Dict[str, Any]] = None,
    ):
        metadata = metadata or {}

        if exception:
            metadata["exceptionType"] = type(exception).__name__
            metadata["exceptionMessage"] = str(exception)
            # Get traceback
            import traceback

            metadata["stackTrace"] = "".join(
                traceback.format_exception(type(exception), exception, exception.__traceback__)
            )

        await self.log(LogSeverity.ERROR, title, message, metadata)

    async def close(self):
        """Close Redis connection"""
        if self.enabled and self.redis:
            await self.redis.close()

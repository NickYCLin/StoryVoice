# syntax=docker/dockerfile:1.7

FROM --platform=linux/arm64 nvcr.io/nvidia/pytorch:25.11-py3

LABEL org.opencontainers.image.source="https://github.com/OpenFormosa/BlueMagpie-TTS" \
      storyvoice.bluemagpie.source-revision="ce384c8cc54efea1aaba7b9f1d7ded6c1c99aa9a" \
      storyvoice.barbet.source-revision="592e36ed77e91c5aed6f054f631df63bd4007fec" \
      storyvoice.bluemagpie.model-revision="6f7cab914a1e27c56b504ec663c0144dc25cc0a3" \
      storyvoice.bluemagpie.female-embedding-sha256="e9556e14723c140985a104c1659d1ff8a5078d2fa28ce2fb756f04906641a8a7"

SHELL ["/bin/bash", "-o", "pipefail", "-c"]

ENV DEBIAN_FRONTEND=noninteractive \
    PIP_DISABLE_PIP_VERSION_CHECK=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    HF_HOME=/cache/huggingface \
    HF_HUB_OFFLINE=1 \
    TRANSFORMERS_OFFLINE=1 \
    HF_HUB_DISABLE_TELEMETRY=1 \
    DO_NOT_TRACK=1 \
    TOKENIZERS_PARALLELISM=false \
    HOME=/tmp/gateway \
    TORCH_HOME=/tmp/torch \
    MPLCONFIGDIR=/tmp/matplotlib \
    NUMBA_CACHE_DIR=/tmp/numba

RUN test "$(uname -m)" = "aarch64" \
 && apt-get update \
 && apt-get install -y --no-install-recommends \
      ca-certificates \
      git \
      libsndfile1 \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /opt/gateway
COPY requirements.txt ./requirements.txt
RUN python -m pip install --no-cache-dir -r requirements.txt

RUN git clone --filter=blob:none \
      https://github.com/OpenFormosa/Barbet.git /opt/src/barbet \
 && git -C /opt/src/barbet checkout --detach \
      592e36ed77e91c5aed6f054f631df63bd4007fec \
 && test "$(git -C /opt/src/barbet rev-parse HEAD)" = \
      "592e36ed77e91c5aed6f054f631df63bd4007fec" \
 && python -m pip install --no-cache-dir --no-deps \
      --no-build-isolation /opt/src/barbet \
 && git clone --filter=blob:none \
      https://github.com/OpenFormosa/BlueMagpie-TTS.git /opt/src/bluemagpie \
 && git -C /opt/src/bluemagpie checkout --detach \
      ce384c8cc54efea1aaba7b9f1d7ded6c1c99aa9a \
 && test "$(git -C /opt/src/bluemagpie rev-parse HEAD)" = \
      "ce384c8cc54efea1aaba7b9f1d7ded6c1c99aa9a" \
 && python -m pip install --no-cache-dir --no-deps \
      --no-build-isolation /opt/src/bluemagpie \
 && rm -rf /opt/src

COPY bluemagpie_gateway ./bluemagpie_gateway

RUN python -m compileall -q bluemagpie_gateway \
 && useradd --create-home --home-dir /tmp/gateway \
      --uid 10001 --shell /usr/sbin/nologin gateway \
 && mkdir -p /tmp/torch /tmp/matplotlib /tmp/numba \
 && chown -R gateway:gateway \
      /tmp/gateway /tmp/torch /tmp/matplotlib /tmp/numba

USER 10001:10001
EXPOSE 8081

HEALTHCHECK --interval=30s --timeout=5s --start-period=90s --retries=3 \
  CMD ["python", "-c", "import urllib.request; urllib.request.urlopen('http://127.0.0.1:8081/health/ready', timeout=3).read()"]

CMD ["uvicorn", "bluemagpie_gateway.main:app", "--host", "0.0.0.0", "--port", "8081", "--workers", "1", "--no-access-log"]

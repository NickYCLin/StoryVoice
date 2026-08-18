# syntax=docker/dockerfile:1.7

FROM python:3.12.11-slim-bookworm

LABEL storyvoice.cosyvoice.source-revision="074ca6dc9e80a2f424f1f74b48bdd7d3fea531cc" \
      storyvoice.cosyvoice.model-id="FunAudioLLM/Fun-CosyVoice3-0.5B-2512" \
      storyvoice.cosyvoice.model-revision="29e01c4e8d000f4bcd70751be16fa94bf3d85a18" \
      storyvoice.local-clone.upstream="http://facespeak-voice-clone:8093"

ENV PIP_DISABLE_PIP_VERSION_CHECK=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    HOME=/tmp/gateway

WORKDIR /opt/gateway
COPY requirements.txt ./requirements.txt
RUN python -m pip install --no-cache-dir -r requirements.txt

COPY local_clone_gateway ./local_clone_gateway
RUN python -m compileall -q local_clone_gateway \
 && useradd --create-home --home-dir /tmp/gateway \
      --uid 10001 --shell /usr/sbin/nologin gateway \
 && chown -R gateway:gateway /tmp/gateway

USER 10001:10001
EXPOSE 8082

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD ["python", "-c", "import urllib.request; urllib.request.urlopen('http://127.0.0.1:8082/health/ready', timeout=3).read()"]

CMD ["uvicorn", "local_clone_gateway.main:app", "--host", "0.0.0.0", "--port", "8082", "--workers", "1", "--limit-concurrency", "4", "--no-access-log", "--no-proxy-headers", "--no-server-header", "--timeout-keep-alive", "5"]

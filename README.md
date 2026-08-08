# StoryVoice

> **AI Story Director — turn books into performances.**

StoryVoice 將使用者有權處理的電子書轉成多角色、具情緒與旁白層次的 AI 有聲書。它不是單純的 EPUB-to-TTS：核心放在 **Story Analyzer、Character Bible、AI Director 與可人工修訂的演出流程**。

## Current status

Phase 1 Foundation 已建立：

- .NET 10 Clean Architecture：API / Application / Domain / Infrastructure / Worker
- PostgreSQL + EF Core migration
- Redis-ready background processing boundary
- React 19 + TypeScript + Vite + Tailwind CSS 4
- Book / Chapter domain model and REST API
- Serilog, OpenAPI, liveness/readiness health checks
- Docker Compose development stack
- Unit and integration tests + GitHub Actions CI

AI、TTS 與 EPUB 上傳尚未假裝完成；它們會依 [`DEVELOPMENT_PLAN.md`](DEVELOPMENT_PLAN.md) 分階段落地。

## Quick start

Prerequisites: Docker 29+ with Compose v2.

```bash
cp .env.example .env
docker compose up --build
```

Open:

- Web UI: <http://localhost:3000>
- API: <http://localhost:8080>
- OpenAPI document (Development mode): `/openapi/v1.json`
- Liveness: <http://localhost:8080/health/live>
- Readiness: <http://localhost:8080/health/ready>

Stop the stack:

```bash
docker compose down
```

Add `-v` only when you intentionally want to remove local PostgreSQL and Redis data.

## Local development

Backend:

```bash
dotnet restore StoryVoice.sln
dotnet build StoryVoice.sln
dotnet test StoryVoice.sln
dotnet run --project src/StoryVoice.Api
```

Frontend:

```bash
cd src/StoryVoice.Web
npm install
npm run dev
```

Vite proxies `/api` and `/health` to `http://localhost:8080`.

## API foundation

```text
POST /api/books
GET  /api/books
GET  /api/books/{id}
```

Example:

```json
{
  "title": "月下故事",
  "author": "StoryVoice",
  "language": "zh-TW",
  "originalFileName": "story.epub",
  "chapters": [
    {
      "chapterNumber": 1,
      "title": "序章",
      "originalText": "故事從月色裡開始。"
    }
  ]
}
```

## Architecture

```text
Electronic Book
      ↓
Book Parser
      ↓
Story Analyzer ──→ Character Bible
      ↓
AI Director ─────→ Voice Casting
      ↓
TTS Provider
      ↓
Audio Composer
      ↓
Web Player
```

```text
src/
├─ StoryVoice.Api
├─ StoryVoice.Application
├─ StoryVoice.Domain
├─ StoryVoice.Infrastructure
├─ StoryVoice.Worker
└─ StoryVoice.Web

tests/
├─ StoryVoice.UnitTests
└─ StoryVoice.IntegrationTests
```

## Roadmap

1. **Book Import** — EPUB / TXT upload, metadata, TOC and chapter extraction
2. **Story Analyzer** — narrator, dialogue, speaker, emotion and confidence
3. **Character Bible** — aliases, merge, voice lock and cross-chapter consistency
4. **Voice Casting / TTS** — provider abstraction, preview, cache and segment regeneration
5. **Audio Composer / Player** — FFmpeg, chapter audio, sentence highlight and resume
6. **AI Director** — tone, speed, pause, volume, scene context
7. **Audio Drama** — ambient sound, effects and BGM

## Security and content rights

- StoryVoice **does not provide DRM circumvention**.
- Process only content you own or have the right to transform.
- API keys belong in environment variables or a secret manager, never Git.
- Uploaded books, generated audio, analysis results and runtime volumes are ignored by Git.
- Generated audio is not automatically licensed for redistribution.

See [`SECURITY.md`](SECURITY.md) for responsible disclosure.

## Contributing

Issues and pull requests are welcome. Read [`CONTRIBUTING.md`](CONTRIBUTING.md) before starting a larger change.

## License

StoryVoice source code is released under the [MIT License](LICENSE). Third-party models, voices and generated content may have separate licenses and terms.

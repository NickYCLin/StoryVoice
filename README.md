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
- EPUB / TXT multipart upload、metadata、TOC 與章節解析
- 博客來電子書櫃 Companion：同步可見 metadata 與官方閱讀連結
- 使用者明確連結合法 EPUB／TXT 正文與手動閱讀筆記（擷取式摘要入口已退場，既有資料暫留供回復）
- 單一神經語音 MVP：持久化工作、租約與重試、取消、私有 MP3 與 owner-scoped Range 串流
- 全書庫處理狀態矩陣：分開標示官方 TTS、合法正文、筆記與 StoryVoice 音訊
- 跨冊系列／固定角色／alias、不可變 cast revision 與整批原子啟用資料邊界
- 章名、獨立旁白、對話與視角角色內心／文件默讀的 deterministic offset segmentation；系列可選擇獨立旁白或「所有非對白皆由 POV 主角朗讀」
- owner-scoped 系列配音管理 API 與伺服器 voice allowlist
- 本機 LLM 角色與 alias 分析、候選勾選／合併、原子套用系列角色表
- 規則優先、本機 LLM 補判的逐句說話者草稿；只有高信心自動確認，其餘進人工審核
- 書冊（獨立於角色配音系列之外的單純書本分類收藏）與冊次排序
- 書庫分類統一使用書冊；舊的瀏覽器「此裝置標籤」已移除
- 書冊唯讀分享：依 email 分享給其他已註冊使用者，可隨時撤銷
- React Router 多頁面前端（書庫／書冊／分享給我的），取代原本的單頁式版面
- Serilog, OpenAPI, liveness/readiness health checks
- Docker Compose development stack
- Unit and integration tests + GitHub Actions CI

AI 與多角色 TTS 仍按 [`DEVELOPMENT_PLAN.md`](DEVELOPMENT_PLAN.md) 分階段落地；
目前已具備角色候選審核、說話者草稿、逐章確認與 staged 多角色產製。逐項完成度與下一個實作入口見
[`docs/PROJECT_STATUS.md`](docs/PROJECT_STATUS.md)。

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

### Private BlueMagpie Taiwan-Mandarin preview and short canary (ARM64 + NVIDIA GPU)

The `bluemagpie` Compose profile adds a self-hosted gateway with no host port on an
internal Docker network. `BLUEMAGPIE_ENABLED=true` enables the fixed-sentence
preview. Formal series narration is a separate opt-in:
`BLUEMAGPIE_FORMAL_NARRATION_ENABLED=true` exposes exactly two built-in voices
(`female_voice` and `hung_yi_lee`) in the series voice catalog and admits staged
multi-character jobs through the local Worker.

The formal path remains an explicit, private/internal opt-in. The Worker persists
validated, deterministic WAV chunks in a separate private volume so a retry or
controlled restart only synthesizes missing chunks. The cache is regenerable, capped
at 32 GiB by default, retained for seven days, and deliberately excluded from published
audio backups. A restart/resume canary and a bounded 36-chunk cold long-form benchmark
have passed without activating their staged audio. Admission now rejects oversized jobs
before creating rows, progress writes are percentage-throttled, and owners can discard a
staged rebuild. Keep complete-book use disabled until exhausted-attempt recovery,
structured long-run metrics, and GPU/LLM coexistence are verified. The model weights are
marked with license `other`, so do not assume redistribution or commercial-use rights.

Preload the pinned model cache, create a random secret of at least 32 characters,
then set these values outside Git:

```bash
BLUEMAGPIE_ENABLED=true
BLUEMAGPIE_FORMAL_NARRATION_ENABLED=false
BLUEMAGPIE_INTERNAL_TOKEN=<random-internal-secret>
BLUEMAGPIE_CACHE_PATH=/absolute/path/to/the/preloaded/cache
VOAI_API_KEY=
VOAI_PAID_API_KEY=
```

Start the private preview with:

```bash
docker compose --profile bluemagpie up -d --build bluemagpie-gateway api worker web
```

For a short BlueMagpie canary, temporarily set the formal flag to `true`, build only
private staged audio, then return it to `false`. Paid VoAI synthesis is deliberately
separate: the Worker ignores the legacy `VOAI_API_KEY` variable and can only enable
paid calls from `VOAI_PAID_API_KEY`. Both keys must remain empty for a no-paid-API
deployment and for the BlueMagpie canary described here.

### Authorized local Clone private preview

The `local-clone` Compose profile adds an internal-only gateway to the existing
FaceSpeak/CosyVoice executor. It does not publish a host port, does not register a
narration provider, and cannot switch a series cast or active audiobook. The API reads
an explicitly allowlisted reference WAV and transcript from the read-only
`local-clone-assets` volume, verifies their configured SHA-256 values, and exposes only
an owner-scoped character preview endpoint.

Keep `LOCAL_CLONE_PREVIEW_ENABLED=false` until the FaceSpeak executor is running the
same shared GPU exclusion lock and returns the pinned source/model attestation. Then
populate a random `LOCAL_CLONE_INTERNAL_TOKEN` (at least 32 characters), the exact
reference/transcript hashes for every allowlisted profile, and provision the private
volume outside Git. Start only the preview boundary with:

```bash
docker compose --profile local-clone up -d --build redis local-clone-gateway api web
```

This path uses the self-hosted model and creates no 3wa or VoAI request. It remains a
private evaluation feature: generated previews are returned with `no-store`, while the
formal narration catalog and Worker remain unchanged.

Compose 只把 Web 與 API 綁在 `127.0.0.1`；對外服務應由同機 reverse proxy 提供 TLS。

要先在本機驗證預定的正式子路徑：

```bash
STORYVOICE_BASE_PATH=/StoryVoice/ docker compose up -d --build
```

接著開啟 <http://localhost:3000/StoryVoice/>。預定正式網址是
<https://aiprod.wrbtycg.tw/StoryVoice/>；host nginx location 範例放在
[`deploy/nginx-storyvoice-location.conf.example`](deploy/nginx-storyvoice-location.conf.example)，
但不會因為本機 Compose 啟動而自動公開。

Stop the stack:

```bash
docker compose down
```

Add `-v` only when you intentionally want to remove local PostgreSQL、Redis
and uploaded-book data.

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
POST /api/books/import
POST /api/books/sources/books-com-tw/import
GET  /api/books
GET  /api/books/{id}
```

Import a UTF-8 TXT or DRM-free EPUB book (10 MiB maximum):

```bash
curl -X POST \
  'http://localhost:8080/api/books/import?author=StoryVoice&language=zh-TW' \
  -F 'file=@./story.txt;type=text/plain'

curl -X POST \
  'http://localhost:8080/api/books/import' \
  -F 'file=@./story.epub;type=application/epub+zip'
```

### Book collections (書冊)

Book collections group existing owner-scoped books together — independent from the
narration-focused `StorySeries` above — and can be shared read-only by email:

```text
GET    /api/collections
GET    /api/collections/{id}
POST   /api/collections
PUT    /api/collections/{id}
DELETE /api/collections/{id}
POST   /api/collections/{id}/books
PUT    /api/collections/{id}/books/{bookId}
DELETE /api/collections/{id}/books/{bookId}
POST   /api/collections/{id}/shares
DELETE /api/collections/{id}/shares/{shareId}
GET    /api/collections/shared-with-me
GET    /api/collections/shared-with-me/{id}
GET    /api/collections/shared-with-me/{id}/books/{bookId}
```

Sharing is read-only and scoped to book titles and chapter text only — reading notes,
extractive summaries, metadata corrections and narration jobs stay private to the owner.

The TXT parser recognizes headings such as `第一章 月下相逢` and
`Chapter 1: Moonlight`; files without headings become one chapter. EPUB imports
metadata, TOC labels and spine reading order, strips executable/style markup,
and stores the original upload under a generated server-side path. EPUB archive
expansion is capped at 100 MiB and 5,000 entries.

Open `http://localhost:3000/library` to import EPUB/TXT files, switch between
books, and expand the parsed chapter text in the read-only library view. Group
books into a collection at `/collections`, and check collections other users
shared with you at `/shared`.

### 博客來電子書櫃 Companion

Chrome／Chromium 可載入 [`extensions/books-com-tw-companion`](extensions/books-com-tw-companion)，
從使用者已登入的官方電子書櫃同步已呈現的書名、作者、封面與官方閱讀連結；
使用者也能明確觸發有輪次／數量上限的完整書櫃展開掃描。
Companion 不讀取帳密、Cookie、購買憑證或電子書內文，也不呼叫博客來未公開 API。
傳送目標採精確 allowlist：本機 `localhost:3000`／`127.0.0.1:3000`，以及正式上線後的
`https://aiprod.wrbtycg.tw/StoryVoice`。

同步後的書籍狀態為 `Linked`，可以在 StoryVoice 書庫辨識來源並回到博客來官方閱讀器；
若要進行故事分析與語音生成，仍須另外匯入使用者有權處理的無 DRM EPUB／TXT。
安裝與測試步驟見 [Companion README](extensions/books-com-tw-companion/README.md)。

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

1. **Book Import** — EPUB / TXT upload、博客來書櫃 metadata link、TOC and chapter extraction
2. **Story Analyzer** — narrator, dialogue, speaker, emotion and confidence
3. **Character Bible** — aliases, merge, voice lock and cross-chapter consistency
4. **Voice Casting / TTS** — provider abstraction, preview, cache and segment regeneration
5. **Audio Composer / Player** — FFmpeg, chapter audio, sentence highlight and resume
6. **AI Director** — tone, speed, pause, volume, scene context
7. **Audio Drama** — ambient sound, effects and BGM

## Security and content rights

- StoryVoice **does not provide DRM circumvention**.
- 博客來 Companion 不接收帳密／Cookie，也不下載或解密博客來電子書內文。
- Process only content you own or have the right to transform.
- 建立系列配音時，合法正文會交給該系列目前設定的語音服務；服務可能是私人本機自架或外部供應商。StoryVoice 不會把博客來官方 TTS 標記當成已生成音訊。
- API keys belong in environment variables or a secret manager, never Git.
- 使用 VoAI 雲端 API 時，待合成文字會透過網路傳送至 VoAI；啟用前請確認內容授權、隱私需求與供應商條款。
- 對外提供 VoAI 產物時，應依適用法規與平台規範揭露該語音由 AI 生成或合成。
- VoAI 免費試用音訊含背景音樂或浮水印，只供串接測試、不可作為商用成品；商用前須購買適用方案並確認聲線授權。
- BlueMagpie 程式碼與模型權重不是同一授權；模型權重目前標示為 `other`。本專案的 BlueMagpie 路徑僅供私人內網固定句試音或短篇 staged canary，公開、重新散布或商業使用前須先取得明確授權。
- Uploaded books, generated audio, analysis results and runtime volumes are ignored by Git.
- Generated audio is not automatically licensed for redistribution.

See [`SECURITY.md`](SECURITY.md) for responsible disclosure.

## Contributing

Issues and pull requests are welcome. Read [`CONTRIBUTING.md`](CONTRIBUTING.md) before starting a larger change.

## License

StoryVoice source code is released under the [MIT License](LICENSE). Third-party models, voices and generated content may have separate licenses and terms.

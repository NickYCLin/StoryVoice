# StoryVoice 開發進度

最後更新：2026-08-11

本文件記錄已由程式碼與測試證實的能力，以及接下來可直接實作的項目。
產品方向與長期資料模型仍以
[`DEVELOPMENT_PLAN.md`](../DEVELOPMENT_PLAN.md) 和
[`plans/2026-08-11-multi-character-series-cast.md`](plans/2026-08-11-multi-character-series-cast.md)
為準。

## 目前可用

- .NET 10 Clean Architecture、PostgreSQL／EF Core、React／TypeScript、Worker 與 Docker Compose 基礎架構。
- 帳號、Cookie session、CSRF、owner-scoped 書庫與私有資料邊界。
- 無 DRM EPUB／TXT 匯入、章節解析、原始檔安全儲存與博客來書櫃 metadata Companion。
- 使用者明確連結合法正文、擷取式摘要、閱讀筆記與書目人工校正。
- 單一聲線朗讀工作：持久化、租約、重試、取消、進度、私有 MP3 與 Range 串流。
- 系列／冊次／角色／alias domain model 與 PostgreSQL 約束；canonical name 與 alias 共用唯一命名空間。
- 不可變 cast revision、staged rebuild batch 與全系列 active epoch 原子切換邊界。
- 以原文 offset 切分章名、旁白與引號對話；所有片段可無遺漏、無重排地重組原文。
- owner-scoped 系列配音 API：系列建立與查詢、冊次加入、角色與 alias 管理、固定聲線更新、伺服器 voice allowlist。

## 多角色系列配音進度

| 工作 | 狀態 | 已驗證範圍 |
|---|---|---|
| Task 0：單聲線 compatibility migration | 已完成 | 舊資料回填、mode 約束、Worker claim 邊界 |
| Task 1：系列與固定角色 domain model | 已完成 | owner、角色、alias、冊次與聲線不變條件 |
| Task 2：系列與 cast EF 持久化 | 已完成 | migration、複合 FK、唯一索引與 rollback guard |
| Task 3：不可變 cast revision 與 rebuild batch | 已完成 | fingerprint、staged visibility、原子 epoch activation |
| Task 4：owner-scoped 系列配音 API | 已完成 | auth、CSRF、owner isolation、voice allowlist、不回正文 |
| Task 5：deterministic speech segmentation | 已完成 | offset、source hash、巢狀／未閉合引號與完整重組 |
| Task 6：受限說話者辨識 | 下一項 | reporting clause、known identity、unknown／review fallback |
| Task 7：speech plan 保存與審核 | 未開始 | draft、confirmed revision、stale 與 immutable job binding |
| Task 8：多聲線 Edge TTS | 未開始 | provider dispatcher、分段合成、ffmpeg／ffprobe、原子發布 |
| Task 9：staged multi-character jobs | 未開始 | active cast、confirmed plan、HistoricalFallback 與 admission gate |
| Task 10：系列 cast 與 speech-plan UI | 未開始 | 角色聲線、alias、低信心審核與行動版 QA |
| Task 11：私有書庫 backfill | 營運工作，未開始 | 必須在 Git 外執行，不可留下私人內容或識別資訊 |
| Task 12：兩階段正式發布 | 未開始 | 備份、candidate、canary、drift check、監控與 rollback proof |

## 下一個實作入口

下一步是 Task 6「受限說話者辨識」。建議依計畫先寫測試，再建立：

- `ISpeakerAttributionProvider`：輸入只接受目前系列已知角色 ID 與有限上下文。
- `RuleBasedSpeakerAttributionProvider`：只對明確 reporting clause 與 exact alias 給高信心結果。
- `LocalSpeakerAttributionProvider`：timeout、無效 JSON、未知角色 ID 一律安全回退成待審核。
- 安全紀錄：只記 segment ID、reason code 與 confidence，不記私人正文。

Task 6 完成後才能可靠建立 Task 7 的人工審核流程；在 Task 7～9 完成前，
README 不應宣稱多角色音訊已可正式使用。

## 公開 repository 邊界

提交前必須確認差異中沒有 API key、token、Cookie、真實書籍正文、使用者識別碼、
私人角色對照、聲音樣本、生成音訊、資料庫 dump、production runtime 檔案或私有部署資訊。
測試資料必須為合成內容；更多規則見 [`SECURITY.md`](../SECURITY.md)。

## 驗證

```bash
dotnet build StoryVoice.sln --configuration Release
dotnet test StoryVoice.sln --configuration Release
python -m unittest discover -s tests/python -v

cd src/StoryVoice.Web
npm ci
npm test
npm run lint
npm run build

cd ../../extensions/books-com-tw-companion
npm ci
npm run check
```

PostgreSQL constraint／migration 測試使用 Testcontainers，因此本機必須先啟動 Docker。

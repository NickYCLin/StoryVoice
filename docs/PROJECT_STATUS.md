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
- 書冊（`BookCollection`）：與角色配音系列(`StorySeries`)各自獨立的單純書本分類收藏，可調整成員書籍排序與冊次標籤。
- 書冊唯讀分享：owner 可依 email 把書冊分享給其他已註冊帳號，被分享者只能唯讀瀏覽書名與章節正文，看不到閱讀筆記、摘要或朗讀音訊；owner 可隨時撤銷。
- 前端已改為 React Router 多頁面架構（`/library`、`/collections`、`/shared` 等），不再是單一長頁面；`NarrationPanel` 已統一為深色主題。
- 受限說話者辨識：規則引擎只在明確 reporting clause 才自動確認，其餘一律待審核；安全層只接受已知角色 ID、逾時／例外／未知 ID 一律安全退回。
- 逐章劇本審核 API：草稿建立／重建、逐片段確認或拒絕、確認為不可變 `ConfirmedSpeechPlanRevision`（含 canonical fingerprint），私人正文不進回應。
- 多聲線 Edge TTS provider：以 JSON manifest 透過 stdin 傳入每個 turn 的文字／聲線／停頓，ffmpeg concat + ffprobe 驗證後才原子發布；provider registry／dispatcher 讓未來新增供應商不用動到既有系列角色 ID。
- Worker 已能實際 claim 並處理 `MultiCharacter` 朗讀工作：從鎖定的 speech plan 與 cast revision 組出 turn 序列（相鄰同聲線合併、章界／換人有界停頓），送出合成前重算 fingerprint 與逐片段文字雜湊，任一不符永久失敗為 `speech_plan_integrity_mismatch`。

## 多角色系列配音進度

| 工作 | 狀態 | 已驗證範圍 |
|---|---|---|
| Task 0：單聲線 compatibility migration | 已完成 | 舊資料回填、mode 約束、Worker claim 邊界 |
| Task 1：系列與固定角色 domain model | 已完成 | owner、角色、alias、冊次與聲線不變條件 |
| Task 2：系列與 cast EF 持久化 | 已完成 | migration、複合 FK、唯一索引與 rollback guard |
| Task 3：不可變 cast revision 與 rebuild batch | 已完成 | fingerprint、staged visibility、原子 epoch activation |
| Task 4：owner-scoped 系列配音 API | 已完成 | auth、CSRF、owner isolation、voice allowlist、不回正文 |
| Task 5：deterministic speech segmentation | 已完成 | offset、source hash、巢狀／未閉合引號與完整重組 |
| Task 6：受限說話者辨識 | 已完成 | reporting clause、known identity、unknown／review fallback |
| Task 7：speech plan 保存與審核 | 已完成 | draft、confirmed revision、stale 與 immutable job binding |
| Task 8：多聲線 Edge TTS | 已完成 | provider dispatcher、分段合成、ffmpeg／ffprobe（含真實二進位檔驗證）、原子發布 |
| Task 9：staged multi-character jobs（部分完成，見下） | Worker 合成路徑已完成；建立／審核 staged job 的 API 尚未完成 | active cast 載入、confirmed plan 載入、turn 合併與停頓、完整性重驗證 |
| Task 10：系列 cast 與 speech-plan UI | 未開始 | 角色聲線、alias、低信心審核與行動版 QA |
| Task 11：私有書庫 backfill | 營運工作，未開始 | 必須在 Git 外執行，不可留下私人內容或識別資訊 |
| Task 12：兩階段正式發布 | 未開始 | 備份、candidate、canary、drift check、監控與 rollback proof |

## Task 9 範圍說明（重要）

Worker 這一半（`StoryPipelineWorker` 認得 `MultiCharacter` job、`MultiCharacterTurnBuilder`
組 turn、`NarrationProviderDispatcher` 送去多聲線 provider）已經完成並有測試覆蓋。

**還沒做、也還不能做的部分**：一般使用者還無法透過 API 建立一個 `MultiCharacter`
staged job。原因是計畫裡描述的「先建立涵蓋全系列書籍的 `SeriesCastRebuildBatch`、
逐冊完成後才原子切換 active cast epoch」這段編排，目前完全沒有 Application
層服務——`PostgreSqlCastEpochActivationPublisher`（Task 3）與
`SeriesCastRebuildBatch`／`SeriesCastRebuildMember`（Task 3 domain）都已經存在且
測試過，但沒有任何東西呼叫它們。要讓「使用者按下『建立多角色朗讀』」變成真的
產生 staged job，還需要：

1. 建立系列 cast revision 的 Application 服務（目前系列 API 只能建立系列本身與角色，
   不能建立 `NarrationCastRevision`）。
2. 建立／推進 `SeriesCastRebuildBatch` 的服務：snapshot 全系列冊次、逐冊在
   對應書籍全部章節 `ConfirmedSpeechPlanRevision` 就緒後建立 staged job 並寫入
   `NarrationJobSpeechPlan`。
3. 全批就緒後呼叫既有的 `PostgreSqlCastEpochActivationPublisher` 原子切換。
4. `NarrationAdmissionOptions`（rollout 用的一次性關閉開關）要跟著這條建立路徑
   一起加，單獨加沒有意義，因此這次沒有建立空殼。

在這段完成前，README 不應宣稱多角色音訊已可透過 UI／API 端對端使用。

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

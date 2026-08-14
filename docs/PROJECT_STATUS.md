# StoryVoice 開發進度

最後更新：2026-08-14（區分對話、內心／文件默讀與音效／排版引文）

本文件記錄已由程式碼與測試證實的能力，以及接下來可直接實作的項目。
產品方向與長期資料模型仍以
[`DEVELOPMENT_PLAN.md`](../DEVELOPMENT_PLAN.md) 和
[`plans/2026-08-11-multi-character-series-cast.md`](plans/2026-08-11-multi-character-series-cast.md)
為準。

## 目前可用

- .NET 10 Clean Architecture、PostgreSQL／EF Core、React／TypeScript、Worker 與 Docker Compose 基礎架構。
- 帳號、Cookie session、CSRF、owner-scoped 書庫與私有資料邊界。
- 無 DRM EPUB／TXT 匯入、章節解析、原始檔安全儲存與博客來書櫃 metadata Companion。
- 使用者明確連結合法正文、閱讀筆記與書目人工校正；擷取式摘要已從 UI、API 與狀態矩陣退場，既有資料表與資料暫留供回復。
- 本機 LLM 角色／alias 分析：`gpt-oss:20b` 逐章讀取完整合法正文，只保存名稱、alias、信心與證據次數，不保存分析用正文。前端可勾選候選、編輯 canonical 名稱、合併 alias、指定角色層級與聲線，再以單一 owner-scoped API 原子加入冊次及建立／重用系列角色；重送不會重複建立。
- 單一聲線朗讀工作：持久化、租約、重試、取消、進度、私有 MP3 與 Range 串流。
- 系列／冊次／角色／alias domain model 與 PostgreSQL 約束；canonical name 與 alias 共用唯一命名空間。
- 不可變 cast revision、staged rebuild batch 與全系列 active epoch 原子切換邊界。
- 以原文 offset 切分章名、旁白、對話與視角角色內心／文件默讀；高信心文件標題、標示與默讀語境不進說話者審核，改用視角角色的中性／基礎聲線，音效／詞中排版引文維持旁白，電話、發話、朗讀給他人聽與咒語仍是對話。所有片段可無遺漏、無重排地重組原文。
- owner-scoped 系列配音 API：系列建立與查詢、冊次加入、角色與 alias 管理、固定聲線更新、伺服器 voice allowlist。
- 書冊（`BookCollection`）：與角色配音系列(`StorySeries`)各自獨立的單純書本分類收藏，可調整成員書籍排序與冊次標籤；書庫的瀏覽器「此裝置標籤」已移除，分類統一使用書冊。
- 書冊唯讀分享：owner 可依 email 把書冊分享給其他已註冊帳號，被分享者只能唯讀瀏覽書名與章節正文，看不到閱讀筆記、摘要或朗讀音訊；owner 可隨時撤銷。
- 前端已改為 React Router 多頁面架構（`/library`、`/collections`、`/shared` 等），不再是單一長頁面；`NarrationPanel` 已統一為深色主題。
- 受限說話者辨識：明確 reporting clause 等強規則維持最高優先，不會被模型覆蓋；其餘對話再整章交給本機 `gpt-oss:20b` 補判。模型 schema 只能輸出目前系列已知角色 ID，≥85 信心才自動確認，中／低信心留在人工審核；逾時、例外、漏答、未知 ID 或卸載失敗都安全退回規則結果／Unknown。系列可另指定第一人稱視角角色。
- 逐章劇本審核 API：草稿建立／重建、逐片段確認或拒絕、確認為不可變 `ConfirmedSpeechPlanRevision`（含 canonical fingerprint），私人正文不進回應。
- 多聲線 Edge TTS provider：以 JSON manifest 透過 stdin 傳入每個 turn 的文字／聲線／停頓，ffmpeg concat + ffprobe 驗證後才原子發布；provider registry／dispatcher 讓未來新增供應商不用動到既有系列角色 ID。
- Worker 已能實際 claim 並處理 `MultiCharacter` 朗讀工作：從鎖定的 speech plan 與 cast revision 組出 turn 序列（相鄰同聲線合併、章界／換人有界停頓），送出合成前重算 fingerprint 與逐片段文字雜湊，任一不符永久失敗為 `speech_plan_integrity_mismatch`。
- 建立／推進全系列 `SeriesCastRebuildBatch` 的 Application 服務已完成並串接前端：owner 可在 `/series` 頁面對已確認劇本的系列建立 staged 多角色朗讀批次，逐冊完成後原子切換 active cast epoch；重試會自動清除同系列失敗的舊批次與孤兒 draft cast revision，不會撞唯一鍵。
- 對白依情緒（緊張／開心／生氣／難過）微調 Edge TTS 的 rate/pitch/volume，規則式判斷只讀取合成當下已合法取得的正文與 reporting clause，不做情感分析宣稱。
- 角色庫（Character Library，見下方獨立章節）：owner-scoped、跨系列共用的角色管理頁面（`/characters`），角色的基本資料（頭像、年齡、性別、生日、個性、口頭禪、人物背景、說話風格）與自訂聲線（Character Voice Studio）都掛在角色庫上，任何系列的多角色配音都能直接選用同一個角色，不用每個系列各自重建。

## 多角色系列配音進度

| 工作 | 狀態 | 已驗證範圍 |
|---|---|---|
| Task 0：單聲線 compatibility migration | 已完成 | 舊資料回填、mode 約束、Worker claim 邊界 |
| Task 1：系列與固定角色 domain model | 已完成 | owner、角色、alias、冊次與聲線不變條件 |
| Task 2：系列與 cast EF 持久化 | 已完成 | migration、複合 FK、唯一索引與 rollback guard |
| Task 3：不可變 cast revision 與 rebuild batch | 已完成 | fingerprint、staged visibility、原子 epoch activation |
| Task 4：owner-scoped 系列配音 API | 已完成 | auth、CSRF、owner isolation、voice allowlist、不回正文 |
| Task 5：deterministic speech segmentation | 已完成 | offset、source hash、巢狀／未閉合引號、對話／內心默讀語意分類與完整重組 |
| Task 6：受限說話者辨識 | 已完成 | 規則優先、本機 LLM 補判、known identity schema、高信心自動確認、unknown／review fallback |
| Task 7：speech plan 保存與審核 | 已完成 | draft、confirmed revision、stale 與 immutable job binding |
| Task 8：多聲線 Edge TTS | 已完成 | provider dispatcher、分段合成、ffmpeg／ffprobe（含真實二進位檔驗證）、原子發布 |
| Task 9：staged multi-character jobs | 已完成 | active cast 載入、confirmed plan 載入、turn 合併與停頓、完整性重驗證、staged batch 建立與原子 epoch 切換 API |
| Task 10：系列 cast 與 speech-plan UI | 已完成 | LLM 候選勾選／alias 合併／套用、系列管理、低信心劇本審核、staged rebuild 狀態與啟用、角色自訂聲線工作室 |
| Task 11：私有書庫 backfill | 營運工作，未開始 | 必須在 Git 外執行，不可留下私人內容或識別資訊 |
| Task 12：兩階段正式發布 | 未開始 | 備份、candidate、canary、drift check、監控與 rollback proof |

## 角色庫（Character Library）與角色自訂聲線工作室（Character Voice Studio）

`/characters` 是獨立於任何系列的 owner-scoped 角色管理頁面：可以建立角色的基本
資料（頭像、年齡、性別、生日、個性、口頭禪、人物背景、說話風格——AI 補完／AI
全部重寫按鈕先保留位置，尚未接 LLM），也可以直接在同一頁替角色建立一組基礎
聲線，以及緊張／開心／生氣／難過（加上「平常」）最多五組情境聲線；每一組都
可以選「文字設計」（只給一段文字描述，立即可用）或「上傳錄音克隆」（需要選擇
同意類型：本人親自錄製／已取得明確同意／已取得合法授權，上傳後走語音辨識草稿
→人工確認文字稿的流程才會就緒）。角色建好之後，在「多角色系列配音」加入角色
時可以直接從角色庫選入，同一個角色（與其聲線）能跨多個系列重複使用，不用每個
系列各自重建一次；系列裡的角色也可以不連結角色庫、維持原本手動設定固定 Edge
聲線的舊流程。合成時依對白情緒查找對應情境聲線，找不到就退回基礎聲線，兩者都
沒有才安全退回旁白聲線，不會讓整個朗讀工作失敗。

資料模型上，`CharacterVoiceProfile` 掛在 owner-scoped 的 `CharacterProfile`（角色庫
條目）底下，不再綁死在單一系列的 `SeriesCharacter`；`SeriesCharacter` 有一個可選的
`CharacterProfileId` 連結欄位，FK 設 `RESTRICT`——系列還在使用中的角色庫角色不能
直接刪除，要先從系列移除。

角色管理頁面另外還有：
- **啟用／停用狀態**：`CharacterProfile.IsActive`，停用只是介面上標記淡出，不影響
  已經連結的系列配音（停用不會把角色從系列裡移除，也不會讓現有朗讀失敗）。
- **角色 ID 顯示與複製**、建立時間／最後更新時間。
- **摘要卡片**：基礎聲線狀態、情境聲線數量（已就緒／5）、樣本語料時數（所有克隆
  聲線參考音檔的 ffprobe 時長加總，API 容器已補上 ffmpeg）、最近進行中的任務。
- **試講**：針對任一已就緒（`Ready`）的聲線，輸入一小段文字（上限 200 字）即時合成
  播放；重用既有的 3wa 合成 client 同步跑一次 submit/poll/result/artifact，不進
  Worker 的 job 佇列、不落地存檔，純粹是 UI 預覽用途。
- **任務紀錄**：以這個角色所有 `CharacterVoiceProfile`（含歷史 Pending／Failed 紀錄）
  依最後更新時間列出的簡易表格；沒有另外做一套持久化的非同步任務追蹤系統，也
  沒有 3wa 端回傳的即時進度百分比（3wa 文件雖提到 status 回應可能帶 `progress`
  欄位，但目前程式碼沒有解析、儲存它）。
- **重設按鈕**：把基本資料表單復原成上次儲存的值，捨棄尚未儲存的編輯。

實作串接的是 3wa Cluster API（`cluster_api.php?mode=voice_generate`）的 VoxCPM2
引擎，分成兩條獨立的非同步流程：`profile_prepare/status/confirm`（Application 層，
建立與確認聲線）、`synthesize` 的 submit/poll/result/artifact（Worker 層，實際產生
朗讀音訊）。**這一段的 HTTP 欄位名稱是從官方文件摘要整理出來的，尚未拿真實
token 對正式環境跑過一次端對端請求**——文件本身對 `synthesize` 的 `operation`
欄位、以及 status/result 回應的確切 JSON 形狀描述得不夠精確，程式碼已經盡量寫得
寬容（缺欄位不直接炸掉、`artifact_url_template`／`ack_url_template` 一律當 opaque
URL 展開），但正式啟用前務必先用一個測試角色實際跑一次 Clone 模式（短 WAV）到
`Ready`，再跑一次 Design 模式，確認回應形狀跟程式碼的假設一致。

已知限制（刻意排除的範圍，不是漏做）：
- **旁白聲線無法克隆**：角色庫只服務 `SeriesCharacter`，系列旁白沒有對應的克隆／
  設計流程。系列旁白 provider 設成 `3wa-voxcpm2` 時，角色可以自由混用 Edge 固定
  聲線與 3wa 自訂聲線，但旁白本身永遠只能是 Edge 聲線。
- 角色基本資料的「AI 補完」／「AI 全部重寫」按鈕只是預留位置，沒有接 LLM（3wa
  Cluster API 目前沒有對應的 chat/生成 mode）。
- 沒有串接 GPT-SoVITS 或其他第二個聲音引擎（`IMultiVoiceNarrationProvider` 的擴充點
  讓這件事之後可以純新增，不用重構既有 provider）。
- 沒有匿名／公開的角色或聲線建立入口，一律維持既有的 owner-scoped 私有資料邊界。

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

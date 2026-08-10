# Multi-character Series Cast Implementation Plan

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** 將 StoryVoice 從單一聲線朗讀升級為可校正的多角色朗讀，並保證同一系列跨冊沿用固定的旁白與角色聲線。

**Architecture:** 以 owner-scoped `StorySeries` 作為跨冊邊界，將書籍、角色、別名與聲線綁在系列下；每次產製引用不可變的 cast revision，避免聲線設定變更後污染既有音訊。正文先建立可審核的 speech plan，再按已確認 speaker 分段合成並依原順序原子串接。說話者辨識採「規則／本機模型建議＋低信心人工確認」，不只靠引號猜測，也不預設把私人正文傳給額外雲端模型。

**Tech Stack:** .NET 10、EF Core／PostgreSQL、ASP.NET Core Minimal API、React／TypeScript、Edge TTS、Python 3、ffmpeg、Docker Compose。

---

## Product decisions

1. **聲線屬於系列角色，不屬於單本書。** 新一冊遇到同一角色時，必須解析至既有 `SeriesCharacter.Id`，並沿用同一 voice profile。
2. **旁白也是系列固定聲線。** `StorySeries` 有獨立 narrator profile；不與任何角色共用預設聲線。
3. **聲線不會自動重抽。** 自動建議只在角色第一次建立時執行一次；寫入後保持固定。
4. **別名不等於新角色。** 本名、稱號、暱稱與代稱透過 `CharacterAlias` 指回同一角色。
5. **換聲必須可追溯。** 每次 cast 變更建立新的 immutable `NarrationCastRevision`；舊工作保留舊 revision，只有明確重製才採新版。
6. **辨識結果可校正。** 明確敘述句可自動確認；模糊對白只產生 suggestion。未確認片段使用旁白 fallback 或停在 review gate，不冒充準確角色。
7. **不額外外送私人正文。** 規則引擎與本機 attribution provider 為預設；外部 LLM provider 必須另行明確同意，且不得記錄正文。
8. **現有單人版不可被覆蓋。** 已完成的 `NarrationJob` 與 MP3 保留；多角色版建立新 job／新 audio path。
9. **相同系列以人工或明確 metadata 指派。** 不依書名模糊比對自動合併系列，避免新版／舊版或同名作品串錯。
10. **跨系列 crossover 暫不自動合併。** MVP 只保證同一系列；未來可加入 owner-scoped shared character identity，但不能用模糊姓名直接合併。

## End-to-end flow

1. 使用者建立系列，設定系列名稱、旁白聲線與預設停頓。
2. 將每本 content book 加入系列並設定卷次／排序；同一 content book 最多屬於一個系列。
3. 系統從首冊建立角色名冊草稿：明確 reporting clause、已知 alias 與本機 attribution provider 產生 speaker suggestions。
4. 使用者確認角色、別名與聲線；主角／配角一旦確認，後續冊次直接沿用。
5. 每章生成 speech plan：旁白及角色 turn、原文 offset、speaker、confidence、decision source。
6. 低信心或未知角色進入 review queue；主角／配角未知不得靜默另建新角色。
7. 建立 immutable cast revision 與 plan hash，再建立多角色 NarrationJob。
8. Worker 合併相鄰同 speaker turn、依聲線分段合成、加入有界停頓、串接並驗證 MP3。
9. 所有片段成功後才原子發布；進度只在成功片段後持久化。
10. 下一冊載入同一系列 cast／aliases；新角色才進入配音設定，既有角色不重新選聲。

## Data model

### `StorySeries`

- `Id`, `OwnerId`, `Name`, `NormalizedName`
- `NarratorProvider`, `NarratorVoice`, `NarratorRate`, `NarratorPitch`, `NarratorVolume`
- `DefaultSpeakerPauseMs`, `CurrentCastRevisionId`
- `CreatedAt`, `UpdatedAt`, `ConcurrencyStamp`
- Unique: `(OwnerId, NormalizedName)`

### `SeriesBook`

- `Id`, `OwnerId`, `SeriesId`, `BookId`
- `VolumeLabel`, `SortOrder`
- Unique: `(OwnerId, BookId)` and `(SeriesId, SortOrder)`
- `BookId` 指向真正含正文的 content book，不指向純外部書目 target。

### `SeriesCharacter`

- `Id`, `OwnerId`, `SeriesId`, `CanonicalName`, `NormalizedName`
- `Role`: `Main | Supporting | Minor`
- `VoiceProvider`, `Voice`, `Rate`, `Pitch`, `Volume`
- `Notes` 僅存角色配音提示，不保存正文。
- `CreatedAt`, `UpdatedAt`, `ConcurrencyStamp`
- Unique: `(SeriesId, NormalizedName)`

### `CharacterAlias`

- `Id`, `OwnerId`, `SeriesId`, `CharacterId`, `Alias`, `NormalizedAlias`
- Unique: `(SeriesId, NormalizedAlias)`，避免同系列一個稱呼同時指向兩人。

### `NarrationCastRevision`

- `Id`, `OwnerId`, `SeriesId`, `RevisionNumber`, `Fingerprint`
- `NarratorProvider`、`NarratorVoice` 及參數 snapshot
- `CreatedAt`
- 建立後不可修改。

### `NarrationCastAssignment`

- `CastRevisionId`, `CharacterId`, `CanonicalNameSnapshot`
- `VoiceProvider`, `Voice`, `Rate`, `Pitch`, `Volume`
- Unique: `(CastRevisionId, CharacterId)`

### `ChapterSpeechPlan`

- `Id`, `OwnerId`, `SeriesId`, `BookId`, `ChapterId`
- `SourceHash`, `PlanVersion`, `Status`: `Draft | NeedsReview | Confirmed | Stale`
- `CreatedAt`, `UpdatedAt`
- Chapter 正文 hash 改變時，舊 plan 必須標記 `Stale`。

### `SpeechSegment`

- `Id`, `SpeechPlanId`, `SortOrder`
- `StartOffset`, `Length`, `TextHash`；不重複保存私人正文。
- `Kind`: `Narration | Dialogue`
- `CharacterId` nullable；旁白為 null。
- `Confidence` 0–100
- `DecisionSource`: `Rule | LocalModel | User`
- `ReviewStatus`: `Suggested | Confirmed | Rejected`
- `Context`、正文、角色句子均不得寫入 application log。

### `NarrationJob` changes

- 新增 `Mode`: `SingleVoice | MultiCharacter`
- 新增 nullable `SeriesId`, `CastRevisionId`, `SpeechPlanFingerprint`
- 單人版保留現有 `Voice`／`Rate` 相容欄位。
- 多角色 job 唯一鍵使用 `(OwnerId, BookId, ContentBookId, SourceHash, Mode, CastRevisionId, SpeechPlanFingerprint)`。

## Speaker attribution rules

1. 先以段落與中文引號 `「」『』“”` 切出 narrator／dialogue candidate；不得把引號內文字直接視為完整角色判定。
2. 明確 reporting clause，例如「某某說／問／回答」且某某精確命中 canonical name 或 alias，才可由 rule 自動確認。
3. 相鄰 turn 的 speaker 延續只能成為 suggestion，不可跨場景或章節無界延伸。
4. 本機模型只能從該系列既有角色 ID 或 `unknown` 中選擇；不能自由編造角色名稱。
5. 新角色候選必須附原文位置、confidence 與 reason code，經使用者確認後才加入 cast。
6. 低於門檻的 dialogue 預設進入 review；使用者可明確選擇 narrator fallback。
7. 新冊分析必須先載入既有 cast／aliases，再找新角色；禁止按 volume 重新建立同名角色。

## Voice assignment rules

1. 旁白、主角與主要配角必須使用明確保存的 voice profile。
2. 聲線清單來自 server allowlist，不接受任意 client voice 名稱。
3. 角色第一次建立時可依使用者選擇或角色屬性提供建議，但一旦保存便不再自動改動。
4. 預設阻止旁白與主角使用完全相同 voice fingerprint；允許使用者明確覆寫。
5. voice fingerprint 包含 provider、voice、rate、pitch、volume 與 provider version。
6. 修改任一 voice 參數會建立新 cast revision，不就地改寫既有 revision。
7. 未知角色不可臨時亂抽聲音；只能 fallback narrator 或停在 review gate。

## Provider capacity and voice diversity

- 2026-08-11 以正式 Worker 的 `edge-tts --list-voices` 驗證，`zh-TW` 只有三個基礎聲線：`HsiaoChen`（女）、`HsiaoYu`（女）、`YunJhe`（男）。
- MVP 可將 base voice 與有界 `rate`／`pitch`／`volume` 組成固定 voice fingerprint，但不得把參數變化宣稱成完全不同的真人聲線。
- 主角、主要配角與旁白優先使用可辨識的獨立 fingerprint；次要角色可重用基礎聲線，但同場景相鄰角色不得使用完全相同 fingerprint。
- `zh-CN` 不得因聲線較多就自動混入臺灣中文作品；口音切換必須由使用者明確選擇。
- Domain 與 cast revision 必須保存 `VoiceProvider`，Worker 透過 provider registry dispatch；如此可在不破壞系列角色 ID 的情況下，日後加入 Azure Speech、合法本機 TTS 或其他已授權聲線來源。
- 更換 provider 或 base voice 一律建立新 cast revision；不得讓供應商清單更新後自動改掉既有角色。

## Audio composition contract

- C# Worker 將已確認 plan 編譯成有序 `NarrationTurn[]`。
- 相鄰且 voice fingerprint 相同的 turn 可合併，但不得跨章、跨 speaker 或超過 5,000 字硬上限。
- C# 以 stdin 傳入 versioned JSON manifest；私人正文不得出現在 command line、process list 或 log。
- Python 對每個 turn 使用其固定 voice profile；每塊最多三次 provider 嘗試。
- speaker 切換加入 120–350ms 有界停頓，章界使用較長停頓；值屬於 series profile。
- 使用 ffmpeg 將合法 MP3 片段／silence 依序串接並做 codec probe；不靠未驗證 byte concatenation 當唯一保證。
- 只有所有 turns 成功並通過 ffprobe 後才原子發布 final MP3。
- 取消、timeout 或失敗必須 kill process tree 並清除所有私人 partial clips／manifest。
- 真實進度按「成功完成的 turn/chunk 字元權重」單調回報；100% 只在 final publish 後寫入。

## UI contract

### Series page

- 建立／編輯系列；加入書籍、卷次排序。
- 固定顯示旁白聲線與角色配音表。
- 每個角色可維護 canonical name、aliases、角色層級與 voice preview。
- voice preview 使用固定的非私人示例句，不把正文拿來試音。

### Speech-plan review

- 逐章顯示 narrator／speaker 標籤、confidence 與前後有限 context。
- 可批次把同 alias 指派給既有角色。
- 可確認新角色，但禁止用同 alias 建立第二個角色。
- 顯示「已確認／待確認／旁白 fallback」統計。
- 主角／配角低信心片段仍存在時，建立多角色朗讀按鈕預設停用。

### Narration panel

- 顯示 `單人朗讀` 或 `多角色演出`。
- 顯示 cast revision、旁白與角色數，不顯示私人正文。
- 舊單人版 MP3 與新多角色版並列，不覆蓋。

---

## Implementation tasks

### Task 1: Add series and fixed cast domain models

**Objective:** 建立 owner-scoped series、book membership、character 與 alias 不變條件。

**Files:**
- Create: `src/StoryVoice.Domain/Series/StorySeries.cs`
- Create: `src/StoryVoice.Domain/Series/SeriesBook.cs`
- Create: `src/StoryVoice.Domain/Series/SeriesCharacter.cs`
- Create: `src/StoryVoice.Domain/Series/CharacterAlias.cs`
- Test: `tests/StoryVoice.UnitTests/StorySeriesTests.cs`

**Steps:**
1. 先寫紅燈測試：同系列 normalized alias 不可重複、跨 owner／空 ID 被拒、既有角色 voice 不會被新增書籍改寫。
2. 執行 `dotnet test tests/StoryVoice.UnitTests/StoryVoice.UnitTests.csproj --filter StorySeriesTests`，確認失敗。
3. 實作最小 domain models、normalization 與 concurrency stamp。
4. 重跑定向測試，預期全綠。
5. Commit：`feat(series): 建立系列與固定角色聲線模型`。

### Task 2: Persist series and cast with EF Core

**Objective:** 建立資料表、唯一索引、owner boundary 與 migration。

**Files:**
- Modify: `src/StoryVoice.Infrastructure/Persistence/StoryVoiceDbContext.cs`
- Create: `src/StoryVoice.Infrastructure/Persistence/StorySeriesConfiguration.cs`
- Create: `src/StoryVoice.Infrastructure/Persistence/SeriesBookConfiguration.cs`
- Create: `src/StoryVoice.Infrastructure/Persistence/SeriesCharacterConfiguration.cs`
- Create: `src/StoryVoice.Infrastructure/Persistence/CharacterAliasConfiguration.cs`
- Create: `src/StoryVoice.Infrastructure/Persistence/Migrations/*_AddSeriesCast.cs`
- Test: `tests/StoryVoice.IntegrationTests/SeriesApiTests.cs`

**Steps:**
1. 先測 owner A 無法讀／改 owner B 的 series，以及 duplicate alias／book membership 回 409 或穩定錯誤碼。
2. 執行定向 integration test，確認紅燈。
3. 加 DbSet、configuration、foreign keys、check constraints 與 migration。
4. 執行 `dotnet ef migrations script --idempotent` 與 pending-model check。
5. 重跑 integration test。
6. Commit：`feat(series): 持久化系列角色配音表`。

### Task 3: Add immutable cast revisions

**Objective:** 讓每個 narration job 引用不可變的跨冊聲線 snapshot。

**Files:**
- Create: `src/StoryVoice.Domain/Narrations/NarrationCastRevision.cs`
- Create: `src/StoryVoice.Domain/Narrations/NarrationCastAssignment.cs`
- Create: `src/StoryVoice.Infrastructure/Persistence/NarrationCastRevisionConfiguration.cs`
- Create: `src/StoryVoice.Infrastructure/Persistence/NarrationCastAssignmentConfiguration.cs`
- Test: `tests/StoryVoice.UnitTests/NarrationCastRevisionTests.cs`

**Steps:**
1. 先測相同 cast 產生相同 fingerprint、聲線修改產生新 revision、舊 revision 不可修改。
2. 實作 canonical fingerprint（固定排序、UTF-8、SHA-256）。
3. 加 EF configuration 與 migration。
4. 重跑 unit／migration tests。
5. Commit：`feat(narration): 建立不可變配音版本`。

### Task 4: Add owner-scoped series and cast APIs

**Objective:** 提供 series、books、cast、aliases 與 voice allowlist API。

**Files:**
- Create: `src/StoryVoice.Application/Series/SeriesContracts.cs`
- Create: `src/StoryVoice.Application/Series/ISeriesService.cs`
- Create: `src/StoryVoice.Infrastructure/Persistence/SeriesService.cs`
- Create: `src/StoryVoice.Api/SeriesEndpoints.cs`
- Modify: `src/StoryVoice.Api/Program.cs`
- Test: `tests/StoryVoice.IntegrationTests/SeriesApiTests.cs`

**Steps:**
1. 先寫 create／list／membership／cast update／alias conflict／non-owner 負向測試。
2. 所有 mutation 加 antiforgery，所有 query 以 current owner filter。
3. voice 只能從 server allowlist 選擇；response 不回正文。
4. 跑定向及完整 integration tests。
5. Commit：`feat(series): 新增系列配音管理 API`。

### Task 5: Build deterministic speech segmentation

**Objective:** 以 offset 建立 narrator／dialogue segments，不複製保存正文。

**Files:**
- Create: `src/StoryVoice.Application/Narrations/SpeechPlanning/ChineseSpeechSegmenter.cs`
- Create: `src/StoryVoice.Application/Narrations/SpeechPlanning/SpeechPlanContracts.cs`
- Test: `tests/StoryVoice.UnitTests/ChineseSpeechSegmenterTests.cs`

**Steps:**
1. 建立包含旁白、`「」`、`『』`、跨行引號、巢狀標點與未閉合引號的紅燈 corpus；不得使用私人小說內容。
2. 實作穩定 offset／length 與 source hash。
3. 未閉合或不確定結構標成 NeedsReview，不吞字、不重排文字。
4. 驗證所有 segment 重組後與輸入完全一致。
5. Commit：`feat(narration): 切分旁白與對話片段`。

### Task 6: Attribute speakers with constrained identities

**Objective:** 用明確規則與可替換本機 provider 產生 speaker suggestions。

**Files:**
- Create: `src/StoryVoice.Application/Narrations/SpeechPlanning/ISpeakerAttributionProvider.cs`
- Create: `src/StoryVoice.Infrastructure/Narrations/RuleBasedSpeakerAttributionProvider.cs`
- Create: `src/StoryVoice.Infrastructure/Narrations/LocalSpeakerAttributionProvider.cs`
- Test: `tests/StoryVoice.UnitTests/SpeakerAttributionTests.cs`

**Steps:**
1. 先測 reporting clause exact alias 可確認；模糊代詞、多人場景與未知名字只能 suggested／unknown。
2. Provider input 只接受 current series cast IDs 與有限 context；output schema 只允許已知 ID 或 `unknown`。
3. local provider timeout、invalid JSON、額外角色 ID 全部安全回退 review。
4. 確認 logs 只含 segment ID、reason code、confidence，不含 text。
5. Commit：`feat(narration): 建立受限說話者辨識`。

### Task 7: Persist and review speech plans

**Objective:** 保存 plan offsets、speaker decision 與 stale boundary。

**Files:**
- Create: `src/StoryVoice.Domain/Narrations/ChapterSpeechPlan.cs`
- Create: `src/StoryVoice.Domain/Narrations/SpeechSegment.cs`
- Create: `src/StoryVoice.Infrastructure/Persistence/ChapterSpeechPlanConfiguration.cs`
- Create: `src/StoryVoice.Infrastructure/Persistence/SpeechSegmentConfiguration.cs`
- Create: `src/StoryVoice.Api/SpeechPlanEndpoints.cs`
- Test: `tests/StoryVoice.IntegrationTests/SpeechPlanApiTests.cs`

**Steps:**
1. 先測 owner isolation、offset validation、alias batch assignment、chapter hash 改變後 stale。
2. 實作 draft／review／confirm transition；Confirmed plan 才能用於 multi-character job。
3. API context response 設 private/no-store，不輸出整章。
4. 跑完整 unit／integration tests。
5. Commit：`feat(narration): 新增角色分段審核流程`。

### Task 8: Implement multi-voice Edge TTS provider

**Objective:** 按固定 speaker voice profile 合成、串接並原子發布完整 MP3。

**Files:**
- Modify: `src/StoryVoice.Worker/INarrationProvider.cs`
- Create: `src/StoryVoice.Worker/MultiVoiceNarrationRequest.cs`
- Create: `src/StoryVoice.Worker/EdgeTtsMultiVoiceNarrationProvider.cs`
- Create: `src/StoryVoice.Worker/edge_tts_multi_voice_provider.py`
- Modify: `docker/worker.Dockerfile`
- Test: `tests/StoryVoice.UnitTests/EdgeTtsMultiVoiceNarrationProviderTests.cs`
- Test: `tests/python/test_edge_tts_multi_voice_provider.py`

**Steps:**
1. 先測 turn order、不同 voice 傳遞、同 speaker bounded merge、重試、取消、timeout、partial cleanup 與 progress monotonicity。
2. 以 stdin 傳 versioned JSON，不將正文放進 argv／logs／temp filename。
3. 每 turn/chunk 仍守 5,000 字上限及三次 provider 嘗試。
4. 使用 ffmpeg concat／silence，完成後執行 ffprobe；所有片段成功才 `os.replace`。
5. 真實 synthetic 三角色 probe 驗證 duration、codec、聲線切換順序及 temp cleanup。
6. Commit：`feat(narration): 合成多角色固定聲線音訊`。

### Task 9: Integrate cast revision into NarrationJob

**Objective:** 保留單人版相容性，新增可重製的 multi-character job。

**Files:**
- Modify: `src/StoryVoice.Domain/Narrations/NarrationJob.cs`
- Modify: `src/StoryVoice.Infrastructure/Persistence/NarrationJobConfiguration.cs`
- Modify: `src/StoryVoice.Infrastructure/Persistence/NarrationService.cs`
- Modify: `src/StoryVoice.Worker/StoryPipelineWorker.cs`
- Modify: `src/StoryVoice.Application/Narrations/NarrationContracts.cs`
- Test: `tests/StoryVoice.UnitTests/NarrationJobTests.cs`
- Test: `tests/StoryVoice.IntegrationTests/NarrationApiTests.cs`

**Steps:**
1. 先測 single／multi 唯一鍵分離、cast revision snapshot、未確認 plan 拒絕建立、舊單人版仍可播放。
2. Worker 只從 confirmed plan＋revision 編譯 turns；未知 speaker 按明確 fallback policy 處理。
3. progress 只在成功 turn/chunk 後更新，lease owner fence 保持不變。
4. 跑完整 .NET／Python tests。
5. Commit：`feat(narration): 串接系列配音與多角色工作`。

### Task 10: Build series cast and review UI

**Objective:** 讓 owner 能設定固定角色聲線並校正跨冊 speaker。

**Files:**
- Create: `src/StoryVoice.Web/src/SeriesCastPanel.tsx`
- Create: `src/StoryVoice.Web/src/SpeechPlanReview.tsx`
- Modify: `src/StoryVoice.Web/src/App.tsx`
- Modify: `src/StoryVoice.Web/src/NarrationPanel.tsx`
- Create: `src/StoryVoice.Web/tests/series-cast-ui.test.mjs`
- Create: `src/StoryVoice.Web/tests/speech-plan-review.test.mjs`

**Steps:**
1. 先寫 source-contract 與 browser tests：voice 固定、alias 衝突、低信心 review、切書不殘留前一本狀態。
2. 實作系列書單、旁白、角色／alias／voice preview。
3. preview 使用固定非私人句子並正確 revoke Blob URL。
4. 多角色建立按鈕在 plan 未確認時禁用，並顯示缺口數。
5. 跑 Web tests、lint、build、桌面／390px browser QA。
6. Commit：`feat(web): 管理跨冊角色配音與審核`。

### Task 11: Backfill existing private books safely

**Objective:** 將既有 content books 加入同一系列，但不猜角色、不重製現有 MP3。

**Files:**
- Create outside Git: `/tmp/storyvoice-private-series-backfill/`
- No private title, owner ID, text, cast, audio, token or mapping may enter repo／image／logs.

**Steps:**
1. 先做 production backup 與隔離 proof DB。
2. 以明確 manifest 指定 series membership／volume order；不得依 title number 猜新版與舊版映射。
3. 初次建立 memberships，第二次執行新增 0，證明冪等。
4. 只建立 series shell；角色與 aliases 需經 review，不自動猜補。
5. 驗證 non-owner 看不到 series／cast／plan。

### Task 12: Release without interrupting current narrations

**Objective:** 完成 migration、candidate、production proof 與 rollback closure。

**Steps:**
1. 等目前 15 本單人版工作進入終態；不得為多角色功能重啟正在合成的 Worker。
2. 全跑 Unit、Integration、Python、Web、lint、build、format、compose、diff 與 credential-shaped scan。
3. Build exact candidate images；用 synthetic public-domain text 做 narrator＋兩角色真實音訊 proof。
4. Release review 檢查 owner isolation、cast revision immutability、speaker misattribution fallback、private text logging、cleanup 與 stale polling。
5. Git pull／push 前 drift check，繁中 Conventional Commit，CI 綠燈。
6. 備份 PostgreSQL、book storage、audio、Data Protection keys、runtime tree 與 images，只保留最新三份。
7. 部署 migration／API／Worker／Web；舊單人 MP3 必須仍能播放。
8. 正式建立 synthetic canary series，驗 narrator／角色聲線固定、第二冊沿用同一 character IDs／voices。
9. owner 正向、匿名／non-owner 負向、audio Range、ffprobe、temp cleanup、logs、public bundle 與 marker 全部通過。
10. 清除 canary 與 candidate artifacts；更新 project memory。

## Acceptance criteria

- 同一系列中，相同 `SeriesCharacter.Id` 在所有冊次使用相同 voice fingerprint。
- 新冊分析不會為既有 alias 建立第二個角色，也不會重新抽選 voice。
- 旁白有獨立且固定的系列聲線。
- 使用者可明確建立新 cast revision；舊 MP3／job 仍保留舊 revision。
- 未知／低信心 speaker 不會被偽裝成確定角色。
- 多角色音訊維持原文順序，所有文字恰好朗讀一次，無遺漏、重複或跨章錯置。
- 任一 turn 失敗時不發布 final MP3，且不留下私人 partial files。
- 進度單調、真實、可持久化；100% 只代表可播放 final audio 已原子發布。
- series、cast、speech plan、audio 全部 owner-isolated；私人正文不進 Git、image、URL、argv 或 application logs。
- 現有單人朗讀流程與已完成音訊保持相容。

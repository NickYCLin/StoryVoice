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
5. **換聲必須以系列 epoch 原子切換。** cast 變更先建立 `Draft` revision；同一系列所有已發布多角色冊次完成新版重製後，才能在單一交易切換 active epoch。舊 revision／MP3 轉為歷史版本，不得與新版混列為目前系列聲線。
6. **辨識結果可校正。** 明確敘述句可自動確認；模糊對白只產生 suggestion。未確認片段使用旁白 fallback 或停在 review gate，不冒充準確角色。
7. **不額外外送私人正文。** 規則引擎與本機 attribution provider 為預設；外部 LLM provider 必須另行明確同意，且不得記錄正文。
8. **現有單人版不可被覆蓋。** 已完成的 `NarrationJob` 與 MP3 保留；多角色版建立新 job／新 audio path。
9. **相同系列以人工或明確 metadata 指派。** 不依書名模糊比對自動合併系列，避免新版／舊版或同名作品串錯。
10. **跨系列 crossover 暫不自動合併。** MVP 只保證同一系列；未來可加入 owner-scoped shared character identity，但不能用模糊姓名直接合併。
11. **Job 不得任選 cast revision。** 新工作只能使用系列 active revision；pending epoch 的整批重製工作由受控 batch 建立並保持不可公開，既有 job 重試則固定沿用原 revision。
12. **Uploaded 書籍預設必須為 MultiCharacter。** `SingleVoice` 只保留既有音訊的 HistoricalFallback／回滾相容性，不算 Uploaded 書籍完成；現有 15 本必須建立多角色 speech plans、固定 series cast 並完成 staged rebuild 後才切換正式音訊。

## Cast epoch and zero-drift invariant

- `StorySeries.ActiveCastRevisionId` 定義目前系列唯一有效的聲線 epoch；公開多角色音訊只能屬於此 revision。
- 角色聲線變更建立 `Draft` revision，不立即影響任何新冊或舊冊。
- 系列已有多角色音訊時，啟用 Draft 必須建立全系列 staged rebuild batch；所有應涵蓋冊次完成並驗證後，在單一 DB transaction 將 Draft 設為 Active、舊 Active 設為 Historical，並切換每冊 current audio pointer。
- 任一冊重製失敗時不得部分切換；既有 Active epoch 繼續服務。
- 新加入的冊次一律使用 Active revision。一般建立 job API 不接受任意 revision ID；只有受控 staged rebuild service 可引用 Draft。
- UI 將 Historical 音訊明確標示為舊版，不得與 Active 音訊並列成同一套目前角色聲線。

## End-to-end flow

1. 使用者建立系列，設定系列名稱、旁白聲線與預設停頓。
2. 將每本 content book 加入系列並設定卷次／排序；同一 content book 最多屬於一個系列。
3. 系統從首冊建立角色名冊草稿：明確 reporting clause、已知 alias 與本機 attribution provider 產生 speaker suggestions。
4. 使用者確認角色、別名與聲線；主角／配角一旦確認，後續冊次直接沿用。
5. 每章生成 speech plan：旁白及角色 turn、原文 offset、speaker、confidence、decision source。
6. 低信心或未知角色進入 review queue；主角／配角未知不得靜默另建新角色。
7. 將每章 draft 確認為 immutable speech-plan revision；在同一 transaction 建立 job、鎖定 cast revision，並寫入每章 `NarrationJobSpeechPlan` 對照。
8. Worker 合併相鄰同 speaker turn、依聲線分段合成、加入有界停頓、串接並驗證 MP3。
9. 所有片段成功後才原子發布；進度只在成功片段後持久化。
10. 下一冊載入同一系列 Active cast／identity keys；新角色才進入配音設定，既有角色不重新選聲。

## Data model

### `StorySeries`

- `Id`, `OwnerId`, `Name`, `NormalizedName`
- `NarratorProvider`, `NarratorVoice`, `NarratorRate`, `NarratorPitch`, `NarratorVolume`
- `DefaultSpeakerPauseMs`, `ActiveCastRevisionId`
- `CreatedAt`, `UpdatedAt`, `ConcurrencyStamp`
- Unique: `(OwnerId, NormalizedName)`

### `SeriesBook`

- `Id`, `OwnerId`, `SeriesId`, `BookId`
- `VolumeLabel`, `SortOrder`, `MembershipRevision`
- `ActiveNarrationJobId` nullable；指向目前 Active cast epoch 的已發布多角色音訊。既有單聲線只記為 HistoricalFallback，不可填入此 pointer。
- Unique: `(OwnerId, BookId)`, `(OwnerId, SeriesId, BookId)` and `(OwnerId, SeriesId, SortOrder)`
- `BookId` 指向真正含正文且 `OwnerId` 非 null 的 content book，不指向純外部書目 target。

### `SeriesCharacter`

- `Id`, `OwnerId`, `SeriesId`, `CanonicalName`, `NormalizedName`
- `Role`: `Main | Supporting | Minor`
- `VoiceProvider`, `Voice`, `Rate`, `Pitch`, `Volume`
- `Notes` 僅存角色配音提示，不保存正文。
- `CreatedAt`, `UpdatedAt`, `ConcurrencyStamp`
- Unique: `(SeriesId, NormalizedName)`

### `SeriesCharacterIdentityKey`

- `Id`, `OwnerId`, `SeriesId`, `CharacterId`, `Kind`: `Canonical | Alias`
- `Value`, `NormalizedValue`
- Unique: `(OwnerId, SeriesId, NormalizedValue)`；canonical name 與 alias 共用同一命名空間。
- `SeriesCharacter.CanonicalIdentityKeyId` 必須指向同 owner、同 series、同 character 且 `Kind=Canonical` 的 key。
- 新增角色、改名與新增 alias 必須在同一 transaction 建立／更新 identity key，讓併發請求由 DB unique constraint 收斂。

### `NarrationCastRevision`

- `Id`, `OwnerId`, `SeriesId`, `RevisionNumber`, `Fingerprint`
- `Status`: `Draft | Active | Historical`，以及 `EpochNumber`
- `NarratorProvider`、`NarratorProviderVersion`、`NarratorVoice` 及參數 snapshot
- `DefaultSpeakerPauseMs`, `ChapterPauseMs`, `CompositionVersion`, `FfmpegProfile`
- `CreatedAt`, `ActivatedAt`
- 建立後不可修改；只有 status transition 與 series active pointer 可在受控 activation transaction 更新。

### `NarrationCastAssignment`

- `OwnerId`, `SeriesId`, `CastRevisionId`, `CharacterId`, `CanonicalNameSnapshot`
- `VoiceProvider`, `ProviderVersion`, `Voice`, `Rate`, `Pitch`, `Volume`
- Unique: `(OwnerId, SeriesId, CastRevisionId, CharacterId)`
- Composite FK 同時指向同 owner／series 的 cast revision 與 character。

### `SeriesCastRebuildBatch` and `SeriesCastRebuildMember`

- Batch: `Id`, `OwnerId`, `SeriesId`, `BaseActiveCastRevisionId`, `DraftCastRevisionId`, `CohortMembershipRevision`, `Status`: `Draft | Building | ReadyToActivate | Activated | Failed`
- Member: `OwnerId`, `SeriesId`, `BatchId`, `SeriesBookId`, `BookId`, `StagedNarrationJobId`, `PreviousActiveNarrationJobId`, `Status`
- 建立 batch 時在 transaction 內鎖定 `StorySeries`，snapshot 全部 `SeriesBook` 與 membership revision；新增／移除／重排冊次會增加 revision 並使未啟用 batch 失效。
- Unique: `(OwnerId, SeriesId, BatchId, BookId)`；member 的 job 必須屬於同 owner／series、`Visibility=Staged` 且使用 batch 的 Draft cast revision。
- Activation transaction 再鎖 series 與全部 members，要求 cohort 與目前 membership 完全一致、所有 staged jobs Completed 且通過 audio validation，才一次更新 `StorySeries.ActiveCastRevisionId`、每本 `SeriesBook.ActiveNarrationJobId`、new jobs `Published`、old jobs `Historical` 與 batch `Activated`。
- 任一條件不符整筆 rollback；不得部分切換。

### `ChapterSpeechPlanDraft`

- `Id`, `OwnerId`, `SeriesId`, `BookId`, `ChapterId`
- `SourceHash`, `PlanVersion`, `Status`: `Draft | NeedsReview | ReadyToConfirm | Stale`
- `CreatedAt`, `UpdatedAt`
- Chapter 標題或正文 hash 改變時，draft 必須標記 `Stale`。

### `ConfirmedSpeechPlanRevision`

- `Id`, `OwnerId`, `SeriesId`, `BookId`, `ChapterId`, `RevisionNumber`
- `SourceHash`, `PlanFingerprint`, `CreatedAt`
- 確認時將 draft segments 複製成 immutable confirmed segments；建立後不可修改或改成 stale，只能由新 revision 取代。
- `SourceHash` 同時涵蓋章名與正文，維持現行 `NarrationSource` 行為。

### `SpeechSegmentDraft` and `ConfirmedSpeechSegment`

- `Id`, `OwnerId`, `SeriesId`, draft／confirmed plan revision ID, `SortOrder`
- `SourceKind`: `ChapterTitle | Body`
- Body 使用 `StartOffset`, `Length`, `TextHash`，不重複保存私人正文；ChapterTitle 使用 `TitleTextHash` 並在執行時從 Chapter 讀取、驗 hash。
- `Kind`: `Narration | Dialogue`；每章第一個 segment 必須是 narrator 的 `ChapterTitle` turn，並明確包含既有句號正規化。
- `CharacterId` nullable；旁白為 null。
- `Confidence` 0–100
- `DecisionSource`: `Rule | LocalModel | User`
- Draft 有 `ReviewStatus`: `Suggested | Confirmed | Rejected`；confirmed segment 本身不可變。
- plan compiler 必須證明章名恰好一次，且所有 body offsets 無重疊、無缺口並按序重組為完整正文。

### `NarrationJobSpeechPlan`

- `OwnerId`, `SeriesId`, `NarrationJobId`, `ConfirmedSpeechPlanRevisionId`, `ChapterSortOrder`
- Unique: `(OwnerId, SeriesId, NarrationJobId, ChapterSortOrder)` and `(OwnerId, SeriesId, NarrationJobId, ConfirmedSpeechPlanRevisionId)`；兩端皆以 owner／series 複合 FK 約束。
- 建立 job 時與 job row 在同一 transaction 寫入；Worker 只依這些 revision IDs 載入。
- Worker 載入後重算 chapter source hash、segment text hashes、offset coverage 與 aggregate plan fingerprint；任一不符永久失敗為 `speech_plan_integrity_mismatch`，不得改讀最新 draft。

### `NarrationJob` changes

- Phase A 先新增 `Mode`: `SingleVoice | MultiCharacter`，既有 rows 回填 `SingleVoice`；compatibility Worker 只 claim supported modes。
- Phase B 新增 nullable `SeriesId`, `CastRevisionId`, `SpeechPlanFingerprint`, `RebuildBatchId`。
- 新增 `Visibility`: `HistoricalFallback | Staged | Published | Historical`；既有 SingleVoice 回填 `HistoricalFallback`。
- 單人版保留現有 `Voice`／`Rate` 相容欄位，但 Uploaded 書籍的新建 API 不再建立 SingleVoice job。
- 多角色 job 唯一鍵使用 `(OwnerId, SeriesId, BookId, ContentBookId, SourceHash, Mode, CastRevisionId, SpeechPlanFingerprint, RebuildBatchId)`。
- 一般 owner list/audio endpoint 不回傳 `Staged` jobs；只允許受控 batch preview endpoint 讓 owner 驗證。

## Speaker attribution rules

1. 先建立章名 narrator turn，再以段落與中文引號 `「」『』“”` 切出正文 narrator／dialogue candidate；不得把引號內文字直接視為完整角色判定。
2. 明確 reporting clause，例如「某某說／問／回答」且某某精確命中系列唯一 identity key，才可由 rule 自動確認。
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
- 更換 provider 或 base voice 一律建立 Draft cast revision；通過全系列 staged rebuild 與原子 epoch activation 前，不得影響 Active 聲線。供應商清單更新也不得自動改掉既有角色。

## Audio composition contract

- C# Worker 只從 job 鎖定的 immutable plan revisions 編譯有序 `NarrationTurn[]`，不得查詢「最新 plan」。
- 每章第一個 turn 是該章標題，由 narrator 朗讀並納入 plan/source fingerprint；正文 offsets 隨後 exact-once 組成，不額外插入未被 fingerprint 涵蓋的文字。
- 相鄰且 voice fingerprint 相同的 turn 可合併，但不得跨章、跨 speaker 或超過 5,000 字硬上限。
- C# 透過 mode/provider dispatcher 選擇既有 single-voice provider 或 multi-voice provider，再以 stdin 傳入 versioned JSON manifest；私人正文不得出現在 command line、process list 或 log。
- Python 對每個 turn 使用其固定 voice profile；每塊最多三次 provider 嘗試。
- speaker 切換加入 120–350ms 有界停頓，章界使用較長停頓；值屬於 series profile。
- 使用 ffmpeg 將合法 MP3 片段／silence 依序串接並做 codec probe；不靠未驗證 byte concatenation 當唯一保證。
- 只有所有 turns 成功並通過 ffprobe 後才原子發布 final MP3。
- 取消、timeout 或失敗必須 kill process tree 並清除所有私人 partial clips／manifest。
- 真實進度按「成功完成的 turn/chunk 字元權重」單調回報；100% 只在 final publish 後寫入。

## Owner and relationship isolation

- `BookConfiguration` 新增 nullable principal key／unique index `(OwnerId, Id)`；只有 `OwnerId` 非 null 的 Book 可加入 `SeriesBook`。`ChapterConfiguration` 新增 `(BookId, Id)` principal key。
- 所有新 root 表建立 `(OwnerId, Id)` alternate key；series child tables 建立 `(OwnerId, SeriesId, Id)`，需要 book 的 plan 再建立 `(OwnerId, SeriesId, BookId, Id)`。
- `SeriesBook` 以 non-null `(OwnerId, BookId)` 指向 owned content book；confirmed plan 以 `(OwnerId, SeriesId, BookId)` 指向 SeriesBook、以 `(BookId, ChapterId)` 指向 Chapter。
- cast assignment、draft／confirmed segments、job-plan joins 與 rebuild members 都實際保存 `OwnerId`、`SeriesId`，並以 owner／series 複合外鍵指向同一 boundary。
- DB constraint 必須阻止 owner A 的 series／job 指向 owner B 的 book、character、revision 或 plan，也必須阻止同 owner 不同 series 串接，即使繞過 API 直接寫 DB。
- Worker 的所有 series、cast、plan、segment、chapter joins 都必須同時 fence `OwnerId`、`SeriesId` 與 job 鎖定 IDs；找不到完整同 owner graph 時永久失敗，不 fallback 到未限定查詢。
- 測試必須包含直接插入污染資料的 DB constraint 負向案例，以及 Worker 面對跨 owner／跨 series 關聯時拒絕合成的案例。

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
- 多角色 activation 前，舊單人版標示為「暫存單聲線」；activation 後移入 HistoricalFallback／歷史版本，不與目前多角色版混列。Staged 音訊不出現在一般列表。

---

## Implementation tasks

### Task 0: Land the compatibility migration before every multi-character migration

**Objective:** 先建立可獨立部署與回滾的純 compatibility release，確保 EF migration 時序真的允許 Phase A／B 分離。

**Files:**
- Create: `src/StoryVoice.Domain/Narrations/NarrationMode.cs`
- Modify: `src/StoryVoice.Domain/Narrations/NarrationJob.cs`
- Modify: `src/StoryVoice.Infrastructure/Persistence/NarrationJobConfiguration.cs`
- Create first: `src/StoryVoice.Infrastructure/Persistence/Migrations/*_AddNarrationModeCompatibility.cs`
- Modify: `src/StoryVoice.Worker/StoryPipelineWorker.cs`
- Test: `tests/StoryVoice.UnitTests/NarrationModeCompatibilityTests.cs`
- Test: `tests/StoryVoice.IntegrationTests/NarrationModeMigrationTests.cs`

**Steps:**
1. 在產生任何 series／cast／plan migration 前，先寫紅燈測試：existing rows 回填 `SingleVoice`、DB default／CHECK、生產舊唯一鍵轉為 `WHERE Mode='SingleVoice'` partial index、compatibility Worker 只 claim `SingleVoice`。
2. migration 必須 additive；old API 仍可插入並由 DB default 得到 `SingleVoice`。
3. focused＋full tests、candidate image、existing SingleVoice live proof、review、commit／push／CI／backup／deploy 全部完成後，才能開始 Task 1 並產生 Phase B migrations。
4. Phase A 部署後禁止回滾到不認識 `Mode` 且會 claim 全部 jobs 的最舊 Worker；rollback baseline 是本 compatibility Worker。

### Task 1: Add series and fixed cast domain models

**Objective:** 建立 owner-scoped series、book membership、character 與 alias 不變條件。

**Files:**
- Create: `src/StoryVoice.Domain/Series/StorySeries.cs`
- Create: `src/StoryVoice.Domain/Series/SeriesBook.cs`
- Create: `src/StoryVoice.Domain/Series/SeriesCharacter.cs`
- Create: `src/StoryVoice.Domain/Series/SeriesCharacterIdentityKey.cs`
- Test: `tests/StoryVoice.UnitTests/StorySeriesTests.cs`

**Steps:**
1. 先寫紅燈測試：canonical／alias 共用 normalized identity key namespace、跨 owner／空 ID 被拒、既有角色 voice 不會被新增書籍改寫。
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
- Create: `src/StoryVoice.Infrastructure/Persistence/SeriesCharacterIdentityKeyConfiguration.cs`
- Create after Task 0 is deployed: `src/StoryVoice.Infrastructure/Persistence/Migrations/*_AddSeriesCast.cs`
- Test: `tests/StoryVoice.IntegrationTests/SeriesApiTests.cs`

**Steps:**
1. 先測 owner A 無法讀／改 owner B 的 series、canonical 與 alias 衝突、duplicate book membership，以及直接跨 owner／跨 series FK 污染會被 DB 拒絕。
2. 執行定向 integration test，確認紅燈。
3. 加 DbSet、configuration、foreign keys、check constraints 與 migration。
4. 執行 `dotnet ef migrations script --idempotent` 與 pending-model check。
5. 重跑 integration test。
6. Commit：`feat(series): 持久化系列角色配音表`。

### Task 3: Add immutable cast revisions and atomic rebuild batches

**Objective:** 讓每個 narration job 引用不可變的跨冊聲線 snapshot。

**Files:**
- Create: `src/StoryVoice.Domain/Narrations/NarrationCastRevision.cs`
- Create: `src/StoryVoice.Domain/Narrations/NarrationCastAssignment.cs`
- Create: `src/StoryVoice.Domain/Narrations/NarrationCastEpochActivation.cs`
- Create: `src/StoryVoice.Domain/Narrations/SeriesCastRebuildBatch.cs`
- Create: `src/StoryVoice.Domain/Narrations/SeriesCastRebuildMember.cs`
- Create: `src/StoryVoice.Infrastructure/Persistence/NarrationCastRevisionConfiguration.cs`
- Create: `src/StoryVoice.Infrastructure/Persistence/NarrationCastAssignmentConfiguration.cs`
- Create: `src/StoryVoice.Infrastructure/Persistence/SeriesCastRebuildBatchConfiguration.cs`
- Test: `tests/StoryVoice.UnitTests/NarrationCastRevisionTests.cs`

**Steps:**
1. 先測相同 cast 產生相同 fingerprint、provider version／pause／composition profile 變更產生新 Draft、一般 job 只能選 Active；snapshot cohort 後新增冊次會使 batch 失效、staged jobs 不出現在一般 API、全系列 members 未齊不得 activation、所有 current pointers 與 epoch 必須同 transaction 切換，activation 失敗不部分發布。
2. 實作包含 provider/version、旁白／角色參數、停頓及 composition profile 的 canonical fingerprint（固定排序、UTF-8、SHA-256）。
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
- Create: `src/StoryVoice.Domain/Narrations/ChapterSpeechPlanDraft.cs`
- Create: `src/StoryVoice.Domain/Narrations/ConfirmedSpeechPlanRevision.cs`
- Create: `src/StoryVoice.Domain/Narrations/SpeechSegmentDraft.cs`
- Create: `src/StoryVoice.Domain/Narrations/ConfirmedSpeechSegment.cs`
- Create: `src/StoryVoice.Domain/Narrations/NarrationJobSpeechPlan.cs`
- Create: `src/StoryVoice.Infrastructure/Persistence/*SpeechPlan*Configuration.cs`
- Create in Phase B after Task 0 release: `src/StoryVoice.Infrastructure/Persistence/Migrations/*_AddSpeechPlanRevisions.cs`
- Create: `src/StoryVoice.Api/SpeechPlanEndpoints.cs`
- Test: `tests/StoryVoice.IntegrationTests/SpeechPlanApiTests.cs`

**Steps:**
1. 先測複合 FK owner isolation、章名 turn、offset exact-once、identity-key batch assignment、chapter 標題／正文 hash 改變後 draft stale，以及 confirmed revision immutable。
2. 實作 draft／review／confirm-copy transition；只有 immutable confirmed revision 能寫入 `NarrationJobSpeechPlan`。
3. API context response 設 private/no-store，不輸出整章。
4. 跑完整 unit／integration tests。
5. Commit：`feat(narration): 新增角色分段審核流程`。

### Task 8: Implement multi-voice Edge TTS provider

**Objective:** 按固定 speaker voice profile 合成、串接並原子發布完整 MP3。

**Files:**
- Modify: `src/StoryVoice.Worker/INarrationProvider.cs`
- Create: `src/StoryVoice.Worker/MultiVoiceNarrationRequest.cs`
- Create: `src/StoryVoice.Worker/EdgeTtsMultiVoiceNarrationProvider.cs`
- Create: `src/StoryVoice.Worker/INarrationProviderRegistry.cs`
- Create: `src/StoryVoice.Worker/NarrationProviderDispatcher.cs`
- Modify: `src/StoryVoice.Worker/Program.cs`
- Create: `src/StoryVoice.Worker/edge_tts_multi_voice_provider.py`
- Modify: `docker/worker.Dockerfile`
- Test: `tests/StoryVoice.UnitTests/EdgeTtsMultiVoiceNarrationProviderTests.cs`
- Test: `tests/python/test_edge_tts_multi_voice_provider.py`

**Steps:**
1. 先測 mode/provider dispatcher、保留既有 single provider、turn order、不同 voice／provider version 傳遞、章名 exact-once、同 speaker bounded merge、重試、取消、timeout、partial cleanup 與 progress monotonicity。
2. 以 stdin 傳 versioned JSON，不將正文放進 argv／logs／temp filename。
3. 每 turn/chunk 仍守 5,000 字上限及三次 provider 嘗試。
4. 使用 ffmpeg concat／silence，完成後執行 ffprobe；所有片段成功才 `os.replace`。
5. 真實 synthetic 三角色 probe 驗證 duration、codec、聲線切換順序及 temp cleanup。
6. Commit：`feat(narration): 合成多角色固定聲線音訊`。

### Task 9: Integrate staged multi-character jobs

**Objective:** 保留單人版 HistoricalFallback 相容性，讓 Uploaded 書籍只建立可 staged／原子發布的 multi-character job。

**Files:**
- Modify: `src/StoryVoice.Domain/Narrations/NarrationJob.cs`
- Modify: `src/StoryVoice.Infrastructure/Persistence/NarrationJobConfiguration.cs`
- Create in Phase B: `src/StoryVoice.Infrastructure/Persistence/Migrations/*_AddMultiCharacterJobFields.cs`
- Modify: `src/StoryVoice.Infrastructure/Persistence/NarrationService.cs`
- Create: `src/StoryVoice.Application/Narrations/NarrationAdmissionOptions.cs`
- Modify: `src/StoryVoice.Worker/StoryPipelineWorker.cs`
- Modify: `src/StoryVoice.Application/Narrations/NarrationContracts.cs`
- Test: `tests/StoryVoice.UnitTests/NarrationJobTests.cs`
- Test: `tests/StoryVoice.IntegrationTests/NarrationApiTests.cs`

**Steps:**
1. Task 0 已負責 `Mode=SingleVoice` 回填與 Phase A indexes。本 task 先測 Phase B required／nullable 條件、Uploaded create 不得再建 SingleVoice、一般 job 只能使用 Active cast、staged job 必須綁 rebuild batch、job-plan revisions 同交易鎖定、未確認 plan 拒絕建立、admission 關閉回穩定錯誤碼、舊單人版仍可作 HistoricalFallback。
2. Worker 只從 job 鎖定的 confirmed plan revisions＋cast revision 編譯 turns並重算完整 fingerprint；所有 joins 使用 owner／series 複合 fence，未知 speaker 按明確 fallback policy 處理。
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

**Objective:** 將既有 15 本 content books 加入同一系列，完成角色校正並 staged 重製全部多聲線 MP3；既有單聲線只保留 HistoricalFallback。

**Files:**
- Create outside Git: `/tmp/storyvoice-private-series-backfill/`
- No private title, owner ID, text, cast, audio, token or mapping may enter repo／image／logs.

**Steps:**
1. 先做 production backup 與隔離 proof DB。
2. 以明確 manifest 指定 series membership／volume order；不得依 title number 猜新版與舊版映射。
3. 初次建立 memberships，第二次執行新增 0，證明冪等。
4. 建立系列、固定 narrator 與 cast；規則／本機模型只產生 suggestions，低信心角色進 review queue，不把 unknown 猜成確定人物。
5. 15 本全部確認 speech-plan revisions 後，建立一個完整 rebuild batch 與 15 個 Staged MultiCharacter jobs；舊 MP3 不刪除，維持 HistoricalFallback 直到整批驗證通過。
6. 15 個新 MP3 全部通過 codec／duration／decode／owner isolation 後才原子 activation；任一本失敗都不部分切換。
7. 驗證 non-owner 看不到 series／cast／plan／staged audio；輸出只含數量與狀態。

### Task 12: Release without interrupting current narrations

**Objective:** 以兩階段 compatibility rollout 完成 migration、candidate、production proof 與安全 rollback。

**Steps:**
1. 嚴格先完成 Task 0 的獨立 commit／CI／backup／production release；其 migration 必須是歷史序列中第一個新增檔，且在此之前不得產生任何 series／cast／plan migration。Phase A 新增 `Mode`、回填 `SingleVoice`、建立 partial indexes，compatibility Worker 明確只 claim `SingleVoice`。
2. 驗證 Phase A 的舊單人 API／Worker 行為、existing MP3、migration idempotence 與 rollback；舊 Worker image 在任何 MultiCharacter row 存在後不得再回滾使用。
3. Task 0 正式 marker 驗證後才產生／部署 Phase B migrations：series／cast／rebuild batch／immutable plan tables、owner-series composite keys、multi job fields、provider dispatcher、API／Web；此時 `Narration__AdmissionEnabled=false` 且 multi feature flag 關閉。
4. 等目前 15 本單人版工作進入終態；切換前關閉所有新 narration admission，並從 DB 即時證明 `Queued=0`、`Running=0`、無有效 lease／provider process；只看 monitor exit code 不算。
5. 全跑 Unit、Integration、Python、Web、lint、build、format、compose、migration script、diff 與 credential-shaped scan。
6. Build exact candidate images；用 synthetic public-domain text 做 narrator＋兩角色、兩章章名 exact-once 真實音訊 proof。
7. Release review 檢查 cast epoch 原子切換、immutable job-plan lock、identity namespace、DB／Worker owner fence、speaker fallback、private text logging、cleanup 與 stale polling。
8. Git pull／push 前 drift check，繁中 Conventional Commit，CI 綠燈。
9. 備份 PostgreSQL、book storage、audio、Data Protection keys、runtime tree 與 images，只保留最新三份。
10. 先讓新 API 套 additive migration，再替換只 claim supported modes 的 Worker，最後替換 Web；每一階段均驗 running image 與 logs。
11. 正式建立 synthetic canary series，驗 narrator／角色固定、第二冊沿用同一 character IDs／Active revision、chapter title exact-once、job-plan immutable IDs。
12. 執行 staged revision N+1 canary：未全冊完成前 active epoch 不變；全部完成後單一 transaction 切換，舊音訊標 Historical。
13. owner 正向、匿名／non-owner 負向、直接跨 owner FK 污染拒絕、audio Range、ffprobe、temp cleanup、logs、public bundle 與 marker 全部通過。
14. 開啟 narration admission 與 multi feature flag；確認新工作只由支援其 Mode 的 Worker claim。
15. 清除 canary 與 candidate artifacts；更新 project memory。對任何已有 MultiCharacter rows 的環境，rollback 只能回 Phase A compatibility Worker，不可回最初不懂 Mode 的 Worker。

## Acceptance criteria

- 同一系列目前公開的所有多角色冊次必須指向同一 Active cast epoch；相同 `SeriesCharacter.Id` 的 voice fingerprint 完全一致。Historical 音訊不得混列為目前系列版本。
- 新冊分析不會為既有 canonical／alias identity key 建立第二個角色，也不會重新抽選 voice。
- 旁白有獨立且固定的系列聲線。
- 使用者可明確建立新 cast revision；舊 MP3／job 仍保留舊 revision。
- 未知／低信心 speaker 不會被偽裝成確定角色。
- 多角色音訊維持章名＋正文順序；章名恰好一次，正文 offsets 完整覆蓋且所有文字恰好朗讀一次，無遺漏、重複或跨章錯置。
- 任一 turn 失敗時不發布 final MP3，且不留下私人 partial files。
- 進度單調、真實、可持久化；100% 只代表可播放 final audio 已原子發布。
- 每個 job 以 `NarrationJobSpeechPlan` 鎖定 immutable confirmed revisions；排隊後 draft 修改、stale 或新 revision 都不能改變該 job 的輸入。
- provider version、停頓與 composition profile 均屬 cast snapshot，重試不受可變 series defaults 影響。
- series、cast、speech plan、audio 全部 owner-isolated；私人正文不進 Git、image、URL、argv 或 application logs。
- 現有單人朗讀音訊只保留 HistoricalFallback／回滾相容；所有 Uploaded 書籍的新工作必須是 MultiCharacter，現有 15 本全數完成 staged multi-voice cohort 後才算完成。

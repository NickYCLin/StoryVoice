# 自有合成聲線的公開、示範與訂閱授權

StoryVoice 的公開聲線目錄只接受「完全由建立者控制來源」的合成聲線。這條路徑不建立
第三方簽署、身分證明或隱私告知欄位；它仍要求一份可撤銷、可到期、可稽核的建立者
授權。

本文件只適用於公開目錄與 `subscription-commercial`。短期私人跨專案開發接用使用
`private-development` 與獨立的 30 天內 development grant，不需要假填本文件的公開
demo、provider commercial rights 或 publication authorization；詳見
[EXTERNAL_VOICE_API.md](EXTERNAL_VOICE_API.md)。

唯一授權 artifact 是 **storyvoice-synthetic-voice-authorization/v1**。它同時綁定：

- owner、角色 profile、公開名稱、AI 揭露、固定示範 WAV；
- 生成工具／模型／版本、實際 generation manifest 與 provider terms snapshot；
- 參考音訊、完全校對逐字稿、素材來源聲明與 provider rights；
- 公開目錄、示範播放、跨專案 API、訂閱、商用與公開散布；
- consumer family、地區、期限、撤銷方式與 authenticated owner action。

這份 artifact 只是登入帳號對「來源與使用權」做的可稽核聲明，不是第三方簽章，也不是
作者身分、著作權成立或不侵權保證。StoryVoice 不判定 AI 產出是否構成受保護著作。

## Eligibility boundary

使用者陳述只能把聲線列為 synthetic candidate，不能取代正式核准。repository 目前沒有
任何 active authorization、真實 provider terms snapshot、真實 generation manifest、
公開 demo、consumer grant 或 API token，也不會因此部署。

若公開卡片使用第三方作品角色名、品牌或足以使人誤認為官方合作的標示，就不能把
**noThirdPartyCharacterOrBrandClaimed** 設為 true。產品既有私人角色試音不會自動取得
public/subscription 權利；正式卡片應使用自有或已明確授權的角色與標示。

## 私有來源檔

generator 接受 canonical owner ID、public alias 與既有 character profile ID，另須保留
並傳入下列實際檔案，而非直接手填看似正確的 64 位 hex：

- generation manifest：記錄建立流程、受控輸入來源及可重現的工具資訊；
- provider terms snapshot：接受條款當時的私有快照；
- reference WAV 與完全校對 canonical transcript；
- 固定、預先產生且不接受任意文字的 public demo WAV。

generator 會讀取上述檔案並自行計算 SHA-256；逐字稿會先依 runtime 相同規則做 NFKC、
trim，拒絕內嵌 control／format character，再對無 BOM UTF-8 canonical bytes 計算 hash。
generation manifest 與 terms snapshot 正式啟用後仍須放在受保護的 catalog asset root，
runtime 會重讀實際 bytes；reference、transcript 與 demo 也必須與既有 owner-scoped
profile／catalog 資產相交驗證。

## 唯一授權 schema

以下是 draft 的 exact shape。所有時間使用秒精度 UTC，例如
**2026-08-19T00:00:00Z**。範例中的 placeholder 不是核准文件：

~~~json
{
  "schema": "storyvoice-synthetic-voice-authorization/v1",
  "authorizationId": "<system-generated-canonical-id>",
  "ownerId": "<canonical-lowercase-owner-guid>",
  "voice": {
    "alias": "<canonical-public-alias>",
    "characterProfileId": "<canonical-lowercase-profile-guid>",
    "displayName": "<public-display-name>",
    "attributionText": "<optional-attribution-or-null>",
    "attributionDisplayAllowed": true,
    "aiDisclosureRequired": true,
    "styles": ["<style>"],
    "useCases": ["<use-case>"],
    "fixedDemoSha256": "<actual-fixed-demo-wav-sha256>",
    "fixedDemoMediaType": "audio/wav"
  },
  "creation": {
    "providerId": "<canonical-provider-id>",
    "toolId": "<tool-id>",
    "modelId": "<model-id>",
    "modelRevision": "<model-revision>",
    "createdAtUtc": "2026-08-18T00:00:00Z",
    "generationManifestSha256": "<actual-generation-manifest-sha256>",
    "licenseIdentifier": "<provider-license-identifier>",
    "termsUri": "https://provider.example/terms",
    "termsSnapshotSha256": "<actual-provider-terms-snapshot-sha256>",
    "termsAcceptedAtUtc": "2026-08-17T00:00:00Z"
  },
  "assetBindings": {
    "referenceAudioSha256": "<actual-reference-audio-sha256>",
    "expectedTranscriptCanonicalSha256": "<actual-canonical-transcript-sha256>"
  },
  "sourceClaims": {
    "allGenerationInputsOwnedOrLicensed": true,
    "noHumanVoiceInputProvided": true,
    "noHumanBiometricTemplateProvided": true,
    "noIdentifiablePersonImitationRequested": true,
    "noKnownIdentifiablePersonImitated": true,
    "noThirdPartyCharacterOrBrandClaimed": true
  },
  "providerRights": {
    "commercialOutputUseAllowed": false,
    "publicOutputDistributionAllowed": false,
    "apiServiceUseAllowed": false,
    "voiceModelDerivationAllowed": false
  },
  "permissions": {
    "catalogDisplay": true,
    "demoPlayback": true,
    "crossProjectApi": true,
    "subscriptionOffering": true,
    "commercialUse": true,
    "publicDistribution": true
  },
  "allowedConsumerFamilies": ["<canonical-consumer-family-id>"],
  "territory": {
    "mode": "country-list",
    "countryCodes": ["TW"]
  },
  "externalProviderPolicy": {
    "mode": "prohibited",
    "allowedProviderIds": []
  },
  "effectiveAtUtc": "2026-08-19T00:00:00Z",
  "expiresAtUtc": "2027-08-19T00:00:00Z",
  "revocation": {
    "scope": "all-authorized-uses",
    "contact": "rights@example.test",
    "process": "Disable the catalog entry and every dependent API grant.",
    "requestedAtUtc": null,
    "effectiveAtUtc": null
  },
  "attestation": {
    "state": "draft",
    "method": null,
    "accountSubjectId": null,
    "auditEventId": null,
    "attestedAtUtc": null,
    "issuedAtUtc": null
  }
}
~~~

全域授權不列 allowedProjects。每個訂閱專案只存在於個別 consumer 設定與
voice-api-synthetic-usage-grant/v1，避免新增訂閱者時重發整份公開授權。

## 產生 draft

[New-SyntheticVoiceAuthorizationDraft.ps1](../scripts/New-SyntheticVoiceAuthorizationDraft.ps1)
永不覆寫既有檔案，且只能建立 draft。範例：

~~~powershell
$parameters = @{
  OwnerId = '<owner-guid>'
  VoiceAlias = '<canonical-public-alias>'
  CharacterProfileId = '<existing-character-profile-guid>'
  ProviderId = '<canonical-provider-id>'
  ToolId = '<tool-id>'
  ModelId = '<model-id>'
  ModelRevision = '<model-revision>'
  CreatedAtUtc = '<creation-UTC-time>'
  GenerationManifestPath = '<private-generation-manifest>'
  LicenseIdentifier = '<license-identifier>'
  TermsUri = 'https://provider.example/terms'
  TermsSnapshotPath = '<private-provider-terms-snapshot>'
  TermsAcceptedAtUtc = '<terms-accepted-UTC-time>'
  ReferenceAudioPath = '<private-reference.wav>'
  ExpectedTranscriptCanonicalPath = '<private-canonical-transcript.txt>'
  SourceClaimsConfirmation = 'confirm'
  DisplayName = '<public-display-name>'
  AttributionText = '<optional-attribution>'
  AttributionDisplayConfirmation = 'confirm'
  Styles = @('<style>')
  UseCases = @('<use-case>')
  FixedDemoPath = '<private-fixed-demo.wav>'
  PermissionsConfirmation = 'confirm'
  AllowedConsumerFamilies = @('<consumer-family-id>')
  TerritoryMode = 'country-list'
  TerritoryCountryCodes = @('TW')
  EffectiveAtUtc = '<effective-UTC-time>'
  ExpiresAtUtc = '<expiry-UTC-time>'
  RevocationContact = '<email-or-https-url>'
  RevocationProcess = '<operational-revocation-process>'
  OutputPath = '<new-private-authorization-draft.json>'
}
./scripts/New-SyntheticVoiceAuthorizationDraft.ps1 @parameters
~~~

generator 刻意把四個 providerRights 全設為 false，也沒有
ProviderRightsConfirmation 參數。建立者不能靠勾選方塊自行宣稱供應商已授權。
EffectiveAtUtc 必須留在未來，讓後續 authenticated owner action 能在生效前完成。

## 受控啟用

啟用必須由受控後台流程完成，不提供 draft-to-active helper：

1. 審查實際 terms snapshot、license identifier 與 terms URI，確認輸出商用、公開散布、
   API 服務及聲線衍生四項權利都明確成立，才可把對應 providerRights 設為 true。
2. 核對 generation manifest、owner／alias／profile、reference、transcript 與固定 demo 的
   exact bytes 及 SHA-256。
3. 由已登入 owner 執行一次 authenticated owner action；受控系統填入 account subject、
   audit event、attested time 與 issued time，並把 state 設為
   active、method 設為 authenticated-owner-action。
4. 時間必須滿足 terms accepted 不晚於 creation，creation 不晚於 attestation，
   attestation 不晚於 issuance，issuance 不晚於 effective；授權仍須未到期且未撤銷。
5. 將 active artifact 及其 exact SHA、manifest／terms／demo 的私有 relative paths
   安裝到受保護的部署設定。不要把素材、內部路徑或稽核記錄提交到 Git。

驗證 active artifact：

~~~powershell
./scripts/Test-SyntheticVoiceAuthorization.ps1 -Path '<private-active-authorization.json>'
./scripts/Test-SyntheticVoicePublicationTooling.ps1
~~~

verifier 對 unknown／duplicate property、false claim、未確認 provider right、較窄
permission、外部 provider allowlist、期限與任何撤銷時間都 fail closed。

## 撤銷與私人試音

收到撤銷要求時，先停用 catalog entry 與所有相依 consumer grant，再保存 requested／
effective 時間及稽核記錄；任何撤銷 timestamp 都會讓目前 active verifier 拒絕。

私人單次試音仍走既有 owner-scoped LocalClonePreview，不經 public catalog，也不因此
取得 API、訂閱、商用或公開散布權。public/subscription 啟用不會改寫正式 narration
cast 或已生成有聲書。

## 著作權說明

自行產生聲線不需要建立不存在的第三方簽署，但不能推導出「AI 輸出一定有
著作權」。人類創意投入、素材權利、角色／品牌使用及是否侵權仍須依個案判斷。參考
智慧財產局官方說明：

- [電子郵件1140516b](https://www.tipo.gov.tw/tw/copyright/692-34249.html)
- [電子郵件1140522c](https://www.tipo.gov.tw/tw/copyright/692-34252.html)

# Contributing to StoryVoice

Thanks for helping build a dependable open-source AI Story Director.

## Before opening a pull request

1. Open or reference an issue for substantial behavior changes.
2. Keep provider-specific code behind an interface.
3. Never commit API keys, uploaded books, generated audio or user data.
4. Do not add DRM circumvention features.
5. Add tests for behavior changes.

## Verification

```bash
dotnet build StoryVoice.sln
dotnet test StoryVoice.sln
cd src/StoryVoice.Web
npm ci
npm run lint
npm run build
```

For runtime changes, also run `docker compose up --build` and verify `/health/ready` plus the web UI.

## Commit style

使用繁體中文 Conventional Commits：

```text
<type>(<scope>): <subject>

<body>

<footer>
```

- `type` 使用 `feat`、`fix`、`docs`、`style`、`refactor`、`test` 或 `chore`。
- `scope` 為選填，應指出實際影響範圍，例如 `series`、`narration`、`web`。
- `subject` 使用直接、現在式的繁體中文敘述，最多 50 個字元，結尾不加句點。
- 標題與內文以空行分隔；內文說明變更的 What 與 Why，單行最多 100 個字元。
- 有 issue 時在 footer 使用 `Closes #123`；不相容變更使用 `BREAKING CHANGE:`。

範例：

```text
feat(series): 新增系列配音管理 API

讓系列、冊次、角色與 alias 都經過 owner 篩選與 CSRF 驗證，
並限制只能選擇伺服器允許的聲線，避免任意 provider ID 進入持久層。
```

格式參考：[Git Commit Message 規範](https://ithelp.ithome.com.tw/articles/10310628)。

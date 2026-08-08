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

Use Conventional Commits, for example:

```text
feat(parser): add EPUB chapter extraction
fix(casting): preserve locked voice across chapters
```

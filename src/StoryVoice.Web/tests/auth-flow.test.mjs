import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

const authSource = readFileSync(new URL('../src/auth.ts', import.meta.url), 'utf8')
const authScreenSource = readFileSync(new URL('../src/AuthScreen.tsx', import.meta.url), 'utf8')
const appLayoutSource = readFileSync(new URL('../src/AppLayout.tsx', import.meta.url), 'utf8')
const apiSource = readFileSync(new URL('../src/api.ts', import.meta.url), 'utf8')
const libraryPageSource = readFileSync(new URL('../src/pages/LibraryPage.tsx', import.meta.url), 'utf8')
const extensionPopup = readFileSync(
  new URL('../../../extensions/books-com-tw-companion/popup.js', import.meta.url),
  'utf8'
)
const extensionHtml = readFileSync(
  new URL('../../../extensions/books-com-tw-companion/popup.html', import.meta.url),
  'utf8'
)
const webDockerfile = readFileSync(new URL('../../../docker/web.Dockerfile', import.meta.url), 'utf8')

test('StoryVoice 先建立自己的帳號工作階段，再顯示個人書庫', () => {
  assert.match(authSource, /\/api\/auth\/session/)
  assert.match(authScreenSource, /\/api\/auth\/register/)
  assert.match(authScreenSource, /\/api\/auth\/login/)
  assert.match(authScreenSource, /登入 StoryVoice/)
  assert.match(authScreenSource, /建立 StoryVoice 帳號/)
  assert.match(authSource, /authenticated/)
})

test('所有 Cookie 寫入都帶 CSRF，登出會整個卸載私人書庫殼層', () => {
  assert.match(authScreenSource, /X-CSRF-TOKEN/)
  assert.match(authSource, /\/api\/auth\/logout/)
  assert.match(apiSource, /credentials:\s*'same-origin'/)
  // AppLayout 在 anonymous 狀態下用提早 return 整段換成 AuthScreen，而不是逐一清空狀態，
  // 讓登出後不會有任何一頁殘留前一個帳號的私人資料：<Outlet> 一定在這個提早 return 之後才會出現。
  const anonymousGuard = appLayoutSource.indexOf("authState.status === 'anonymous'")
  const outletRender = appLayoutSource.indexOf('<Outlet')
  assert.notEqual(anonymousGuard, -1)
  assert.notEqual(outletRender, -1)
  assert.ok(anonymousGuard < outletRender, 'Outlet 必須在 anonymous 提早 return 之後才會渲染')
  assert.match(appLayoutSource, /<AuthScreen /)
})

test('博客來流程明講先登入 StoryVoice，再到官方博客來登入', () => {
  const storyVoiceStep = authScreenSource.indexOf('登入 StoryVoice')
  const booksStep = authScreenSource.indexOf('登入自己的博客來')
  assert.notEqual(storyVoiceStep, -1)
  assert.notEqual(booksStep, -1)
  assert.ok(storyVoiceStep < booksStep)
  assert.match(libraryPageSource, /\/api\/auth\/companion-token/)
  assert.match(libraryPageSource, /\/api\/auth\/companion-token\/revoke/)
  assert.doesNotMatch(authScreenSource, /博客來密碼/)
  assert.doesNotMatch(libraryPageSource, /博客來密碼/)
})

test('博客來流程可直接下載 Companion，並顯示 Chrome 載入未封裝項目的完整步驟', () => {
  const dockerSteps = webDockerfile
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line && !line.startsWith('#'))
  const packageStep = dockerSteps.indexOf('RUN node /companion/scripts/package.mjs /app/public/storyvoice-books-companion.zip')
  const webBuildStep = dockerSteps.indexOf('RUN npm run build')
  const finalCopyStep = dockerSteps.indexOf('COPY --from=build /app/dist /usr/share/nginx/html')

  assert.match(libraryPageSource, /import\.meta\.env\.BASE_URL}storyvoice-books-companion\.zip/)
  assert.match(libraryPageSource, /下載 Companion ZIP/)
  assert.match(libraryPageSource, /chrome:\/\/extensions/)
  assert.match(libraryPageSource, /開發人員模式/)
  assert.match(libraryPageSource, /載入未封裝項目/)
  assert.match(libraryPageSource, /解壓縮後的資料夾/)
  assert.notEqual(packageStep, -1)
  assert.ok(packageStep < webBuildStep, 'Companion ZIP 必須在 Vite build 前放進 public')
  assert.ok(webBuildStep < finalCopyStep, '最終 image 必須複製包含 Companion ZIP 的 dist')
})

test('Companion 使用使用者建立的 Bearer 金鑰，不借用博客來 Cookie 或 StoryVoice Cookie', () => {
  assert.match(extensionHtml, /StoryVoice 連線金鑰/)
  assert.match(extensionPopup, /Authorization/)
  assert.match(extensionPopup, /Bearer/)
  assert.match(extensionPopup, /storyVoiceAccessToken/)
  assert.match(extensionPopup, /credentials:\s*'omit'/)
})

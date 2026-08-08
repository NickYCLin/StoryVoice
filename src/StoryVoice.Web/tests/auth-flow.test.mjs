import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

const appSource = readFileSync(new URL('../src/App.tsx', import.meta.url), 'utf8')
const extensionPopup = readFileSync(
  new URL('../../../extensions/books-com-tw-companion/popup.js', import.meta.url),
  'utf8'
)
const extensionHtml = readFileSync(
  new URL('../../../extensions/books-com-tw-companion/popup.html', import.meta.url),
  'utf8'
)

test('StoryVoice 先建立自己的帳號工作階段，再顯示個人書庫', () => {
  assert.match(appSource, /\/api\/auth\/session/)
  assert.match(appSource, /\/api\/auth\/register/)
  assert.match(appSource, /\/api\/auth\/login/)
  assert.match(appSource, /登入 StoryVoice/)
  assert.match(appSource, /建立 StoryVoice 帳號/)
  assert.match(appSource, /authenticated/)
})

test('所有 Cookie 寫入都帶 CSRF，登出後清空個人書庫狀態', () => {
  assert.match(appSource, /X-CSRF-TOKEN/)
  assert.match(appSource, /\/api\/auth\/logout/)
  assert.match(appSource, /setBooks\(\[\]\)/)
  assert.match(appSource, /credentials:\s*'same-origin'/)
})

test('博客來流程明講先登入 StoryVoice，再到官方博客來登入', () => {
  const storyVoiceStep = appSource.indexOf('登入 StoryVoice')
  const booksStep = appSource.indexOf('登入自己的博客來')
  assert.notEqual(storyVoiceStep, -1)
  assert.notEqual(booksStep, -1)
  assert.ok(storyVoiceStep < booksStep)
  assert.match(appSource, /\/api\/auth\/companion-token/)
  assert.match(appSource, /\/api\/auth\/companion-token\/revoke/)
  assert.doesNotMatch(appSource, /博客來密碼/)
})

test('Companion 使用使用者建立的 Bearer 金鑰，不借用博客來 Cookie 或 StoryVoice Cookie', () => {
  assert.match(extensionHtml, /StoryVoice 連線金鑰/)
  assert.match(extensionPopup, /Authorization/)
  assert.match(extensionPopup, /Bearer/)
  assert.match(extensionPopup, /storyVoiceAccessToken/)
  assert.match(extensionPopup, /credentials:\s*'omit'/)
})

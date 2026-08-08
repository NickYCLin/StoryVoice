import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

const panel = readFileSync(new URL('../src/NarrationPanel.tsx', import.meta.url), 'utf8')
const app = readFileSync(new URL('../src/App.tsx', import.meta.url), 'utf8')

test('AI narration requires explicit rights attestation and eligible authorized text', () => {
  assert.ok(panel.includes('rightsAttested: attested'))
  assert.ok(panel.includes('book.authorizedTextAvailable || book.contentBookId !== null'))
  assert.ok(panel.includes('需要繞過 DRM'))
  assert.ok(panel.includes('disabled={!attested || loading || active}'))
})

test('narration discloses external neural provider and distinguishes official TTS metadata', () => {
  assert.ok(panel.includes('Microsoft Edge 神經語音服務'))
  assert.ok(panel.includes('博客來官方閱讀器的 TTS 標記是不同能力'))
  assert.ok(panel.includes('博客來官方 TTS 標記不等於 StoryVoice 音訊'))
  assert.ok(panel.includes('音訊完成後保存於你的私人 StoryVoice 帳號'))
})

test('narration mutations use CSRF, poll durable jobs, support cancel and private audio playback', () => {
  assert.ok(panel.includes("'X-CSRF-TOKEN': csrfToken"))
  assert.ok(panel.includes('window.setInterval'))
  assert.ok(panel.includes('/cancel`'))
  assert.ok(panel.includes('/audio`)}'))
  assert.ok(panel.includes('<audio'))
  assert.ok(app.includes('<NarrationPanel book={selectedBook} csrfToken={authState.csrfToken} />'))
  assert.ok(app.includes('onBookUpdated({ ...book, contentBookId: contentSelection || null })'))
})

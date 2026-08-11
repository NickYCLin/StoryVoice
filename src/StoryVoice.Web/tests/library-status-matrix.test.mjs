import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const matrix = await readFile(new URL('../src/LibraryStatusMatrix.tsx', import.meta.url), 'utf8')
const app = await readFile(new URL('../src/pages/LibraryPage.tsx', import.meta.url), 'utf8')

test('library status matrix separates source capabilities from StoryVoice processing', () => {
  assert.match(matrix, /\/api\/library\/status-matrix\//)
  assert.match(matrix, /官方 TTS.*只代表來源閱讀器宣告的能力/)
  assert.match(matrix, /StoryVoice 音訊.*合法 EPUB／TXT/)
  assert.match(matrix, /storyVoiceNarrationMatchesAuthorizedText/)
  assert.match(matrix, /既有 StoryVoice 音訊（非目前合法正文）/)
  assert.doesNotMatch(matrix, /if \(!status\.authorizedTextAvailable\) return/)
  assert.match(matrix, /合法正文：未提供/)
  assert.match(matrix, /擷取式摘要：未建立/)
  assert.match(matrix, /你的筆記/)
  assert.match(matrix, /authorized_text_required/)
  assert.match(matrix, /Blocked：需由你明確上傳並連結合法、無 DRM 的 EPUB／TXT 正文/)
})

test('authenticated library renders the matrix and refreshes after explicit content link changes', () => {
  assert.match(app, /<LibraryStatusMatrix/)
  assert.match(app, /book\.contentBookId/)
})

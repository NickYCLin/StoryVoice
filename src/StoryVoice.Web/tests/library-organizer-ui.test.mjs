import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const app = await readFile(new URL('../src/App.tsx', import.meta.url), 'utf8')

test('library organizer exposes accessible search, source, layout, TTS, tag, and sort controls', () => {
  for (const marker of [
    'aria-label="書庫整理工具"',
    '搜尋書名、作者、書籍 ID 或標籤',
    '博客來版型',
    '博客來官方 TTS',
    '此裝置標籤',
    '符合 {visibleBooks.length}／全部 {books.length} 本',
    '沒有符合條件的書',
  ]) assert.ok(app.includes(marker), `missing ${marker}`)
})

test('library renders filtered books and moves selection away from hidden results', () => {
  assert.match(app, /visibleBooks\.map\(\(book\)/)
  assert.match(app, /!visibleBooks\.some\(\(book\) => book\.id === selectedBookId\)/)
  assert.match(app, /setSelectedBookId\(visibleBooks\[0\]\.id\)/)
})

test('device tags are explicitly local-only and persisted without a server request', () => {
  assert.ok(app.includes("storyvoice:device-book-tags:v1"))
  assert.ok(app.includes('只保存在目前瀏覽器；不會傳到博客來'))
  assert.ok(app.includes('localStorage.setItem(deviceTagsStorageKey'))
  assert.doesNotMatch(app, /fetch\([^)]*deviceTagsStorageKey/)
})

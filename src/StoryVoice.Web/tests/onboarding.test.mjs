import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

const libraryPage = readFileSync(new URL('../src/pages/LibraryPage.tsx', import.meta.url), 'utf8')

function positionOf(source, marker) {
  const position = source.indexOf(marker)
  assert.notEqual(position, -1, `找不到必要的新手介面標記：${marker}`)
  return position
}

test('直接上傳是推薦主流程，博客來同步收在進階選項', () => {
  const upload = positionOf(libraryPage, 'id="book-file"')
  const advancedSync = positionOf(libraryPage, '進階：同步博客來書櫃書目')

  assert.ok(upload < advancedSync, '直接上傳應比進階書櫃同步更早出現')
  assert.match(libraryPage, /推薦方式/)
  assert.match(libraryPage, /只要準備一個無 DRM 的 EPUB 或 UTF-8 TXT/)
})

test('空書庫畫面直接教使用者 3 步驟開始，不需要另一個行銷頁面', () => {
  const emptyState = positionOf(libraryPage, "books.length === 0")
  const stepOne = positionOf(libraryPage, '準備一本你有權處理、無 DRM 的 EPUB 或 UTF-8 TXT')
  const stepTwo = positionOf(libraryPage, '選擇檔案並按「匯入並解析」')
  const stepThree = positionOf(libraryPage, '匯入後選書、展開章節檢查解析內容')

  assert.ok(emptyState < stepOne, '3 步驟教學應該在空書庫狀態內')
  assert.ok(stepOne < stepTwo && stepTwo < stepThree, '3 步驟教學必須依序出現')
})

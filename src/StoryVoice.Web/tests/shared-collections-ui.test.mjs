import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

const sharedWithMePage = readFileSync(new URL('../src/pages/SharedWithMePage.tsx', import.meta.url), 'utf8')
const sharedCollectionPage = readFileSync(new URL('../src/pages/SharedCollectionPage.tsx', import.meta.url), 'utf8')

test('分享給我的列表只讀取 shared-with-me 端點，並顯示書冊擁有者', () => {
  assert.match(sharedWithMePage, /\/api\/collections\/shared-with-me/)
  assert.match(sharedWithMePage, /ownerEmail/)
  assert.doesNotMatch(sharedWithMePage, /method:\s*'(POST|PUT|DELETE)'/)
})

test('分享書冊頁只能唯讀瀏覽章節，沒有任何編輯、刪除或朗讀功能', () => {
  assert.match(sharedCollectionPage, /\/api\/collections\/shared-with-me\/\$\{collectionId\}/)
  assert.match(sharedCollectionPage, /\/books\/\$\{bookId\}/)
  for (const forbidden of [
    'csrfToken',
    'NarrationPanel',
    'BookInsightsPanel',
    "method: 'DELETE'",
    "method: 'PUT'",
    "method: 'POST'",
    '刪除',
    '移出書冊',
    '建立 AI 朗讀',
  ]) {
    assert.doesNotMatch(sharedCollectionPage, new RegExp(forbidden.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')))
  }
})

import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

const appSource = readFileSync(new URL('../src/App.tsx', import.meta.url), 'utf8')

function positionOf(marker) {
  const position = appSource.indexOf(marker)
  assert.notEqual(position, -1, `找不到必要的新手介面標記：${marker}`)
  return position
}

test('首頁先提供明確的新手入口，再進入書庫與產品藍圖', () => {
  assert.match(appSource, /開始使用：匯入一本書/)
  assert.match(appSource, /第一次來？照這 3 步就能開始/)

  const quickStart = positionOf('id="quick-start"')
  const library = positionOf('id="library"')
  const pipeline = positionOf('id="pipeline"')

  assert.ok(quickStart < library, '三步教學應放在書庫之前')
  assert.ok(library < pipeline, '可操作的書庫應放在產品藍圖之前')
})

test('首頁清楚區分目前可用功能與仍在開發中的功能', () => {
  assert.match(appSource, /目前可用：匯入書籍、解析章節、閱讀管理/)
  assert.match(appSource, /角色辨識與多聲線演出正在開發中/)
  assert.doesNotMatch(appSource, /Foundation online/)
})

test('直接上傳是推薦主流程，博客來同步收在進階選項', () => {
  const upload = positionOf('id="book-file"')
  const advancedSync = positionOf('進階：同步博客來書櫃書目')

  assert.ok(upload < advancedSync, '直接上傳應比進階書櫃同步更早出現')
  assert.match(appSource, /推薦方式/)
  assert.match(appSource, /只要準備一個無 DRM 的 EPUB 或 UTF-8 TXT/)
})

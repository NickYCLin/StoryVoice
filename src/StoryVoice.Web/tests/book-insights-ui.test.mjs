import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

const app = readFileSync(new URL('../src/BookInsightsPanel.tsx', import.meta.url), 'utf8')

test('linked metadata requires an explicit authorized EPUB or TXT association', () => {
  assert.ok(app.includes('連結你合法持有的正文'))
  assert.ok(app.includes('StoryVoice 不會依書名自動配對，也不會抓取博客來正文'))
  assert.ok(app.includes('candidate.authorizedTextAvailable'))
  assert.ok(app.includes("/content-link`"))
})

test('retired extractive summary has no remaining UI or API request', () => {
  assert.ok(!app.includes('擷取式摘要'))
  assert.ok(!app.includes('/summary`'))
})

test('manual notes work independently from provider text and mutations carry CSRF', () => {
  assert.ok(app.includes('我的閱讀筆記'))
  assert.ok(app.includes('這裡只保存你親自輸入的帳號筆記'))
  assert.ok(app.includes("body: JSON.stringify({ body, chapterId: null })"))
  assert.ok(app.includes("method: 'DELETE'"))
  assert.ok(app.includes("headers: { 'X-CSRF-TOKEN': csrfToken }"))
})

test('local LLM candidates can be checked, merged by canonical name and applied to a series cast', () => {
  assert.ok(app.includes('本機 LLM 角色與 alias 分析'))
  assert.ok(app.includes('Canonical 名稱'))
  assert.ok(app.includes('Aliases（以、分隔）'))
  assert.ok(app.includes('建立／合併系列角色表'))
  assert.ok(app.includes("/character-analysis`"))
  assert.ok(app.includes('/analyzed-characters`'))
  assert.ok(app.includes('handleGenerateCharacterAnalysis'))
  assert.ok(app.includes("method: 'PUT'"))
  assert.ok(app.includes('本機 LLM 正在逐章讀取完整正文'))
  assert.ok(app.includes('完成後立即卸載'))
  assert.ok(app.includes('candidateDrafts[candidate.name]?.selected'))
  assert.ok(app.includes("to={selectedSeriesId ? `/series?seriesId=${selectedSeriesId}` : '/series'}"))
  assert.ok(!app.includes('/character-candidates`'))
  assert.ok(!app.includes('偵測到的說話角色'))
  assert.ok(!app.includes('FirstPersonNarrator'))
})

test('owner-scoped source metadata corrections cover title, author, cover, and reset', () => {
  assert.ok(app.includes('書名、作者與封面校正'))
  assert.ok(app.includes('/metadata-corrections`'))
  assert.ok(app.includes('重新同步也不會覆蓋校正'))
  assert.ok(app.includes('還原來源資料'))
  assert.ok(app.includes("{ 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrfToken }"))
})

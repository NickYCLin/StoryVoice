import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

const review = readFileSync(new URL('../src/SpeechPlanReview.tsx', import.meta.url), 'utf8')

test('劇本審核只以 owner 取得的章節正文切片呈現，並標示低信心／待審核分段', () => {
  assert.match(review, /NeedsReview/)
  assert.match(review, /chapter\.originalText\.slice/)
  assert.match(review, /segment\.confidence/)
  assert.match(review, /確認這段角色/)
})

test('計畫未確認時禁止建立 staged 多角色工作，並顯示缺口數', () => {
  assert.match(review, /confirmedGapCount/)
  assert.match(review, /disabled=\{confirmedGapCount > 0/)
  assert.match(review, /\/narration-rebuilds/)
  assert.match(review, /rightsAttested/)
})

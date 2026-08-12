import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

const panel = readFileSync(new URL('../src/SeriesCastPanel.tsx', import.meta.url), 'utf8')
const narrationPanel = readFileSync(new URL('../src/NarrationPanel.tsx', import.meta.url), 'utf8')
const app = readFileSync(new URL('../src/App.tsx', import.meta.url), 'utf8')
const voiceProfilesPanel = readFileSync(new URL('../src/CharacterVoiceProfilesPanel.tsx', import.meta.url), 'utf8')

test('系列配音控制台固定使用 owner-scoped series、角色與 alias API', () => {
  assert.match(panel, /\/api\/series\/voice-options/)
  assert.match(panel, /\/api\/series\/\$\{details\.id\}\/books/)
  assert.match(panel, /\/api\/series\/\$\{details\.id\}\/characters/)
  assert.match(panel, /\/aliases/)
  assert.match(panel, /固定角色聲線/)
  assert.doesNotMatch(panel, /fuzzy|模糊比對/i)
})

test('staged rebuild 狀態以既有系列成員解析書名，不假設 rebuild API 回傳額外 bookTitle', () => {
  assert.match(panel, /members: Array<\{ id: string; bookId: string; status: string/)
  assert.doesNotMatch(panel, /members: Array<\{ id: string; bookTitle: string/)
  assert.match(panel, /details\.books\.find/)
})

test('書庫朗讀面板不再建立單聲線工作，而是導向系列配音流程', () => {
  assert.match(narrationPanel, /\/series/)
  assert.match(narrationPanel, /多角色系列配音/)
  assert.doesNotMatch(narrationPanel, /function createNarration/)
})

test('App 可開啟系列配音控制台', () => {
  assert.match(app, /path="\/series"/)
  assert.match(app, /SeriesCastPanel/)
})

test('系列配音控制台每個角色都可以展開自訂聲線工作室', () => {
  assert.match(panel, /import { CharacterVoiceProfilesPanel } from '\.\/CharacterVoiceProfilesPanel'/)
  assert.match(panel, /<CharacterVoiceProfilesPanel/)
  assert.match(panel, /自訂聲線/)
})

test('自訂聲線工作室固定使用 owner-scoped voice-profiles API，涵蓋基礎與五種情境聲線', () => {
  assert.match(voiceProfilesPanel, /\/api\/series\/\$\{seriesId\}\/characters\/\$\{characterId\}\/voice-profiles/)
  assert.match(voiceProfilesPanel, /sceneCode: 'neutral'/)
  assert.match(voiceProfilesPanel, /sceneCode: 'nervous'/)
  assert.match(voiceProfilesPanel, /sceneCode: 'happy'/)
  assert.match(voiceProfilesPanel, /sceneCode: 'angry'/)
  assert.match(voiceProfilesPanel, /sceneCode: 'sad'/)
  assert.match(voiceProfilesPanel, /self_recorded/)
  assert.match(voiceProfilesPanel, /explicit_permission/)
  assert.match(voiceProfilesPanel, /licensed_voice/)
  assert.doesNotMatch(voiceProfilesPanel, /fuzzy|模糊比對/i)
})

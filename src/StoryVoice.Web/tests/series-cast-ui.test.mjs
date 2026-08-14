import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

const panel = readFileSync(new URL('../src/SeriesCastPanel.tsx', import.meta.url), 'utf8')
const narrationPanel = readFileSync(new URL('../src/NarrationPanel.tsx', import.meta.url), 'utf8')
const app = readFileSync(new URL('../src/App.tsx', import.meta.url), 'utf8')
const voiceProfilesPanel = readFileSync(new URL('../src/CharacterVoiceProfilesPanel.tsx', import.meta.url), 'utf8')
const characterLibraryPage = readFileSync(new URL('../src/pages/CharacterLibraryPage.tsx', import.meta.url), 'utf8')
const appLayout = readFileSync(new URL('../src/AppLayout.tsx', import.meta.url), 'utf8')

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

test('角色分析套用後可由 query string 直接開啟指定系列', () => {
  assert.match(panel, /useSearchParams/)
  assert.match(panel, /searchParams\.get\('seriesId'\)/)
  assert.match(panel, /item\.id === requestedSeriesId/)
})

test('系列配音控制台每個角色都可以展開自訂聲線工作室', () => {
  assert.match(panel, /import { CharacterVoiceProfilesPanel } from '\.\/CharacterVoiceProfilesPanel'/)
  assert.match(panel, /<CharacterVoiceProfilesPanel/)
  assert.match(panel, /自訂聲線/)
})

test('自訂聲線工作室固定使用 owner-scoped 角色庫 voice-profiles API，涵蓋基礎與四種情境聲線', () => {
  assert.match(voiceProfilesPanel, /\/api\/character-profiles\/\$\{characterProfileId\}\/voice-profiles/)
  assert.doesNotMatch(voiceProfilesPanel, /\/api\/series\/.*\/characters\/.*\/voice-profiles/)
  assert.doesNotMatch(voiceProfilesPanel, /sceneCode: 'neutral'/)
  assert.match(voiceProfilesPanel, /sceneCode: 'nervous'/)
  assert.match(voiceProfilesPanel, /sceneCode: 'happy'/)
  assert.match(voiceProfilesPanel, /sceneCode: 'angry'/)
  assert.match(voiceProfilesPanel, /sceneCode: 'sad'/)
  assert.match(voiceProfilesPanel, /self_recorded/)
  assert.match(voiceProfilesPanel, /explicit_permission/)
  assert.match(voiceProfilesPanel, /licensed_voice/)
  assert.doesNotMatch(voiceProfilesPanel, /fuzzy|模糊比對/i)
})

test('系列角色只有連結角色庫時才顯示自訂聲線，且加入角色時可以選角色庫既有角色', () => {
  assert.match(panel, /character\.characterProfileId &&/)
  assert.match(panel, /characterProfileId={character\.characterProfileId}/)
  assert.match(panel, /\/api\/character-profiles/)
  assert.match(panel, /selectedCharacterProfileId/)
  assert.match(panel, /characterProfileId: usingLibraryCharacter \? selectedCharacterProfileId : null/)
})

test('角色管理頁面走 owner-scoped 角色庫 API，並串接自訂聲線工作室', () => {
  assert.match(characterLibraryPage, /\/api\/character-profiles/)
  assert.match(characterLibraryPage, /<CharacterVoiceProfilesPanel/)
  assert.match(characterLibraryPage, /canonicalName/)
  assert.match(characterLibraryPage, /avatar/)
  assert.doesNotMatch(characterLibraryPage, /fuzzy|模糊比對/i)
})

test('App 與主導覽都能開啟角色管理頁面', () => {
  assert.match(app, /path="characters"/)
  assert.match(app, /CharacterLibraryPage/)
  assert.match(appLayout, /to="\/characters"/)
})

test('聲線卡片顯示字數與時長，並提供即時播放試講', () => {
  assert.match(voiceProfilesPanel, /referenceAudioDurationSeconds/)
  assert.match(voiceProfilesPanel, /字數/)
  assert.match(voiceProfilesPanel, /formatDuration/)
  assert.match(voiceProfilesPanel, /\/preview/)
  assert.match(voiceProfilesPanel, /playPreview/)
  assert.match(voiceProfilesPanel, /new Audio\(url\)/)
})

test('系列配音控制台可以設定視角角色，讓第一人稱自述對白自動歸給主角', () => {
  assert.match(panel, /\/api\/series\/\$\{details\.id\}\/point-of-view-character/)
  assert.match(panel, /視角角色（第一人稱敘事者）/)
  assert.match(panel, /pointOfViewCharacterId/)
  assert.match(panel, /method: 'PUT'/)
})

test('角色管理頁面提供啟用狀態切換、摘要卡片、重設按鈕、試講與任務紀錄分頁', () => {
  assert.match(characterLibraryPage, /isActive/)
  assert.match(characterLibraryPage, /'activate'/)
  assert.match(characterLibraryPage, /'deactivate'/)
  assert.match(characterLibraryPage, /基礎聲線狀態/)
  assert.match(characterLibraryPage, /情境聲線數量/)
  assert.match(characterLibraryPage, /樣本語料時數/)
  assert.match(characterLibraryPage, /最近任務狀態/)
  assert.match(characterLibraryPage, /resetForm/)
  assert.match(characterLibraryPage, /'preview'/)
  assert.match(characterLibraryPage, /'tasks'/)
  assert.match(characterLibraryPage, /copyId/)
  assert.match(characterLibraryPage, /AI 輔助生成尚未提供/)
})

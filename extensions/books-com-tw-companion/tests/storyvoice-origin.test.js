const test = require('node:test')
const assert = require('node:assert/strict')

test('accepts the exact local StoryVoice origins', async () => {
  const { validateStoryVoiceOrigin } = await import('../storyvoice-origin.mjs')

  assert.equal(validateStoryVoiceOrigin('http://localhost:3000'), 'http://localhost:3000')
  assert.equal(validateStoryVoiceOrigin('http://127.0.0.1:3000/'), 'http://127.0.0.1:3000')
})

test('accepts the exact future HTTPS StoryVoice subpath', async () => {
  const { validateStoryVoiceOrigin } = await import('../storyvoice-origin.mjs')

  assert.equal(
    validateStoryVoiceOrigin('https://aiprod.wrbtycg.tw/StoryVoice/'),
    'https://aiprod.wrbtycg.tw/StoryVoice'
  )
})

test('rejects other hosts, paths, protocols, ports, and credentials', async () => {
  const { validateStoryVoiceOrigin } = await import('../storyvoice-origin.mjs')
  const rejected = [
    'https://example.com/StoryVoice',
    'http://aiprod.wrbtycg.tw/StoryVoice',
    'https://aiprod.wrbtycg.tw/storyvoice',
    'https://aiprod.wrbtycg.tw/StoryVoice/extra',
    'https://aiprod.wrbtycg.tw:444/StoryVoice',
    'https://user:password@aiprod.wrbtycg.tw/StoryVoice',
    'http://localhost:3000/StoryVoice',
    'http://localhost:3001'
  ]

  for (const value of rejected) {
    assert.throws(() => validateStoryVoiceOrigin(value), /StoryVoice/)
  }
})
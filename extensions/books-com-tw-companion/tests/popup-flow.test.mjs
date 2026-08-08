import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import vm from 'node:vm'

const popupSource = readFileSync(new URL('../popup.js', import.meta.url), 'utf8')
const popupHtml = readFileSync(new URL('../popup.html', import.meta.url), 'utf8')

function response(status, body) {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
  }
}

function createHarness({ responses, openLibrary = async () => ({}) }) {
  const events = []
  const statuses = []
  let selectedIds = []
  const elements = new Map()

  const element = (selector) => {
    if (!elements.has(selector)) {
      elements.set(selector, {
        value: '',
        textContent: '',
        className: '',
        disabled: false,
        addEventListener() {},
        append() {},
        replaceChildren() {},
        querySelectorAll() {
          return selectedIds.map((value) => ({ value }))
        },
      })
    }
    return elements.get(selector)
  }

  const status = element('#status')
  Object.defineProperty(status, 'textContent', {
    get() {
      return this.currentText ?? ''
    },
    set(value) {
      this.currentText = value
      statuses.push({ message: value, kind: this.className })
    },
  })
  element('#storyvoice-origin').value = 'https://aiprod.wrbtycg.tw/StoryVoice'
  element('#storyvoice-access-token').value = `svcp_${'a'.repeat(64)}`

  const pendingInitialization = new Promise(() => {})
  const context = {
    console,
    document: {
      querySelector: element,
      createElement: () => element(Symbol()),
    },
    validateStoryVoiceOrigin: (value) => value,
    validateCompanionToken: (value) => value,
    fetch: async () => {
      events.push('fetch')
      const next = responses.shift()
      assert.ok(next, 'unexpected sync request')
      return next
    },
    chrome: {
      storage: {
        local: {
          set: async () => {},
          get: () => pendingInitialization,
        },
      },
      tabs: {
        query: async () => [],
        sendMessage: async () => ({ ok: false }),
        create: async (options) => {
          events.push('open')
          return openLibrary(options)
        },
      },
    },
  }

  const executableSource = popupSource
    .replace(/^import .*$/gm, '')
    .concat('\nglobalThis.__syncBooks = syncBooks; globalThis.__state = state;')
  vm.runInNewContext(executableSource, context, { filename: 'popup.js' })

  return {
    events,
    statuses,
    getStatus() {
      return { message: status.textContent, kind: status.className }
    },
    setBooks(books) {
      context.__state.books = books
      selectedIds = books.map((book) => book.externalId)
    },
    sync: () => context.__syncBooks(),
  }
}

const books = (count) => Array.from({ length: count }, (_, index) => ({
  externalId: `book-${index + 1}`,
  title: `Book ${index + 1}`,
  author: 'Author',
}))

test('所有同步批次成功後才開啟 StoryVoice 書庫', async () => {
  const harness = createHarness({
    responses: [
      response(200, { createdCount: 200, updatedCount: 0 }),
      response(200, { createdCount: 1, updatedCount: 0 }),
    ],
    openLibrary: async ({ url }) => {
      assert.equal(url, 'https://aiprod.wrbtycg.tw/StoryVoice/#library')
    },
  })
  harness.setBooks(books(201))

  await harness.sync()

  assert.deepEqual(harness.events, ['fetch', 'fetch', 'open'])
  assert.match(harness.getStatus().message, /同步完成：新增 201 本/)
  assert.match(harness.getStatus().kind, /success/)
})

test('任一同步批次失敗時不開啟 StoryVoice 書庫', async () => {
  const harness = createHarness({
    responses: [
      response(200, { createdCount: 200, updatedCount: 0 }),
      response(500, { detail: 'second batch failed' }),
    ],
  })
  harness.setBooks(books(201))

  await harness.sync()

  assert.deepEqual(harness.events, ['fetch', 'fetch'])
  assert.equal(harness.getStatus().message, 'second batch failed')
  assert.match(harness.getStatus().kind, /error/)
})

test('書庫分頁開啟失敗不會把已完成的同步改判為失敗', async () => {
  const harness = createHarness({
    responses: [response(200, { createdCount: 1, updatedCount: 0 })],
    openLibrary: async () => {
      throw new Error('tab unavailable')
    },
  })
  harness.setBooks(books(1))

  await harness.sync()

  assert.deepEqual(harness.events, ['fetch', 'open'])
  assert.match(harness.getStatus().message, /同步完成：新增 1 本.*請回 StoryVoice/)
  assert.match(harness.getStatus().kind, /success/)
  assert.match(popupHtml, /同步成功後會自動開啟 StoryVoice 書庫/)
})

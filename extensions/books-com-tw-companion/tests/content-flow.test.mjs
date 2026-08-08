import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'
import vm from 'node:vm'

const contentSource = await readFile(new URL('../content.js', import.meta.url), 'utf8')

function buildRuntime() {
  const attributes = new Map()
  const windowListeners = new Map()
  let messageHandler = null
  let extractCandidatesCalls = 0

  const root = {
    getAttribute: (name) => attributes.get(name) ?? null,
    setAttribute: (name, value) => attributes.set(name, value),
    removeAttribute: (name) => attributes.delete(name),
  }
  const windowObject = {
    scrollY: 0,
    innerHeight: 800,
    addEventListener(type, handler) {
      const handlers = windowListeners.get(type) ?? []
      handlers.push(handler)
      windowListeners.set(type, handlers)
    },
    removeEventListener(type, handler) {
      windowListeners.set(type, (windowListeners.get(type) ?? []).filter((value) => value !== handler))
    },
    scrollTo() {},
  }
  const documentObject = {
    documentElement: root,
    body: { scrollHeight: 1000 },
    defaultView: {},
    querySelectorAll: () => [],
    dispatchEvent(event) {
      assert.equal(event.type, 'storyvoice:books-com-tw:extract-request')
      const nonce = attributes.get('data-storyvoice-books-request')
      const handlers = windowListeners.get('message') ?? []
      for (const handler of handlers) {
        handler({ source: windowObject, origin: 'https://evil.example', data: { source: 'storyvoice-books-com-tw-page-bridge', nonce, books: [{ externalId: 'unsafe' }] } })
        handler({
          source: windowObject,
          origin: 'https://viewer-ebook.books.com.tw',
          data: {
            source: 'storyvoice-books-com-tw-page-bridge',
            nonce,
            books: [{ externalId: 'E050029958_reflowable_normal', title: '已驗證書籍' }],
          },
        })
      }
    },
  }
  const extractor = {
    extractBooks: () => [],
    extractFromCandidates(candidates) {
      extractCandidatesCalls += 1
      return candidates.map((candidate) => ({ ...candidate, normalized: true }))
    },
    crawlShelf: async () => ({ books: [], rounds: 0, truncated: false }),
  }
  const context = {
    chrome: { runtime: { onMessage: { addListener(handler) { messageHandler = handler } } } },
    crypto: { randomUUID: () => 'test-nonce' },
    document: documentObject,
    Event: class Event { constructor(type) { this.type = type } },
    globalThis: { StoryVoiceBooksComTwExtractor: extractor, crypto: { randomUUID: () => 'test-nonce' } },
    location: { href: 'https://viewer-ebook.books.com.tw/viewer/index.html?readlist=all', origin: 'https://viewer-ebook.books.com.tw' },
    Math,
    MutationObserver: class MutationObserver {},
    setTimeout,
    clearTimeout,
    window: windowObject,
  }
  context.globalThis.globalThis = context.globalThis
  vm.runInNewContext(contentSource, context, { filename: 'content.js' })
  return { getHandler: () => messageHandler, getExtractCandidatesCalls: () => extractCandidatesCalls, root }
}

test('scan uses the MAIN-world bridge, rejects a wrong-origin response, and normalizes candidates', async () => {
  const runtime = buildRuntime()
  const handler = runtime.getHandler()
  assert.equal(typeof handler, 'function')

  const response = await new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error('scan response timeout')), 100)
    const asyncResponse = handler({ type: 'storyvoice:scan-books-com-tw-shelf' }, {}, (value) => {
      clearTimeout(timeout)
      resolve(value)
    })
    assert.equal(asyncResponse, true)
  })

  assert.equal(response.ok, true)
  assert.equal(response.books.length, 1)
  assert.equal(response.books[0].externalId, 'E050029958_reflowable_normal')
  assert.equal(response.books[0].normalized, true)
  assert.equal(runtime.getExtractCandidatesCalls(), 1)
  assert.equal(runtime.root.getAttribute('data-storyvoice-books-request'), null)
})

const test = require('node:test')
const assert = require('node:assert/strict')

function node(textContent = '', extras = {}) {
  return { textContent, ...extras }
}

function card({ externalId, title, author, cover, meta }) {
  const globalRef = { current: null }
  const link = {
    click() {
      globalRef.current.open(`https://viewer-ebook.books.com.tw/viewer/epub_v3/?book_uni_id=${externalId}&ran=provider-secret`)
    }
  }
  const image = { currentSrc: cover, src: cover, getAttribute: () => cover }
  const values = {
    '.book__cover a': link,
    '.book__cover img': image,
    '.book__description__title, .book__title': node(title),
    '.book__description__author, [class*="author"]': node(author),
    '.book__description__meta': node(meta)
  }
  return {
    globalRef,
    textContent: `${title} ${author} ${meta}`,
    querySelector(selector) { return values[selector] ?? null }
  }
}

test('captures current Books shelf cards through official reader clicks without opening tabs', () => {
  const normal = card({
    externalId: 'E050029958_reflowable_normal',
    title: '第一本書',
    author: '作者甲',
    cover: 'https://s3public-ebook.books.com.tw/cover/AA/1/E050029958.jpg',
    meta: 'EPUB 流動版型 TTS 語音朗讀功能：支援'
  })
  const trial = card({
    externalId: 'E050000001_reflowable_trial',
    title: '試閱書',
    author: '作者乙',
    cover: 'https://s3public-ebook.books.com.tw/cover/BB/2/E050000001.jpg',
    meta: '試閱 EPUB 流動版型 TTS 語音朗讀功能：不支援'
  })
  const cards = [normal, trial]
  let realOpenCalls = 0
  const globalObject = {
    open() { realOpenCalls += 1 },
    location: { href: 'https://viewer-ebook.books.com.tw/viewer/index.html?readlist=all' }
  }
  cards.forEach((value) => { value.globalRef.current = globalObject })
  const documentObject = {
    documentElement: { lang: 'zh-TW' },
    querySelectorAll(selector) {
      assert.equal(selector, '.bookshelf__book')
      return cards
    }
  }

  const bridge = require('../page-bridge.js')
  const books = bridge.captureBooks(documentObject, globalObject)

  assert.equal(realOpenCalls, 0)
  assert.equal(books.length, 2)
  assert.deepEqual(books[0], {
    externalId: 'E050029958_reflowable_normal',
    title: '第一本書',
    author: '作者甲',
    language: 'zh-TW',
    sourceUrl: 'https://viewer-ebook.books.com.tw/viewer/epub_v3/?book_uni_id=E050029958_reflowable_normal&ran=provider-secret',
    coverImageUrl: 'https://s3public-ebook.books.com.tw/cover/AA/1/E050029958.jpg',
    nativeTtsAvailable: true,
    ebookLayout: 'Reflowable'
  })
  assert.equal(books[1].externalId, 'E050000001_reflowable_trial')
  assert.equal(books[1].nativeTtsAvailable, false)
})

test('skips cards that do not yield an official matching reader URL', () => {
  const globalObject = {
    open() {},
    location: { href: 'https://viewer-ebook.books.com.tw/viewer/' }
  }
  const unsafeCard = {
    textContent: '不安全卡片',
    querySelector(selector) {
      if (selector === '.book__cover a') return { click: () => globalObject.open('https://example.com/steal?id=bad') }
      if (selector === '.book__description__title, .book__title') return node('不安全卡片')
      return null
    }
  }
  const bridge = require('../page-bridge.js')
  assert.deepEqual(bridge.captureBooks({ documentElement: { lang: 'zh-TW' }, querySelectorAll: () => [unsafeCard] }, globalObject), [])
})

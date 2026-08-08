const test = require('node:test')
const assert = require('node:assert/strict')
const extractor = require('../extractor.js')

test('extracts stable ids from viewer and product links', () => {
  assert.equal(
    extractor.externalIdFromUrl('https://viewer-ebook.books.com.tw/viewer/epub_v3/?book_uni_id=E050145360'),
    'E050145360'
  )
  assert.equal(
    extractor.externalIdFromUrl('https://www.books.com.tw/products/E050145361'),
    'E050145361'
  )
})

test('normalizes official metadata and accepts the official cover asset domain', () => {
  const book = extractor.normalizeCandidate({
    externalId: ' E050145360 ',
    title: '  月下的故事  ',
    author: '  比比工程師 ',
    language: 'zh-TW',
    sourceUrl: 'https://viewer-ebook.books.com.tw/viewer/epub_v3/?book_uni_id=E050145360&access_token=provider-secret#chapter',
    coverImageUrl: 'https://im1.book.com.tw/image/getImage?i=E050145360&signature=provider-secret',
    nativeTtsAvailable: true,
    ebookLayout: 'Reflowable'
  })

  assert.deepEqual(book, {
    externalId: 'E050145360',
    title: '月下的故事',
    author: '比比工程師',
    language: 'zh-TW',
    sourceUrl: 'https://viewer-ebook.books.com.tw/viewer/epub_v3/?book_uni_id=E050145360',
    coverImageUrl: 'https://im1.book.com.tw/image/getImage?i=E050145360',
    nativeTtsAvailable: true,
    ebookLayout: 'Reflowable'
  })
})

test('keeps title and author copy out of official capability inference', () => {
  const container = {
    dataset: {
      bookUniId: 'E050145365',
      title: 'TTS 語音朗讀功能大全',
      author: '固定版型研究者'
    },
    textContent: 'TTS 語音朗讀功能大全 固定版型研究者',
    ownerDocument: { documentElement: { lang: 'zh-TW' } },
    querySelector: () => null,
    querySelectorAll: () => []
  }
  const link = {
    href: 'https://www.books.com.tw/products/E050145365',
    parentElement: container,
    closest: () => container,
    getAttribute: () => null
  }
  const documentObject = {
    baseURI: 'https://viewer-ebook.books.com.tw/viewer/index.html?readlist=all',
    querySelector: () => null,
    querySelectorAll: () => [link]
  }

  const [book] = extractor.extractBooks(documentObject)

  assert.equal(book.nativeTtsAvailable, null)
  assert.equal(book.ebookLayout, null)

  container.querySelectorAll = () => [
    {
      textContent: 'TTS 語音朗讀功能：支援',
      dataset: {},
      getAttribute: () => null
    },
    {
      textContent: 'EPUB 固定版型',
      dataset: {},
      getAttribute: () => null
    }
  ]
  const [markedBook] = extractor.extractBooks(documentObject)

  assert.equal(markedBook.nativeTtsAvailable, true)
  assert.equal(markedBook.ebookLayout, 'Fixed')

  container.querySelectorAll = () => [
    {
      textContent: '',
      dataset: { nativeTtsAvailable: 'false' },
      getAttribute: () => null
    },
    {
      textContent: '',
      dataset: { ebookLayout: 'Reflowable' },
      getAttribute: () => null
    }
  ]
  const [attributeBook] = extractor.extractBooks(documentObject)

  assert.equal(attributeBook.nativeTtsAvailable, false)
  assert.equal(attributeBook.ebookLayout, 'Reflowable')
})

test('keeps official TTS metadata tri-state and rejects unknown layout labels', () => {
  const unavailable = extractor.normalizeCandidate({
    externalId: 'E050145363',
    title: '固定版型書籍',
    sourceUrl: 'https://www.books.com.tw/products/E050145363',
    nativeTtsAvailable: false,
    ebookLayout: 'Fixed'
  })
  const unknown = extractor.normalizeCandidate({
    externalId: 'E050145364',
    title: '未標示版型書籍',
    sourceUrl: 'https://www.books.com.tw/products/E050145364',
    nativeTtsAvailable: 'maybe',
    ebookLayout: 'PDF'
  })

  assert.equal(unavailable.nativeTtsAvailable, false)
  assert.equal(unavailable.ebookLayout, 'Fixed')
  assert.equal(unknown.nativeTtsAvailable, null)
  assert.equal(unknown.ebookLayout, null)
  assert.equal(extractor.parseNativeTtsAvailable(null, 'TTS 語音朗讀無障礙體驗'), null)
  assert.equal(extractor.parseNativeTtsAvailable(null, 'TTS 語音朗讀功能'), true)
})

test('rejects non-Books source links and drops non-Books cover links', () => {
  assert.equal(extractor.normalizeCandidate({
    externalId: 'unsafe',
    title: '不安全來源',
    sourceUrl: 'https://example.com/books/unsafe'
  }), null)

  const book = extractor.normalizeCandidate({
    externalId: 'E050145362',
    title: '安全來源',
    sourceUrl: 'https://www.books.com.tw/products/E050145362',
    coverImageUrl: 'https://tracker.example.com/pixel.png'
  })
  assert.equal(book.coverImageUrl, null)
})

test('rejects source links that do not identify the same external book', () => {
  assert.equal(extractor.normalizeCandidate({
    externalId: 'E050145362',
    title: '識別不一致',
    sourceUrl: 'https://www.books.com.tw/products/E050145399'
  }), null)
  assert.equal(extractor.normalizeCandidate({
    externalId: 'E050145362',
    title: '閱讀器識別不一致',
    sourceUrl: 'https://viewer-ebook.books.com.tw/viewer/epub_v3/?book_uni_id=E050145399'
  }), null)
  assert.equal(extractor.normalizeCandidate({
    externalId: 'E050145362',
    title: '閱讀器重複識別衝突',
    sourceUrl: 'https://viewer-ebook.books.com.tw/viewer/epub_v3/?book_uni_id=E050145362&id=E050145399'
  }), null)

  const container = {
    dataset: { bookUniId: 'E050145362', title: '卡片識別衝突' },
    ownerDocument: { documentElement: { lang: 'zh-TW' } },
    querySelector: () => null,
    querySelectorAll: () => []
  }
  const link = {
    href: 'https://viewer-ebook.books.com.tw/viewer/epub_v3/?book_uni_id=E050145399',
    parentElement: container,
    closest: () => container,
    getAttribute: () => null
  }
  assert.deepEqual(extractor.extractBooks({
    baseURI: 'https://viewer-ebook.books.com.tw/viewer/index.html?readlist=all',
    querySelector: () => null,
    querySelectorAll: () => [link]
  }), [])

  assert.equal(extractor.normalizeCandidate({
    externalId: 'E050145362',
    title: '不是書籍入口',
    sourceUrl: 'https://www.books.com.tw/web/sys_qalist/qa_1_80'
  }), null)
})

test('deduplicates the currently visible shelf by external id and keeps latest metadata', () => {
  const books = extractor.extractFromCandidates([
    {
      externalId: 'E050145360',
      title: '舊書名',
      sourceUrl: 'https://www.books.com.tw/products/E050145360'
    },
    {
      externalId: 'E050145360',
      title: '新書名',
      author: '新版作者',
      sourceUrl: 'https://viewer-ebook.books.com.tw/viewer/epub_v3/?book_uni_id=E050145360'
    }
  ])

  assert.equal(books.length, 1)
  assert.equal(books[0].title, '新書名')
  assert.equal(books[0].author, '新版作者')
})

test('explicit full-shelf crawl merges virtualized cards and stops when no more content can be revealed', async () => {
  const pages = [
    [{ externalId: 'A', title: '第一本', sourceUrl: 'https://www.books.com.tw/products/A' }],
    [{ externalId: 'B', title: '第二本', sourceUrl: 'https://www.books.com.tw/products/B' }]
  ]
  let page = 0

  const result = await extractor.crawlShelf({
    readBooks: () => pages[page],
    revealMore: () => {
      if (page >= pages.length - 1) return false
      page += 1
      return true
    },
    waitForUpdate: async () => {},
    maxBooks: 10,
    maxRounds: 10
  })

  assert.deepEqual(result.books.map((book) => book.externalId), ['A', 'B'])
  assert.equal(result.rounds, 1)
  assert.equal(result.truncated, false)
})

test('full-shelf crawl enforces a local book cap and reports truncation', async () => {
  const result = await extractor.crawlShelf({
    readBooks: () => [
      { externalId: 'A', title: '第一本', sourceUrl: 'https://www.books.com.tw/products/A' },
      { externalId: 'B', title: '第二本', sourceUrl: 'https://www.books.com.tw/products/B' },
      { externalId: 'C', title: '第三本', sourceUrl: 'https://www.books.com.tw/products/C' }
    ],
    revealMore: () => false,
    waitForUpdate: async () => {},
    maxBooks: 2,
    maxRounds: 10
  })

  assert.deepEqual(result.books.map((book) => book.externalId), ['A', 'B'])
  assert.equal(result.truncated, true)
})

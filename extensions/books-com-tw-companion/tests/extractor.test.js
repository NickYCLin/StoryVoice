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

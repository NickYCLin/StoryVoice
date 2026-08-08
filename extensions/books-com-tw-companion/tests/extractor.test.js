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
    sourceUrl: 'https://viewer-ebook.books.com.tw/viewer/epub_v3/?book_uni_id=E050145360#chapter',
    coverImageUrl: 'https://im1.book.com.tw/image/getImage?i=E050145360'
  })

  assert.deepEqual(book, {
    externalId: 'E050145360',
    title: '月下的故事',
    author: '比比工程師',
    language: 'zh-TW',
    sourceUrl: 'https://viewer-ebook.books.com.tw/viewer/epub_v3/?book_uni_id=E050145360',
    coverImageUrl: 'https://im1.book.com.tw/image/getImage?i=E050145360'
  })
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

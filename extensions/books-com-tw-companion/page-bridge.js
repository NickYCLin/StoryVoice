(function attachBooksComTwPageBridge(global) {
  const requestEventName = 'storyvoice:books-com-tw:extract-request'
  const responseSource = 'storyvoice-books-com-tw-page-bridge'
  const viewerHost = 'viewer-ebook.books.com.tw'

  function normalizeText(value) {
    return String(value ?? '').replace(/\s+/g, ' ').trim()
  }

  function parseReaderUrl(value, baseUrl) {
    try {
      const url = new URL(value, baseUrl)
      if (url.protocol !== 'https:' || url.hostname !== viewerHost ||
          !url.pathname.startsWith('/viewer/') || url.username || url.password) {
        return null
      }
      const externalId = normalizeText(url.searchParams.get('book_uni_id'))
      if (!/^[A-Za-z0-9._:-]{1,128}$/.test(externalId)) return null
      return { externalId, sourceUrl: url.href }
    } catch {
      return null
    }
  }

  function parseNativeTtsAvailable(text) {
    if (/TTS\s*語音朗讀(?:功能)?\s*[:：]?\s*(?:不支援|不可使用|否|未開放|未提供|無法使用|沒有)/i.test(text)) {
      return false
    }
    if (/TTS\s*語音朗讀(?:功能)?\s*[:：]?\s*(?:支援|有此功能|可使用|是|已開放|提供)/i.test(text) ||
        /TTS\s*語音朗讀功能/i.test(text)) {
      return true
    }
    return null
  }

  function parseEbookLayout(text) {
    if (/EPUB\s*流動版型|流動版型/i.test(text)) return 'Reflowable'
    if (/EPUB\s*固定版型|固定版型/i.test(text)) return 'Fixed'
    return null
  }

  function captureBooks(documentObject = global.document, globalObject = global) {
    if (!documentObject?.querySelectorAll || typeof globalObject?.open !== 'function') return []
    const originalOpen = globalObject.open
    const books = []
    let capturedUrl = null

    try {
      globalObject.open = (url) => {
        capturedUrl = typeof url === 'string' || url instanceof URL ? String(url) : null
        return { focus() {}, close() {} }
      }

      if (globalObject.open === originalOpen) return []

      for (const card of documentObject.querySelectorAll('.bookshelf__book')) {
        const link = card.querySelector?.('.book__cover a')
        if (!link || typeof link.click !== 'function') continue
        if (normalizeText(link.getAttribute?.('href'))) continue

        capturedUrl = null
        try {
          link.click()
        } catch {
          continue
        }

        const reader = parseReaderUrl(capturedUrl, globalObject.location?.href)
        if (!reader) continue
        const image = card.querySelector?.('.book__cover img')
        const titleNode = card.querySelector?.('.book__description__title, .book__title')
        const authorNode = card.querySelector?.('.book__description__author, [class*="author"]')
        const metadataText = normalizeText(
          card.querySelector?.('.book__description__meta')?.textContent ?? card.textContent
        )
        const title = normalizeText(titleNode?.textContent)
        if (!title) continue

        books.push({
          externalId: reader.externalId,
          title,
          author: normalizeText(authorNode?.textContent) || '未知作者',
          language: documentObject.documentElement?.lang || 'zh-TW',
          sourceUrl: reader.sourceUrl,
          coverImageUrl: image?.currentSrc || image?.getAttribute?.('src') || image?.src || null,
          nativeTtsAvailable: parseNativeTtsAvailable(metadataText),
          ebookLayout: parseEbookLayout(metadataText)
        })
      }
    } catch {
      return []
    } finally {
      try {
        globalObject.open = originalOpen
      } catch {
        // Stay fail-closed if the page changes the property descriptor during capture.
      }
    }

    return books
  }

  const api = { captureBooks, parseEbookLayout, parseNativeTtsAvailable, parseReaderUrl }
  global.StoryVoiceBooksComTwPageBridge = api
  if (!global.document && typeof module !== 'undefined' && module.exports) module.exports = api

  if (global.document?.addEventListener && typeof global.postMessage === 'function') {
    global.document.addEventListener(requestEventName, () => {
      const nonce = global.document.documentElement?.getAttribute('data-storyvoice-books-request')
      if (!nonce) return
      global.postMessage({
        source: responseSource,
        nonce,
        books: captureBooks(global.document, global)
      }, global.location.origin)
    })
  }
})(globalThis)

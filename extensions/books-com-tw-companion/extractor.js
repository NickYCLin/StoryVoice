(function attachBooksComTwExtractor(global) {
  const sourceHosts = ['books.com.tw']
  const coverHosts = ['books.com.tw', 'book.com.tw']
  const ignoredTitles = new Set(['閱讀', '立即閱讀', '開啟', '試閱', '看更多'])

  function normalizeText(value) {
    return String(value ?? '').replace(/\s+/g, ' ').trim()
  }

  function isAllowedHost(hostname, roots) {
    const host = hostname.toLowerCase()
    return roots.some((root) => host === root || host.endsWith(`.${root}`))
  }

  function safeUrl(value, baseUrl, roots) {
    if (!value) return null
    try {
      const url = new URL(value, baseUrl)
      if (url.protocol !== 'https:' || url.username || url.password || !isAllowedHost(url.hostname, roots)) {
        return null
      }
      url.hash = ''
      return url.href
    } catch {
      return null
    }
  }

  function externalIdFromUrl(sourceUrl, element) {
    const dataId = element?.dataset?.bookUniId ?? element?.dataset?.bookId ?? element?.dataset?.productId
    if (dataId) return normalizeText(dataId)

    try {
      const url = new URL(sourceUrl)
      for (const key of ['book_uni_id', 'bookUniId', 'book_id', 'product_id', 'id']) {
        const value = normalizeText(url.searchParams.get(key))
        if (value) return value
      }
      const productMatch = url.pathname.match(/\/products\/([A-Za-z0-9._:-]+)/i)
      return productMatch?.[1] ?? null
    } catch {
      return null
    }
  }

  function firstMeaningfulText(values) {
    for (const value of values) {
      const text = normalizeText(value)
      if (text && !ignoredTitles.has(text)) return text
    }
    return null
  }

  function candidateFromLink(link, baseUrl) {
    const container = link.closest(
      '[data-book-uni-id], [data-book-id], [data-product-id], article, li, [class*="book-item"], [class*="book__item"], [class*="bookItem"]'
    ) ?? link.parentElement
    const sourceUrl = safeUrl(link.href, baseUrl, sourceHosts)
    if (!sourceUrl || !container) return null

    const externalId = externalIdFromUrl(sourceUrl, container) ?? externalIdFromUrl(sourceUrl, link)
    if (!externalId) return null

    const image = container.querySelector('img')
    const titleNode = container.querySelector('[data-title], [class*="title"], h2, h3, h4')
    const authorNode = container.querySelector('[data-author], [class*="author"]')
    const title = firstMeaningfulText([
      container.dataset?.title,
      titleNode?.textContent,
      link.getAttribute('title'),
      image?.getAttribute('alt')
    ])
    if (!title) return null

    return {
      externalId,
      title,
      author: firstMeaningfulText([container.dataset?.author, authorNode?.textContent]) ?? '未知作者',
      language: container.ownerDocument?.documentElement?.lang || 'zh-TW',
      sourceUrl,
      coverImageUrl: safeUrl(
        image?.currentSrc || image?.getAttribute('src') || image?.dataset?.src,
        baseUrl,
        coverHosts
      )
    }
  }

  function collectCandidates(documentObject) {
    const root = documentObject.querySelector('.bookshelf__main') ?? documentObject
    const links = root.querySelectorAll('a[href]')
    return Array.from(links, (link) => candidateFromLink(link, documentObject.baseURI)).filter(Boolean)
  }

  function normalizeCandidate(candidate) {
    const externalId = normalizeText(candidate?.externalId)
    const title = normalizeText(candidate?.title)
    const sourceUrl = safeUrl(candidate?.sourceUrl, 'https://viewer-ebook.books.com.tw/', sourceHosts)
    if (!externalId || !/^[A-Za-z0-9._:-]{1,128}$/.test(externalId) || !title || !sourceUrl) return null

    return {
      externalId,
      title: title.slice(0, 500),
      author: normalizeText(candidate.author).slice(0, 300) || '未知作者',
      language: normalizeText(candidate.language).slice(0, 20) || 'zh-TW',
      sourceUrl,
      coverImageUrl: safeUrl(candidate.coverImageUrl, sourceUrl, coverHosts)
    }
  }

  function extractFromCandidates(candidates) {
    const byId = new Map()
    for (const candidate of candidates) {
      const normalized = normalizeCandidate(candidate)
      if (normalized) byId.set(normalized.externalId, normalized)
    }
    return Array.from(byId.values())
  }

  async function crawlShelf({
    readBooks,
    revealMore,
    waitForUpdate,
    maxBooks = 500,
    maxRounds = 30
  }) {
    const bookLimit = Math.max(1, Math.min(Number(maxBooks) || 500, 500))
    const roundLimit = Math.max(1, Math.min(Number(maxRounds) || 30, 30))
    let books = extractFromCandidates(await readBooks())
    let rounds = 0
    let truncated = books.length > bookLimit

    while (!truncated && rounds < roundLimit) {
      const revealed = await revealMore({ rounds, bookCount: books.length })
      if (!revealed) break

      rounds += 1
      await waitForUpdate({ rounds, bookCount: books.length })
      books = extractFromCandidates([...books, ...await readBooks()])
      truncated = books.length > bookLimit
    }

    if (!truncated && rounds === roundLimit) truncated = true
    return { books: books.slice(0, bookLimit), rounds, truncated }
  }

  function extractBooks(documentObject = document) {
    return extractFromCandidates(collectCandidates(documentObject))
  }

  const api = { crawlShelf, extractBooks, extractFromCandidates, externalIdFromUrl, normalizeCandidate }
  global.StoryVoiceBooksComTwExtractor = api
  if (typeof module !== 'undefined' && module.exports) module.exports = api
})(globalThis)

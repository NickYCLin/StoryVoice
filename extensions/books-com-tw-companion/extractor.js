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

  function safeUrl(value, baseUrl, roots, canonicalQuery = null) {
    if (!value) return null
    try {
      const url = new URL(value, baseUrl)
      if (url.protocol !== 'https:' || url.username || url.password || !isAllowedHost(url.hostname, roots)) {
        return null
      }
      url.hash = ''
      url.search = ''
      const canonicalHostMatches = canonicalQuery?.host === url.hostname.toLowerCase() ||
        (canonicalQuery?.hostRoot && isAllowedHost(url.hostname, [canonicalQuery.hostRoot]))
      if (canonicalQuery?.key && canonicalQuery?.value && canonicalHostMatches &&
          url.pathname.startsWith(canonicalQuery.pathPrefix)) {
        url.searchParams.set(canonicalQuery.key, canonicalQuery.value)
      }
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

  function sourceUrlMatchesExternalId(value, externalId) {
    try {
      const url = new URL(value, 'https://viewer-ebook.books.com.tw/')
      if (url.protocol !== 'https:' || url.username || url.password || !isAllowedHost(url.hostname, sourceHosts)) {
        return false
      }

      if (url.hostname.toLowerCase() === 'viewer-ebook.books.com.tw' &&
          url.pathname.startsWith('/viewer/')) {
        const identityKeys = ['book_uni_id', 'bookUniId', 'book_id', 'product_id', 'id']
        const linkedIds = []
        for (const [key, linkedId] of url.searchParams.entries()) {
          if (identityKeys.some((identityKey) => identityKey.toLowerCase() === key.toLowerCase())) {
            linkedIds.push(linkedId)
          }
        }
        return linkedIds.every((linkedId) => linkedId.toLowerCase() === externalId.toLowerCase())
      }

      const isProductHost = ['books.com.tw', 'www.books.com.tw'].includes(url.hostname.toLowerCase())
      const pathSegments = url.pathname.split('/').filter(Boolean)
      return isProductHost && pathSegments.length === 2 &&
        pathSegments[0].toLowerCase() === 'products' &&
        pathSegments[1].toLowerCase() === externalId.toLowerCase()
    } catch {
      return false
    }
  }

  function firstMeaningfulText(values) {
    for (const value of values) {
      const text = normalizeText(value)
      if (text && !ignoredTitles.has(text)) return text
    }
    return null
  }

  function capabilityMarkerText(container) {
    const nodes = container.querySelectorAll(
      '[data-native-tts-available], [data-tts-available], [data-ebook-layout], [class~="tts-capability"], [class~="ebook-layout"], [class~="book-format"]'
    )
    const markers = []
    for (const node of nodes) {
      markers.push(node.textContent, node.getAttribute?.('aria-label'), node.getAttribute?.('title'))
      const ttsValue = node.dataset?.nativeTtsAvailable ?? node.dataset?.ttsAvailable
      const parsedTts = parseNativeTtsAvailable(ttsValue)
      if (parsedTts === true) markers.push('TTS 語音朗讀功能：支援')
      if (parsedTts === false) markers.push('TTS 語音朗讀功能：不支援')
      const parsedLayout = parseEbookLayout(node.dataset?.ebookLayout)
      if (parsedLayout === 'Reflowable') markers.push('EPUB 流動版型')
      if (parsedLayout === 'Fixed') markers.push('EPUB 固定版型')
    }
    return markers
      .map(normalizeText)
      .filter((text) => text && !ignoredTitles.has(text))
      .join(' ')
  }

  function parseNativeTtsAvailable(value, visibleText = '') {
    if (typeof value === 'boolean') return value
    const normalized = normalizeText(value).toLowerCase()
    if (['true', 'yes', '1', 'available', 'supported'].includes(normalized)) return true
    if (['false', 'no', '0', 'unavailable', 'unsupported'].includes(normalized)) return false

    const text = normalizeText(visibleText)
    if (/TTS\s*語音朗讀(?:功能)?\s*[:：]?\s*(?:不支援|不可使用|否|未開放|未提供|無法使用|沒有)/i.test(text)) return false
    if (/TTS\s*語音朗讀(?:功能)?\s*[:：]?\s*(?:支援|有此功能|可使用|是|已開放|提供)/i.test(text)) return true
    if (/TTS\s*語音朗讀功能/i.test(text)) return true
    return null
  }

  function parseEbookLayout(value, visibleText = '') {
    const normalized = normalizeText(value).toLowerCase()
    if (['reflowable', 'flow', 'epub-flow'].includes(normalized)) return 'Reflowable'
    if (['fixed', 'fixed-layout', 'epub-fixed'].includes(normalized)) return 'Fixed'

    const text = normalizeText(visibleText)
    if (/EPUB\s*流動版型|流動版型/i.test(text)) return 'Reflowable'
    if (/EPUB\s*固定版型|固定版型/i.test(text)) return 'Fixed'
    return null
  }

  function candidateFromLink(link, baseUrl) {
    const container = link.closest(
      '[data-book-uni-id], [data-book-id], [data-product-id], article, li, [class*="book-item"], [class*="book__item"], [class*="bookItem"]'
    ) ?? link.parentElement
    if (!container) return null
    const externalId = externalIdFromUrl(link.href, container) ?? externalIdFromUrl(link.href, link)
    if (!externalId || !sourceUrlMatchesExternalId(link.href, externalId)) return null
    const sourceUrl = safeUrl(link.href, baseUrl, sourceHosts, {
      host: 'viewer-ebook.books.com.tw',
      pathPrefix: '/viewer/',
      key: 'book_uni_id',
      value: externalId
    })
    if (!sourceUrl) return null

    const image = container.querySelector('img')
    const titleNode = container.querySelector('[data-title], [class*="title"], h2, h3, h4')
    const authorNode = container.querySelector('[data-author], [class*="author"]')
    const capabilityText = capabilityMarkerText(container)
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
        coverHosts,
        {
          hostRoot: 'book.com.tw',
          pathPrefix: '/image/getImage',
          key: 'i',
          value: externalId
        }
      ),
      nativeTtsAvailable: parseNativeTtsAvailable(
        container.dataset?.nativeTtsAvailable ?? container.dataset?.ttsAvailable,
        capabilityText
      ),
      ebookLayout: parseEbookLayout(container.dataset?.ebookLayout, capabilityText)
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
    if (!externalId || !/^[A-Za-z0-9._:-]{1,128}$/.test(externalId) || !title) return null
    if (!sourceUrlMatchesExternalId(candidate?.sourceUrl, externalId)) return null
    const sourceUrl = safeUrl(candidate?.sourceUrl, 'https://viewer-ebook.books.com.tw/', sourceHosts, {
      host: 'viewer-ebook.books.com.tw',
      pathPrefix: '/viewer/',
      key: 'book_uni_id',
      value: externalId
    })
    if (!sourceUrl) return null

    return {
      externalId,
      title: title.slice(0, 500),
      author: normalizeText(candidate.author).slice(0, 300) || '未知作者',
      language: normalizeText(candidate.language).slice(0, 20) || 'zh-TW',
      sourceUrl,
      coverImageUrl: safeUrl(candidate.coverImageUrl, sourceUrl, coverHosts, {
        hostRoot: 'book.com.tw',
        pathPrefix: '/image/getImage',
        key: 'i',
        value: externalId
      }),
      nativeTtsAvailable: parseNativeTtsAvailable(candidate.nativeTtsAvailable),
      ebookLayout: parseEbookLayout(candidate.ebookLayout)
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

  const api = {
    crawlShelf,
    extractBooks,
    extractFromCandidates,
    externalIdFromUrl,
    normalizeCandidate,
    parseEbookLayout,
    parseNativeTtsAvailable
  }
  global.StoryVoiceBooksComTwExtractor = api
  if (typeof module !== 'undefined' && module.exports) module.exports = api
})(globalThis)

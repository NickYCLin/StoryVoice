const FULL_SHELF_LIMIT = 500
const FULL_SHELF_ROUNDS = 30
const LOAD_MORE_PATTERN = /^(看更多|載入更多|顯示更多|更多書籍|下一頁)$/

function visibleText(element) {
  return String(element.textContent || element.getAttribute('aria-label') || element.getAttribute('title') || '')
    .replace(/\s+/g, ' ')
    .trim()
}

function isVisibleControl(element) {
  if (element.disabled || element.getAttribute('aria-disabled') === 'true') return false
  const style = getComputedStyle(element)
  return style.display !== 'none' && style.visibility !== 'hidden' && style.opacity !== '0'
}

function isShelfNavigationControl(element) {
  const bookCard = element.closest(
    '[data-book-uni-id], [data-book-id], [data-product-id], article, [class*="book-item"], [class*="book__item"], [class*="bookItem"]'
  )
  if (bookCard) return false
  if (element.tagName !== 'A') return true

  try {
    const url = new URL(element.href, location.href)
    return url.origin === location.origin && url.pathname.startsWith('/viewer/')
  } catch {
    return false
  }
}

function findLoadMoreControl() {
  const root = document.querySelector('.bookshelf__main') ?? document.querySelector('main') ?? document.body
  return Array.from(root.querySelectorAll('button, a[href], [role="button"]'))
    .find((element) => LOAD_MORE_PATTERN.test(visibleText(element)) && isVisibleControl(element) && isShelfNavigationControl(element)) ?? null
}

function revealMoreShelf() {
  const control = findLoadMoreControl()
  if (control) {
    control.click()
    return true
  }

  const view = document.defaultView
  if (!view) return false
  const bottom = Math.max(0, document.documentElement.scrollHeight - view.innerHeight)
  if (bottom <= view.scrollY + 2) return false
  view.scrollTo({ top: bottom, behavior: 'auto' })
  return true
}

function waitForShelfUpdate() {
  const root = document.querySelector('.bookshelf__main') ?? document.body
  return new Promise((resolve) => {
    let settled = false
    const finish = () => {
      if (settled) return
      settled = true
      observer.disconnect()
      clearTimeout(timer)
      resolve()
    }
    const observer = new MutationObserver(finish)
    observer.observe(root, { childList: true, subtree: true })
    const timer = setTimeout(finish, 900)
  })
}

async function crawlFullShelf() {
  const initialScrollY = window.scrollY
  try {
    return await globalThis.StoryVoiceBooksComTwExtractor.crawlShelf({
      readBooks: () => globalThis.StoryVoiceBooksComTwExtractor.extractBooks(document),
      revealMore: revealMoreShelf,
      waitForUpdate: waitForShelfUpdate,
      maxBooks: FULL_SHELF_LIMIT,
      maxRounds: FULL_SHELF_ROUNDS
    })
  } finally {
    window.scrollTo({ top: initialScrollY, behavior: 'auto' })
  }
}

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message?.type === 'storyvoice:scan-books-com-tw-shelf') {
    try {
      const books = globalThis.StoryVoiceBooksComTwExtractor.extractBooks(document)
      sendResponse({ ok: true, books, pageUrl: location.href })
    } catch {
      sendResponse({
        ok: false,
        error: '無法讀取目前書櫃，請重新整理博客來電子書櫃後再試。'
      })
    }
    return
  }

  if (message?.type === 'storyvoice:crawl-books-com-tw-shelf') {
    crawlFullShelf()
      .then((result) => sendResponse({ ok: true, ...result, pageUrl: location.href }))
      .catch(() => sendResponse({
        ok: false,
        error: '無法展開完整書櫃，請重新整理博客來電子書櫃後再試。'
      }))
    return true
  }
})

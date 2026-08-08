const state = { books: [] }
const originInput = document.querySelector('#storyvoice-origin')
const count = document.querySelector('#count')
const bookList = document.querySelector('#book-list')
const status = document.querySelector('#status')
const scanButton = document.querySelector('#scan')
const syncButton = document.querySelector('#sync')

function setStatus(message, kind = '') {
  status.textContent = message
  status.className = `status ${kind}`.trim()
}

function validateStoryVoiceOrigin(value) {
  const url = new URL(value)
  const allowedHost = url.hostname === 'localhost' || url.hostname === '127.0.0.1'
  if (url.protocol !== 'http:' || !allowedHost || url.port !== '3000' || url.username || url.password) {
    throw new Error('MVP 僅允許 http://localhost:3000 或 http://127.0.0.1:3000。')
  }
  return url.origin
}

function selectedBooks() {
  const selectedIds = new Set(
    Array.from(bookList.querySelectorAll('input[type="checkbox"]:checked'), (input) => input.value)
  )
  return state.books.filter((book) => selectedIds.has(book.externalId))
}

function updateSelection() {
  const selected = selectedBooks().length
  syncButton.disabled = selected === 0
  syncButton.textContent = selected > 0 ? `同步 ${selected} 本書` : '同步勾選書籍'
}

function renderBooks(books) {
  state.books = books
  bookList.replaceChildren()
  count.textContent = books.length > 0 ? `找到 ${books.length} 本可見書籍` : '未找到可同步書籍'

  for (const book of books) {
    const label = document.createElement('label')
    label.className = 'book'

    const checkbox = document.createElement('input')
    checkbox.type = 'checkbox'
    checkbox.value = book.externalId
    checkbox.checked = true
    checkbox.addEventListener('change', updateSelection)

    const cover = book.coverImageUrl ? document.createElement('img') : document.createElement('span')
    if (book.coverImageUrl) {
      cover.src = book.coverImageUrl
      cover.alt = ''
      cover.referrerPolicy = 'no-referrer'
    } else {
      cover.className = 'cover-fallback'
      cover.textContent = 'SV'
    }

    const copy = document.createElement('span')
    const title = document.createElement('strong')
    title.textContent = book.title
    title.title = book.title
    const author = document.createElement('small')
    author.textContent = book.author
    copy.append(title, author)
    label.append(checkbox, cover, copy)
    bookList.append(label)
  }

  updateSelection()
}

async function scanShelf() {
  scanButton.disabled = true
  syncButton.disabled = true
  setStatus('正在讀取目前頁面已顯示的書籍…')
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true })
    if (!tab?.id || !tab.url?.startsWith('https://viewer-ebook.books.com.tw/viewer/')) {
      throw new Error('目前分頁不是博客來電子書櫃。')
    }

    const response = await chrome.tabs.sendMessage(tab.id, { type: 'storyvoice:scan-books-com-tw-shelf' })
    if (!response?.ok) throw new Error(response?.error ?? '無法掃描書櫃。')
    renderBooks(response.books ?? [])
    setStatus(
      response.books?.length
        ? '僅同步目前頁面已載入且勾選的 metadata。'
        : '沒有找到書籍；請確認已登入，並先在書櫃捲動或按「看更多」。'
    )
  } catch (error) {
    renderBooks([])
    setStatus(error instanceof Error ? error.message : '無法掃描書櫃。', 'error')
  } finally {
    scanButton.disabled = false
  }
}

async function syncBooks() {
  const books = selectedBooks()
  if (books.length === 0) return

  syncButton.disabled = true
  setStatus('正在同步 metadata 到 StoryVoice…')
  try {
    const origin = validateStoryVoiceOrigin(originInput.value.trim())
    await chrome.storage.local.set({ storyVoiceOrigin: origin })
    const response = await fetch(`${origin}/api/books/sources/books-com-tw/import`, {
      method: 'POST',
      credentials: 'omit',
      referrerPolicy: 'no-referrer',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ books })
    })
    const body = await response.json().catch(() => null)
    if (!response.ok) throw new Error(body?.detail ?? `StoryVoice 回傳 ${response.status}`)

    setStatus(`同步完成：新增 ${body.createdCount} 本、更新 ${body.updatedCount} 本。`, 'success')
  } catch (error) {
    setStatus(error instanceof Error ? error.message : '同步失敗，請確認 StoryVoice 已啟動。', 'error')
  } finally {
    updateSelection()
  }
}

scanButton.addEventListener('click', scanShelf)
syncButton.addEventListener('click', syncBooks)
originInput.addEventListener('change', () => {
  try {
    originInput.value = validateStoryVoiceOrigin(originInput.value.trim())
  } catch (error) {
    setStatus(error.message, 'error')
  }
})

chrome.storage.local.get('storyVoiceOrigin').then(({ storyVoiceOrigin }) => {
  if (storyVoiceOrigin) originInput.value = storyVoiceOrigin
  scanShelf()
})

import { validateStoryVoiceOrigin } from './storyvoice-origin.mjs'
import { validateCompanionToken } from './companion-token.mjs'

const state = { books: [] }
const originInput = document.querySelector('#storyvoice-origin')
const tokenInput = document.querySelector('#storyvoice-access-token')
const count = document.querySelector('#count')
const bookList = document.querySelector('#book-list')
const status = document.querySelector('#status')
const scanButton = document.querySelector('#scan')
const crawlButton = document.querySelector('#crawl')
const syncButton = document.querySelector('#sync')
const IMPORT_BATCH_SIZE = 200

function setStatus(message, kind = '') {
  status.textContent = message
  status.className = `status ${kind}`.trim()
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
    const ttsLabel = book.nativeTtsAvailable === true
      ? ' · 官方 TTS'
      : book.nativeTtsAvailable === false
        ? ' · 未開放 TTS'
        : ''
    const layoutLabel = book.ebookLayout === 'Reflowable'
      ? ' · 流動版'
      : book.ebookLayout === 'Fixed'
        ? ' · 固定版'
        : ''
    author.textContent = `${book.author}${ttsLabel}${layoutLabel}`
    copy.append(title, author)
    label.append(checkbox, cover, copy)
    bookList.append(label)
  }

  updateSelection()
}

async function scanShelf(fullShelf = false) {
  scanButton.disabled = true
  crawlButton.disabled = true
  syncButton.disabled = true
  setStatus(fullShelf ? '正在展開書櫃並收集已呈現的 metadata…' : '正在讀取目前頁面已顯示的書籍…')
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true })
    if (!tab?.id || !tab.url?.startsWith('https://viewer-ebook.books.com.tw/viewer/')) {
      throw new Error('目前分頁不是博客來電子書櫃。')
    }

    const response = await chrome.tabs.sendMessage(tab.id, {
      type: fullShelf ? 'storyvoice:crawl-books-com-tw-shelf' : 'storyvoice:scan-books-com-tw-shelf'
    })
    if (!response?.ok) throw new Error(response?.error ?? '無法掃描書櫃。')
    renderBooks(response.books ?? [])
    if (!response.books?.length) {
      setStatus('沒有找到書籍；請確認已在博客來官方頁面完成登入。')
    } else if (fullShelf && response.truncated) {
      setStatus(`已收集 ${response.books.length} 本並達安全上限；不會進入閱讀器或讀取內文。`)
    } else if (fullShelf) {
      setStatus(`已展開 ${response.rounds} 次並收集書櫃 metadata；請確認勾選內容後再同步。`)
    } else {
      setStatus('目前頁面掃描完成；完整書櫃需另外按「展開完整書櫃」。')
    }
  } catch (error) {
    renderBooks([])
    setStatus(error instanceof Error ? error.message : '無法掃描書櫃。', 'error')
  } finally {
    scanButton.disabled = false
    crawlButton.disabled = false
  }
}

async function syncBooks() {
  const books = selectedBooks()
  if (books.length === 0) return

  syncButton.disabled = true
  setStatus('正在同步 metadata 到 StoryVoice…')
  try {
    const origin = validateStoryVoiceOrigin(originInput.value.trim())
    const accessToken = validateCompanionToken(tokenInput.value)
    await chrome.storage.local.set({
      storyVoiceOrigin: origin,
      storyVoiceAccessToken: accessToken
    })
    let createdCount = 0
    let updatedCount = 0
    for (let offset = 0; offset < books.length; offset += IMPORT_BATCH_SIZE) {
      const batch = books.slice(offset, offset + IMPORT_BATCH_SIZE)
      setStatus(`正在同步 ${offset + 1}–${offset + batch.length}／${books.length} 本 metadata…`)
      const response = await fetch(`${origin}/api/books/sources/books-com-tw/import`, {
        method: 'POST',
        credentials: 'omit',
        referrerPolicy: 'no-referrer',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${accessToken}`
        },
        body: JSON.stringify({ books: batch })
      })
      const body = await response.json().catch(() => null)
      if (!response.ok) {
        if (response.status === 401) {
          throw new Error('StoryVoice 連線金鑰已失效；請登入 StoryVoice 重新建立。')
        }
        throw new Error(body?.detail ?? `StoryVoice 回傳 ${response.status}`)
      }
      createdCount += body.createdCount
      updatedCount += body.updatedCount
    }

    setStatus(`同步完成：新增 ${createdCount} 本、更新 ${updatedCount} 本。`, 'success')
  } catch (error) {
    setStatus(error instanceof Error ? error.message : '同步失敗，請確認 StoryVoice 已啟動。', 'error')
  } finally {
    updateSelection()
  }
}

scanButton.addEventListener('click', () => scanShelf(false))
crawlButton.addEventListener('click', () => scanShelf(true))
syncButton.addEventListener('click', syncBooks)
originInput.addEventListener('change', () => {
  try {
    originInput.value = validateStoryVoiceOrigin(originInput.value.trim())
  } catch (error) {
    setStatus(error.message, 'error')
  }
})

tokenInput.addEventListener('change', async () => {
  try {
    const accessToken = validateCompanionToken(tokenInput.value)
    tokenInput.value = accessToken
    await chrome.storage.local.set({ storyVoiceAccessToken: accessToken })
    setStatus('StoryVoice 連線金鑰已保存在這個 extension。')
  } catch (error) {
    setStatus(error instanceof Error ? error.message : 'StoryVoice 連線金鑰格式不正確。', 'error')
  }
})

chrome.storage.local.get(['storyVoiceOrigin', 'storyVoiceAccessToken']).then(({
  storyVoiceOrigin,
  storyVoiceAccessToken
}) => {
  if (storyVoiceOrigin) originInput.value = storyVoiceOrigin
  if (storyVoiceAccessToken) tokenInput.value = storyVoiceAccessToken
  scanShelf(false)
})

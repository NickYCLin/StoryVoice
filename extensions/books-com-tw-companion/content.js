chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message?.type !== 'storyvoice:scan-books-com-tw-shelf') return

  try {
    const books = globalThis.StoryVoiceBooksComTwExtractor.extractBooks(document)
    sendResponse({ ok: true, books, pageUrl: location.href })
  } catch {
    sendResponse({
      ok: false,
      error: '無法讀取目前書櫃，請重新整理博客來電子書櫃後再試。'
    })
  }
})

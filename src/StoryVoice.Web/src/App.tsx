import { useEffect, useState, type CSSProperties, type FormEvent } from 'react'

type BookSummary = {
  id: string
  title: string
  author: string
  language: string
  fileType: string
  status: string
  chapterCount: number
  createdAt: string
  sourceProvider: string | null
  externalSourceId: string | null
  sourceUrl: string | null
  coverImageUrl: string | null
  sourceSyncedAt: string | null
}

type Chapter = {
  id: string
  chapterNumber: number
  sortOrder: number
  title: string
  originalText: string
}

type BookDetails = Omit<BookSummary, 'chapterCount'> & {
  originalFileName: string
  chapters: Chapter[]
}

type LoadState = 'idle' | 'loading' | 'ready' | 'error'

const basePath = import.meta.env.BASE_URL.replace(/\/+$/, '')
const apiUrl = (path: string) => `${basePath}${path.startsWith('/') ? path : `/${path}`}`

const pipeline = [
  ['01', '整理章節', '解析 EPUB／TXT，建立可閱讀的章節與書庫。', '目前可用'],
  ['02', '辨識角色', '建立 Character Bible，讓角色跨章節保持一致。', '開發中'],
  ['03', '導演演出', '決定說話者、情緒、語氣、停頓與節奏。', '開發中'],
  ['04', '合成聲音', '透過可替換的 TTS Provider 生成多角色音訊。', '開發中'],
]

function App() {
  const [books, setBooks] = useState<BookSummary[]>([])
  const [libraryState, setLibraryState] = useState<'loading' | 'ready' | 'error'>('loading')
  const [selectedBookId, setSelectedBookId] = useState<string | null>(null)
  const [selectedBook, setSelectedBook] = useState<BookDetails | null>(null)
  const [detailState, setDetailState] = useState<LoadState>('idle')
  const [uploadState, setUploadState] = useState<LoadState>('idle')
  const [uploadMessage, setUploadMessage] = useState('')

  useEffect(() => {
    const controller = new AbortController()

    fetch(apiUrl('/api/books'), { signal: controller.signal })
      .then((response) => {
        if (!response.ok) throw new Error(`API returned ${response.status}`)
        return response.json() as Promise<BookSummary[]>
      })
      .then((items) => {
        setBooks(items)
        setSelectedBookId((current) => current ?? items[0]?.id ?? null)
        setLibraryState('ready')
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setLibraryState('error')
      })

    return () => controller.abort()
  }, [])

  useEffect(() => {
    if (!selectedBookId) {
      setSelectedBook(null)
      setDetailState('idle')
      return
    }

    const controller = new AbortController()
    setDetailState('loading')
    fetch(apiUrl(`/api/books/${selectedBookId}`), { signal: controller.signal })
      .then((response) => {
        if (!response.ok) throw new Error(`API returned ${response.status}`)
        return response.json() as Promise<BookDetails>
      })
      .then((book) => {
        setSelectedBook(book)
        setDetailState('ready')
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setDetailState('error')
      })

    return () => controller.abort()
  }, [selectedBookId])

  async function handleUpload(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = event.currentTarget
    const formData = new FormData(form)
    const file = formData.get('file')
    if (!(file instanceof File) || file.size === 0) {
      setUploadState('error')
      setUploadMessage('請先選擇 EPUB 或 UTF-8 TXT 檔案。')
      return
    }
    if (file.size > 10 * 1024 * 1024) {
      setUploadState('error')
      setUploadMessage('檔案不可超過 10 MiB。')
      return
    }

    setUploadState('loading')
    setUploadMessage('正在解析章節並存入書庫…')
    try {
      const response = await fetch(apiUrl('/api/books/import'), { method: 'POST', body: formData })
      if (!response.ok) {
        const problem = await response.json().catch(() => null) as { detail?: string; title?: string } | null
        throw new Error(problem?.detail ?? problem?.title ?? `匯入失敗（${response.status}）`)
      }

      const imported = await response.json() as BookDetails
      const listResponse = await fetch(apiUrl('/api/books'))
      if (!listResponse.ok) throw new Error(`書庫重新整理失敗（${listResponse.status}）`)
      const items = await listResponse.json() as BookSummary[]
      setBooks(items)
      setLibraryState('ready')
      setSelectedBook(imported)
      setSelectedBookId(imported.id)
      setDetailState('ready')
      setUploadState('ready')
      setUploadMessage(`「${imported.title}」已匯入，共 ${imported.chapters.length} 章。`)
      form.reset()
    } catch (error) {
      setUploadState('error')
      setUploadMessage(error instanceof Error ? error.message : '匯入失敗，請稍後再試。')
    }
  }

  async function handleLibraryRefresh() {
    setLibraryState('loading')
    try {
      const response = await fetch(apiUrl('/api/books'))
      if (!response.ok) throw new Error(`API returned ${response.status}`)
      const items = await response.json() as BookSummary[]
      setBooks(items)
      setSelectedBookId(items[0]?.id ?? null)
      setLibraryState('ready')
    } catch {
      setLibraryState('error')
    }
  }

  return (
    <main className="relative min-h-screen overflow-hidden bg-[#09070d] text-[#f7f2ea]">
      <div className="ambient ambient-one" aria-hidden="true" />
      <div className="ambient ambient-two" aria-hidden="true" />

      <header className="relative z-10 mx-auto flex max-w-7xl items-center justify-between px-6 py-6 lg:px-10">
        <a className="group flex items-center gap-3" href="#top" aria-label="StoryVoice 首頁">
          <span className="grid h-11 w-11 place-items-center rounded-2xl border border-amber-300/20 bg-amber-100/10 font-serif text-lg text-amber-200 shadow-[0_0_32px_rgba(245,158,11,.12)] transition group-hover:border-amber-300/40">
            SV
          </span>
          <span>
            <strong className="block font-serif text-lg tracking-wide">StoryVoice</strong>
            <span className="block text-[10px] uppercase tracking-[.26em] text-stone-500">AI Story Director</span>
          </span>
        </a>

        <div className="flex items-center gap-4">
          <span className="hidden items-center gap-2 text-xs text-stone-400 sm:flex">
            <span className="h-2 w-2 rounded-full bg-emerald-400 shadow-[0_0_12px_rgba(52,211,153,.85)]" />
            書庫功能可用
          </span>
          <a
            className="rounded-full border border-white/10 bg-white/[.04] px-4 py-2 text-sm text-stone-200 transition hover:border-amber-300/30 hover:bg-amber-200/[.06]"
            href="https://github.com/NickYCLin/StoryVoice"
            rel="noreferrer"
            target="_blank"
          >
            GitHub ↗
          </a>
        </div>
      </header>

      <section id="top" className="relative z-10 mx-auto grid min-h-[74vh] max-w-7xl items-center gap-16 px-6 py-16 lg:grid-cols-[1.08fr_.92fr] lg:px-10 lg:py-24">
        <div>
          <p className="mb-6 flex items-center gap-3 text-xs font-semibold uppercase tracking-[.3em] text-amber-300/80">
            <span className="h-px w-10 bg-amber-300/50" />
            從一本書開始
          </p>
          <h1 className="max-w-4xl font-serif text-5xl leading-[1.04] tracking-[-.04em] text-stone-50 sm:text-6xl lg:text-7xl">
            把電子書放進來，
            <span className="mt-2 block bg-gradient-to-r from-amber-200 via-orange-300 to-rose-300 bg-clip-text text-transparent">先讓章節清楚亮起來。</span>
          </h1>
          <p className="mt-8 max-w-2xl text-base leading-8 text-stone-300 sm:text-lg">
            只要準備一個無 DRM 的 EPUB 或 UTF-8 TXT，StoryVoice 會替你解析章節、整理書庫，並保留原始內容供閱讀。
          </p>
          <p className="mt-3 max-w-2xl text-sm leading-7 text-stone-500">
            目前可用：匯入書籍、解析章節、閱讀管理。角色辨識與多聲線演出正在開發中。
          </p>
          <div className="mt-10 flex flex-col gap-3 sm:flex-row sm:flex-wrap">
            <a className="primary-button" href="#book-file">開始使用：匯入一本書</a>
            <a className="secondary-button" href="#quick-start">先看 3 步教學</a>
          </div>
          <div className="mt-10 flex flex-wrap gap-3 text-xs">
            <span className="rounded-full border border-emerald-300/20 bg-emerald-300/[.07] px-3 py-1.5 text-emerald-200">✓ EPUB / TXT 匯入可用</span>
            <span className="rounded-full border border-white/[.08] px-3 py-1.5 text-stone-500">角色與語音功能開發中</span>
          </div>
        </div>

        <div className="relative mx-auto w-full max-w-xl">
          <div className="absolute -inset-8 rounded-[3rem] bg-gradient-to-br from-amber-300/10 via-transparent to-rose-400/10 blur-2xl" />
          <div className="stage-card relative overflow-hidden rounded-[2rem] border border-white/10 bg-[#120f18]/85 p-6 shadow-2xl shadow-black/50 backdrop-blur-xl sm:p-8">
            <div className="mb-10 flex items-center justify-between">
              <div>
                <p className="text-[10px] uppercase tracking-[.28em] text-stone-600">產品願景預覽</p>
                <h2 className="mt-2 font-serif text-2xl">未來的多角色演出</h2>
              </div>
              <span className="rounded-full border border-amber-300/20 bg-amber-300/10 px-3 py-1 text-xs text-amber-200">功能開發中</span>
            </div>

            <div className="voice-wave mb-10" aria-label="音訊波形視覺化">
              {Array.from({ length: 42 }, (_, index) => (
                <span key={index} style={{ '--wave': `${18 + ((index * 23) % 64)}%`, '--delay': `${index * -37}ms` } as CSSProperties} />
              ))}
            </div>

            <div className="space-y-4">
              <div className="script-line active-line">
                <span className="speaker narrator">旁</span>
                <div><strong>旁白</strong><p>風穿過長廊，吹熄了最後一盞燈。</p></div>
                <span className="emotion">低沉</span>
              </div>
              <div className="script-line">
                <span className="speaker character">雪</span>
                <div><strong>林雪</strong><p>「你終於來了。」</p></div>
                <span className="emotion">平靜</span>
              </div>
              <div className="script-line opacity-50">
                <span className="speaker male">浩</span>
                <div><strong>張浩</strong><p>「抱歉，讓你久等了。」</p></div>
                <span className="emotion">疲憊</span>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section id="quick-start" className="relative z-10 border-y border-white/[.06] bg-white/[.018]">
        <div className="mx-auto max-w-7xl px-6 py-20 lg:px-10">
          <div className="mb-10 max-w-3xl">
            <p className="eyebrow">Quick start</p>
            <h2 className="mt-3 font-serif text-4xl tracking-tight sm:text-5xl">第一次來？照這 3 步就能開始。</h2>
            <p className="mt-4 text-sm leading-7 text-stone-400">不用先設定 AI 金鑰，也不用安裝桌面程式。先把一本書整理進書庫，後面的功能才有故事可接手。</p>
          </div>

          <ol className="grid gap-4 lg:grid-cols-3">
            <li className="rounded-3xl border border-white/[.08] bg-[#0d0a11] p-6 sm:p-7">
              <span className="grid h-10 w-10 place-items-center rounded-2xl bg-amber-200/10 font-mono text-sm text-amber-200">01</span>
              <h3 className="mt-6 font-serif text-2xl text-stone-100">準備一本電子書</h3>
              <p className="mt-3 text-sm leading-7 text-stone-500">使用你有權處理、沒有 DRM 的 EPUB，或 UTF-8 編碼 TXT；檔案上限 10 MiB。</p>
            </li>
            <li className="rounded-3xl border border-amber-300/20 bg-gradient-to-br from-amber-200/[.08] to-orange-300/[.03] p-6 sm:p-7">
              <span className="grid h-10 w-10 place-items-center rounded-2xl bg-amber-200/15 font-mono text-sm text-amber-100">02</span>
              <h3 className="mt-6 font-serif text-2xl text-stone-100">選擇檔案並匯入</h3>
              <p className="mt-3 text-sm leading-7 text-stone-400">按下方的「選擇檔案」，再點「匯入並解析」。完成後會自動選中這本書。</p>
              <a className="mt-6 inline-flex text-sm font-semibold text-amber-200 transition hover:text-amber-100" href="#book-file">前往選擇檔案 ↓</a>
            </li>
            <li className="rounded-3xl border border-white/[.08] bg-[#0d0a11] p-6 sm:p-7">
              <span className="grid h-10 w-10 place-items-center rounded-2xl bg-rose-200/10 font-mono text-sm text-rose-200">03</span>
              <h3 className="mt-6 font-serif text-2xl text-stone-100">選書並展開章節</h3>
              <p className="mt-3 text-sm leading-7 text-stone-500">左邊選一本書，右邊點章節名稱，就能檢查解析後的內容。先做到這裡就成功了。</p>
            </li>
          </ol>

          <div className="mt-6 flex flex-col justify-between gap-4 rounded-2xl border border-sky-300/10 bg-sky-300/[.035] px-5 py-4 text-sm text-stone-400 sm:flex-row sm:items-center">
            <p><strong className="text-stone-200">手邊沒有 EPUB？</strong> 可以先建立一個 UTF-8 TXT，放入幾段自己寫的文字測試。</p>
            <a className="shrink-0 font-semibold text-sky-200 transition hover:text-sky-100" href="#library">直接前往故事書庫 ↓</a>
          </div>
        </div>
      </section>

      <section id="library" className="relative z-10 mx-auto max-w-7xl px-6 py-24 lg:px-10">
        <div className="mb-10 flex flex-col justify-between gap-6 sm:flex-row sm:items-end">
          <div>
            <p className="eyebrow">Start here</p>
            <h2 className="mt-3 font-serif text-4xl tracking-tight sm:text-5xl">先匯入一本書。</h2>
            <p className="mt-4 max-w-2xl text-sm leading-7 text-stone-400">這是目前最直接、最完整的使用方式。匯入完成後，下方會立刻出現書籍與章節。</p>
          </div>
          <span className="rounded-full border border-white/[.08] px-3 py-1 text-xs text-stone-500">書庫已有 {books.length} 本</span>
        </div>

        <form className="mb-6 overflow-hidden rounded-3xl border border-amber-300/20 bg-gradient-to-br from-amber-200/[.08] via-white/[.025] to-orange-300/[.04] p-5 sm:p-7" onSubmit={handleUpload}>
          <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_auto] lg:items-end">
            <div className="min-w-0">
              <span className="inline-flex rounded-full border border-emerald-300/20 bg-emerald-300/[.08] px-3 py-1 text-xs font-semibold text-emerald-200">推薦方式</span>
              <label className="mt-4 block font-serif text-2xl text-stone-100" htmlFor="book-file">選擇 EPUB 或 TXT</label>
              <p className="mt-2 text-sm leading-7 text-stone-400">只要準備一個無 DRM 的 EPUB 或 UTF-8 TXT。選好後按「匯入並解析」，不用填其他欄位。</p>
              <input
                accept=".epub,.txt,application/epub+zip,text/plain"
                className="mt-5 block w-full scroll-mt-6 cursor-pointer rounded-2xl border border-white/10 bg-black/20 p-2.5 text-sm text-stone-400 file:mr-4 file:rounded-xl file:border-0 file:bg-amber-200/10 file:px-4 file:py-2.5 file:font-semibold file:text-amber-100 hover:border-amber-300/25"
                id="book-file"
                name="file"
                required
                type="file"
              />
              <p className={`mt-3 min-h-5 text-xs ${uploadState === 'error' ? 'text-rose-300' : uploadState === 'ready' ? 'text-emerald-300' : 'text-stone-500'}`} role="status">
                {uploadMessage || '支援 EPUB、UTF-8 TXT，最大 10 MiB；請只處理你有權使用的內容。'}
              </p>
            </div>
            <button className="primary-button w-full disabled:cursor-wait disabled:opacity-60 lg:w-auto" disabled={uploadState === 'loading'} type="submit">
              {uploadState === 'loading' ? '正在解析，請稍候…' : '匯入並解析'}
            </button>
          </div>
        </form>

        <details className="group mb-10 overflow-hidden rounded-2xl border border-white/[.07] bg-white/[.018]">
          <summary className="flex cursor-pointer list-none items-center justify-between gap-4 px-5 py-4 text-sm text-stone-300 sm:px-6">
            <span><strong className="font-semibold text-stone-200">進階：同步博客來書櫃書目</strong><span className="ml-2 text-stone-600">不含受保護內文</span></span>
            <span className="text-stone-600 transition group-open:rotate-45">＋</span>
          </summary>
          <div className="grid gap-6 border-t border-white/[.06] px-5 py-6 sm:px-6 lg:grid-cols-[minmax(0,1fr)_auto] lg:items-center">
            <div className="min-w-0">
              <p className="text-sm leading-7 text-stone-400">這條路只同步書名、作者、封面與官方閱讀連結，不會把電子書內文匯入 StoryVoice。第一次使用建議先走上方的直接上傳。</p>
              <ol className="mt-4 grid gap-2 text-xs text-stone-500 sm:grid-cols-3">
                <li><span className="mr-2 text-orange-200/70">01</span>登入博客來官方書櫃</li>
                <li><span className="mr-2 text-orange-200/70">02</span>用 Companion 勾選同步</li>
                <li><span className="mr-2 text-orange-200/70">03</span>回來重新整理書庫</li>
              </ol>
            </div>
            <div className="grid gap-3 sm:grid-cols-3 lg:w-52 lg:grid-cols-1">
              <a className="secondary-button text-center" href="https://viewer-ebook.books.com.tw/viewer/index.html?readlist=all" rel="noreferrer" target="_blank">開啟官方書櫃 ↗</a>
              <a className="secondary-button text-center" href="https://github.com/NickYCLin/StoryVoice/tree/main/extensions/books-com-tw-companion" rel="noreferrer" target="_blank">安裝 Companion ↗</a>
              <button className="secondary-button disabled:cursor-wait disabled:opacity-60" disabled={libraryState === 'loading'} onClick={handleLibraryRefresh} type="button">
                {libraryState === 'loading' ? '重新整理中…' : '重新整理書庫'}
              </button>
            </div>
          </div>
        </details>

        {libraryState === 'loading' && <div className="library-state">正在連接 StoryVoice API…</div>}
        {libraryState === 'error' && <div className="library-state border-rose-400/20 text-rose-200">API 尚未連線。請確認後端服務已啟動。</div>}
        {libraryState === 'ready' && books.length === 0 && (
          <div className="library-state min-h-64">
            <div>
              <span className="mx-auto mb-5 grid h-14 w-14 place-items-center rounded-2xl border border-amber-300/20 bg-amber-300/[.06] text-2xl">◇</span>
              <h3 className="font-serif text-2xl text-stone-200">還沒有書，從上面的檔案選擇開始。</h3>
              <p className="mx-auto mt-3 max-w-md text-sm leading-7 text-stone-500">選好 EPUB 或 TXT，再按「匯入並解析」。完成後，書名與章節會出現在這裡。</p>
              <a className="mt-5 inline-flex text-sm font-semibold text-amber-200 transition hover:text-amber-100" href="#book-file">回到選擇檔案 ↑</a>
            </div>
          </div>
        )}
        {libraryState === 'ready' && books.length > 0 && (
          <div className="grid gap-6 lg:grid-cols-[minmax(0,.82fr)_minmax(0,1.18fr)]">
            <div className="space-y-3">
              {books.map((book) => (
                <button
                  className={`book-card w-full text-left ${selectedBookId === book.id ? 'selected-book' : ''}`}
                  key={book.id}
                  onClick={() => setSelectedBookId(book.id)}
                  type="button"
                >
                  <div className="book-cover">
                    {book.coverImageUrl
                      ? <img alt="" loading="lazy" referrerPolicy="no-referrer" src={book.coverImageUrl} />
                      : <span>{book.title.slice(0, 1)}</span>}
                  </div>
                  <div className="min-w-0 flex-1">
                    <p className="truncate font-serif text-xl text-stone-100">{book.title}</p>
                    <p className="mt-1 truncate text-sm text-stone-500">{book.author}</p>
                    <div className="mt-7 flex flex-wrap items-center gap-3 text-xs text-stone-600">
                      <span>{book.chapterCount} 章</span><span>·</span><span>{book.sourceProvider === 'books-com-tw' ? '博客來' : book.fileType.toUpperCase()}</span><span>·</span><span>{book.status === 'Linked' ? '已連結' : book.status}</span>
                    </div>
                  </div>
                </button>
              ))}
            </div>

            <aside className="min-h-80 rounded-3xl border border-white/[.07] bg-white/[.018] p-5 sm:p-7">
              {detailState === 'loading' && <div className="library-state h-full">正在展開章節…</div>}
              {detailState === 'error' && <div className="library-state h-full border-rose-400/20 text-rose-200">章節讀取失敗，請重新選擇書籍。</div>}
              {detailState === 'ready' && selectedBook && (
                <div>
                  <div className="flex flex-col justify-between gap-4 border-b border-white/[.07] pb-6 sm:flex-row sm:items-start">
                    <div className="min-w-0">
                      <p className="eyebrow">{selectedBook.sourceProvider === 'books-com-tw' ? 'Books.com.tw linked book' : 'Selected story'}</p>
                      <h3 className="mt-2 break-words font-serif text-3xl text-stone-100">{selectedBook.title}</h3>
                      <p className="mt-2 text-sm text-stone-500">{selectedBook.author} · {selectedBook.language} · {selectedBook.sourceProvider === 'books-com-tw' ? '博客來書櫃' : selectedBook.fileType.toUpperCase()}</p>
                      {selectedBook.sourceUrl && (
                        <a className="mt-4 inline-flex text-sm text-orange-200 transition hover:text-orange-100" href={selectedBook.sourceUrl} rel="noreferrer" target="_blank">回博客來官方閱讀器 ↗</a>
                      )}
                    </div>
                    <span className="shrink-0 rounded-full border border-white/[.08] px-3 py-1 text-xs text-stone-500">{selectedBook.chapters.length} 章</span>
                  </div>
                  <div className="mt-5 space-y-3">
                    {selectedBook.chapters.length === 0 && selectedBook.sourceProvider === 'books-com-tw' && (
                      <div className="library-state min-h-52">
                        <div>
                          <span className="mx-auto mb-4 grid h-12 w-12 place-items-center rounded-2xl border border-orange-300/20 bg-orange-300/[.06] text-orange-200">↗</span>
                          <h4 className="font-serif text-xl text-stone-200">書櫃資料已連結，內文仍在博客來</h4>
                          <p className="mx-auto mt-3 max-w-md text-sm leading-7 text-stone-500">StoryVoice 不會抓取或解密受保護內容。要進行故事分析，請另外匯入你有權處理的無 DRM EPUB／TXT。</p>
                          <a className="mt-5 inline-flex text-sm text-amber-200" href="#book-file">前往合法檔案匯入 ↑</a>
                        </div>
                      </div>
                    )}
                    {selectedBook.chapters.map((chapter) => (
                      <details className="chapter-panel group" key={chapter.id}>
                        <summary className="flex cursor-pointer list-none items-center gap-4 px-4 py-4 text-left">
                          <span className="grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-amber-200/[.07] font-mono text-xs text-amber-200/70">{String(chapter.chapterNumber).padStart(2, '0')}</span>
                          <span className="min-w-0 flex-1 truncate font-serif text-lg text-stone-200">{chapter.title}</span>
                          <span className="text-stone-600 transition group-open:rotate-45">＋</span>
                        </summary>
                        <div className="max-h-80 overflow-y-auto whitespace-pre-wrap border-t border-white/[.06] px-4 py-5 text-sm leading-7 text-stone-400">{chapter.originalText}</div>
                      </details>
                    ))}
                  </div>
                </div>
              )}
            </aside>
          </div>
        )}
      </section>

      <section id="pipeline" className="relative z-10 border-y border-white/[.06] bg-white/[.018]">
        <div className="mx-auto max-w-7xl px-6 py-20 lg:px-10">
          <div className="mb-12 flex flex-col justify-between gap-5 md:flex-row md:items-end">
            <div>
              <p className="eyebrow">Product roadmap</p>
              <h2 className="mt-3 max-w-2xl font-serif text-4xl tracking-tight sm:text-5xl">書庫是第一步，聲音演出在後面。</h2>
            </div>
            <p className="max-w-md text-sm leading-7 text-stone-500">這裡是產品藍圖，不是現在每一項都能操作。綠色標記代表正式可用，其餘功能仍在開發。</p>
          </div>

          <div className="grid gap-px overflow-hidden rounded-3xl border border-white/[.07] bg-white/[.07] md:grid-cols-2 lg:grid-cols-4">
            {pipeline.map(([number, title, description, status]) => (
              <article className="group bg-[#0d0a11] p-7 transition hover:bg-[#151019]" key={number}>
                <div className="flex items-center justify-between gap-3">
                  <span className="font-mono text-xs text-amber-300/50">{number}</span>
                  <span className={`rounded-full border px-2.5 py-1 text-[10px] font-semibold ${status === '目前可用' ? 'border-emerald-300/20 bg-emerald-300/[.07] text-emerald-200' : 'border-white/[.08] text-stone-600'}`}>{status}</span>
                </div>
                <h3 className="mt-10 font-serif text-2xl text-stone-100">{title}</h3>
                <p className="mt-4 text-sm leading-7 text-stone-500">{description}</p>
                <div className="mt-8 h-px w-10 bg-gradient-to-r from-amber-300/60 to-transparent transition-all group-hover:w-20" />
              </article>
            ))}
          </div>
        </div>
      </section>

      <footer className="relative z-10 border-t border-white/[.06] px-6 py-8 text-center text-xs leading-6 text-stone-600">
        StoryVoice is open source. No DRM circumvention. Process only content you have the right to use.
      </footer>
    </main>
  )
}

export default App

import { useEffect, useState, type CSSProperties } from 'react'

type BookSummary = {
  id: string
  title: string
  author: string
  language: string
  fileType: string
  status: string
  chapterCount: number
  createdAt: string
}

const pipeline = [
  ['01', '理解章節', '解析 EPUB、章節、段落與對話邊界。'],
  ['02', '辨識角色', '建立 Character Bible，讓角色跨章節保持一致。'],
  ['03', '導演演出', '決定說話者、情緒、語氣、停頓與節奏。'],
  ['04', '合成聲音', '透過可替換的 TTS Provider 生成多角色音訊。'],
]

function App() {
  const [books, setBooks] = useState<BookSummary[]>([])
  const [libraryState, setLibraryState] = useState<'loading' | 'ready' | 'error'>('loading')

  useEffect(() => {
    const controller = new AbortController()

    fetch('/api/books', { signal: controller.signal })
      .then((response) => {
        if (!response.ok) throw new Error(`API returned ${response.status}`)
        return response.json() as Promise<BookSummary[]>
      })
      .then((items) => {
        setBooks(items)
        setLibraryState('ready')
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setLibraryState('error')
      })

    return () => controller.abort()
  }, [])

  return (
    <main className="min-h-screen overflow-hidden bg-[#09070d] text-[#f7f2ea]">
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
            Foundation online
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

      <section id="top" className="relative z-10 mx-auto grid min-h-[78vh] max-w-7xl items-center gap-16 px-6 py-20 lg:grid-cols-[1.08fr_.92fr] lg:px-10 lg:py-28">
        <div>
          <p className="mb-6 flex items-center gap-3 text-xs font-semibold uppercase tracking-[.3em] text-amber-300/80">
            <span className="h-px w-10 bg-amber-300/50" />
            Turn books into performances
          </p>
          <h1 className="max-w-4xl font-serif text-5xl leading-[1.04] tracking-[-.04em] text-stone-50 sm:text-6xl lg:text-7xl">
            不只是念書。
            <span className="mt-2 block bg-gradient-to-r from-amber-200 via-orange-300 to-rose-300 bg-clip-text text-transparent">
              <span className="block sm:inline">讓故事真正</span><span className="whitespace-nowrap">開口。</span>
            </span>
          </h1>
          <p className="mt-8 max-w-2xl text-base leading-8 text-stone-400 sm:text-lg">
            StoryVoice 先理解旁白、角色與情緒，再用一致的聲線演出每一章。
            核心不是文字轉語音，而是一位能讀懂故事的 AI 導演。
          </p>
          <div className="mt-10 flex flex-wrap gap-4">
            <a className="primary-button" href="#library">查看書庫</a>
            <a className="secondary-button" href="#pipeline">探索處理流程</a>
          </div>
          <div className="mt-14 flex flex-wrap gap-x-8 gap-y-3 text-xs uppercase tracking-[.18em] text-stone-600">
            <span>EPUB / TXT</span>
            <span>Character Bible</span>
            <span>Multi Voice</span>
            <span>Provider Agnostic</span>
          </div>
        </div>

        <div className="relative mx-auto w-full max-w-xl">
          <div className="absolute -inset-8 rounded-[3rem] bg-gradient-to-br from-amber-300/10 via-transparent to-rose-400/10 blur-2xl" />
          <div className="stage-card relative overflow-hidden rounded-[2rem] border border-white/10 bg-[#120f18]/85 p-6 shadow-2xl shadow-black/50 backdrop-blur-xl sm:p-8">
            <div className="mb-10 flex items-center justify-between">
              <div>
                <p className="text-[10px] uppercase tracking-[.28em] text-stone-600">Now directing</p>
                <h2 className="mt-2 font-serif text-2xl">月色下的序章</h2>
              </div>
              <span className="rounded-full border border-rose-300/20 bg-rose-300/10 px-3 py-1 text-xs text-rose-200">情緒分析中</span>
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

      <section id="pipeline" className="relative z-10 border-y border-white/[.06] bg-white/[.018]">
        <div className="mx-auto max-w-7xl px-6 py-24 lg:px-10">
          <div className="mb-12 flex flex-col justify-between gap-5 md:flex-row md:items-end">
            <div>
              <p className="eyebrow">From page to performance</p>
              <h2 className="mt-3 max-w-2xl font-serif text-4xl tracking-tight sm:text-5xl">一本書，四層理解。</h2>
            </div>
            <p className="max-w-md text-sm leading-7 text-stone-500">每一層都有明確資料邊界，也都能人工修訂。AI 可以導演，不能替你失控。</p>
          </div>

          <div className="grid gap-px overflow-hidden rounded-3xl border border-white/[.07] bg-white/[.07] md:grid-cols-2 lg:grid-cols-4">
            {pipeline.map(([number, title, description]) => (
              <article className="group bg-[#0d0a11] p-7 transition hover:bg-[#151019]" key={number}>
                <span className="font-mono text-xs text-amber-300/50">{number}</span>
                <h3 className="mt-12 font-serif text-2xl text-stone-100">{title}</h3>
                <p className="mt-4 text-sm leading-7 text-stone-500">{description}</p>
                <div className="mt-8 h-px w-10 bg-gradient-to-r from-amber-300/60 to-transparent transition-all group-hover:w-20" />
              </article>
            ))}
          </div>
        </div>
      </section>

      <section id="library" className="relative z-10 mx-auto max-w-7xl px-6 py-24 lg:px-10">
        <div className="mb-10 flex items-end justify-between gap-6">
          <div>
            <p className="eyebrow">Your library</p>
            <h2 className="mt-3 font-serif text-4xl tracking-tight sm:text-5xl">故事書庫</h2>
          </div>
          <span className="rounded-full border border-white/[.08] px-3 py-1 text-xs text-stone-500">{books.length} 本</span>
        </div>

        {libraryState === 'loading' && <div className="library-state">正在連接 StoryVoice API…</div>}
        {libraryState === 'error' && <div className="library-state border-rose-400/20 text-rose-200">API 尚未連線。請確認後端服務已啟動。</div>}
        {libraryState === 'ready' && books.length === 0 && (
          <div className="library-state min-h-64">
            <div>
              <span className="mx-auto mb-5 grid h-14 w-14 place-items-center rounded-2xl border border-amber-300/20 bg-amber-300/[.06] text-2xl">◇</span>
              <h3 className="font-serif text-2xl text-stone-200">書庫還在等第一個故事</h3>
              <p className="mt-3 text-sm text-stone-500">Foundation 已就緒。EPUB 上傳與解析將在 Phase 2 接上。</p>
            </div>
          </div>
        )}
        {libraryState === 'ready' && books.length > 0 && (
          <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
            {books.map((book) => (
              <article className="book-card" key={book.id}>
                <div className="book-cover"><span>{book.title.slice(0, 1)}</span></div>
                <div className="min-w-0 flex-1">
                  <p className="truncate font-serif text-xl text-stone-100">{book.title}</p>
                  <p className="mt-1 truncate text-sm text-stone-500">{book.author}</p>
                  <div className="mt-7 flex items-center gap-3 text-xs text-stone-600">
                    <span>{book.chapterCount} 章</span><span>·</span><span>{book.fileType.toUpperCase()}</span><span>·</span><span>{book.status}</span>
                  </div>
                </div>
              </article>
            ))}
          </div>
        )}
      </section>

      <footer className="relative z-10 border-t border-white/[.06] px-6 py-8 text-center text-xs leading-6 text-stone-600">
        StoryVoice is open source. No DRM circumvention. Process only content you have the right to use.
      </footer>
    </main>
  )
}

export default App

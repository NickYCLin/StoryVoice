import { useCallback, useEffect, useMemo, useState, type CSSProperties, type FormEvent } from 'react'

import {
  filterAndSortBooks,
  normalizeDeviceTag,
  type DeviceBookTags,
  type LibraryCatalogFilters,
} from './libraryCatalog'

type AuthSession = {
  authenticated: boolean
  email: string | null
  csrfToken: string
}

type AuthState =
  | { status: 'loading' | 'error'; email: null; csrfToken: string }
  | { status: 'anonymous'; email: null; csrfToken: string }
  | { status: 'authenticated'; email: string; csrfToken: string }

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
  nativeTtsAvailable: boolean | null
  ebookLayout: 'Reflowable' | 'Fixed' | null
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
const companionDownloadUrl = `${import.meta.env.BASE_URL}storyvoice-books-companion.zip`
const deviceTagsStorageKey = 'storyvoice:device-book-tags:v1'
const defaultCatalogFilters: LibraryCatalogFilters = {
  query: '',
  source: 'all',
  layout: 'all',
  tts: 'all',
  tag: 'all',
  sort: 'created-desc',
}

function readDeviceBookTags(): DeviceBookTags {
  try {
    const stored = JSON.parse(localStorage.getItem(deviceTagsStorageKey) ?? '{}') as unknown
    if (!stored || typeof stored !== 'object' || Array.isArray(stored)) return {}

    return Object.fromEntries(Object.entries(stored).flatMap(([bookId, values]) => {
      if (!Array.isArray(values)) return []
      const tags = values
        .filter((value): value is string => typeof value === 'string')
        .map(normalizeDeviceTag)
        .filter((value): value is string => value !== null)
        .slice(0, 8)
      return tags.length > 0 ? [[bookId, tags]] : []
    }))
  } catch {
    return {}
  }
}

const pipeline = [
  ['01', '整理章節', '解析 EPUB／TXT，建立可閱讀的章節與書庫。', '目前可用'],
  ['02', '辨識角色', '建立 Character Bible，讓角色跨章節保持一致。', '開發中'],
  ['03', '導演演出', '決定說話者、情緒、語氣、停頓與節奏。', '開發中'],
  ['04', '合成聲音', '透過可替換的 TTS Provider 生成多角色音訊。', '開發中'],
]

type AuthScreenProps = {
  csrfToken: string
  onAuthenticated: () => Promise<void>
}

function AuthScreen({ csrfToken, onAuthenticated }: AuthScreenProps) {
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [state, setState] = useState<LoadState>('idle')
  const [message, setMessage] = useState('')

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = event.currentTarget
    const formData = new FormData(form)
    const email = String(formData.get('email') ?? '').trim()
    const password = String(formData.get('password') ?? '')
    const rememberMe = formData.get('rememberMe') === 'on'
    setState('loading')
    setMessage(mode === 'login' ? '正在登入你的 StoryVoice…' : '正在建立你的 StoryVoice 帳號…')

    try {
      const authEndpoint = mode === 'login' ? '/api/auth/login' : '/api/auth/register'
      const response = await fetch(apiUrl(authEndpoint), {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrfToken },
        body: JSON.stringify({ email, password, rememberMe }),
      })
      if (!response.ok) {
        const problem = await response.json().catch(() => null) as {
          detail?: string
          errors?: Record<string, string[]>
        } | null
        const validationMessage = problem?.errors
          ? Object.values(problem.errors).flat()[0]
          : null
        throw new Error(validationMessage ?? problem?.detail ?? (response.status === 401 ? '電子郵件或密碼不正確。' : `登入失敗（${response.status}）`))
      }

      await onAuthenticated()
    } catch (error) {
      setState('error')
      setMessage(error instanceof Error ? error.message : '帳號操作失敗，請稍後再試。')
    }
  }

  return (
    <main className="relative grid min-h-screen place-items-center overflow-hidden bg-[#09070d] px-5 py-12 text-[#f7f2ea]">
      <div className="ambient ambient-one" aria-hidden="true" />
      <div className="ambient ambient-two" aria-hidden="true" />
      <section className="relative z-10 grid w-full max-w-5xl overflow-hidden rounded-[2rem] border border-white/10 bg-[#100d15]/90 shadow-2xl shadow-black/50 backdrop-blur-xl lg:grid-cols-[1.05fr_.95fr]">
        <div className="border-b border-white/[.07] p-8 sm:p-12 lg:border-b-0 lg:border-r">
          <div className="flex items-center gap-3">
            <span className="grid h-12 w-12 place-items-center rounded-2xl border border-amber-300/20 bg-amber-100/10 font-serif text-lg text-amber-200">SV</span>
            <div>
              <strong className="block font-serif text-xl">StoryVoice</strong>
              <span className="text-[10px] uppercase tracking-[.26em] text-stone-500">Your private story library</span>
            </div>
          </div>
          <p className="eyebrow mt-14">Step 1</p>
          <h1 className="mt-4 max-w-xl font-serif text-4xl leading-tight sm:text-5xl">先登入 StoryVoice，<span className="text-amber-200">再連接自己的書櫃。</span></h1>
          <p className="mt-6 max-w-xl text-sm leading-7 text-stone-400">每個帳號都有獨立書庫。StoryVoice 只保存你主動匯入的檔案與書目，不會接收博客來帳密、Cookie 或受 DRM 保護的內文。</p>
          <ol className="mt-10 space-y-4 text-sm text-stone-400">
            <li><span className="mr-3 text-amber-200">01</span>登入或建立 StoryVoice 帳號</li>
            <li><span className="mr-3 text-orange-200">02</span>登入自己的博客來官方書櫃</li>
            <li><span className="mr-3 text-rose-200">03</span>用 Companion 同步已呈現的書目</li>
          </ol>
        </div>

        <div className="p-8 sm:p-12">
          <div className="flex rounded-full border border-white/[.08] bg-black/20 p-1">
            <button className={`auth-tab ${mode === 'login' ? 'active' : ''}`} onClick={() => { setMode('login'); setMessage('') }} type="button">登入</button>
            <button className={`auth-tab ${mode === 'register' ? 'active' : ''}`} onClick={() => { setMode('register'); setMessage('') }} type="button">建立帳號</button>
          </div>
          <h2 className="mt-9 font-serif text-3xl">{mode === 'login' ? '登入 StoryVoice' : '建立 StoryVoice 帳號'}</h2>
          <p className="mt-2 text-sm text-stone-500">{mode === 'login' ? '回到你的個人故事書庫。' : '使用電子郵件建立獨立書庫。'}</p>

          <form className="mt-8 space-y-5" onSubmit={handleSubmit}>
            <label className="block text-sm text-stone-300">
              電子郵件
              <input autoComplete="email" className="auth-input mt-2" name="email" required type="email" />
            </label>
            <label className="block text-sm text-stone-300">
              密碼
              <input autoComplete={mode === 'login' ? 'current-password' : 'new-password'} className="auth-input mt-2" minLength={10} name="password" required type="password" />
            </label>
            {mode === 'register' && <p className="text-xs leading-6 text-stone-600">至少 10 字元，並包含大小寫英文字母、數字與符號。</p>}
            {mode === 'login' && (
              <label className="flex items-center gap-2 text-xs text-stone-500">
                <input className="h-4 w-4 accent-amber-300" name="rememberMe" type="checkbox" />
                在這台裝置保持登入
              </label>
            )}
            <button className="primary-button w-full disabled:cursor-wait disabled:opacity-60" disabled={state === 'loading'} type="submit">
              {state === 'loading' ? '請稍候…' : mode === 'login' ? '登入 StoryVoice' : '建立帳號並登入'}
            </button>
            <p className={`min-h-6 text-sm ${state === 'error' ? 'text-rose-300' : 'text-stone-500'}`} role="status">{message}</p>
          </form>
        </div>
      </section>
    </main>
  )
}

function App() {
  const [authState, setAuthState] = useState<AuthState>({ status: 'loading', email: null, csrfToken: '' })
  const [companionToken, setCompanionToken] = useState('')
  const [companionTokenState, setCompanionTokenState] = useState<LoadState>('idle')
  const [companionTokenMessage, setCompanionTokenMessage] = useState('')
  const [books, setBooks] = useState<BookSummary[]>([])
  const [libraryState, setLibraryState] = useState<'loading' | 'ready' | 'error'>('loading')
  const [selectedBookId, setSelectedBookId] = useState<string | null>(null)
  const [selectedBook, setSelectedBook] = useState<BookDetails | null>(null)
  const [detailState, setDetailState] = useState<LoadState>('idle')
  const [uploadState, setUploadState] = useState<LoadState>('idle')
  const [uploadMessage, setUploadMessage] = useState('')
  const [catalogFilters, setCatalogFilters] = useState<LibraryCatalogFilters>(defaultCatalogFilters)
  const [deviceBookTags, setDeviceBookTags] = useState<DeviceBookTags>(readDeviceBookTags)
  const [deviceTagDraft, setDeviceTagDraft] = useState('')

  const visibleBooks = useMemo(
    () => filterAndSortBooks(books, catalogFilters, deviceBookTags),
    [books, catalogFilters, deviceBookTags],
  )
  const availableDeviceTags = useMemo(
    () => [...new Set(books.flatMap((book) => deviceBookTags[book.id] ?? []))]
      .sort((left, right) => left.localeCompare(right, 'zh-TW')),
    [books, deviceBookTags],
  )
  const hasCatalogFilters = catalogFilters.query !== ''
    || catalogFilters.source !== 'all'
    || catalogFilters.layout !== 'all'
    || catalogFilters.tts !== 'all'
    || catalogFilters.tag !== 'all'
    || catalogFilters.sort !== 'created-desc'

  const loadAuthSession = useCallback(async () => {
    try {
      const response = await fetch(apiUrl('/api/auth/session'), { credentials: 'same-origin' })
      if (!response.ok) throw new Error(`Auth API returned ${response.status}`)
      const session = await response.json() as AuthSession
      if (session.authenticated && session.email) {
        setAuthState({ status: 'authenticated', email: session.email, csrfToken: session.csrfToken })
      } else {
        setAuthState({ status: 'anonymous', email: null, csrfToken: session.csrfToken })
      }
    } catch {
      setAuthState({ status: 'error', email: null, csrfToken: '' })
    }
  }, [])

  useEffect(() => {
    void loadAuthSession()
  }, [loadAuthSession])

  useEffect(() => {
    if (authState.status !== 'authenticated') return

    const controller = new AbortController()
    setLibraryState('loading')

    fetch(apiUrl('/api/books'), { signal: controller.signal, credentials: 'same-origin' })
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
  }, [authState.status])

  useEffect(() => {
    if (libraryState !== 'ready' || books.length === 0) return
    if (visibleBooks.length === 0) {
      setSelectedBookId(null)
      return
    }
    if (!selectedBookId || !visibleBooks.some((book) => book.id === selectedBookId)) {
      setSelectedBookId(visibleBooks[0].id)
    }
  }, [books.length, libraryState, selectedBookId, visibleBooks])

  useEffect(() => {
    if (catalogFilters.tag !== 'all' && !availableDeviceTags.includes(catalogFilters.tag)) {
      setCatalogFilters((current) => ({ ...current, tag: 'all' }))
    }
  }, [availableDeviceTags, catalogFilters.tag])

  useEffect(() => {
    setDeviceTagDraft('')
    if (authState.status !== 'authenticated' || !selectedBookId) {
      setSelectedBook(null)
      setDetailState('idle')
      return
    }

    const controller = new AbortController()
    setDetailState('loading')
    fetch(apiUrl(`/api/books/${selectedBookId}`), {
      signal: controller.signal,
      credentials: 'same-origin',
    })
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
  }, [authState.status, selectedBookId])

  function saveDeviceBookTags(update: (current: DeviceBookTags) => DeviceBookTags) {
    setDeviceBookTags((current) => {
      const next = update(current)
      try {
        localStorage.setItem(deviceTagsStorageKey, JSON.stringify(next))
      } catch {
        // The organizer remains usable for this page even if private browsing blocks storage.
      }
      return next
    })
  }

  function handleAddDeviceTag(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!selectedBookId) return
    const tag = normalizeDeviceTag(deviceTagDraft)
    if (!tag) return

    saveDeviceBookTags((current) => {
      const existing = current[selectedBookId] ?? []
      if (existing.length >= 8 || existing.some((value) => value.localeCompare(tag, 'zh-TW', { sensitivity: 'base' }) === 0)) return current
      return { ...current, [selectedBookId]: [...existing, tag] }
    })
    setDeviceTagDraft('')
  }

  function removeDeviceTag(bookId: string, tag: string) {
    saveDeviceBookTags((current) => {
      const remaining = (current[bookId] ?? []).filter((value) => value !== tag)
      const next = { ...current }
      if (remaining.length > 0) next[bookId] = remaining
      else delete next[bookId]
      return next
    })
  }

  async function handleUpload(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (authState.status !== 'authenticated') return

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
      const response = await fetch(apiUrl('/api/books/import'), {
        method: 'POST',
        body: formData,
        credentials: 'same-origin',
        headers: { 'X-CSRF-TOKEN': authState.csrfToken },
      })
      if (!response.ok) {
        const problem = await response.json().catch(() => null) as { detail?: string; title?: string } | null
        throw new Error(problem?.detail ?? problem?.title ?? `匯入失敗（${response.status}）`)
      }

      const imported = await response.json() as BookDetails
      const listResponse = await fetch(apiUrl('/api/books'), { credentials: 'same-origin' })
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

  async function handleIssueCompanionToken() {
    if (authState.status !== 'authenticated') return

    setCompanionToken('')
    setCompanionTokenState('loading')
    setCompanionTokenMessage('正在建立只限書櫃同步的連線金鑰…')
    try {
      const response = await fetch(apiUrl('/api/auth/companion-token'), {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': authState.csrfToken },
        body: JSON.stringify({}),
      })
      const body = await response.json().catch(() => null) as {
        accessToken?: string
        expiresAt?: string
        detail?: string
      } | null
      if (!response.ok || !body?.accessToken) {
        throw new Error(body?.detail ?? `建立連線金鑰失敗（${response.status}）`)
      }

      setCompanionToken(body.accessToken)
      setCompanionTokenState('ready')
      const expiresAt = body.expiresAt ? new Date(body.expiresAt).toLocaleString('zh-TW') : '7 天後'
      setCompanionTokenMessage(`請立即複製到 Companion；有效期限至 ${expiresAt}。`)
    } catch (error) {
      setCompanionTokenState('error')
      setCompanionTokenMessage(error instanceof Error ? error.message : '建立連線金鑰失敗。')
    }
  }

  async function handleCopyCompanionToken() {
    if (!companionToken) return
    try {
      await navigator.clipboard.writeText(companionToken)
      setCompanionTokenMessage('連線金鑰已複製；請貼到 Companion。')
    } catch {
      setCompanionTokenMessage('瀏覽器不允許自動複製，請手動選取金鑰。')
    }
  }

  async function handleRevokeCompanionTokens() {
    if (authState.status !== 'authenticated') return

    setCompanionTokenState('loading')
    setCompanionTokenMessage('正在撤銷 Companion 連線金鑰…')
    try {
      const response = await fetch(apiUrl('/api/auth/companion-token/revoke'), {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': authState.csrfToken },
        body: JSON.stringify({}),
      })
      if (!response.ok) throw new Error(`撤銷失敗（${response.status}）`)
      setCompanionToken('')
      setCompanionTokenState('ready')
      setCompanionTokenMessage('所有 Companion 連線金鑰已撤銷。')
    } catch (error) {
      setCompanionTokenState('error')
      setCompanionTokenMessage(error instanceof Error ? error.message : '撤銷連線金鑰失敗。')
    }
  }

  async function handleLogout() {
    if (authState.status !== 'authenticated') return

    const response = await fetch(apiUrl('/api/auth/logout'), {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': authState.csrfToken },
      body: JSON.stringify({}),
    })
    if (!response.ok) return

    setBooks([])
    setSelectedBookId(null)
    setSelectedBook(null)
    setCatalogFilters({ ...defaultCatalogFilters })
    setDeviceTagDraft('')
    setLibraryState('loading')
    setDetailState('idle')
    setUploadState('idle')
    setUploadMessage('')
    setCompanionToken('')
    setCompanionTokenState('idle')
    setCompanionTokenMessage('')
    setAuthState({ status: 'loading', email: null, csrfToken: '' })
    await loadAuthSession()
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

  if (authState.status === 'loading') {
    return <main className="grid min-h-screen place-items-center bg-[#09070d] text-stone-400">正在確認 StoryVoice 登入狀態…</main>
  }

  if (authState.status === 'error') {
    return <main className="grid min-h-screen place-items-center bg-[#09070d] px-6 text-center text-rose-200">無法連接登入服務，請重新整理頁面。</main>
  }

  if (authState.status === 'anonymous') {
    return <AuthScreen csrfToken={authState.csrfToken} onAuthenticated={loadAuthSession} />
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
          {authState.status === 'authenticated' && (
            <span className="hidden max-w-52 truncate text-xs text-stone-500 md:inline">{authState.email}</span>
          )}
          {authState.status === 'authenticated' && (
            <button className="rounded-full border border-white/10 px-4 py-2 text-sm text-stone-300 transition hover:border-rose-300/30 hover:text-rose-200" onClick={handleLogout} type="button">登出</button>
          )}
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
            <h2 className="mt-3 font-serif text-4xl tracking-tight sm:text-5xl">整理你的故事書庫。</h2>
            <p className="mt-4 max-w-2xl text-sm leading-7 text-stone-400">搜尋、篩選、排序並加上此裝置標籤；有合法正文的 EPUB／TXT 仍可直接匯入章節。</p>
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
          <div className="grid gap-6 border-t border-white/[.06] px-5 py-6 sm:px-6 lg:grid-cols-[minmax(0,1fr)_auto] lg:items-start">
            <div className="min-w-0">
              <p className="text-sm leading-7 text-stone-400">這條路只同步書名、作者、封面、官方閱讀連結，以及頁面明確標示的版型／官方 TTS 狀態；不會把電子書內文匯入 StoryVoice。</p>
              <ol className="mt-4 grid gap-2 text-xs text-stone-500 sm:grid-cols-3">
                <li><span className="mr-2 text-orange-200/70">01</span>登入博客來官方書櫃</li>
                <li><span className="mr-2 text-orange-200/70">02</span>建立金鑰並貼到 Companion</li>
                <li><span className="mr-2 text-orange-200/70">03</span>勾選同步後重新整理</li>
              </ol>
              <div className="mt-5 rounded-2xl border border-sky-300/15 bg-sky-300/[.035] p-4">
                <strong className="text-sm text-stone-200">第一次安裝 Companion</strong>
                <ol className="mt-3 space-y-2 text-xs leading-6 text-stone-500">
                  <li><span className="mr-2 text-sky-200/70">1.</span>按「下載 Companion ZIP」，下載後解壓縮。</li>
                  <li><span className="mr-2 text-sky-200/70">2.</span>在 Chrome 網址列輸入 <code className="text-sky-200">chrome://extensions</code>，開啟「開發人員模式」。</li>
                  <li><span className="mr-2 text-sky-200/70">3.</span>按「載入未封裝項目」，選擇剛才解壓縮後的資料夾。</li>
                </ol>
                <p className="mt-3 text-xs leading-5 text-stone-600">Chrome 基於安全規則，這一步必須由你親自確認；安裝後不用提供博客來帳密給 StoryVoice。</p>
                <a className="mt-3 inline-flex text-xs text-sky-200 transition hover:text-sky-100" href="https://github.com/NickYCLin/StoryVoice/tree/main/extensions/books-com-tw-companion" rel="noreferrer" target="_blank">檢視 Companion 原始碼 ↗</a>
              </div>
              <div className="mt-5 rounded-2xl border border-orange-300/15 bg-orange-300/[.035] p-4">
                <div className="flex flex-col justify-between gap-3 sm:flex-row sm:items-center">
                  <div>
                    <strong className="text-sm text-stone-200">StoryVoice 連線金鑰</strong>
                    <p className="mt-1 text-xs leading-5 text-stone-500">只允許 Companion 把 metadata 同步到你的帳號；重新建立會撤銷舊金鑰。</p>
                  </div>
                  <div className="flex shrink-0 flex-wrap gap-2">
                    <button className="secondary-button disabled:cursor-wait disabled:opacity-60" disabled={companionTokenState === 'loading'} onClick={handleIssueCompanionToken} type="button">
                      {companionTokenState === 'loading' ? '處理中…' : companionToken ? '重新建立金鑰' : '建立連線金鑰'}
                    </button>
                    <button className="secondary-button disabled:cursor-wait disabled:opacity-60" disabled={companionTokenState === 'loading'} onClick={handleRevokeCompanionTokens} type="button">撤銷所有金鑰</button>
                  </div>
                </div>
                {companionToken && (
                  <div className="mt-4 flex flex-col gap-2 sm:flex-row">
                    <input aria-label="只顯示一次的 StoryVoice 連線金鑰" className="auth-input min-w-0 flex-1 font-mono text-xs" readOnly type="text" value={companionToken} />
                    <button className="secondary-button shrink-0" onClick={handleCopyCompanionToken} type="button">複製金鑰</button>
                  </div>
                )}
                <p className={`mt-2 min-h-5 text-xs ${companionTokenState === 'error' ? 'text-rose-300' : 'text-stone-500'}`} role="status">{companionTokenMessage || '金鑰只會在建立當下顯示；請貼到 Companion 後再同步。'}</p>
              </div>
            </div>
            <div className="grid gap-3 sm:grid-cols-3 lg:w-52 lg:grid-cols-1">
              <a className="primary-button text-center" download href={companionDownloadUrl}>下載 Companion ZIP</a>
              <a className="secondary-button text-center" href="https://viewer-ebook.books.com.tw/viewer/index.html?readlist=all" rel="noreferrer" target="_blank">開啟官方書櫃 ↗</a>
              <button className="secondary-button disabled:cursor-wait disabled:opacity-60" disabled={libraryState === 'loading'} onClick={handleLibraryRefresh} type="button">
                {libraryState === 'loading' ? '重新整理中…' : '重新整理書庫'}
              </button>
            </div>
          </div>
        </details>

        {libraryState === 'ready' && books.length > 0 && (
          <section aria-label="書庫整理工具" className="mb-6 rounded-3xl border border-white/[.07] bg-white/[.018] p-5 sm:p-6">
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              <label className="text-xs text-stone-500 sm:col-span-2 lg:col-span-1">
                搜尋書名、作者、書籍 ID 或標籤
                <input
                  className="auth-input mt-2"
                  onChange={(event) => setCatalogFilters((current) => ({ ...current, query: event.target.value }))}
                  placeholder="輸入關鍵字"
                  type="search"
                  value={catalogFilters.query}
                />
              </label>
              <label className="text-xs text-stone-500">
                來源
                <select className="auth-input mt-2" onChange={(event) => setCatalogFilters((current) => ({ ...current, source: event.target.value as LibraryCatalogFilters['source'] }))} value={catalogFilters.source}>
                  <option value="all">全部來源</option>
                  <option value="books-com-tw">博客來連結</option>
                  <option value="uploaded">StoryVoice 匯入</option>
                </select>
              </label>
              <label className="text-xs text-stone-500">
                博客來版型
                <select className="auth-input mt-2" onChange={(event) => setCatalogFilters((current) => ({ ...current, layout: event.target.value as LibraryCatalogFilters['layout'] }))} value={catalogFilters.layout}>
                  <option value="all">全部版型</option>
                  <option value="Reflowable">流動版</option>
                  <option value="Fixed">固定版</option>
                  <option value="unknown">未標示／非博客來</option>
                </select>
              </label>
              <label className="text-xs text-stone-500">
                博客來官方 TTS
                <select className="auth-input mt-2" onChange={(event) => setCatalogFilters((current) => ({ ...current, tts: event.target.value as LibraryCatalogFilters['tts'] }))} value={catalogFilters.tts}>
                  <option value="all">全部狀態</option>
                  <option value="true">可用</option>
                  <option value="false">未開放</option>
                  <option value="unknown">未標示／非博客來</option>
                </select>
              </label>
              <label className="text-xs text-stone-500">
                此裝置標籤
                <select className="auth-input mt-2" disabled={availableDeviceTags.length === 0} onChange={(event) => setCatalogFilters((current) => ({ ...current, tag: event.target.value }))} value={catalogFilters.tag}>
                  <option value="all">全部標籤</option>
                  {availableDeviceTags.map((tag) => <option key={tag} value={tag}>{tag}</option>)}
                </select>
              </label>
              <label className="text-xs text-stone-500">
                排序
                <select className="auth-input mt-2" onChange={(event) => setCatalogFilters((current) => ({ ...current, sort: event.target.value as LibraryCatalogFilters['sort'] }))} value={catalogFilters.sort}>
                  <option value="created-desc">最近加入</option>
                  <option value="title">書名</option>
                  <option value="author">作者</option>
                  <option value="synced-desc">最近同步</option>
                </select>
              </label>
            </div>
            <div className="mt-5 flex flex-col justify-between gap-3 border-t border-white/[.06] pt-4 text-xs text-stone-500 sm:flex-row sm:items-center">
              <span role="status">符合 {visibleBooks.length}／全部 {books.length} 本</span>
              <button className="secondary-button disabled:opacity-40" disabled={!hasCatalogFilters} onClick={() => setCatalogFilters(defaultCatalogFilters)} type="button">清除條件</button>
            </div>
          </section>
        )}

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
        {libraryState === 'ready' && books.length > 0 && visibleBooks.length === 0 && (
          <div className="library-state min-h-56">
            <div>
              <h3 className="font-serif text-2xl text-stone-200">沒有符合條件的書。</h3>
              <p className="mt-3 text-sm text-stone-500">換一組條件，或清除全部篩選。</p>
              <button className="secondary-button mt-5" onClick={() => setCatalogFilters(defaultCatalogFilters)} type="button">清除條件</button>
            </div>
          </div>
        )}
        {libraryState === 'ready' && visibleBooks.length > 0 && (
          <div className="grid gap-6 lg:grid-cols-[minmax(0,.82fr)_minmax(0,1.18fr)]">
            <div className="space-y-3">
              {visibleBooks.map((book) => (
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
                      {book.nativeTtsAvailable === true && <><span>·</span><span className="text-emerald-300/80">官方 TTS</span></>}
                    </div>
                    {(deviceBookTags[book.id]?.length ?? 0) > 0 && (
                      <div className="mt-3 flex flex-wrap gap-2">
                        {deviceBookTags[book.id].slice(0, 3).map((tag) => (
                          <span className="rounded-full border border-sky-300/15 bg-sky-300/[.04] px-2 py-1 text-[10px] text-sky-200/70" key={tag}>{tag}</span>
                        ))}
                      </div>
                    )}
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
                      {selectedBook.sourceProvider === 'books-com-tw' && (
                        <p className="mt-2 text-xs text-stone-600">
                          {selectedBook.ebookLayout === 'Reflowable' ? 'EPUB 流動版型' : selectedBook.ebookLayout === 'Fixed' ? 'EPUB 固定版型' : '版型未標示'}
                          {' · '}
                          {selectedBook.nativeTtsAvailable === true ? '博客來官方 TTS 可用' : selectedBook.nativeTtsAvailable === false ? '博客來官方 TTS 未開放' : '官方 TTS 狀態未標示'}
                        </p>
                      )}
                      {selectedBook.sourceUrl && (
                        <a className="mt-4 inline-flex text-sm text-orange-200 transition hover:text-orange-100" href={selectedBook.sourceUrl} rel="noreferrer" target="_blank">回博客來官方閱讀器 ↗</a>
                      )}
                    </div>
                    <span className="shrink-0 rounded-full border border-white/[.08] px-3 py-1 text-xs text-stone-500">{selectedBook.chapters.length} 章</span>
                  </div>
                  <div className="mt-5 rounded-2xl border border-sky-300/10 bg-sky-300/[.025] p-4">
                    <div className="flex flex-wrap items-center gap-2">
                      <strong className="mr-1 text-xs text-stone-300">此裝置標籤</strong>
                      {(deviceBookTags[selectedBook.id] ?? []).map((tag) => (
                        <button
                          aria-label={`移除標籤 ${tag}`}
                          className="rounded-full border border-sky-300/15 bg-sky-300/[.05] px-2.5 py-1 text-xs text-sky-200/80 transition hover:border-rose-300/25 hover:text-rose-200"
                          key={tag}
                          onClick={() => removeDeviceTag(selectedBook.id, tag)}
                          type="button"
                        >
                          {tag} ×
                        </button>
                      ))}
                      {(deviceBookTags[selectedBook.id]?.length ?? 0) === 0 && <span className="text-xs text-stone-600">尚未加標籤</span>}
                    </div>
                    <form className="mt-3 flex flex-col gap-2 sm:flex-row" onSubmit={handleAddDeviceTag}>
                      <input
                        aria-label="新增此裝置標籤"
                        className="auth-input min-w-0 flex-1"
                        maxLength={24}
                        onChange={(event) => setDeviceTagDraft(event.target.value)}
                        placeholder="例如：待讀、小說、工作"
                        value={deviceTagDraft}
                      />
                      <button className="secondary-button shrink-0" disabled={!normalizeDeviceTag(deviceTagDraft) || (deviceBookTags[selectedBook.id]?.length ?? 0) >= 8} type="submit">加入標籤</button>
                    </form>
                    <p className="mt-2 text-[10px] leading-5 text-stone-600">最多 8 個，只保存在目前瀏覽器；不會傳到博客來。</p>
                  </div>
                  <div className="mt-5 space-y-3">
                    {selectedBook.chapters.length === 0 && selectedBook.sourceProvider === 'books-com-tw' && (
                      <div className="library-state min-h-52">
                        <div>
                          <span className="mx-auto mb-4 grid h-12 w-12 place-items-center rounded-2xl border border-orange-300/20 bg-orange-300/[.06] text-orange-200">↗</span>
                          <h4 className="font-serif text-xl text-stone-200">{selectedBook.nativeTtsAvailable === true ? '這本書可用博客來官方 TTS' : '書櫃資料已連結，內文仍在博客來'}</h4>
                          <p className="mx-auto mt-3 max-w-md text-sm leading-7 text-stone-500">{selectedBook.nativeTtsAvailable === true ? '請從上方官方連結開啟博客來 App／閱讀器使用授權朗讀；StoryVoice 不會接收書籍正文。' : 'StoryVoice 不會抓取或解密受保護內容。要進行故事分析，請另外匯入你有權處理的無 DRM EPUB／TXT。'}</p>
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

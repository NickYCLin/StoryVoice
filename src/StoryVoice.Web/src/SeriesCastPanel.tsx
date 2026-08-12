import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'

import { fetchJson } from './api'
import { useAuthedOutletContext } from './authOutletContext'
import { ConfirmDialog } from './components/ConfirmDialog'
import { SpeechPlanReview, type SeriesCharacterChoice, type SpeechPlanDraft } from './SpeechPlanReview'
import type { BookDetails, BookSummary } from './types'

type VoiceOption = {
  provider: string
  voice: string
  displayName: string
  locale: string
}

type SeriesSummary = {
  id: string
  name: string
  bookCount: number
  characterCount: number
  activeCastRevisionId: string | null
  createdAt: string
  updatedAt: string
}

type SeriesBook = {
  id: string
  bookId: string
  bookTitle: string
  volumeLabel: string
  sortOrder: number
  membershipRevision: number
  activeNarrationJobId: string | null
}

type SeriesCharacter = SeriesCharacterChoice & {
  role: 'Main' | 'Supporting' | 'Minor'
  voiceProvider: string
  rate: string
  pitch: string
  volume: string
  notes: string | null
  aliases: Array<{ id: string; value: string }>
}

type SeriesDetails = {
  id: string
  name: string
  narratorProvider: string
  narratorVoice: string
  narratorRate: string
  narratorPitch: string
  narratorVolume: string
  defaultSpeakerPauseMs: number
  activeCastRevisionId: string | null
  books: SeriesBook[]
  characters: SeriesCharacter[]
}

type RebuildBatch = {
  id: string
  status: 'Building' | 'ReadyToActivate' | 'Activated' | 'Failed' | 'Invalidated'
  members: Array<{ id: string; bookId: string; status: string; stagedNarrationJobId: string | null }>
}

type LoadState = 'idle' | 'loading' | 'ready' | 'error'

const profileDefaults = { rate: '+0%', pitch: '+0Hz', volume: '+0%' }
const previewSentence = '這是一段不含書籍正文的聲線示範。'

function voiceLabel(voice: string, options: VoiceOption[]) {
  const option = options.find((candidate) => candidate.voice === voice)
  return option ? `${option.displayName}（${option.locale}）` : '已設定的固定聲線'
}

export function SeriesCastPanel() {
  const { csrfToken } = useAuthedOutletContext()
  const [series, setSeries] = useState<SeriesSummary[]>([])
  const [voiceOptions, setVoiceOptions] = useState<VoiceOption[]>([])
  const [libraryBooks, setLibraryBooks] = useState<BookSummary[]>([])
  const [selectedSeriesId, setSelectedSeriesId] = useState('')
  const [details, setDetails] = useState<SeriesDetails | null>(null)
  const [state, setState] = useState<LoadState>('loading')
  const [message, setMessage] = useState('')

  const [seriesName, setSeriesName] = useState('')
  const [narratorVoice, setNarratorVoice] = useState('')
  const [bookId, setBookId] = useState('')
  const [volumeLabel, setVolumeLabel] = useState('')
  const [characterName, setCharacterName] = useState('')
  const [characterRole, setCharacterRole] = useState<SeriesCharacter['role']>('Supporting')
  const [characterVoice, setCharacterVoice] = useState('')
  const [aliasCharacterId, setAliasCharacterId] = useState('')
  const [alias, setAlias] = useState('')
  const [formState, setFormState] = useState<LoadState>('idle')

  const [bookDetails, setBookDetails] = useState<Record<string, BookDetails>>({})
  const [draftsByChapter, setDraftsByChapter] = useState<Record<string, SpeechPlanDraft>>({})
  const [batch, setBatch] = useState<RebuildBatch | null>(null)
  const [activateDialogOpen, setActivateDialogOpen] = useState(false)

  const loadSeries = useCallback(async () => {
    setState('loading')
    try {
      const [items, voices, books] = await Promise.all([
        fetchJson<SeriesSummary[]>('/api/series/'),
        fetchJson<VoiceOption[]>('/api/series/voice-options'),
        fetchJson<BookSummary[]>('/api/books'),
      ])
      setSeries(items)
      setVoiceOptions(voices)
      setLibraryBooks(books)
      setNarratorVoice((current) => current || voices[0]?.voice || '')
      setCharacterVoice((current) => current || voices[0]?.voice || '')
      setSelectedSeriesId((current) => current || items[0]?.id || '')
      setState('ready')
    } catch (error) {
      setState('error')
      setMessage(error instanceof Error ? error.message : '無法讀取系列配音資料。')
    }
  }, [])

  const loadDetails = useCallback(async (seriesId: string) => {
    if (!seriesId) {
      setDetails(null)
      return
    }
    try {
      const detail = await fetchJson<SeriesDetails>(`/api/series/${seriesId}`)
      setDetails(detail)
      setAliasCharacterId((current) => current || detail.characters[0]?.id || '')
      setBookId('')
      setVolumeLabel('')
      setBatch(null)
    } catch (error) {
      setDetails(null)
      setMessage(error instanceof Error ? error.message : '無法讀取這個系列。')
    }
  }, [])

  useEffect(() => {
    void loadSeries()
  }, [loadSeries])

  useEffect(() => {
    void loadDetails(selectedSeriesId)
  }, [loadDetails, selectedSeriesId])

  useEffect(() => {
    if (!details) {
      setBookDetails({})
      setDraftsByChapter({})
      return
    }

    let stale = false
    void (async () => {
      const loadedBooks = await Promise.all(details.books.map(async (member) => {
        const book = await fetchJson<BookDetails>(`/api/books/${member.bookId}`)
        return [member.bookId, book] as const
      })).catch(() => [])
      if (stale) return
      const detailsById = Object.fromEntries(loadedBooks)
      setBookDetails(detailsById)

      const loadedDrafts = await Promise.all(loadedBooks.flatMap(([, book]) => book.chapters.map(async (chapter) => {
        try {
          const draft = await fetchJson<SpeechPlanDraft>(`/api/series/${details.id}/books/${book.id}/chapters/${chapter.id}/speech-plan`)
          return [chapter.id, draft] as const
        } catch {
          return null
        }
      })))
      if (stale) return
      setDraftsByChapter(Object.fromEntries(loadedDrafts.filter((item): item is readonly [string, SpeechPlanDraft] => item !== null)))
    })()

    return () => {
      stale = true
    }
  }, [details])

  useEffect(() => {
    if (!details || !batch || batch.status !== 'Building') return
    const timer = window.setInterval(() => {
      fetchJson<RebuildBatch>(`/api/series/${details.id}/narration-rebuilds/${batch.id}`)
        .then(setBatch)
        .catch(() => undefined)
    }, 2_000)
    return () => window.clearInterval(timer)
  }, [batch, details])

  const eligibleBooks = useMemo(() => {
    const memberIds = new Set(details?.books.map((member) => member.bookId) ?? [])
    return libraryBooks.filter((book) => book.authorizedTextAvailable && book.status !== 'Linked' && !memberIds.has(book.id))
  }, [details, libraryBooks])

  const reviewEntries = useMemo(() => {
    if (!details) return []
    return details.books
      .slice()
      .sort((left, right) => left.sortOrder - right.sortOrder)
      .flatMap((member) => {
        const book = bookDetails[member.bookId]
        return book ? book.chapters.map((chapter) => ({ book, chapter, draft: draftsByChapter[chapter.id] ?? null })) : []
      })
  }, [bookDetails, details, draftsByChapter])

  function previewVoice(voice: string) {
    if (!('speechSynthesis' in window)) {
      setMessage('這個瀏覽器不支援安全的固定示範句 preview。')
      return
    }
    window.speechSynthesis.cancel()
    const utterance = new SpeechSynthesisUtterance(previewSentence)
    const exactVoice = window.speechSynthesis.getVoices().find((candidate) => candidate.name === voice)
    if (exactVoice) utterance.voice = exactVoice
    utterance.lang = exactVoice?.lang ?? 'zh-TW'
    window.speechSynthesis.speak(utterance)
    setMessage('正在播放固定示範句；瀏覽器找不到同名 voice 時會使用本機語音，最終以 Edge TTS 成品為準。')
  }

  async function createSeries(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!seriesName.trim() || !narratorVoice) return
    const selectedVoice = voiceOptions.find((option) => option.voice === narratorVoice)
    if (!selectedVoice) return
    setFormState('loading')
    try {
      const created = await fetchJson<SeriesDetails>('/api/series/', {
        method: 'POST',
        csrfToken,
        body: {
          name: seriesName.trim(),
          narratorProvider: selectedVoice.provider,
          narratorVoice,
          narratorRate: profileDefaults.rate,
          narratorPitch: profileDefaults.pitch,
          narratorVolume: profileDefaults.volume,
          defaultSpeakerPauseMs: 180,
        },
      })
      setSeries((current) => [{ id: created.id, name: created.name, bookCount: 0, characterCount: 0, activeCastRevisionId: null, createdAt: '', updatedAt: '' }, ...current])
      setSelectedSeriesId(created.id)
      setDetails(created)
      setSeriesName('')
      setMessage('已建立系列；接著加入每一冊與固定角色聲線。')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '建立系列失敗。')
    } finally {
      setFormState('idle')
    }
  }

  async function addBook(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!details || !bookId) return
    setFormState('loading')
    try {
      const updated = await fetchJson<SeriesDetails>(`/api/series/${details.id}/books`, {
        method: 'POST',
        csrfToken,
        body: { bookId, volumeLabel: volumeLabel.trim() || '未命名冊次', sortOrder: details.books.length + 1 },
      })
      setDetails(updated)
      setBookId('')
      setVolumeLabel('')
      setMessage('已加入系列。')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '加入系列失敗。')
    } finally {
      setFormState('idle')
    }
  }

  async function addCharacter(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!details || !characterName.trim() || !characterVoice) return
    const selectedVoice = voiceOptions.find((option) => option.voice === characterVoice)
    if (!selectedVoice) return
    setFormState('loading')
    try {
      const updated = await fetchJson<SeriesDetails>(`/api/series/${details.id}/characters`, {
        method: 'POST',
        csrfToken,
        body: {
          canonicalName: characterName.trim(),
          role: characterRole,
          voiceProvider: selectedVoice.provider,
          voice: characterVoice,
          rate: profileDefaults.rate,
          pitch: profileDefaults.pitch,
          volume: profileDefaults.volume,
          notes: null,
        },
      })
      setDetails(updated)
      setCharacterName('')
      setAliasCharacterId((current) => current || updated.characters.at(-1)?.id || '')
      setMessage('角色與固定聲線已加入這個系列。')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '新增角色失敗。')
    } finally {
      setFormState('idle')
    }
  }

  async function addAlias(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!details || !aliasCharacterId || !alias.trim()) return
    setFormState('loading')
    try {
      const updated = await fetchJson<SeriesDetails>(`/api/series/${details.id}/characters/${aliasCharacterId}/aliases`, {
        method: 'POST',
        csrfToken,
        body: { alias: alias.trim() },
      })
      setDetails(updated)
      setAlias('')
      setMessage('別名已加入；重複或跨角色衝突會由系列規則拒絕。')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '加入別名失敗。')
    } finally {
      setFormState('idle')
    }
  }

  async function activateBatch() {
    if (!details || !batch) return
    setActivateDialogOpen(false)
    try {
      const activated = await fetchJson<RebuildBatch>(`/api/series/${details.id}/narration-rebuilds/${batch.id}/activate`, {
        method: 'POST',
        csrfToken,
        body: {},
      })
      setBatch(activated)
      setMessage('已原子啟用完整 series cast epoch；舊音訊保留為歷史版本。')
      void loadDetails(details.id)
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '尚未符合啟用條件。')
    }
  }

  if (state === 'loading') {
    return <main className="relative z-10 mx-auto max-w-7xl px-6 py-12 lg:px-10"><div className="library-state">正在讀取系列配音控制台…</div></main>
  }

  if (state === 'error') {
    return <main className="relative z-10 mx-auto max-w-7xl px-6 py-12 lg:px-10"><div className="library-state border-rose-300 text-rose-700">{message || '系列配音控制台暫時無法使用。'}</div></main>
  }

  return (
    <main className="relative z-10 mx-auto max-w-7xl px-6 py-12 lg:px-10">
      <section className="rounded-3xl border border-stone-200 bg-white p-5 sm:p-7">
        <p className="text-xs font-semibold uppercase tracking-[.22em] text-amber-700">Series cast</p>
        <h1 className="mt-2 font-serif text-3xl text-stone-900">多角色系列配音</h1>
        <p className="mt-3 max-w-3xl text-sm leading-6 text-stone-500">同一系列固定旁白與角色聲線。先逐章校正、鎖定劇本，再建立整批 staged 音訊；任何一冊失敗都不會偷換目前版本。</p>

        <form className="mt-6 grid gap-3 border-t border-stone-200 pt-5 sm:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto] sm:items-end" onSubmit={createSeries}>
          <label className="text-xs text-stone-500">新系列名稱<input className="auth-input mt-2" maxLength={200} onChange={(event) => setSeriesName(event.target.value)} required value={seriesName} /></label>
          <label className="text-xs text-stone-500">固定旁白聲線<select className="auth-input mt-2" onChange={(event) => setNarratorVoice(event.target.value)} value={narratorVoice}><option value="">選擇聲線</option>{voiceOptions.map((option) => <option key={`${option.provider}:${option.voice}`} value={option.voice}>{option.displayName}（{option.locale}）</option>)}</select></label>
          <button className="secondary-button disabled:cursor-wait disabled:opacity-60" disabled={formState === 'loading' || !seriesName.trim() || !narratorVoice} type="submit">建立系列</button>
        </form>
      </section>

      <section className="mt-6 grid gap-6 lg:grid-cols-[18rem_minmax(0,1fr)]">
        <aside className="rounded-3xl border border-stone-200 bg-white p-4">
          <h2 className="font-serif text-xl text-stone-900">我的系列</h2>
          <div className="mt-3 space-y-2">
            {series.map((item) => <button className={`w-full rounded-xl border p-3 text-left text-sm transition ${item.id === selectedSeriesId ? 'border-amber-300 bg-amber-50 text-amber-800' : 'border-stone-200 bg-stone-50 text-stone-500 hover:text-stone-800'}`} key={item.id} onClick={() => setSelectedSeriesId(item.id)} type="button"><span className="block truncate">{item.name}</span><span className="mt-1 block text-xs opacity-70">{item.bookCount} 冊 · {item.characterCount} 角色</span></button>)}
            {series.length === 0 && <p className="px-1 text-sm leading-6 text-stone-500">先建立第一個系列；不會用書名自動猜測成員。</p>}
          </div>
        </aside>

        <section>
          {!details && <div className="library-state">選擇或建立一個系列，開始設定固定角色聲線。</div>}
          {details && (
            <>
              <div className="rounded-3xl border border-stone-200 bg-white p-5 sm:p-7">
                <div className="flex flex-wrap items-start justify-between gap-4"><div><p className="text-xs text-stone-500">系列</p><h2 className="mt-1 font-serif text-3xl text-stone-900">{details.name}</h2><p className="mt-2 text-sm text-stone-500">旁白：{voiceLabel(details.narratorVoice, voiceOptions)} · 對白間隔 {details.defaultSpeakerPauseMs}ms</p></div><button className="secondary-button px-3 py-2 text-xs" onClick={() => previewVoice(details.narratorVoice)} type="button">播放固定示範句</button></div>

                <section className="mt-6 border-t border-stone-200 pt-5" aria-label="系列書籍"><h3 className="font-serif text-xl text-stone-900">系列書籍</h3><div className="mt-3 space-y-2">{details.books.slice().sort((left, right) => left.sortOrder - right.sortOrder).map((member) => <div className="rounded-xl border border-stone-200 bg-stone-50 p-3" key={member.id}><p className="text-sm text-stone-800">{member.volumeLabel} · {member.bookTitle}</p><p className="mt-1 text-xs text-stone-500">membership revision {member.membershipRevision}</p></div>)}{details.books.length === 0 && <p className="text-sm text-stone-500">尚未加入正文書籍。</p>}</div>
                  <form className="mt-4 grid gap-3 sm:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto] sm:items-end" onSubmit={addBook}><label className="text-xs text-stone-500">從可用正文加入<select className="auth-input mt-2" onChange={(event) => setBookId(event.target.value)} value={bookId}><option value="">選擇書籍</option>{eligibleBooks.map((book) => <option key={book.id} value={book.id}>{book.title}</option>)}</select></label><label className="text-xs text-stone-500">冊次標籤<input className="auth-input mt-2" maxLength={100} onChange={(event) => setVolumeLabel(event.target.value)} placeholder="第一冊" value={volumeLabel} /></label><button className="secondary-button" disabled={formState === 'loading' || !bookId} type="submit">加入系列</button></form>
                </section>

                <section className="mt-7 border-t border-stone-200 pt-5" aria-label="固定角色聲線"><h3 className="font-serif text-xl text-stone-900">固定角色聲線</h3><p className="mt-1 text-sm text-stone-500">角色與別名都限制在這個 owner 的系列；跨角色 alias 衝突會直接拒絕。</p><div className="mt-3 space-y-2">{details.characters.map((character) => <div className="rounded-xl border border-stone-200 bg-stone-50 p-3" key={character.id}><div className="flex flex-wrap items-center justify-between gap-2"><p className="text-sm text-stone-800">{character.canonicalName} · {character.role}</p><button className="text-xs text-amber-700" onClick={() => previewVoice(character.voice)} type="button">播放固定示範句</button></div><p className="mt-1 text-xs text-stone-500">{voiceLabel(character.voice, voiceOptions)} · 別名：{character.aliases.map((item) => item.value).join('、') || '—'}</p></div>)}</div>
                  <form className="mt-4 grid gap-3 sm:grid-cols-3" onSubmit={addCharacter}><label className="text-xs text-stone-500">角色名稱<input className="auth-input mt-2" maxLength={160} onChange={(event) => setCharacterName(event.target.value)} required value={characterName} /></label><label className="text-xs text-stone-500">角色定位<select className="auth-input mt-2" onChange={(event) => setCharacterRole(event.target.value as SeriesCharacter['role'])} value={characterRole}><option value="Main">主要</option><option value="Supporting">配角</option><option value="Minor">次要</option></select></label><label className="text-xs text-stone-500">聲線<select className="auth-input mt-2" onChange={(event) => setCharacterVoice(event.target.value)} value={characterVoice}>{voiceOptions.map((option) => <option key={`${option.provider}:${option.voice}`} value={option.voice}>{option.displayName}（{option.locale}）</option>)}</select></label><button className="secondary-button sm:col-span-3" disabled={formState === 'loading' || !characterName.trim() || !characterVoice} type="submit">加入角色與固定聲線</button></form>
                  <form className="mt-4 grid gap-3 sm:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto] sm:items-end" onSubmit={addAlias}><label className="text-xs text-stone-500">角色<select className="auth-input mt-2" onChange={(event) => setAliasCharacterId(event.target.value)} value={aliasCharacterId}><option value="">選擇角色</option>{details.characters.map((character) => <option key={character.id} value={character.id}>{character.canonicalName}</option>)}</select></label><label className="text-xs text-stone-500">別名<input className="auth-input mt-2" maxLength={160} onChange={(event) => setAlias(event.target.value)} placeholder="例如：小明" value={alias} /></label><button className="secondary-button" disabled={formState === 'loading' || !aliasCharacterId || !alias.trim()} type="submit">加入別名</button></form>
                </section>
              </div>

              {reviewEntries.length > 0 && <SpeechPlanReview characters={details.characters} csrfToken={csrfToken} entries={reviewEntries} onDraftUpdated={(draft) => setDraftsByChapter((current) => ({ ...current, [draft.chapterId]: draft }))} onRebuildCreated={(created) => { setBatch({ ...created, members: [] }); void fetchJson<RebuildBatch>(`/api/series/${details.id}/narration-rebuilds/${created.id}`).then(setBatch).catch(() => undefined) }} seriesId={details.id} />}
              {details.books.length > 0 && reviewEntries.length === 0 && <div className="library-state mt-8">正在讀取僅屬於你的章節與劇本狀態…</div>}

              {batch && <section className="mt-8 rounded-3xl border border-stone-200 bg-white p-5 sm:p-7" aria-label="staged rebuild 狀態"><div className="flex flex-wrap items-center justify-between gap-3"><div><p className="text-xs font-semibold uppercase tracking-[.22em] text-amber-700">Staged rebuild</p><h2 className="mt-1 font-serif text-2xl text-stone-900">{batch.status}</h2></div>{batch.status === 'ReadyToActivate' && <button className="secondary-button" onClick={() => setActivateDialogOpen(true)} type="button">人工啟用完整系列音訊</button>}</div><ul className="mt-4 space-y-2">{batch.members.map((member) => <li className="flex items-center justify-between gap-3 rounded-xl border border-stone-200 bg-stone-50 p-3 text-sm" key={member.id}><span className="text-stone-700">{details.books.find((book) => book.bookId === member.bookId)?.bookTitle ?? '系列書籍已變更'}</span><span className="text-xs text-stone-500">{member.status}</span></li>)}</ul></section>}
            </>
          )}
        </section>
      </section>
      <p aria-live="polite" className="mt-5 min-h-5 text-sm text-stone-500">{message}</p>
      <ConfirmDialog confirmLabel="啟用完整系列" description="這會在單一交易中切換整個系列的 current audio；只有所有 staged 冊次都完成時才能成功。" onCancel={() => setActivateDialogOpen(false)} onConfirm={() => void activateBatch()} open={activateDialogOpen} title="確定啟用完整多角色系列音訊？" />
    </main>
  )
}

import { useEffect, useState, type FormEvent } from 'react'

import { apiUrl, responseProblem } from './api'
import type { BookDetails, BookSummary } from './types'

type LoadState = 'idle' | 'loading' | 'ready' | 'error'

type SummaryExcerpt = {
  chapterId: string
  chapterTitle: string
  startOffset: number
  length: number
  text: string
}

type ExtractiveBookSummary = {
  bookId: string
  contentBookId: string
  kind: 'Extractive'
  generator: string
  version: string
  sourceHash: string
  generatedAt: string
  excerpts: SummaryExcerpt[]
}

type LocalLlmCharacterCandidate = {
  name: string
  confidence: 'high' | 'medium'
  dialogueEvidenceCount: number
  evidenceChapterNumbers: number[]
}

type LocalLlmCharacterAnalysis = {
  bookId: string
  contentBookId: string
  generator: 'local-ollama'
  model: string
  promptVersion: string
  sourceHash: string
  generatedAt: string
  candidates: LocalLlmCharacterCandidate[]
}

type ReadingNote = {
  id: string
  bookId: string
  chapterId: string | null
  body: string
  createdAt: string
  updatedAt: string
}

type BookInsightsPanelProps = {
  book: BookDetails
  books: BookSummary[]
  csrfToken: string
  onBookUpdated: (book: BookDetails) => void
}

export function BookInsightsPanel({ book, books, csrfToken, onBookUpdated }: BookInsightsPanelProps) {
  const [metadataTitle, setMetadataTitle] = useState(book.title)
  const [metadataAuthor, setMetadataAuthor] = useState(book.author)
  const [metadataCover, setMetadataCover] = useState(book.coverImageUrl ?? '')
  const [metadataState, setMetadataState] = useState<LoadState>('idle')
  const [metadataMessage, setMetadataMessage] = useState('')
  const [linkedContentId, setLinkedContentId] = useState(book.contentBookId ?? '')
  const [contentSelection, setContentSelection] = useState(book.contentBookId ?? '')
  const [linkState, setLinkState] = useState<LoadState>('idle')
  const [linkMessage, setLinkMessage] = useState('')
  const [summary, setSummary] = useState<ExtractiveBookSummary | null>(null)
  const [summaryState, setSummaryState] = useState<LoadState>('loading')
  const [summaryMessage, setSummaryMessage] = useState('')
  const [notes, setNotes] = useState<ReadingNote[]>([])
  const [notesState, setNotesState] = useState<LoadState>('loading')
  const [noteDraft, setNoteDraft] = useState('')
  const [noteMessage, setNoteMessage] = useState('')
  const [characterAnalysis, setCharacterAnalysis] = useState<LocalLlmCharacterAnalysis | null>(null)
  const [characterAnalysisState, setCharacterAnalysisState] = useState<LoadState>('loading')
  const [characterAnalysisMessage, setCharacterAnalysisMessage] = useState('')

  const eligibleContentBooks = books.filter((candidate) => candidate.id !== book.id
    && candidate.authorizedTextAvailable)
  const canGenerateSummary = book.authorizedTextAvailable || linkedContentId !== ''

  useEffect(() => {
    const controller = new AbortController()
    setLinkedContentId(book.contentBookId ?? '')
    setContentSelection(book.contentBookId ?? '')
    setSummary(null)
    setSummaryState('loading')
    setSummaryMessage('')
    setNotes([])
    setNotesState('loading')
    setNoteDraft('')
    setNoteMessage('')
    setCharacterAnalysis(null)
    setCharacterAnalysisState('loading')
    setCharacterAnalysisMessage('')

    fetch(apiUrl(`/api/books/${book.id}/character-analysis`), {
      credentials: 'same-origin',
      signal: controller.signal,
    }).then(async (response) => {
      if (response.status === 404 || response.status === 409) return null
      if (!response.ok) throw new Error(await responseProblem(response, '本機 LLM 角色分析讀取失敗。'))
      return response.json() as Promise<LocalLlmCharacterAnalysis>
    }).then((analysis) => {
      setCharacterAnalysis(analysis)
      setCharacterAnalysisState('ready')
    }).catch((error) => {
      if (error instanceof DOMException && error.name === 'AbortError') return
      setCharacterAnalysisState('error')
      setCharacterAnalysisMessage(error instanceof Error ? error.message : '本機 LLM 角色分析讀取失敗。')
    })

    fetch(apiUrl(`/api/books/${book.id}/summary`), {
      credentials: 'same-origin',
      signal: controller.signal,
    }).then(async (response) => {
      if (response.status === 404) return null
      if (!response.ok) throw new Error(await responseProblem(response, '摘要讀取失敗。'))
      return response.json() as Promise<ExtractiveBookSummary>
    }).then((value) => {
      setSummary(value)
      setSummaryState('ready')
    }).catch((error) => {
      if (error instanceof DOMException && error.name === 'AbortError') return
      setSummaryState('error')
      setSummaryMessage(error instanceof Error ? error.message : '摘要讀取失敗。')
    })

    fetch(apiUrl(`/api/books/${book.id}/notes`), {
      credentials: 'same-origin',
      signal: controller.signal,
    }).then(async (response) => {
      if (!response.ok) throw new Error(await responseProblem(response, '閱讀筆記讀取失敗。'))
      return response.json() as Promise<ReadingNote[]>
    }).then((items) => {
      setNotes(items)
      setNotesState('ready')
    }).catch((error) => {
      if (error instanceof DOMException && error.name === 'AbortError') return
      setNotesState('error')
      setNoteMessage(error instanceof Error ? error.message : '閱讀筆記讀取失敗。')
    })

    return () => controller.abort()
  }, [book.id, book.contentBookId])

  async function handleMetadataCorrection(clear = false) {
    setMetadataState('loading')
    setMetadataMessage(clear ? '正在還原來源 metadata…' : '正在保存人工校正…')
    try {
      const response = await fetch(apiUrl(`/api/books/${book.id}/metadata-corrections`), {
        method: 'PUT',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrfToken },
        body: JSON.stringify(clear
          ? { title: null, author: null, coverImageUrl: null }
          : { title: metadataTitle, author: metadataAuthor, coverImageUrl: metadataCover || null }),
      })
      if (!response.ok) throw new Error(await responseProblem(response, '書目校正保存失敗。'))
      const updated = await response.json() as BookDetails
      setMetadataTitle(updated.title)
      setMetadataAuthor(updated.author)
      setMetadataCover(updated.coverImageUrl ?? '')
      setMetadataState('ready')
      setMetadataMessage(clear ? '已還原最近同步的來源 metadata。' : '人工校正已保存；後續同步不會覆蓋。')
      onBookUpdated(updated)
    } catch (error) {
      setMetadataState('error')
      setMetadataMessage(error instanceof Error ? error.message : '書目校正保存失敗。')
    }
  }

  async function handleContentLink() {
    setLinkState('loading')
    setLinkMessage(contentSelection ? '正在連結你選擇的合法正文…' : '正在解除正文連結…')
    try {
      const response = await fetch(apiUrl(`/api/books/${book.id}/content-link`), {
        method: contentSelection ? 'PUT' : 'DELETE',
        credentials: 'same-origin',
        headers: contentSelection
          ? { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrfToken }
          : { 'X-CSRF-TOKEN': csrfToken },
        body: contentSelection ? JSON.stringify({ contentBookId: contentSelection }) : undefined,
      })
      if (!response.ok) throw new Error(await responseProblem(response, '正文連結失敗。'))
      setLinkedContentId(contentSelection)
      setSummary(null)
      setSummaryState('ready')
      setCharacterAnalysis(null)
      setCharacterAnalysisState('ready')
      setCharacterAnalysisMessage(contentSelection ? '正文已變更，舊的本機 LLM 分析已清除。' : '已解除正文連結，舊的本機 LLM 分析已清除。')
      setLinkState('ready')
      setLinkMessage(contentSelection ? '已明確連結這份合法正文。' : '已解除正文連結。')
      onBookUpdated({ ...book, contentBookId: contentSelection || null })
    } catch (error) {
      setLinkState('error')
      setLinkMessage(error instanceof Error ? error.message : '正文連結失敗。')
    }
  }

  async function handleGenerateSummary() {
    setSummaryState('loading')
    setSummaryMessage('正在從已保存的合法正文挑選原文句子…')
    try {
      const response = await fetch(apiUrl(`/api/books/${book.id}/summary`), {
        method: 'PUT',
        credentials: 'same-origin',
        headers: { 'X-CSRF-TOKEN': csrfToken },
      })
      if (!response.ok) throw new Error(await responseProblem(response, '摘要建立失敗。'))
      setSummary(await response.json() as ExtractiveBookSummary)
      setSummaryState('ready')
      setSummaryMessage('擷取式摘要已建立；每段都保留原章節與文字位置。')
    } catch (error) {
      setSummaryState('error')
      setSummaryMessage(error instanceof Error ? error.message : '摘要建立失敗。')
    }
  }

  async function handleCopyCharacterCandidateName(candidate: LocalLlmCharacterCandidate) {
    try {
      await navigator.clipboard.writeText(candidate.name)
      setCharacterAnalysisMessage(`已複製「${candidate.name}」。這只是本機 LLM 待確認候選，尚未建立角色或指派聲線。`)
    } catch {
      setCharacterAnalysisMessage(`「${candidate.name}」：這個瀏覽器不支援自動複製，請手動選取文字。`)
    }
  }

  async function handleGenerateCharacterAnalysis() {
    setCharacterAnalysisState('loading')
    setCharacterAnalysisMessage('本機 LLM 正在逐章讀取完整正文並彙整候選；此期間會獨占本機模型，完成後立即卸載。')
    try {
      const response = await fetch(apiUrl(`/api/books/${book.id}/character-analysis`), {
        method: 'PUT',
        credentials: 'same-origin',
        headers: { 'X-CSRF-TOKEN': csrfToken },
      })
      if (!response.ok) throw new Error(await responseProblem(response, '本機 LLM 角色分析失敗。'))
      const analysis = await response.json() as LocalLlmCharacterAnalysis
      setCharacterAnalysis(analysis)
      setCharacterAnalysisState('ready')
      setCharacterAnalysisMessage('本機 LLM 分析完成；所有候選仍待人工確認，沒有建立角色、指派聲線或產生音檔。')
    } catch (error) {
      setCharacterAnalysisState('error')
      setCharacterAnalysisMessage(error instanceof Error ? error.message : '本機 LLM 角色分析失敗。')
    }
  }

  async function handleAddNote(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const body = noteDraft.trim()
    if (!body) return
    setNotesState('loading')
    setNoteMessage('正在保存你的筆記…')
    try {
      const response = await fetch(apiUrl(`/api/books/${book.id}/notes`), {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrfToken },
        body: JSON.stringify({ body, chapterId: null }),
      })
      if (!response.ok) throw new Error(await responseProblem(response, '筆記保存失敗。'))
      const note = await response.json() as ReadingNote
      setNotes((current) => [note, ...current])
      setNoteDraft('')
      setNotesState('ready')
      setNoteMessage('你的手動閱讀筆記已保存。')
    } catch (error) {
      setNotesState('error')
      setNoteMessage(error instanceof Error ? error.message : '筆記保存失敗。')
    }
  }

  async function handleDeleteNote(noteId: string) {
    if (!window.confirm('確定刪除這則閱讀筆記？')) return
    setNotesState('loading')
    try {
      const response = await fetch(apiUrl(`/api/books/${book.id}/notes/${noteId}`), {
        method: 'DELETE',
        credentials: 'same-origin',
        headers: { 'X-CSRF-TOKEN': csrfToken },
      })
      if (!response.ok) throw new Error(await responseProblem(response, '筆記刪除失敗。'))
      setNotes((current) => current.filter((note) => note.id !== noteId))
      setNotesState('ready')
      setNoteMessage('筆記已刪除。')
    } catch (error) {
      setNotesState('error')
      setNoteMessage(error instanceof Error ? error.message : '筆記刪除失敗。')
    }
  }

  return (
    <div className="mt-5 space-y-5">
      {book.sourceProvider === 'books-com-tw' && (
        <section className="rounded-2xl border border-sky-100 bg-sky-50/70 p-4" aria-label="作者與封面校正">
          <h4 className="font-serif text-lg text-stone-800">書名、作者與封面校正</h4>
          <p className="mt-2 text-xs leading-6 text-stone-500">人工校正只影響你的 StoryVoice 書庫顯示；博客來同步資料仍保留，重新同步也不會覆蓋校正。</p>
          <div className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2">
            <label className="text-xs text-stone-500">顯示書名<input className="auth-input mt-2" maxLength={500} onChange={(event) => setMetadataTitle(event.target.value)} value={metadataTitle} /></label>
            <label className="text-xs text-stone-500">顯示作者<input className="auth-input mt-2" maxLength={300} onChange={(event) => setMetadataAuthor(event.target.value)} value={metadataAuthor} /></label>
            <label className="text-xs text-stone-500 sm:col-span-2">封面圖片網址<input className="auth-input mt-2" maxLength={2000} onChange={(event) => setMetadataCover(event.target.value)} placeholder="https://…" type="url" value={metadataCover} /></label>
          </div>
          <div className="mt-3 flex flex-wrap gap-2">
            <button className="secondary-button disabled:cursor-wait disabled:opacity-60" disabled={metadataState === 'loading' || !metadataTitle.trim() || !metadataAuthor.trim()} onClick={() => handleMetadataCorrection(false)} type="button">保存校正</button>
            <button className="text-xs text-stone-500 transition hover:text-stone-700 disabled:opacity-40" disabled={metadataState === 'loading' || (!book.titleCorrection && !book.authorCorrection && !book.coverImageUrlCorrection)} onClick={() => handleMetadataCorrection(true)} type="button">還原來源資料</button>
          </div>
          <p className={`mt-2 min-h-5 text-xs ${metadataState === 'error' ? 'text-rose-600' : 'text-stone-500'}`} role="status">{metadataMessage}</p>
        </section>
      )}
      {book.sourceProvider === 'books-com-tw' && (
        <section className="rounded-2xl border border-orange-100 bg-orange-50/70 p-4" aria-label="合法正文連結">
          <h4 className="font-serif text-lg text-stone-800">連結你合法持有的正文</h4>
          <p className="mt-2 text-xs leading-6 text-stone-500">只列出你已上傳且成功解析的 EPUB／TXT；StoryVoice 不會依書名自動配對，也不會抓取博客來正文。</p>
          <div className="mt-3 flex flex-col gap-2 sm:flex-row">
            <select aria-label="選擇已授權正文" className="auth-input min-w-0 flex-1" onChange={(event) => setContentSelection(event.target.value)} value={contentSelection}>
              <option value="">不連結正文</option>
              {eligibleContentBooks.map((candidate) => <option key={candidate.id} value={candidate.id}>{candidate.title} · {candidate.chapterCount} 章</option>)}
            </select>
            <button className="secondary-button shrink-0 disabled:cursor-wait disabled:opacity-60" disabled={linkState === 'loading' || contentSelection === linkedContentId} onClick={handleContentLink} type="button">{contentSelection ? '儲存連結' : '解除連結'}</button>
          </div>
          {eligibleContentBooks.length === 0 && <p className="mt-3 text-xs text-amber-700">目前沒有可連結的合法正文；請先由上方匯入無 DRM EPUB／TXT。</p>}
          <p className={`mt-2 min-h-5 text-xs ${linkState === 'error' ? 'text-rose-600' : 'text-stone-500'}`} role="status">{linkMessage}</p>
        </section>
      )}

      <section className="rounded-2xl border border-emerald-100 bg-emerald-50/70 p-4" aria-label="擷取式摘要">
        <div className="flex flex-col justify-between gap-3 sm:flex-row sm:items-start">
          <div>
            <h4 className="font-serif text-lg text-stone-800">擷取式摘要 <span className="text-xs font-sans text-emerald-700">原文選句，非 AI 改寫</span></h4>
            <p className="mt-2 text-xs leading-6 text-stone-500">依章節順序挑選原文句子，保留可驗證的章節與文字位置。</p>
          </div>
          <button className="secondary-button shrink-0 disabled:cursor-not-allowed disabled:opacity-40" disabled={!canGenerateSummary || summaryState === 'loading'} onClick={handleGenerateSummary} type="button">{summary ? '重新驗證摘要' : '建立摘要'}</button>
        </div>
        {!canGenerateSummary && <p className="mt-3 text-xs text-amber-700">等待合法正文：上傳 EPUB／TXT，並在外部書目上明確選擇連結。</p>}
        {summary && (
          <ol className="mt-4 space-y-3">
            {summary.excerpts.map((excerpt) => (
              <li className="rounded-xl border border-stone-200 bg-stone-50 p-3" key={`${excerpt.chapterId}-${excerpt.startOffset}`}>
                <p className="text-[10px] uppercase tracking-widest text-emerald-700">{excerpt.chapterTitle} · offset {excerpt.startOffset}</p>
                <p className="mt-2 text-sm leading-7 text-stone-600">{excerpt.text}</p>
              </li>
            ))}
          </ol>
        )}
        <p className={`mt-3 min-h-5 text-xs ${summaryState === 'error' ? 'text-rose-600' : 'text-stone-500'}`} role="status">{summaryState === 'loading' && !summaryMessage ? '正在讀取摘要…' : summaryMessage}</p>
      </section>

      <section className="rounded-2xl border border-rose-100 bg-rose-50/70 p-4" aria-label="本機 LLM 角色分析">
        <div className="flex flex-col justify-between gap-3 sm:flex-row sm:items-start">
          <div>
            <h4 className="font-serif text-lg text-stone-800">本機 LLM 角色分析 <span className="text-xs font-sans text-rose-700">分析建議，待人工確認</span></h4>
            <p className="mt-2 text-xs leading-6 text-stone-500">每章完整交給本機模型閱讀後再匯總；只保留高／中信心且正文確實出現的名稱。候選不會自動建立角色、覆蓋系列資料、指派聲線或產生音檔。</p>
          </div>
          <button className="secondary-button shrink-0 disabled:cursor-not-allowed disabled:opacity-40" disabled={!canGenerateSummary || characterAnalysisState === 'loading'} onClick={handleGenerateCharacterAnalysis} type="button">{characterAnalysis ? '重新以本機 LLM 分析' : '以本機 LLM 分析'}</button>
        </div>
        {!canGenerateSummary && <p className="mt-3 text-xs text-amber-700">等待合法正文：上傳 EPUB／TXT，並在外部書目上明確選擇連結。</p>}
        {characterAnalysis && (
          <>
            <p className="mt-3 text-[10px] tracking-wide text-stone-400">模型 {characterAnalysis.model} · {characterAnalysis.promptVersion} · {new Date(characterAnalysis.generatedAt).toLocaleString('zh-TW')}</p>
            {characterAnalysis.candidates.length > 0 && (
              <ul className="mt-4 flex flex-wrap gap-2">
                {characterAnalysis.candidates.map((candidate) => (
                  <li key={candidate.name}>
                    <button
                      className="rounded-full border border-rose-200 bg-white px-3 py-1.5 text-left text-xs text-stone-600 transition hover:border-rose-400 hover:text-rose-700"
                      onClick={() => void handleCopyCharacterCandidateName(candidate)}
                      title={`本機 LLM：${candidate.confidence === 'high' ? '高' : '中'}信心；章節 ${candidate.evidenceChapterNumbers.join('、')}`}
                      type="button"
                    >
                      <span className="font-medium text-stone-800">{candidate.name}</span>
                      <span className={`ml-1.5 ${candidate.confidence === 'high' ? 'text-rose-700' : 'text-amber-700'}`}>{candidate.confidence === 'high' ? '高信心' : '中信心'}</span>
                      <span className="ml-1.5 text-stone-400">× {candidate.dialogueEvidenceCount} 句 · 第 {candidate.evidenceChapterNumbers.join('、')} 章</span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
            {characterAnalysis.candidates.length === 0 && <p className="mt-3 text-xs text-stone-400">本機模型沒有足夠證據列出命名說話者；未知比亂填好。</p>}
          </>
        )}
        {canGenerateSummary && characterAnalysisState === 'ready' && !characterAnalysis && <p className="mt-3 text-xs text-stone-400">尚未執行本機 LLM 分析；按上方按鈕才會使用本機模型。</p>}
        <p className={`mt-3 min-h-5 text-xs ${characterAnalysisState === 'error' ? 'text-rose-600' : 'text-stone-500'}`} role="status">{characterAnalysisState === 'loading' && !characterAnalysisMessage ? '正在讀取本機 LLM 分析…' : characterAnalysisMessage}</p>
      </section>

      <section className="rounded-2xl border border-violet-100 bg-violet-50/70 p-4" aria-label="我的閱讀筆記">
        <h4 className="font-serif text-lg text-stone-800">我的閱讀筆記</h4>
        <p className="mt-2 text-xs leading-6 text-stone-500">這裡只保存你親自輸入的帳號筆記；書目只有 metadata 也能記事，不代表 StoryVoice 擁有正文。</p>
        <form className="mt-3 space-y-3" onSubmit={handleAddNote}>
          <textarea aria-label="閱讀筆記內容" className="auth-input min-h-28 resize-y" maxLength={4000} onChange={(event) => setNoteDraft(event.target.value)} placeholder="寫下你的想法、待查資料或閱讀進度…" value={noteDraft} />
          <div className="flex justify-between gap-3 text-xs text-stone-400">
            <span>{noteDraft.length}／4000</span>
            <button className="secondary-button disabled:cursor-not-allowed disabled:opacity-40" disabled={!noteDraft.trim() || notesState === 'loading'} type="submit">保存筆記</button>
          </div>
        </form>
        {notes.length > 0 && (
          <ul className="mt-4 space-y-3">
            {notes.map((note) => (
              <li className="rounded-xl border border-stone-200 bg-stone-50 p-3" key={note.id}>
                <p className="whitespace-pre-wrap text-sm leading-7 text-stone-600">{note.body}</p>
                <div className="mt-3 flex items-center justify-between gap-3 text-[10px] text-stone-400">
                  <time dateTime={note.updatedAt}>{new Date(note.updatedAt).toLocaleString('zh-TW')}</time>
                  <button className="text-rose-500 transition hover:text-rose-700" onClick={() => handleDeleteNote(note.id)} type="button">刪除</button>
                </div>
              </li>
            ))}
          </ul>
        )}
        {notesState === 'ready' && notes.length === 0 && <p className="mt-4 text-xs text-stone-400">尚未建立閱讀筆記。</p>}
        <p className={`mt-3 min-h-5 text-xs ${notesState === 'error' ? 'text-rose-600' : 'text-stone-500'}`} role="status">{notesState === 'loading' && !noteMessage ? '正在讀取筆記…' : noteMessage}</p>
      </section>
    </div>
  )
}

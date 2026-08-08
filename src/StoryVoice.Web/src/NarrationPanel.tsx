import { useCallback, useEffect, useMemo, useState } from 'react'

type NarrationBook = {
  id: string
  contentBookId: string | null
  authorizedTextAvailable: boolean
}

type NarrationJob = {
  id: string
  bookId: string
  contentBookId: string
  sourceHash: string
  voice: string
  rate: string
  status: 'Queued' | 'Running' | 'Completed' | 'Failed' | 'Cancelled'
  progressPercent: number
  attempts: number
  cancellationRequested: boolean
  errorCode: string | null
  audioBytes: number | null
  rightsAttestedAt: string
  createdAt: string
  updatedAt: string
  completedAt: string | null
}

type Props = {
  book: NarrationBook
  csrfToken: string
}

const basePath = import.meta.env.BASE_URL.replace(/\/+$/, '')
const apiUrl = (path: string) => `${basePath}${path.startsWith('/') ? path : `/${path}`}`

async function responseProblem(response: Response, fallback: string) {
  const body = await response.json().catch(() => null) as { detail?: string } | null
  return body?.detail ?? fallback
}

const statusLabels: Record<NarrationJob['status'], string> = {
  Queued: '排隊中',
  Running: '正在產生語音',
  Completed: '語音已完成',
  Failed: '產製失敗',
  Cancelled: '已取消',
}

export function NarrationPanel({ book, csrfToken }: Props) {
  const [jobs, setJobs] = useState<NarrationJob[]>([])
  const [attested, setAttested] = useState(false)
  const [loading, setLoading] = useState(true)
  const [message, setMessage] = useState('')
  const eligible = book.authorizedTextAvailable || book.contentBookId !== null
  const active = useMemo(
    () => jobs.some((job) => job.status === 'Queued' || job.status === 'Running'),
    [jobs],
  )

  const loadJobs = useCallback(async (signal?: AbortSignal) => {
    const response = await fetch(apiUrl(`/api/books/${book.id}/narrations/`), {
      credentials: 'same-origin',
      signal,
    })
    if (!response.ok) throw new Error(await responseProblem(response, '朗讀工作讀取失敗。'))
    setJobs(await response.json() as NarrationJob[])
  }, [book.id])

  useEffect(() => {
    const controller = new AbortController()
    setLoading(true)
    setMessage('')
    loadJobs(controller.signal)
      .catch((error) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setMessage(error instanceof Error ? error.message : '朗讀工作讀取失敗。')
      })
      .finally(() => setLoading(false))
    return () => controller.abort()
  }, [loadJobs])

  useEffect(() => {
    if (!active) return
    const timer = window.setInterval(() => {
      loadJobs().catch(() => undefined)
    }, 2_000)
    return () => window.clearInterval(timer)
  }, [active, loadJobs])

  async function createNarration() {
    setLoading(true)
    setMessage('正在建立神經語音工作…')
    try {
      const response = await fetch(apiUrl(`/api/books/${book.id}/narrations/`), {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrfToken },
        body: JSON.stringify({ rightsAttested: attested }),
      })
      if (!response.ok) throw new Error(await responseProblem(response, '朗讀工作建立失敗。'))
      const job = await response.json() as NarrationJob
      setJobs((current) => [job, ...current.filter((item) => item.id !== job.id)])
      setMessage('已排入語音產製；頁面會自動更新進度。')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '朗讀工作建立失敗。')
    } finally {
      setLoading(false)
    }
  }

  async function cancelNarration(jobId: string) {
    setMessage('正在取消朗讀工作…')
    try {
      const response = await fetch(apiUrl(`/api/narrations/${jobId}/cancel`), {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrfToken },
        body: '{}',
      })
      if (!response.ok) throw new Error(await responseProblem(response, '朗讀工作取消失敗。'))
      const job = await response.json() as NarrationJob
      setJobs((current) => current.map((item) => item.id === job.id ? job : item))
      setMessage('取消要求已送出。')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '朗讀工作取消失敗。')
    }
  }

  return (
    <section aria-label="AI 朗讀與有聲書" className="mt-5 rounded-[1.75rem] border border-amber-200 bg-amber-50/70 p-5 text-stone-800">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-xs font-semibold uppercase tracking-[0.22em] text-amber-800">合法正文神經語音</p>
          <h3 className="mt-1 text-xl font-black text-stone-950">AI 朗讀與有聲書</h3>
          <p className="mt-2 max-w-3xl text-sm leading-6 text-stone-700">
            StoryVoice 只處理你上傳或明確連結的合法 EPUB／TXT 正文。文字會送往 Microsoft Edge 神經語音服務，音訊完成後保存於你的私人 StoryVoice 帳號；這與博客來官方閱讀器的 TTS 標記是不同能力。
          </p>
        </div>
        <span className="rounded-full bg-white px-3 py-1 text-xs font-bold text-amber-800 shadow-sm">
          {eligible ? '正文已就緒' : '等待合法正文'}
        </span>
      </div>

      {!eligible && (
        <p className="mt-4 rounded-2xl border border-amber-200 bg-white p-4 text-sm leading-6">
          目前只有書目資料；先上傳你合法持有、無 DRM 的 EPUB／TXT，再於上方明確連結正文。博客來官方 TTS 標記不等於 StoryVoice 音訊。
        </p>
      )}

      {eligible && (
        <div className="mt-4 rounded-2xl border border-amber-200 bg-white p-4">
          <label className="flex items-start gap-3 text-sm leading-6">
            <input
              checked={attested}
              className="mt-1 h-4 w-4 accent-amber-700"
              onChange={(event) => setAttested(event.target.checked)}
              type="checkbox"
            />
            <span>我確認自己有權將這份合法正文送交上述語音服務處理，且內容不含需要繞過 DRM 才能取得的資料。</span>
          </label>
          <button
            className="mt-3 rounded-full bg-stone-950 px-5 py-2.5 text-sm font-bold text-white disabled:cursor-not-allowed disabled:opacity-45"
            disabled={!attested || loading || active}
            onClick={createNarration}
            type="button"
          >
            {active ? '語音產製中' : '建立 AI 朗讀'}
          </button>
        </div>
      )}

      <div aria-live="polite" className="mt-3 text-sm font-semibold text-stone-700">{message}</div>

      <div className="mt-4 space-y-3">
        {jobs.map((job) => (
          <article className="rounded-2xl border border-stone-200 bg-white p-4" key={job.id}>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <p className="font-black text-stone-950">{statusLabels[job.status]}</p>
                <p className="mt-1 text-xs text-stone-500">{job.voice} · 語速 {job.rate} · 嘗試 {job.attempts}/3</p>
              </div>
              <span className="rounded-full bg-stone-100 px-3 py-1 text-xs font-bold text-stone-700">{job.progressPercent}%</span>
            </div>
            {(job.status === 'Queued' || job.status === 'Running') && (
              <button
                className="mt-3 rounded-full border border-stone-300 px-4 py-2 text-xs font-bold text-stone-700"
                onClick={() => cancelNarration(job.id)}
                type="button"
              >
                取消工作
              </button>
            )}
            {job.status === 'Completed' && (
              <audio className="mt-4 w-full" controls preload="metadata" src={apiUrl(`/api/narrations/${job.id}/audio`)}>
                你的瀏覽器不支援音訊播放。
              </audio>
            )}
            {job.status === 'Failed' && (
              <p className="mt-3 text-sm text-rose-700">語音服務未能完成這次工作（{job.errorCode ?? 'provider_failed'}）。重新確認授權後可再次建立。</p>
            )}
          </article>
        ))}
        {!loading && jobs.length === 0 && eligible && (
          <p className="text-sm text-stone-600">這本書尚未建立 StoryVoice 音訊。</p>
        )}
      </div>
    </section>
  )
}

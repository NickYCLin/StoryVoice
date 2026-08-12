import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'

import { fetchJson } from '../api'
import { useAuthedOutletContext } from '../authOutletContext'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { StatusMessage } from '../components/StatusMessage'
import type { BookCollectionSummary } from '../collectionsTypes'

type LoadState = 'idle' | 'loading' | 'ready' | 'error'

export function CollectionsPage() {
  const { csrfToken } = useAuthedOutletContext()
  const [collections, setCollections] = useState<BookCollectionSummary[]>([])
  const [listState, setListState] = useState<LoadState>('loading')
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [createState, setCreateState] = useState<LoadState>('idle')
  const [createMessage, setCreateMessage] = useState('')
  const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null)

  const loadCollections = useCallback(async () => {
    setListState('loading')
    try {
      const items = await fetchJson<BookCollectionSummary[]>('/api/collections')
      setCollections(items)
      setListState('ready')
    } catch {
      setListState('error')
    }
  }, [])

  useEffect(() => {
    void loadCollections()
  }, [loadCollections])

  async function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!name.trim()) return

    setCreateState('loading')
    setCreateMessage('正在建立書冊…')
    try {
      await fetchJson('/api/collections', {
        method: 'POST',
        csrfToken,
        body: { name, description: description.trim() || null },
      })
      setName('')
      setDescription('')
      setCreateState('ready')
      setCreateMessage('書冊已建立。')
      await loadCollections()
    } catch (error) {
      setCreateState('error')
      setCreateMessage(error instanceof Error ? error.message : '書冊建立失敗。')
    }
  }

  async function handleDelete(collectionId: string) {
    setPendingDeleteId(null)
    try {
      await fetchJson(`/api/collections/${collectionId}`, { method: 'DELETE', csrfToken })
      await loadCollections()
    } catch {
      setListState('error')
    }
  }

  return (
    <section className="relative z-10 mx-auto max-w-7xl px-6 py-12 lg:px-10">
      <div className="mb-10">
        <p className="eyebrow">Collections</p>
        <h1 className="mt-3 font-serif text-4xl tracking-tight sm:text-5xl">把同系列的書整理成書冊。</h1>
        <p className="mt-4 max-w-2xl text-sm leading-7 text-stone-600">
          書冊只是單純的書本分類收藏，跟角色配音無關；可以把系列作品排序收在一起，也可以唯讀分享給其他 StoryVoice 使用者。
        </p>
      </div>

      <form className="mb-10 overflow-hidden rounded-3xl border border-amber-200 bg-gradient-to-br from-amber-50 via-white to-orange-50 p-5 sm:p-7" onSubmit={handleCreate}>
        <h2 className="font-serif text-2xl text-stone-900">建立新書冊</h2>
        <div className="mt-4 grid gap-4 sm:grid-cols-2">
          <label className="text-xs text-stone-500">
            書冊名稱
            <input className="auth-input mt-2" maxLength={200} onChange={(event) => setName(event.target.value)} required value={name} />
          </label>
          <label className="text-xs text-stone-500">
            描述（選填）
            <input className="auth-input mt-2" maxLength={2000} onChange={(event) => setDescription(event.target.value)} value={description} />
          </label>
        </div>
        <div className="mt-4 flex items-center gap-3">
          <button className="primary-button disabled:cursor-wait disabled:opacity-60" disabled={createState === 'loading' || !name.trim()} type="submit">建立書冊</button>
          <StatusMessage message={createMessage} status={createState} />
        </div>
      </form>

      {listState === 'loading' && <div className="library-state">正在讀取書冊…</div>}
      {listState === 'error' && <div className="library-state border-rose-300 text-rose-700">書冊讀取失敗，請重新整理頁面。</div>}
      {listState === 'ready' && collections.length === 0 && (
        <div className="library-state min-h-52">
          <div>
            <h3 className="font-serif text-2xl text-stone-800">還沒有書冊。</h3>
            <p className="mt-3 text-sm text-stone-500">用上面的表單建立第一個書冊，再把書庫裡的書加進去。</p>
          </div>
        </div>
      )}
      {listState === 'ready' && collections.length > 0 && (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {collections.map((collection) => (
            <div className="book-card flex-col items-start gap-3" key={collection.id}>
              <Link className="block w-full" to={`/collections/${collection.id}`}>
                <p className="font-serif text-xl text-stone-900">{collection.name}</p>
                {collection.description && <p className="mt-2 line-clamp-2 text-sm text-stone-500">{collection.description}</p>}
                <div className="mt-4 flex flex-wrap items-center gap-3 text-xs text-stone-400">
                  <span>{collection.bookCount} 本書</span><span>·</span><span>{collection.shareCount} 個分享</span>
                </div>
              </Link>
              <button
                className="mt-1 text-xs text-rose-500 transition hover:text-rose-700"
                onClick={() => setPendingDeleteId(collection.id)}
                type="button"
              >
                刪除書冊
              </button>
            </div>
          ))}
        </div>
      )}

      <ConfirmDialog
        confirmLabel="刪除書冊"
        description="刪除後這個書冊的成員關係與分享都會一併移除，不會影響書庫裡的原始書籍。"
        onCancel={() => setPendingDeleteId(null)}
        onConfirm={() => pendingDeleteId && void handleDelete(pendingDeleteId)}
        open={pendingDeleteId !== null}
        title="確定刪除這個書冊？"
      />
    </section>
  )
}

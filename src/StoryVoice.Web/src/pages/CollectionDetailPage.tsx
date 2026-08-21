import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'

import { fetchJson } from '../api'
import { useAuthedOutletContext } from '../authOutletContext'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { StatusMessage } from '../components/StatusMessage'
import type { BookCollectionDetails } from '../collectionsTypes'
import type { BookSummary } from '../types'

type LoadState = 'idle' | 'loading' | 'ready' | 'error'
type PendingRemoval = { kind: 'book'; id: string; label: string } | { kind: 'share'; id: string; label: string }

export function CollectionDetailPage() {
  const { csrfToken } = useAuthedOutletContext()
  const { collectionId } = useParams<{ collectionId: string }>()
  const navigate = useNavigate()

  const [collection, setCollection] = useState<BookCollectionDetails | null>(null)
  const [detailState, setDetailState] = useState<LoadState>('loading')
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [renameState, setRenameState] = useState<LoadState>('idle')
  const [renameMessage, setRenameMessage] = useState('')

  const [libraryBooks, setLibraryBooks] = useState<BookSummary[]>([])
  const [addBookId, setAddBookId] = useState('')
  const [addVolumeLabel, setAddVolumeLabel] = useState('')
  const [addSortOrder, setAddSortOrder] = useState('1')
  const [addBookState, setAddBookState] = useState<LoadState>('idle')
  const [addBookMessage, setAddBookMessage] = useState('')

  const [shareEmail, setShareEmail] = useState('')
  const [shareState, setShareState] = useState<LoadState>('idle')
  const [shareMessage, setShareMessage] = useState('')

  const [pendingRemoval, setPendingRemoval] = useState<PendingRemoval | null>(null)

  const loadCollection = useCallback(async () => {
    if (!collectionId) return
    setDetailState('loading')
    try {
      const details = await fetchJson<BookCollectionDetails>(`/api/collections/${collectionId}`)
      setCollection(details)
      setName(details.name)
      setDescription(details.description ?? '')
      setDetailState('ready')
    } catch {
      setDetailState('error')
    }
  }, [collectionId])

  useEffect(() => {
    void loadCollection()
  }, [loadCollection])

  useEffect(() => {
    fetchJson<BookSummary[]>('/api/books').then(setLibraryBooks).catch(() => setLibraryBooks([]))
  }, [])

  const eligibleBooks = useMemo(() => {
    const memberIds = new Set((collection?.books ?? []).map((entry) => entry.bookId))
    return libraryBooks.filter((book) => book.status !== 'Linked' && !memberIds.has(book.id))
  }, [collection, libraryBooks])

  async function handleRename(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!collectionId || !name.trim()) return

    setRenameState('loading')
    setRenameMessage('正在保存書冊資訊…')
    try {
      const updated = await fetchJson<BookCollectionDetails>(`/api/collections/${collectionId}`, {
        method: 'PUT',
        csrfToken,
        body: { name, description: description.trim() || null },
      })
      setCollection(updated)
      setRenameState('ready')
      setRenameMessage('已保存。')
    } catch (error) {
      setRenameState('error')
      setRenameMessage(error instanceof Error ? error.message : '保存失敗。')
    }
  }

  async function handleAddBook(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!collectionId || !addBookId) return

    setAddBookState('loading')
    setAddBookMessage('正在加入書籍…')
    try {
      const updated = await fetchJson<BookCollectionDetails>(`/api/collections/${collectionId}/books`, {
        method: 'POST',
        csrfToken,
        body: {
          bookId: addBookId,
          volumeLabel: addVolumeLabel.trim() || null,
          sortOrder: Number(addSortOrder) || 0,
        },
      })
      setCollection(updated)
      setAddBookId('')
      setAddVolumeLabel('')
      setAddSortOrder(String((updated.books.length ?? 0) + 1))
      setAddBookState('ready')
      setAddBookMessage('已加入書冊。')
    } catch (error) {
      setAddBookState('error')
      setAddBookMessage(error instanceof Error ? error.message : '加入失敗。')
    }
  }

  async function handleRemoveBook(bookId: string) {
    setPendingRemoval(null)
    if (!collectionId) return
    try {
      const updated = await fetchJson<BookCollectionDetails>(`/api/collections/${collectionId}/books/${bookId}`, {
        method: 'DELETE',
        csrfToken,
      })
      setCollection(updated)
    } catch (error) {
      // 單次操作失敗不應把整頁換成「找不到書冊」；資料還在，就地顯示錯誤。
      setAddBookState('error')
      setAddBookMessage(error instanceof Error ? error.message : '移除書籍失敗，請再試一次。')
    }
  }

  async function handleAddShare(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!collectionId || !shareEmail.trim()) return

    setShareState('loading')
    setShareMessage('正在建立分享…')
    try {
      const updated = await fetchJson<BookCollectionDetails>(`/api/collections/${collectionId}/shares`, {
        method: 'POST',
        csrfToken,
        body: { email: shareEmail.trim() },
      })
      setCollection(updated)
      setShareEmail('')
      setShareState('ready')
      setShareMessage('已分享（唯讀）。')
    } catch (error) {
      setShareState('error')
      setShareMessage(error instanceof Error ? error.message : '分享失敗，請確認對方 email 已在 StoryVoice 註冊。')
    }
  }

  async function handleRevokeShare(shareId: string) {
    setPendingRemoval(null)
    if (!collectionId) return
    try {
      const updated = await fetchJson<BookCollectionDetails>(`/api/collections/${collectionId}/shares/${shareId}`, {
        method: 'DELETE',
        csrfToken,
      })
      setCollection(updated)
    } catch (error) {
      setShareState('error')
      setShareMessage(error instanceof Error ? error.message : '撤銷分享失敗，請再試一次。')
    }
  }

  if (detailState === 'loading') {
    return <section className="relative z-10 mx-auto max-w-5xl px-6 py-12 lg:px-10"><div className="library-state">正在讀取書冊…</div></section>
  }

  if (detailState === 'error' || !collection) {
    return (
      <section className="relative z-10 mx-auto max-w-5xl px-6 py-12 lg:px-10">
        <div className="library-state border-rose-300 text-rose-700">找不到這個書冊，或已被刪除。</div>
        <button className="secondary-button mt-6" onClick={() => navigate('/collections')} type="button">回到書冊列表</button>
      </section>
    )
  }

  return (
    <section className="relative z-10 mx-auto max-w-5xl px-6 py-12 lg:px-10">
      <Link className="text-sm text-stone-500 transition hover:text-stone-800" to="/collections">← 回到書冊列表</Link>

      <form className="mt-6 rounded-3xl border border-stone-200 bg-white p-5 sm:p-7" onSubmit={handleRename}>
        <label className="block text-xs text-stone-500">
          書冊名稱
          <input className="auth-input mt-2 font-serif text-2xl" maxLength={200} onChange={(event) => setName(event.target.value)} required value={name} />
        </label>
        <label className="mt-4 block text-xs text-stone-500">
          描述
          <textarea className="auth-input mt-2 min-h-20 resize-y" maxLength={2000} onChange={(event) => setDescription(event.target.value)} value={description} />
        </label>
        <div className="mt-4 flex items-center gap-3">
          <button className="secondary-button disabled:cursor-wait disabled:opacity-60" disabled={renameState === 'loading' || !name.trim()} type="submit">保存書冊資訊</button>
          <StatusMessage message={renameMessage} status={renameState} />
        </div>
      </form>

      <section className="mt-8 rounded-3xl border border-stone-200 bg-white p-5 sm:p-7" aria-label="書冊內書籍">
        <h2 className="font-serif text-2xl text-stone-900">書冊內書籍</h2>
        {collection.books.length === 0 && <p className="mt-3 text-sm text-stone-500">這個書冊還沒有書。</p>}
        <ul className="mt-4 space-y-2">
          {collection.books
            .slice()
            .sort((left, right) => left.sortOrder - right.sortOrder)
            .map((entry) => (
              <li className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-stone-200 bg-stone-50 p-3" key={entry.id}>
                <div className="min-w-0">
                  <p className="truncate text-sm text-stone-800">{entry.volumeLabel ? `${entry.volumeLabel} · ` : ''}{entry.bookTitle}</p>
                  <p className="mt-1 text-xs text-stone-400">{entry.bookAuthor} · 排序 {entry.sortOrder}</p>
                </div>
                <button
                  className="text-xs text-rose-500 transition hover:text-rose-700"
                  onClick={() => setPendingRemoval({ kind: 'book', id: entry.bookId, label: entry.bookTitle })}
                  type="button"
                >
                  移出書冊
                </button>
              </li>
            ))}
        </ul>

        <form className="mt-6 grid grid-cols-1 gap-3 border-t border-stone-200 pt-6 sm:grid-cols-[minmax(0,1fr)_auto_auto_auto] sm:items-end" onSubmit={handleAddBook}>
          <label className="text-xs text-stone-500">
            從書庫加入書籍
            <select className="auth-input mt-2" onChange={(event) => setAddBookId(event.target.value)} value={addBookId}>
              <option value="">選擇書籍</option>
              {eligibleBooks.map((book) => <option key={book.id} value={book.id}>{book.title}</option>)}
            </select>
          </label>
          <label className="text-xs text-stone-500">
            冊次標籤（選填）
            <input className="auth-input mt-2" maxLength={100} onChange={(event) => setAddVolumeLabel(event.target.value)} placeholder="第一冊" value={addVolumeLabel} />
          </label>
          <label className="text-xs text-stone-500">
            排序
            <input className="auth-input mt-2 w-20" min={0} onChange={(event) => setAddSortOrder(event.target.value)} type="number" value={addSortOrder} />
          </label>
          <button className="secondary-button disabled:cursor-wait disabled:opacity-60" disabled={addBookState === 'loading' || !addBookId} type="submit">加入</button>
        </form>
        {eligibleBooks.length === 0 && <p className="mt-2 text-xs text-amber-700">書庫裡沒有可加入的書；只能加入你自己上傳、含正文的書籍。</p>}
        <StatusMessage className="mt-2" message={addBookMessage} status={addBookState} />
      </section>

      <section className="mt-8 rounded-3xl border border-stone-200 bg-white p-5 sm:p-7" aria-label="唯讀分享">
        <h2 className="font-serif text-2xl text-stone-900">分享給其他使用者（唯讀）</h2>
        <p className="mt-2 text-sm text-stone-500">分享對象只能瀏覽書名、章節與正文，看不到你的閱讀筆記、摘要或朗讀音訊。</p>

        {collection.shares.length === 0 && <p className="mt-4 text-sm text-stone-500">還沒有分享給任何人。</p>}
        <ul className="mt-4 space-y-2">
          {collection.shares.map((share) => (
            <li className="flex items-center justify-between gap-3 rounded-xl border border-stone-200 bg-stone-50 p-3" key={share.id}>
              <span className="text-sm text-stone-700">{share.granteeEmail}</span>
              <button
                className="text-xs text-rose-500 transition hover:text-rose-700"
                onClick={() => setPendingRemoval({ kind: 'share', id: share.id, label: share.granteeEmail })}
                type="button"
              >
                撤銷分享
              </button>
            </li>
          ))}
        </ul>

        <form className="mt-6 flex flex-col gap-3 border-t border-stone-200 pt-6 sm:flex-row" onSubmit={handleAddShare}>
          <label className="flex-1 text-xs text-stone-500">
            對方的 StoryVoice 帳號 email
            <input className="auth-input mt-2" onChange={(event) => setShareEmail(event.target.value)} placeholder="friend@example.com" type="email" value={shareEmail} />
          </label>
          <button className="secondary-button shrink-0 self-end disabled:cursor-wait disabled:opacity-60" disabled={shareState === 'loading' || !shareEmail.trim()} type="submit">分享書冊</button>
        </form>
        <StatusMessage className="mt-2" message={shareMessage} status={shareState} />
      </section>

      <ConfirmDialog
        confirmLabel={pendingRemoval?.kind === 'share' ? '撤銷分享' : '移出書冊'}
        description={pendingRemoval?.kind === 'share'
          ? `「${pendingRemoval.label}」將無法再看到這個書冊。`
          : `「${pendingRemoval?.label}」只會移出書冊，不會刪除原始書籍。`}
        onCancel={() => setPendingRemoval(null)}
        onConfirm={() => {
          if (!pendingRemoval) return
          if (pendingRemoval.kind === 'book') void handleRemoveBook(pendingRemoval.id)
          else void handleRevokeShare(pendingRemoval.id)
        }}
        open={pendingRemoval !== null}
        title={pendingRemoval?.kind === 'share' ? '確定撤銷這個分享？' : '確定把這本書移出書冊？'}
      />
    </section>
  )
}

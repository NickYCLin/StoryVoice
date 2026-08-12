import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'

import { fetchJson } from '../api'
import type { SharedCollectionBookContent, SharedCollectionDetails } from '../collectionsTypes'

type LoadState = 'loading' | 'ready' | 'error'

function SharedBookChapters({ collectionId, bookId }: { collectionId: string; bookId: string }) {
  const [content, setContent] = useState<SharedCollectionBookContent | null>(null)
  const [state, setState] = useState<LoadState>('loading')

  useEffect(() => {
    let cancelled = false
    fetchJson<SharedCollectionBookContent>(`/api/collections/shared-with-me/${collectionId}/books/${bookId}`)
      .then((value) => {
        if (cancelled) return
        setContent(value)
        setState('ready')
      })
      .catch(() => {
        if (!cancelled) setState('error')
      })
    return () => { cancelled = true }
  }, [collectionId, bookId])

  if (state === 'loading') return <p className="px-4 py-4 text-xs text-stone-500">正在讀取章節…</p>
  if (state === 'error' || !content) return <p className="px-4 py-4 text-xs text-rose-600">章節讀取失敗。</p>
  if (content.chapters.length === 0) return <p className="px-4 py-4 text-xs text-stone-500">這本書還沒有章節。</p>

  return (
    <div className="space-y-2 px-4 py-4">
      {content.chapters.map((chapter) => (
        <details className="chapter-panel group" key={chapter.id}>
          <summary className="flex cursor-pointer list-none items-center gap-4 px-4 py-4 text-left">
            <span className="grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-amber-50 font-mono text-xs text-amber-700">{String(chapter.chapterNumber).padStart(2, '0')}</span>
            <span className="min-w-0 flex-1 truncate font-serif text-lg text-stone-800">{chapter.title}</span>
            <span className="text-stone-400 transition group-open:rotate-45">＋</span>
          </summary>
          <div className="reading-text">{chapter.originalText}</div>
        </details>
      ))}
    </div>
  )
}

export function SharedCollectionPage() {
  const { collectionId } = useParams<{ collectionId: string }>()
  const [collection, setCollection] = useState<SharedCollectionDetails | null>(null)
  const [state, setState] = useState<LoadState>('loading')

  useEffect(() => {
    if (!collectionId) return
    const controller = new AbortController()
    setState('loading')
    fetchJson<SharedCollectionDetails>(`/api/collections/shared-with-me/${collectionId}`, { signal: controller.signal })
      .then((value) => {
        setCollection(value)
        setState('ready')
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setState('error')
      })
    return () => controller.abort()
  }, [collectionId])

  if (state === 'loading') {
    return <section className="relative z-10 mx-auto max-w-5xl px-6 py-12 lg:px-10"><div className="library-state">正在讀取書冊…</div></section>
  }

  if (state === 'error' || !collection || !collectionId) {
    return (
      <section className="relative z-10 mx-auto max-w-5xl px-6 py-12 lg:px-10">
        <div className="library-state border-rose-300 text-rose-700">找不到這個分享，或已被撤銷。</div>
        <Link className="secondary-button mt-6 inline-flex" to="/shared">回到分享列表</Link>
      </section>
    )
  }

  return (
    <section className="relative z-10 mx-auto max-w-5xl px-6 py-12 lg:px-10">
      <Link className="text-sm text-stone-500 transition hover:text-stone-800" to="/shared">← 回到分享列表</Link>

      <div className="mt-6 rounded-3xl border border-stone-200 bg-white p-5 sm:p-7">
        <p className="eyebrow">來自 {collection.ownerEmail} · 唯讀</p>
        <h1 className="mt-2 font-serif text-3xl text-stone-900">{collection.name}</h1>
        {collection.description && <p className="mt-3 text-sm leading-7 text-stone-600">{collection.description}</p>}
      </div>

      <section className="mt-8 space-y-3" aria-label="書冊內書籍">
        {collection.books.length === 0 && <p className="text-sm text-stone-500">這個書冊還沒有書。</p>}
        {collection.books
          .slice()
          .sort((left, right) => left.sortOrder - right.sortOrder)
          .map((book) => (
            <details className="chapter-panel group" key={book.id}>
              <summary className="flex cursor-pointer list-none items-center gap-4 px-4 py-4 text-left">
                <div className="book-cover h-14 w-11 shrink-0 text-base">
                  {book.coverImageUrl
                    ? <img alt="" loading="lazy" referrerPolicy="no-referrer" src={book.coverImageUrl} />
                    : <span>{book.bookTitle.slice(0, 1)}</span>}
                </div>
                <div className="min-w-0 flex-1">
                  <p className="truncate font-serif text-lg text-stone-800">{book.volumeLabel ? `${book.volumeLabel} · ` : ''}{book.bookTitle}</p>
                  <p className="mt-1 truncate text-xs text-stone-400">{book.bookAuthor}</p>
                </div>
                <span className="text-stone-400 transition group-open:rotate-45">＋</span>
              </summary>
              <div className="border-t border-stone-200">
                <SharedBookChapters bookId={book.id} collectionId={collectionId} />
              </div>
            </details>
          ))}
      </section>
    </section>
  )
}

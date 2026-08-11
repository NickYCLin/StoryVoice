import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'

import { fetchJson } from '../api'
import type { SharedCollectionSummary } from '../collectionsTypes'

type LoadState = 'loading' | 'ready' | 'error'

export function SharedWithMePage() {
  const [collections, setCollections] = useState<SharedCollectionSummary[]>([])
  const [state, setState] = useState<LoadState>('loading')

  useEffect(() => {
    const controller = new AbortController()
    fetchJson<SharedCollectionSummary[]>('/api/collections/shared-with-me', { signal: controller.signal })
      .then((items) => {
        setCollections(items)
        setState('ready')
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setState('error')
      })
    return () => controller.abort()
  }, [])

  return (
    <section className="relative z-10 mx-auto max-w-7xl px-6 py-12 lg:px-10">
      <div className="mb-10">
        <p className="eyebrow">Shared with me</p>
        <h1 className="mt-3 font-serif text-4xl tracking-tight sm:text-5xl">其他使用者分享給你的書冊。</h1>
        <p className="mt-4 max-w-2xl text-sm leading-7 text-stone-400">
          這裡只能唯讀瀏覽書名、章節與正文；閱讀筆記、摘要與朗讀音訊仍屬於書冊擁有者的私人資料。
        </p>
      </div>

      {state === 'loading' && <div className="library-state">正在讀取分享…</div>}
      {state === 'error' && <div className="library-state border-rose-400/20 text-rose-200">分享讀取失敗，請重新整理頁面。</div>}
      {state === 'ready' && collections.length === 0 && (
        <div className="library-state min-h-52">
          <div>
            <h3 className="font-serif text-2xl text-stone-200">還沒有人跟你分享書冊。</h3>
            <p className="mt-3 text-sm text-stone-500">請對方在自己的書冊頁面，用你的帳號 email 分享給你。</p>
          </div>
        </div>
      )}
      {state === 'ready' && collections.length > 0 && (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {collections.map((collection) => (
            <Link className="book-card flex-col items-start gap-3" key={collection.id} to={`/shared/${collection.id}`}>
              <p className="font-serif text-xl text-stone-100">{collection.name}</p>
              {collection.description && <p className="mt-2 line-clamp-2 text-sm text-stone-500">{collection.description}</p>}
              <div className="mt-4 flex flex-wrap items-center gap-3 text-xs text-stone-600">
                <span>來自 {collection.ownerEmail}</span><span>·</span><span>{collection.bookCount} 本書</span>
              </div>
            </Link>
          ))}
        </div>
      )}
    </section>
  )
}

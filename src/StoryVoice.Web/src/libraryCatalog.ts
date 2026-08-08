export type LibrarySourceFilter = 'all' | 'books-com-tw' | 'uploaded'
export type LibraryLayoutFilter = 'all' | 'Reflowable' | 'Fixed' | 'unknown'
export type LibraryTtsFilter = 'all' | 'true' | 'false' | 'unknown'
export type LibrarySort = 'created-desc' | 'title' | 'author' | 'synced-desc'

export type LibraryCatalogFilters = {
  query: string
  source: LibrarySourceFilter
  layout: LibraryLayoutFilter
  tts: LibraryTtsFilter
  tag: string | 'all'
  sort: LibrarySort
}

export type LibraryCatalogBook = {
  id: string
  title: string
  author: string
  createdAt: string
  sourceProvider: string | null
  externalSourceId: string | null
  nativeTtsAvailable: boolean | null
  ebookLayout: 'Reflowable' | 'Fixed' | null
  sourceSyncedAt: string | null
}

export type DeviceBookTags = Record<string, string[]>

const collator = new Intl.Collator('zh-Hant', { numeric: true, sensitivity: 'base' })

function normalized(value: string | null | undefined) {
  return String(value ?? '').normalize('NFKC').replace(/\s+/g, ' ').trim().toLocaleLowerCase('zh-TW')
}

function timestamp(value: string | null) {
  if (!value) return Number.NEGATIVE_INFINITY
  const parsed = Date.parse(value)
  return Number.isNaN(parsed) ? Number.NEGATIVE_INFINITY : parsed
}

export function normalizeDeviceTag(value: string) {
  const tag = value.normalize('NFKC').replace(/\s+/g, ' ').trim()
  return tag.length > 0 && tag.length <= 24 ? tag : null
}

export function filterAndSortBooks<T extends LibraryCatalogBook>(
  books: readonly T[],
  filters: LibraryCatalogFilters,
  tagsByBook: DeviceBookTags = {},
) {
  const query = normalized(filters.query)
  const visible = books.filter((book) => {
    const tags = tagsByBook[book.id] ?? []
    if (filters.source === 'books-com-tw' && book.sourceProvider !== 'books-com-tw') return false
    if (filters.source === 'uploaded' && book.sourceProvider !== null) return false
    if (filters.layout === 'unknown' && book.ebookLayout !== null) return false
    if (filters.layout !== 'all' && filters.layout !== 'unknown' && book.ebookLayout !== filters.layout) return false
    if (filters.tts === 'true' && book.nativeTtsAvailable !== true) return false
    if (filters.tts === 'false' && book.nativeTtsAvailable !== false) return false
    if (filters.tts === 'unknown' && book.nativeTtsAvailable !== null) return false
    if (filters.tag !== 'all' && !tags.some((tag) => normalized(tag) === normalized(filters.tag))) return false
    if (!query) return true

    return [book.title, book.author, book.externalSourceId, ...tags]
      .some((value) => normalized(value).includes(query))
  })

  return [...visible].sort((left, right) => {
    let result = 0
    if (filters.sort === 'title') result = collator.compare(left.title, right.title)
    if (filters.sort === 'author') result = collator.compare(left.author, right.author)
    if (filters.sort === 'created-desc') result = timestamp(right.createdAt) - timestamp(left.createdAt)
    if (filters.sort === 'synced-desc') result = timestamp(right.sourceSyncedAt) - timestamp(left.sourceSyncedAt)
    return result || collator.compare(left.title, right.title) || collator.compare(left.id, right.id)
  })
}

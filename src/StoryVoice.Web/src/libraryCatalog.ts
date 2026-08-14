export type LibrarySourceFilter = 'all' | 'books-com-tw' | 'uploaded'
export type LibraryLayoutFilter = 'all' | 'Reflowable' | 'Fixed' | 'unknown'
export type LibraryTtsFilter = 'all' | 'true' | 'false' | 'unknown'
export type LibrarySort = 'created-desc' | 'title' | 'author' | 'synced-desc'

export type LibraryCatalogFilters = {
  query: string
  source: LibrarySourceFilter
  layout: LibraryLayoutFilter
  tts: LibraryTtsFilter
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

const collator = new Intl.Collator('zh-Hant', { numeric: true, sensitivity: 'base' })

function normalized(value: string | null | undefined) {
  return String(value ?? '').normalize('NFKC').replace(/\s+/g, ' ').trim().toLocaleLowerCase('zh-TW')
}

const CJK_DIGIT: Record<string, number> = { 零: 0, 一: 1, 二: 2, 兩: 2, 三: 3, 四: 4, 五: 5, 六: 6, 七: 7, 八: 8, 九: 9 }
const CJK_UNIT: Record<string, number> = { 十: 10, 百: 100, 千: 1000 }

// Converts a run of Chinese numeral characters (e.g. "十六", "二十一") to its Arabic value.
function chineseNumeralToArabic(chunk: string): number | null {
  let section = 0
  let digit = 0
  let sawUnit = false
  for (const char of chunk) {
    if (char in CJK_DIGIT) {
      digit = CJK_DIGIT[char]
    } else if (char in CJK_UNIT) {
      section += (digit || 1) * CJK_UNIT[char]
      digit = 0
      sawUnit = true
    }
  }
  const total = section + digit
  return sawUnit || digit > 0 ? total : null
}

// Book titles often embed the volume number as Chinese numerals ("第十六部") rather
// than Arabic digits ("vol.16"). Intl.Collator's `numeric` option only understands
// Arabic digit runs, so title sort would otherwise order "第十部" before "第二部".
// Rewriting numeral runs to Arabic digits lets the collator sort volumes correctly.
function withArabicNumerals(value: string) {
  return value.replace(/[零一二兩三四五六七八九十百千]+/g, (match) => {
    const arabic = chineseNumeralToArabic(match)
    return arabic === null ? match : String(arabic)
  })
}

function compareTitles(left: string, right: string) {
  return collator.compare(withArabicNumerals(left), withArabicNumerals(right))
}

function timestamp(value: string | null) {
  if (!value) return Number.NEGATIVE_INFINITY
  const parsed = Date.parse(value)
  return Number.isNaN(parsed) ? Number.NEGATIVE_INFINITY : parsed
}

export function filterAndSortBooks<T extends LibraryCatalogBook>(
  books: readonly T[],
  filters: LibraryCatalogFilters,
) {
  const query = normalized(filters.query)
  const visible = books.filter((book) => {
    if (filters.source === 'books-com-tw' && book.sourceProvider !== 'books-com-tw') return false
    if (filters.source === 'uploaded' && book.sourceProvider !== null) return false
    if (filters.layout === 'unknown' && book.ebookLayout !== null) return false
    if (filters.layout !== 'all' && filters.layout !== 'unknown' && book.ebookLayout !== filters.layout) return false
    if (filters.tts === 'true' && book.nativeTtsAvailable !== true) return false
    if (filters.tts === 'false' && book.nativeTtsAvailable !== false) return false
    if (filters.tts === 'unknown' && book.nativeTtsAvailable !== null) return false
    if (!query) return true

    return [book.title, book.author, book.externalSourceId]
      .some((value) => normalized(value).includes(query))
  })

  return [...visible].sort((left, right) => {
    let result = 0
    if (filters.sort === 'title') result = compareTitles(left.title, right.title)
    if (filters.sort === 'author') result = collator.compare(left.author, right.author)
    if (filters.sort === 'created-desc') result = timestamp(right.createdAt) - timestamp(left.createdAt)
    if (filters.sort === 'synced-desc') result = timestamp(right.sourceSyncedAt) - timestamp(left.sourceSyncedAt)
    return result || compareTitles(left.title, right.title) || collator.compare(left.id, right.id)
  })
}

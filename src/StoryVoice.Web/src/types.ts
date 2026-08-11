export type BookSummary = {
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
  contentBookId: string | null
  authorizedTextAvailable: boolean
  titleCorrection: string | null
  authorCorrection: string | null
  coverImageUrlCorrection: string | null
}

export type Chapter = {
  id: string
  chapterNumber: number
  sortOrder: number
  title: string
  originalText: string
}

export type BookDetails = Omit<BookSummary, 'chapterCount'> & {
  originalFileName: string
  chapters: Chapter[]
}

export function toBookSummary(book: BookDetails): BookSummary {
  const { chapters, originalFileName: _originalFileName, ...summary } = book
  return { ...summary, chapterCount: chapters.length }
}

export type BookCollectionSummary = {
  id: string
  name: string
  description: string | null
  bookCount: number
  shareCount: number
  createdAt: string
  updatedAt: string
}

export type BookCollectionBookEntry = {
  id: string
  bookId: string
  bookTitle: string
  bookAuthor: string
  coverImageUrl: string | null
  volumeLabel: string | null
  sortOrder: number
}

export type CollectionShareEntry = {
  id: string
  granteeEmail: string
  createdAt: string
}

export type BookCollectionDetails = {
  id: string
  name: string
  description: string | null
  books: BookCollectionBookEntry[]
  shares: CollectionShareEntry[]
  createdAt: string
  updatedAt: string
}

export type SharedCollectionSummary = {
  id: string
  name: string
  description: string | null
  ownerEmail: string
  bookCount: number
  sharedAt: string
}

export type SharedCollectionBookEntry = {
  id: string
  bookTitle: string
  bookAuthor: string
  coverImageUrl: string | null
  volumeLabel: string | null
  sortOrder: number
}

export type SharedCollectionDetails = {
  id: string
  name: string
  description: string | null
  ownerEmail: string
  books: SharedCollectionBookEntry[]
  sharedAt: string
}

export type SharedChapter = {
  id: string
  chapterNumber: number
  title: string
  originalText: string
}

export type SharedCollectionBookContent = {
  id: string
  bookTitle: string
  bookAuthor: string
  volumeLabel: string | null
  chapters: SharedChapter[]
}

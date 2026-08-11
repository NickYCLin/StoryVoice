const basePath = import.meta.env.BASE_URL.replace(/\/+$/, '')

export const apiUrl = (path: string) => `${basePath}${path.startsWith('/') ? path : `/${path}`}`

export async function responseProblem(response: Response, fallback: string) {
  const problem = await response.json().catch(() => null) as { detail?: string } | null
  return problem?.detail ?? `${fallback}（${response.status}）`
}

type FetchJsonOptions = {
  method?: string
  csrfToken?: string
  body?: unknown
  signal?: AbortSignal
}

export async function fetchJson<T>(path: string, options: FetchJsonOptions = {}): Promise<T> {
  const { method = 'GET', csrfToken, body, signal } = options
  const headers: Record<string, string> = {}
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  if (csrfToken) headers['X-CSRF-TOKEN'] = csrfToken

  const response = await fetch(apiUrl(path), {
    method,
    credentials: 'same-origin',
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
    signal,
  })
  if (!response.ok) throw new Error(await responseProblem(response, '要求失敗'))
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

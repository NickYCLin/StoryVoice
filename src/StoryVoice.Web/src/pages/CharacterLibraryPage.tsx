import { useCallback, useEffect, useState, type FormEvent } from 'react'

import { apiUrl, fetchJson, responseProblem } from '../api'
import { useAuthedOutletContext } from '../authOutletContext'
import { CharacterVoiceProfilesPanel } from '../CharacterVoiceProfilesPanel'
import { ConfirmDialog } from '../components/ConfirmDialog'

type CharacterProfile = {
  id: string
  canonicalName: string
  hasAvatar: boolean
  age: string | null
  gender: string | null
  birthday: string | null
  personality: string | null
  catchphrase: string | null
  background: string | null
  speakingStyle: string | null
  createdAt: string
  updatedAt: string
}

type LoadState = 'idle' | 'loading' | 'ready' | 'error'

const emptyForm = {
  canonicalName: '',
  age: '',
  gender: '',
  birthday: '',
  personality: '',
  catchphrase: '',
  background: '',
  speakingStyle: '',
}

export function CharacterLibraryPage() {
  const { csrfToken } = useAuthedOutletContext()
  const [characters, setCharacters] = useState<CharacterProfile[]>([])
  const [listState, setListState] = useState<LoadState>('loading')
  const [selectedId, setSelectedId] = useState('')
  const [newCharacterName, setNewCharacterName] = useState('')
  const [creating, setCreating] = useState(false)
  const [form, setForm] = useState(emptyForm)
  const [saveState, setSaveState] = useState<LoadState>('idle')
  const [message, setMessage] = useState('')
  const [pendingDelete, setPendingDelete] = useState(false)
  const [avatarBusy, setAvatarBusy] = useState(false)
  const [avatarVersion, setAvatarVersion] = useState(0)

  const loadCharacters = useCallback(async () => {
    setListState('loading')
    try {
      const items = await fetchJson<CharacterProfile[]>('/api/character-profiles')
      setCharacters(items)
      setListState('ready')
      setSelectedId((current) => current || items[0]?.id || '')
    } catch {
      setListState('error')
    }
  }, [])

  useEffect(() => {
    void loadCharacters()
  }, [loadCharacters])

  const selected = characters.find((character) => character.id === selectedId) ?? null

  useEffect(() => {
    setForm(selected
      ? {
        canonicalName: selected.canonicalName,
        age: selected.age ?? '',
        gender: selected.gender ?? '',
        birthday: selected.birthday ?? '',
        personality: selected.personality ?? '',
        catchphrase: selected.catchphrase ?? '',
        background: selected.background ?? '',
        speakingStyle: selected.speakingStyle ?? '',
      }
      : emptyForm)
  }, [selected])

  async function createCharacter(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!newCharacterName.trim()) return
    setCreating(true)
    try {
      const created = await fetchJson<CharacterProfile>('/api/character-profiles', {
        method: 'POST',
        csrfToken,
        body: { ...emptyForm, canonicalName: newCharacterName.trim() },
      })
      setNewCharacterName('')
      await loadCharacters()
      setSelectedId(created.id)
      setMessage(`已建立角色「${created.canonicalName}」，可以在下方補齊資料與聲線。`)
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '建立角色失敗。')
    } finally {
      setCreating(false)
    }
  }

  async function saveCharacter(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!selected || !form.canonicalName.trim()) return
    setSaveState('loading')
    try {
      const updated = await fetchJson<CharacterProfile>(`/api/character-profiles/${selected.id}`, {
        method: 'PUT',
        csrfToken,
        body: {
          canonicalName: form.canonicalName.trim(),
          age: form.age.trim() || null,
          gender: form.gender.trim() || null,
          birthday: form.birthday.trim() || null,
          personality: form.personality.trim() || null,
          catchphrase: form.catchphrase.trim() || null,
          background: form.background.trim() || null,
          speakingStyle: form.speakingStyle.trim() || null,
        },
      })
      setCharacters((current) => current.map((character) => (character.id === updated.id ? updated : character)))
      setSaveState('ready')
      setMessage('角色資料已儲存。')
    } catch (error) {
      setSaveState('error')
      setMessage(error instanceof Error ? error.message : '儲存角色失敗。')
    }
  }

  async function uploadAvatar(event: FormEvent<HTMLInputElement>) {
    const file = event.currentTarget.files?.[0]
    event.currentTarget.value = ''
    if (!selected || !file) return
    if (file.size > 5 * 1024 * 1024) {
      setMessage('頭像檔案不可超過 5 MiB。')
      return
    }

    setAvatarBusy(true)
    try {
      const formData = new FormData()
      formData.set('avatar', file)
      const response = await fetch(apiUrl(`/api/character-profiles/${selected.id}/avatar`), {
        method: 'POST',
        body: formData,
        credentials: 'same-origin',
        headers: { 'X-CSRF-TOKEN': csrfToken },
      })
      if (!response.ok) throw new Error(await responseProblem(response, '上傳頭像失敗'))
      await loadCharacters()
      setAvatarVersion((current) => current + 1)
      setMessage('頭像已更新。')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '上傳頭像失敗。')
    } finally {
      setAvatarBusy(false)
    }
  }

  async function deleteCharacter() {
    if (!selected) return
    setPendingDelete(false)
    try {
      await fetchJson(`/api/character-profiles/${selected.id}`, { method: 'DELETE', csrfToken })
      setSelectedId('')
      await loadCharacters()
      setMessage('角色已刪除。')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '刪除角色失敗——如果這個角色還在某個系列裡使用中，要先從系列移除。')
    }
  }

  return (
    <main className="relative z-10 mx-auto max-w-7xl px-6 py-12 lg:px-10">
      <section className="rounded-3xl border border-stone-200 bg-white p-5 sm:p-7">
        <p className="text-xs font-semibold uppercase tracking-[.22em] text-amber-700">Character library</p>
        <h1 className="mt-2 font-serif text-3xl text-stone-900">角色管理</h1>
        <p className="mt-3 max-w-3xl text-sm leading-6 text-stone-500">
          在這裡建立角色的基本資料與自訂聲線，建好之後就能在「多角色系列配音」裡直接選用，同一個角色可以跨系列重複使用。
        </p>
      </section>

      <section className="mt-6 grid gap-6 lg:grid-cols-[20rem_minmax(0,1fr)]">
        <aside className="rounded-3xl border border-stone-200 bg-white p-4">
          <h2 className="font-serif text-xl text-stone-900">我的角色</h2>

          <form className="mt-3 flex gap-2" onSubmit={createCharacter}>
            <input
              className="auth-input flex-1"
              maxLength={200}
              onChange={(event) => setNewCharacterName(event.target.value)}
              placeholder="新角色名稱"
              value={newCharacterName}
            />
            <button className="secondary-button px-3 disabled:cursor-wait disabled:opacity-60" disabled={creating || !newCharacterName.trim()} type="submit">
              新增
            </button>
          </form>

          {listState === 'loading' && <p className="mt-4 text-sm text-stone-500">正在讀取角色…</p>}
          {listState === 'error' && <p className="mt-4 text-sm text-rose-700">角色讀取失敗，請重新整理頁面。</p>}
          <div className="mt-3 space-y-2">
            {characters.map((character) => (
              <button
                className={`flex w-full items-center gap-3 rounded-xl border p-3 text-left text-sm transition ${character.id === selectedId ? 'border-amber-300 bg-amber-50 text-amber-800' : 'border-stone-200 bg-stone-50 text-stone-500 hover:text-stone-800'}`}
                key={character.id}
                onClick={() => setSelectedId(character.id)}
                type="button"
              >
                {character.hasAvatar ? (
                  <img
                    alt=""
                    className="h-9 w-9 rounded-full object-cover"
                    src={apiUrl(`/api/character-profiles/${character.id}/avatar?v=${character.id === selectedId ? avatarVersion : 0}`)}
                  />
                ) : (
                  <span className="grid h-9 w-9 shrink-0 place-items-center rounded-full border border-stone-200 bg-white font-serif text-sm text-stone-400">
                    {character.canonicalName.slice(0, 1)}
                  </span>
                )}
                <span className="truncate">{character.canonicalName}</span>
              </button>
            ))}
            {listState === 'ready' && characters.length === 0 && (
              <p className="px-1 text-sm leading-6 text-stone-500">還沒有角色；用上面的表單建立第一個。</p>
            )}
          </div>
        </aside>

        <section>
          {!selected && <div className="library-state">選擇或建立一個角色，開始設定基本資料與自訂聲線。</div>}
          {selected && (
            <div className="space-y-6">
              <div className="rounded-3xl border border-stone-200 bg-white p-5 sm:p-7">
                <div className="flex flex-wrap items-center gap-4">
                  {selected.hasAvatar ? (
                    <img
                      alt=""
                      className="h-16 w-16 rounded-full object-cover"
                      src={apiUrl(`/api/character-profiles/${selected.id}/avatar?v=${avatarVersion}`)}
                    />
                  ) : (
                    <span className="grid h-16 w-16 place-items-center rounded-full border border-stone-200 bg-stone-50 font-serif text-2xl text-stone-400">
                      {selected.canonicalName.slice(0, 1)}
                    </span>
                  )}
                  <div>
                    <h2 className="font-serif text-2xl text-stone-900">{selected.canonicalName}</h2>
                    <label className="mt-1 inline-block text-xs text-amber-700">
                      <span className="cursor-pointer underline">{avatarBusy ? '上傳中…' : '上傳／更換頭像'}</span>
                      <input accept="image/jpeg,image/png,image/webp" className="hidden" onChange={(event) => void uploadAvatar(event)} type="file" />
                    </label>
                  </div>
                  <button className="ml-auto text-xs text-rose-600" onClick={() => setPendingDelete(true)} type="button">刪除這個角色</button>
                </div>

                <form className="mt-6 grid gap-4 border-t border-stone-200 pt-5 sm:grid-cols-2" onSubmit={saveCharacter}>
                  <label className="text-xs text-stone-500 sm:col-span-2">
                    角色名稱
                    <input
                      className="auth-input mt-2"
                      maxLength={200}
                      onChange={(event) => setForm((current) => ({ ...current, canonicalName: event.target.value }))}
                      required
                      value={form.canonicalName}
                    />
                  </label>
                  <label className="text-xs text-stone-500">
                    年齡
                    <input className="auth-input mt-2" maxLength={100} onChange={(event) => setForm((current) => ({ ...current, age: event.target.value }))} value={form.age} />
                  </label>
                  <label className="text-xs text-stone-500">
                    性別
                    <input className="auth-input mt-2" maxLength={100} onChange={(event) => setForm((current) => ({ ...current, gender: event.target.value }))} value={form.gender} />
                  </label>
                  <label className="text-xs text-stone-500 sm:col-span-2">
                    生日
                    <input className="auth-input mt-2" maxLength={100} onChange={(event) => setForm((current) => ({ ...current, birthday: event.target.value }))} placeholder="例如：2009-11-23，虛構角色可以留空" value={form.birthday} />
                  </label>
                  <label className="text-xs text-stone-500 sm:col-span-2">
                    <div className="flex items-center justify-between">
                      <span>個性</span>
                      <span className="rounded-full border border-stone-200 px-2 py-0.5 text-[10px] text-stone-400" title="尚未串接 AI，之後可以再接上">AI 補完（尚未提供）</span>
                    </div>
                    <textarea className="auth-input mt-2 min-h-16 w-full" maxLength={2000} onChange={(event) => setForm((current) => ({ ...current, personality: event.target.value }))} value={form.personality} />
                  </label>
                  <label className="text-xs text-stone-500 sm:col-span-2">
                    口頭禪
                    <textarea className="auth-input mt-2 min-h-12 w-full" maxLength={2000} onChange={(event) => setForm((current) => ({ ...current, catchphrase: event.target.value }))} value={form.catchphrase} />
                  </label>
                  <label className="text-xs text-stone-500 sm:col-span-2">
                    <div className="flex items-center justify-between">
                      <span>人物背景</span>
                      <span className="rounded-full border border-stone-200 px-2 py-0.5 text-[10px] text-stone-400" title="尚未串接 AI，之後可以再接上">AI 補完（尚未提供）</span>
                    </div>
                    <textarea className="auth-input mt-2 min-h-20 w-full" maxLength={4000} onChange={(event) => setForm((current) => ({ ...current, background: event.target.value }))} value={form.background} />
                  </label>
                  <label className="text-xs text-stone-500 sm:col-span-2">
                    <div className="flex items-center justify-between">
                      <span>說話風格</span>
                      <span className="rounded-full border border-stone-200 px-2 py-0.5 text-[10px] text-stone-400" title="尚未串接 AI，之後可以再接上">AI 補完（尚未提供）</span>
                    </div>
                    <textarea className="auth-input mt-2 min-h-16 w-full" maxLength={2000} onChange={(event) => setForm((current) => ({ ...current, speakingStyle: event.target.value }))} value={form.speakingStyle} />
                  </label>
                  <button className="secondary-button sm:col-span-2 disabled:cursor-wait disabled:opacity-60" disabled={saveState === 'loading' || !form.canonicalName.trim()} type="submit">
                    儲存角色資料
                  </button>
                </form>
              </div>

              <CharacterVoiceProfilesPanel characterName={selected.canonicalName} characterProfileId={selected.id} csrfToken={csrfToken} />
            </div>
          )}
        </section>
      </section>

      <p aria-live="polite" className="mt-5 min-h-5 text-sm text-stone-500">{message}</p>
      <ConfirmDialog
        confirmLabel="刪除角色"
        description="如果這個角色還連結在某個系列的多角色配音裡，會先被拒絕，需要從系列移除後再刪除。"
        onCancel={() => setPendingDelete(false)}
        onConfirm={() => void deleteCharacter()}
        open={pendingDelete}
        title="確定刪除這個角色？"
      />
    </main>
  )
}

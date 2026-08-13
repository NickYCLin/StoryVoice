import { useCallback, useEffect, useState, type FormEvent } from 'react'

import { apiUrl, fetchJson, responseProblem } from './api'

type VoiceProfile = {
  id: string
  characterProfileId: string
  kind: 'Base' | 'Scene'
  sceneCode: string | null
  mode: 'Design' | 'Clone'
  consentType: string | null
  voicePromptText: string | null
  transcript: string | null
  transcriptConfirmed: boolean
  status: 'Pending' | 'AwaitingTranscriptConfirmation' | 'Ready' | 'Failed'
  referenceAudioDurationSeconds: number | null
  createdAt: string
  updatedAt: string
}

type Slot = { sceneCode: string | null; label: string; description: string }

const SLOTS: Slot[] = [
  { sceneCode: null, label: '基礎聲線', description: '沒有更精確情境時的預設聲音' },
  { sceneCode: 'neutral', label: '平常', description: '日常對話狀態' },
  { sceneCode: 'nervous', label: '緊張', description: '感到緊張或不安時' },
  { sceneCode: 'happy', label: '開心', description: '開心、興奮時' },
  { sceneCode: 'angry', label: '生氣', description: '生氣、提高音量時' },
  { sceneCode: 'sad', label: '難過', description: '難過、低落時' },
]

const DEFAULT_PREVIEW_TEXT = '你好，這是我的聲音示範。'

function formatDuration(seconds: number | null) {
  if (seconds === null) return '—'
  const totalSeconds = Math.round(seconds)
  const minutes = Math.floor(totalSeconds / 60)
  const remainder = totalSeconds % 60
  return `${minutes}:${remainder.toString().padStart(2, '0')}`
}

const CONSENT_OPTIONS: Array<{ value: string; label: string }> = [
  { value: 'self_recorded', label: '本人親自錄製' },
  { value: 'explicit_permission', label: '已取得聲音所有人明確同意' },
  { value: 'licensed_voice', label: '已取得合法授權的聲音' },
]

const STATUS_LABEL: Record<VoiceProfile['status'], string> = {
  Pending: '處理中',
  AwaitingTranscriptConfirmation: '待確認文字稿',
  Ready: '已完成',
  Failed: '失敗',
}

const STATUS_STYLE: Record<VoiceProfile['status'], string> = {
  Pending: 'border-stone-200 bg-stone-100 text-stone-600',
  AwaitingTranscriptConfirmation: 'border-amber-200 bg-amber-50 text-amber-700',
  Ready: 'border-emerald-200 bg-emerald-50 text-emerald-700',
  Failed: 'border-rose-200 bg-rose-50 text-rose-700',
}

function basePath(characterProfileId: string) {
  return `/api/character-profiles/${characterProfileId}/voice-profiles`
}

function slotPath(characterProfileId: string, sceneCode: string | null) {
  const root = basePath(characterProfileId)
  return sceneCode ? `${root}/scenes/${sceneCode}` : `${root}/base`
}

type Props = {
  characterProfileId: string
  characterName: string
  csrfToken: string
}

export function CharacterVoiceProfilesPanel({ characterProfileId, characterName, csrfToken }: Props) {
  const [profiles, setProfiles] = useState<VoiceProfile[] | null>(null)
  const [message, setMessage] = useState('')
  const [busySlot, setBusySlot] = useState<string | null>(null)
  const [openSlot, setOpenSlot] = useState<string | null>(null)
  const [mode, setMode] = useState<Record<string, 'Design' | 'Clone'>>({})
  const [promptText, setPromptText] = useState<Record<string, string>>({})
  const [consentType, setConsentType] = useState<Record<string, string>>({})
  const [transcriptDraft, setTranscriptDraft] = useState<Record<string, string>>({})
  const [previewingId, setPreviewingId] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      const list = await fetchJson<VoiceProfile[]>(basePath(characterProfileId))
      setProfiles(list)
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '無法讀取這個角色的自訂聲線。')
    }
  }, [characterProfileId])

  useEffect(() => {
    void load()
  }, [load])

  function profileFor(sceneCode: string | null) {
    return profiles?.find((profile) => (sceneCode === null ? profile.kind === 'Base' : profile.sceneCode === sceneCode)) ?? null
  }

  async function createDesigned(slotKey: string, sceneCode: string | null) {
    const text = promptText[slotKey]?.trim()
    if (!text) return
    setBusySlot(slotKey)
    try {
      await fetchJson(`${slotPath(characterProfileId, sceneCode)}/design`, {
        method: 'POST',
        csrfToken,
        body: { voicePrompt: text },
      })
      setPromptText((current) => ({ ...current, [slotKey]: '' }))
      setOpenSlot(null)
      await load()
      setMessage('聲線已建立，文字設計模式不需要等待處理。')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '建立聲線失敗。')
    } finally {
      setBusySlot(null)
    }
  }

  async function createCloned(event: FormEvent<HTMLFormElement>, slotKey: string, sceneCode: string | null) {
    event.preventDefault()
    const form = event.currentTarget
    const formData = new FormData(form)
    const file = formData.get('referenceAudio')
    if (!(file instanceof File) || file.size === 0) {
      setMessage('請先選擇 WAV 參考錄音。')
      return
    }
    if (file.size > 20 * 1024 * 1024) {
      setMessage('參考音檔不可超過 20 MiB。')
      return
    }
    formData.set('consentType', consentType[slotKey] ?? CONSENT_OPTIONS[0].value)

    setBusySlot(slotKey)
    try {
      const response = await fetch(apiUrl(slotPath(characterProfileId, sceneCode)), {
        method: 'POST',
        body: formData,
        credentials: 'same-origin',
        headers: { 'X-CSRF-TOKEN': csrfToken },
      })
      if (!response.ok) throw new Error(await responseProblem(response, '建立聲線失敗'))
      form.reset()
      setOpenSlot(null)
      await load()
      setMessage('參考錄音已上傳，正在等待語音辨識草稿；完成後請回來確認文字稿。')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '建立聲線失敗。')
    } finally {
      setBusySlot(null)
    }
  }

  async function refreshStatus(profile: VoiceProfile) {
    setBusySlot(profile.id)
    try {
      await fetchJson(`${basePath(characterProfileId)}/${profile.id}/refresh-status`, { method: 'POST', csrfToken, body: {} })
      await load()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '更新狀態失敗。')
    } finally {
      setBusySlot(null)
    }
  }

  async function confirmTranscript(profile: VoiceProfile) {
    const transcript = (transcriptDraft[profile.id] ?? profile.transcript ?? '').trim()
    if (!transcript) return
    setBusySlot(profile.id)
    try {
      await fetchJson(`${basePath(characterProfileId)}/${profile.id}/confirm-transcript`, {
        method: 'POST',
        csrfToken,
        body: { transcript },
      })
      await load()
      setMessage('文字稿已確認並鎖定，聲線可以開始使用。')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '確認文字稿失敗。')
    } finally {
      setBusySlot(null)
    }
  }

  async function rebuild(profile: VoiceProfile) {
    setBusySlot(profile.id)
    try {
      await fetchJson(`${basePath(characterProfileId)}/${profile.id}/rebuild`, { method: 'POST', csrfToken, body: {} })
      await load()
      setMessage('已用本地保存的錄音與文字稿重新建立聲線。')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '重建聲線失敗。')
    } finally {
      setBusySlot(null)
    }
  }

  async function playPreview(profile: VoiceProfile, text: string) {
    setPreviewingId(profile.id)
    try {
      const response = await fetch(apiUrl(`${basePath(characterProfileId)}/${profile.id}/preview`), {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrfToken },
        body: JSON.stringify({ text }),
      })
      if (!response.ok) throw new Error(await responseProblem(response, '試講失敗'))
      const blob = await response.blob()
      const url = URL.createObjectURL(blob)
      const audio = new Audio(url)
      audio.addEventListener('ended', () => URL.revokeObjectURL(url))
      await audio.play()
      setMessage('正在播放試講，聲音直接來自這組聲線的即時合成。')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '試講失敗。')
    } finally {
      setPreviewingId(null)
    }
  }

  async function remove(profile: VoiceProfile) {
    setBusySlot(profile.id)
    try {
      await fetchJson(`${basePath(characterProfileId)}/${profile.id}`, { method: 'DELETE', csrfToken })
      await load()
      setMessage('聲線已刪除。')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '刪除聲線失敗。')
    } finally {
      setBusySlot(null)
    }
  }

  return (
    <div className="rounded-xl border border-stone-200 bg-white p-4">
      <p className="text-sm text-stone-500">
        {characterName} 的自訂聲線 — 基礎聲線之外，可以替緊張／開心／生氣／難過各自設計或克隆一組聲線；找不到的情境會自動退回基礎聲線。
      </p>
      <div className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {SLOTS.map((slot) => {
          const slotKey = `${characterProfileId}:${slot.sceneCode ?? 'base'}`
          const profile = profileFor(slot.sceneCode)
          const isOpen = openSlot === slotKey
          const isBusy = busySlot === slotKey || (profile !== null && busySlot === profile.id)
          const slotMode = mode[slotKey] ?? 'Design'

          return (
            <div className="rounded-xl border border-stone-200 bg-stone-50 p-3" key={slotKey}>
              <div className="flex items-center justify-between gap-2">
                <p className="text-sm font-medium text-stone-800">{slot.label}</p>
                {profile && (
                  <span className={`rounded-full border px-3 py-1 text-xs ${STATUS_STYLE[profile.status]}`}>
                    {STATUS_LABEL[profile.status]}
                  </span>
                )}
              </div>
              <p className="mt-0.5 text-[11px] text-stone-400">{slot.description}</p>

              {!profile && (
                <div className="mt-2">
                  {!isOpen && (
                    <button className="secondary-button w-full px-3 py-2 text-xs" onClick={() => setOpenSlot(slotKey)} type="button">
                      建立聲線
                    </button>
                  )}
                  {isOpen && (
                    <div className="mt-1 space-y-2">
                      <div className="flex gap-2 text-xs">
                        <button
                          className={`rounded-full border px-3 py-1 ${slotMode === 'Design' ? 'border-amber-300 bg-amber-50 text-amber-800' : 'border-stone-200 text-stone-500'}`}
                          onClick={() => setMode((current) => ({ ...current, [slotKey]: 'Design' }))}
                          type="button"
                        >
                          文字設計
                        </button>
                        <button
                          className={`rounded-full border px-3 py-1 ${slotMode === 'Clone' ? 'border-amber-300 bg-amber-50 text-amber-800' : 'border-stone-200 text-stone-500'}`}
                          onClick={() => setMode((current) => ({ ...current, [slotKey]: 'Clone' }))}
                          type="button"
                        >
                          上傳錄音克隆
                        </button>
                      </div>

                      {slotMode === 'Design' && (
                        <div className="space-y-2">
                          <textarea
                            className="auth-input min-h-20 w-full"
                            maxLength={2000}
                            onChange={(event) => setPromptText((current) => ({ ...current, [slotKey]: event.target.value }))}
                            placeholder="描述這個聲線的音色與語氣，例如：溫柔、略帶沙啞的年輕女聲"
                            value={promptText[slotKey] ?? ''}
                          />
                          <div className="flex gap-2">
                            <button
                              className="secondary-button flex-1 px-3 py-2 text-xs disabled:cursor-wait disabled:opacity-60"
                              disabled={isBusy || !promptText[slotKey]?.trim()}
                              onClick={() => void createDesigned(slotKey, slot.sceneCode)}
                              type="button"
                            >
                              建立
                            </button>
                            <button className="px-3 py-2 text-xs text-stone-500" onClick={() => setOpenSlot(null)} type="button">
                              取消
                            </button>
                          </div>
                        </div>
                      )}

                      {slotMode === 'Clone' && (
                        <form className="space-y-2" onSubmit={(event) => void createCloned(event, slotKey, slot.sceneCode)}>
                          <input accept="audio/wav" className="auth-input w-full text-xs" name="referenceAudio" required type="file" />
                          <select
                            className="auth-input w-full"
                            onChange={(event) => setConsentType((current) => ({ ...current, [slotKey]: event.target.value }))}
                            value={consentType[slotKey] ?? CONSENT_OPTIONS[0].value}
                          >
                            {CONSENT_OPTIONS.map((option) => (
                              <option key={option.value} value={option.value}>{option.label}</option>
                            ))}
                          </select>
                          <div className="flex gap-2">
                            <button
                              className="secondary-button flex-1 px-3 py-2 text-xs disabled:cursor-wait disabled:opacity-60"
                              disabled={isBusy}
                              type="submit"
                            >
                              上傳並建立
                            </button>
                            <button className="px-3 py-2 text-xs text-stone-500" onClick={() => setOpenSlot(null)} type="button">
                              取消
                            </button>
                          </div>
                        </form>
                      )}
                    </div>
                  )}
                </div>
              )}

              {profile && (
                <div className="mt-2 space-y-2 text-xs text-stone-600">
                  <p>
                    {profile.mode === 'Design' ? '文字設計' : '錄音克隆'}
                    {' · 字數 '}
                    {(profile.transcript ?? profile.voicePromptText ?? '').length}
                    {' · 時長 '}
                    {formatDuration(profile.referenceAudioDurationSeconds)}
                  </p>

                  {profile.status === 'Ready' && (
                    <button
                      className="secondary-button w-full px-3 py-2 text-xs disabled:cursor-wait disabled:opacity-60"
                      disabled={previewingId === profile.id}
                      onClick={() => void playPreview(profile, DEFAULT_PREVIEW_TEXT)}
                      type="button"
                    >
                      {previewingId === profile.id ? '合成中…' : '▶ 播放試講'}
                    </button>
                  )}

                  {profile.status === 'Pending' && (
                    <button
                      className="secondary-button w-full px-3 py-2 text-xs disabled:cursor-wait disabled:opacity-60"
                      disabled={isBusy}
                      onClick={() => void refreshStatus(profile)}
                      type="button"
                    >
                      更新處理狀態
                    </button>
                  )}

                  {profile.status === 'AwaitingTranscriptConfirmation' && (
                    <div className="space-y-2">
                      <textarea
                        className="auth-input min-h-16 w-full"
                        maxLength={2000}
                        onChange={(event) => setTranscriptDraft((current) => ({ ...current, [profile.id]: event.target.value }))}
                        value={transcriptDraft[profile.id] ?? profile.transcript ?? ''}
                      />
                      <button
                        className="secondary-button w-full px-3 py-2 text-xs disabled:cursor-wait disabled:opacity-60"
                        disabled={isBusy}
                        onClick={() => void confirmTranscript(profile)}
                        type="button"
                      >
                        確認文字稿
                      </button>
                    </div>
                  )}

                  {profile.status === 'Failed' && profile.mode === 'Clone' && (
                    <button
                      className="secondary-button w-full px-3 py-2 text-xs disabled:cursor-wait disabled:opacity-60"
                      disabled={isBusy}
                      onClick={() => void rebuild(profile)}
                      type="button"
                    >
                      用保存的錄音重建
                    </button>
                  )}

                  <button
                    className="w-full px-3 py-2 text-xs text-rose-600 disabled:cursor-wait disabled:opacity-60"
                    disabled={isBusy}
                    onClick={() => void remove(profile)}
                    type="button"
                  >
                    刪除這組聲線
                  </button>
                </div>
              )}
            </div>
          )
        })}
      </div>
      <p aria-live="polite" className="mt-3 min-h-5 text-xs text-stone-500">{message}</p>
    </div>
  )
}

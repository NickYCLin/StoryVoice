import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'

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

type PreviewAudio = {
  characterProfileId: string
  profileId: string
  url: string
  fileName: string
}

type PreviewRequestState = {
  characterProfileId: string
  profileId: string
}

type PreviewRequest = PreviewRequestState & {
  controller: AbortController
}

type LoadedProfiles = {
  characterProfileId: string
  items: VoiceProfile[]
}

const SLOTS: Slot[] = [
  { sceneCode: null, label: '基礎聲線', description: '沒有更精確情境時的預設聲音，也是日常對話狀態使用的聲音' },
  { sceneCode: 'nervous', label: '緊張', description: '感到緊張或不安時' },
  { sceneCode: 'happy', label: '開心', description: '開心、興奮時' },
  { sceneCode: 'angry', label: '生氣', description: '生氣、提高音量時' },
  { sceneCode: 'sad', label: '難過', description: '難過、低落時' },
]

const DEFAULT_PREVIEW_TEXT = '你好，這是我的聲音示範。'
// Code-pinned to the verified provider contract; this must not be an environment toggle.
const DESIGN_VOICE_AVAILABLE = false

function formatDuration(seconds: number | null) {
  if (seconds === null) return '—'
  const totalSeconds = Math.round(seconds)
  const minutes = Math.floor(totalSeconds / 60)
  const remainder = totalSeconds % 60
  return `${minutes}:${remainder.toString().padStart(2, '0')}`
}

function safeDownloadFileNamePart(value: string) {
  const withoutControlCharacters = Array.from(value, (character) => {
    const codePoint = character.codePointAt(0) ?? 0
    return codePoint < 32 || codePoint === 127 ? '_' : character
  }).join('')
  const cleaned = withoutControlCharacters
    .trim()
    .replace(/[<>:"/\\|?*]/g, '_')
    .replace(/[. ]+$/g, '')
    .slice(0, 80)
    .replace(/[. ]+$/g, '')
  const candidate = cleaned || '角色'
  return /^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/i.test(candidate) ? `_${candidate}` : candidate
}

function audioFileExtension(contentType: string) {
  const mediaType = contentType.split(';', 1)[0].trim().toLowerCase()
  if (mediaType === 'audio/wav' || mediaType === 'audio/x-wav' || mediaType === 'audio/vnd.wave') return 'wav'
  if (mediaType === 'audio/mpeg') return 'mp3'
  if (mediaType === 'audio/mp4' || mediaType === 'audio/x-m4a' || mediaType === 'audio/aac') return 'm4a'
  if (mediaType === 'audio/ogg') return 'ogg'

  const subtype = mediaType.startsWith('audio/') ? mediaType.slice('audio/'.length).replace(/^x-/, '') : ''
  return /^[a-z0-9]+$/.test(subtype) ? subtype : 'audio'
}

function previewSlotLabel(profile: VoiceProfile) {
  if (profile.kind === 'Base') return '基礎聲線'
  return SLOTS.find((slot) => slot.sceneCode === profile.sceneCode)?.label ?? '情境聲線'
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
  const [profilesState, setProfilesState] = useState<LoadedProfiles | null>(null)
  const [message, setMessage] = useState('')
  const [busySlot, setBusySlot] = useState<string | null>(null)
  const [openSlot, setOpenSlot] = useState<string | null>(null)
  const [mode, setMode] = useState<Record<string, 'Design' | 'Clone'>>({})
  const [promptText, setPromptText] = useState<Record<string, string>>({})
  const [consentType, setConsentType] = useState<Record<string, string>>({})
  const [transcriptDraft, setTranscriptDraft] = useState<Record<string, string>>({})
  const [previewing, setPreviewing] = useState<PreviewRequestState | null>(null)
  const [previewAudio, setPreviewAudio] = useState<PreviewAudio | null>(null)
  const currentCharacterProfileIdRef = useRef(characterProfileId)
  const loadGenerationRef = useRef(0)
  const previewAudioRef = useRef<PreviewAudio | null>(null)
  const previewRequestRef = useRef<PreviewRequest | null>(null)

  currentCharacterProfileIdRef.current = characterProfileId

  const profiles = profilesState?.characterProfileId === characterProfileId ? profilesState.items : null
  const previewingId = previewing?.characterProfileId === characterProfileId ? previewing.profileId : null

  const load = useCallback(async (signal?: AbortSignal) => {
    const requestedCharacterProfileId = characterProfileId
    const requestedGeneration = loadGenerationRef.current
    try {
      const list = await fetchJson<VoiceProfile[]>(basePath(requestedCharacterProfileId), { signal })
      if (signal?.aborted
        || loadGenerationRef.current !== requestedGeneration
        || currentCharacterProfileIdRef.current !== requestedCharacterProfileId) return
      setProfilesState({ characterProfileId: requestedCharacterProfileId, items: list })
    } catch (error) {
      if (signal?.aborted
        || loadGenerationRef.current !== requestedGeneration
        || currentCharacterProfileIdRef.current !== requestedCharacterProfileId) return
      setMessage(error instanceof Error ? error.message : '無法讀取這個角色的自訂聲線。')
    }
  }, [characterProfileId])

  useEffect(() => {
    loadGenerationRef.current += 1
    const controller = new AbortController()
    setProfilesState(null)
    void load(controller.signal)
    return () => {
      loadGenerationRef.current += 1
      controller.abort()
    }
  }, [load])

  useEffect(() => {
    setPreviewAudio(null)
    setPreviewing(null)
    return () => {
      const request = previewRequestRef.current
      previewRequestRef.current = null
      request?.controller.abort()

      const audio = previewAudioRef.current
      previewAudioRef.current = null
      if (audio) URL.revokeObjectURL(audio.url)
    }
  }, [characterProfileId])

  function profileFor(sceneCode: string | null) {
    return profiles?.find((profile) => (sceneCode === null ? profile.kind === 'Base' : profile.sceneCode === sceneCode)) ?? null
  }

  async function createDesigned(slotKey: string, sceneCode: string | null) {
    if (!DESIGN_VOICE_AVAILABLE) {
      setMessage('3wa 目前不會把 voice_prompt 傳給 VoxCPM2，文字設計聲線無法區分角色；請改用已授權的錄音克隆。')
      return
    }

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
    if (previewingId !== null) return

    previewRequestRef.current?.controller.abort()
    const controller = new AbortController()
    const request = { characterProfileId, profileId: profile.id, controller }
    previewRequestRef.current = request
    setPreviewing({ characterProfileId, profileId: profile.id })
    try {
      const response = await fetch(apiUrl(`${basePath(characterProfileId)}/${profile.id}/preview`), {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrfToken },
        body: JSON.stringify({ text }),
        signal: controller.signal,
      })
      if (!response.ok) throw new Error(await responseProblem(response, '試講失敗'))
      const blob = await response.blob()
      const url = URL.createObjectURL(blob)
      if (controller.signal.aborted) {
        URL.revokeObjectURL(url)
        return
      }

      const previousAudio = previewAudioRef.current
      const nextAudio = {
        characterProfileId,
        profileId: profile.id,
        url,
        fileName: `${safeDownloadFileNamePart(characterName)}-${safeDownloadFileNamePart(previewSlotLabel(profile))}-試音.${audioFileExtension(blob.type)}`,
      }
      previewAudioRef.current = nextAudio
      setPreviewAudio(nextAudio)
      if (previousAudio) URL.revokeObjectURL(previousAudio.url)
      setMessage('試講已產生，可以重複播放或下載；聲音直接來自這組聲線的即時合成。')
    } catch (error) {
      if (controller.signal.aborted) return
      setMessage(error instanceof Error ? error.message : '試講失敗。')
    } finally {
      if (previewRequestRef.current === request) {
        previewRequestRef.current = null
        setPreviewing((current) => current?.characterProfileId === characterProfileId && current.profileId === profile.id ? null : current)
      }
    }
  }

  function releasePreviewAfterError(url: string) {
    const current = previewAudioRef.current
    if (current?.url !== url) return
    previewAudioRef.current = null
    URL.revokeObjectURL(current.url)
    setPreviewAudio((audio) => audio?.url === current.url ? null : audio)
    setMessage('瀏覽器無法播放這段試講，已釋放暫存音訊，請重新產生。')
  }

  function releasePreviewForProfile(profileId: string) {
    const request = previewRequestRef.current
    if (request?.characterProfileId === characterProfileId && request.profileId === profileId) {
      previewRequestRef.current = null
      request.controller.abort()
      setPreviewing((current) => current?.characterProfileId === characterProfileId && current.profileId === profileId ? null : current)
    }

    const current = previewAudioRef.current
    if (current?.characterProfileId !== characterProfileId || current.profileId !== profileId) return
    previewAudioRef.current = null
    URL.revokeObjectURL(current.url)
    setPreviewAudio((audio) => audio?.url === current.url ? null : audio)
  }

  async function remove(profile: VoiceProfile) {
    releasePreviewForProfile(profile.id)
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
        {characterName} 的自訂聲線 — 基礎聲線之外，可以替緊張／開心／生氣／難過各自克隆一組聲線；找不到的情境會自動退回基礎聲線。
      </p>
      {!DESIGN_VOICE_AVAILABLE && (
        <p className="mt-3 rounded-xl border border-amber-200 bg-amber-50 px-3 py-2 text-xs leading-5 text-amber-800">
          3wa 目前沒有把 voice_prompt 傳給 VoxCPM2，不同文字描述會產生同一聲音。文字設計已停用，現有資料仍會保留但不可試音或正式配音；請使用已取得授權的 WAV 錄音克隆。
        </p>
      )}
      <div className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {SLOTS.map((slot) => {
          const slotKey = `${characterProfileId}:${slot.sceneCode ?? 'base'}`
          const profile = profileFor(slot.sceneCode)
          const isOpen = openSlot === slotKey
          const isBusy = busySlot === slotKey || (profile !== null && busySlot === profile.id)
          const slotMode = mode[slotKey] ?? 'Clone'
          const designUnavailable = profile?.mode === 'Design' && !DESIGN_VOICE_AVAILABLE
          const currentPreview = profile
            && previewAudio?.characterProfileId === characterProfileId
            && previewAudio.profileId === profile.id
            ? previewAudio
            : null

          return (
            <div className="rounded-xl border border-stone-200 bg-stone-50 p-3" key={slotKey}>
              <div className="flex items-center justify-between gap-2">
                <p className="text-sm font-medium text-stone-800">{slot.label}</p>
                {profile && (
                  <span className={`rounded-full border px-3 py-1 text-xs ${designUnavailable ? 'border-amber-200 bg-amber-50 text-amber-700' : STATUS_STYLE[profile.status]}`}>
                    {designUnavailable ? '不可用' : STATUS_LABEL[profile.status]}
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
                          disabled={!DESIGN_VOICE_AVAILABLE}
                          onClick={() => setMode((current) => ({ ...current, [slotKey]: 'Design' }))}
                          type="button"
                        >
                          文字設計（目前不支援）
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
                    {profile.mode === 'Design' ? '文字設計（供應商目前不支援）' : '錄音克隆'}
                    {' · 字數 '}
                    {(profile.transcript ?? profile.voicePromptText ?? '').length}
                    {' · 時長 '}
                    {formatDuration(profile.referenceAudioDurationSeconds)}
                  </p>

                  {profile.status === 'Ready' && !designUnavailable && (
                    <button
                      className="secondary-button w-full px-3 py-2 text-xs disabled:cursor-wait disabled:opacity-60"
                      disabled={previewingId !== null}
                      onClick={() => void playPreview(profile, DEFAULT_PREVIEW_TEXT)}
                      type="button"
                    >
                      {previewingId === profile.id ? '合成中…' : '▶ 播放試講'}
                    </button>
                  )}

                  {currentPreview && (
                    <div className="space-y-2 rounded-lg border border-stone-200 bg-white p-2">
                      <audio
                        aria-label={`${characterName}的${previewSlotLabel(profile)}試音`}
                        autoPlay
                        className="w-full"
                        controls
                        onError={() => releasePreviewAfterError(currentPreview.url)}
                        preload="metadata"
                        src={currentPreview.url}
                      >
                        你的瀏覽器無法播放這段試音。
                      </audio>
                      <a className="block text-center text-xs text-amber-700 underline" download={currentPreview.fileName} href={currentPreview.url}>
                        下載試音
                      </a>
                    </div>
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

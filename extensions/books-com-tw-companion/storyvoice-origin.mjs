const publicStoryVoiceUrl = 'https://aiprod.wrbtycg.tw/StoryVoice'

export function validateStoryVoiceOrigin(value) {
  const url = new URL(value)
  const hasUnsafeParts = Boolean(url.username || url.password || url.search || url.hash)
  const isLocal =
    url.protocol === 'http:' &&
    (url.hostname === 'localhost' || url.hostname === '127.0.0.1') &&
    url.port === '3000' &&
    url.pathname === '/'
  const isPublicStoryVoice =
    url.protocol === 'https:' &&
    url.hostname === 'aiprod.wrbtycg.tw' &&
    url.port === '' &&
    (url.pathname === '/StoryVoice' || url.pathname === '/StoryVoice/')

  if (hasUnsafeParts || (!isLocal && !isPublicStoryVoice)) {
    throw new Error(`StoryVoice 僅允許本機位址或 ${publicStoryVoiceUrl}。`)
  }

  return isLocal ? url.origin : publicStoryVoiceUrl
}
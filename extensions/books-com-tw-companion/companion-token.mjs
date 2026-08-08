export function validateCompanionToken(value) {
  const token = String(value ?? '').trim()
  if (!/^svc_[A-Za-z0-9_-]{43}$/.test(token)) {
    throw new Error('StoryVoice 連線金鑰格式不正確；請回 StoryVoice 重新建立。')
  }

  return token
}

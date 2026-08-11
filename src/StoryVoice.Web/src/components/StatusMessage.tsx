type StatusMessageProps = {
  status: 'idle' | 'loading' | 'ready' | 'error'
  message: string
  loadingMessage?: string
  className?: string
}

export function StatusMessage({ status, message, loadingMessage, className = '' }: StatusMessageProps) {
  const text = status === 'loading' && !message ? (loadingMessage ?? '處理中…') : message
  const tone = status === 'error' ? 'text-rose-300' : status === 'ready' ? 'text-emerald-300' : 'text-stone-500'
  return (
    <p className={`min-h-5 text-xs ${tone} ${className}`} role="status">{text}</p>
  )
}

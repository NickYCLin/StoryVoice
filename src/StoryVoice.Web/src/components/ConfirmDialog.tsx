import { useEffect, useRef } from 'react'

type ConfirmDialogProps = {
  open: boolean
  title: string
  description?: string
  confirmLabel?: string
  cancelLabel?: string
  destructive?: boolean
  onConfirm: () => void
  onCancel: () => void
}

export function ConfirmDialog({
  open,
  title,
  description,
  confirmLabel = '確定',
  cancelLabel = '取消',
  destructive = true,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  const confirmButtonRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    if (!open) return
    confirmButtonRef.current?.focus()

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') onCancel()
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [open, onCancel])

  if (!open) return null

  return (
    <div
      aria-labelledby="confirm-dialog-title"
      aria-modal="true"
      className="fixed inset-0 z-50 grid place-items-center bg-stone-900/40 px-5 backdrop-blur-sm"
      role="dialog"
    >
      <div className="w-full max-w-sm rounded-2xl border border-stone-200 bg-white p-6 shadow-2xl shadow-stone-400/30">
        <h3 className="font-serif text-xl text-stone-900" id="confirm-dialog-title">{title}</h3>
        {description && <p className="mt-3 text-sm leading-6 text-stone-600">{description}</p>}
        <div className="mt-6 flex justify-end gap-3">
          <button className="secondary-button" onClick={onCancel} type="button">{cancelLabel}</button>
          <button
            className={destructive
              ? 'rounded-full bg-rose-600 px-5 py-2.5 text-sm font-semibold text-white transition hover:bg-rose-700'
              : 'primary-button'}
            onClick={onConfirm}
            ref={confirmButtonRef}
            type="button"
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  )
}

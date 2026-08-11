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
      className="fixed inset-0 z-50 grid place-items-center bg-black/60 px-5 backdrop-blur-sm"
      role="dialog"
    >
      <div className="w-full max-w-sm rounded-2xl border border-white/10 bg-[#100d15] p-6 shadow-2xl shadow-black/50">
        <h3 className="font-serif text-xl text-stone-100" id="confirm-dialog-title">{title}</h3>
        {description && <p className="mt-3 text-sm leading-6 text-stone-400">{description}</p>}
        <div className="mt-6 flex justify-end gap-3">
          <button className="secondary-button" onClick={onCancel} type="button">{cancelLabel}</button>
          <button
            className={destructive
              ? 'rounded-full bg-rose-500/90 px-5 py-2.5 text-sm font-semibold text-white transition hover:bg-rose-500'
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

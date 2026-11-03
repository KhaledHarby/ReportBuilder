import type { PointerEvent as ReactPointerEvent } from 'react'

type Props = {
  label: string
  onPointerDown: (event: ReactPointerEvent<HTMLButtonElement>) => void
  className?: string
}

export function DesignerResizer({ label, onPointerDown, className = '' }: Props) {
  return (
    <button
      type="button"
      className={`designer-resizer ${className}`.trim()}
      aria-label={label}
      title={label}
      onPointerDown={onPointerDown}
    />
  )
}

type SpinnerSize = 'sm' | 'md' | 'lg'

type SpinnerProps = {
  size?: SpinnerSize
  label?: string
  fullscreen?: boolean
  overlay?: boolean
  page?: boolean
}

export function Spinner({
  size = 'md',
  label,
  fullscreen = false,
  overlay = false,
  page = false,
}: SpinnerProps) {
  const resolvedSize: SpinnerSize = size === 'md' && (fullscreen || overlay || page) ? 'lg' : size
  const spinner = (
    <span
      className={`app-spinner app-spinner--${resolvedSize}`}
      role="status"
      aria-live="polite"
      aria-label={label}
    >
      <span className="app-spinner-track" aria-hidden="true">
        <span className="app-spinner-ring" />
        {resolvedSize !== 'sm' && <span className="app-spinner-ring app-spinner-ring-inner" />}
        {resolvedSize !== 'sm' && <span className="app-spinner-mark">M</span>}
      </span>
      {label ? <span className="app-spinner-label">{label}</span> : null}
    </span>
  )

  if (fullscreen) {
    return <div className="app-spinner-screen">{spinner}</div>
  }
  if (overlay) {
    return <div className="app-spinner-overlay">{spinner}</div>
  }
  if (page) {
    return <div className="app-spinner-page">{spinner}</div>
  }
  return spinner
}

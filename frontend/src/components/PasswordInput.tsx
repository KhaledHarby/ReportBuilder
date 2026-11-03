import { useState, type InputHTMLAttributes } from 'react'
import { useLocale } from '../i18n/LocaleProvider'
import { IconEye, IconEyeOff } from './Icons'

type Props = Omit<InputHTMLAttributes<HTMLInputElement>, 'type'>

export function PasswordInput({ className, disabled, ...props }: Props) {
  const { t } = useLocale()
  const [visible, setVisible] = useState(false)

  return (
    <div className="password-input">
      <input
        {...props}
        className={className}
        type={visible ? 'text' : 'password'}
        disabled={disabled}
      />
      <button
        type="button"
        className="password-toggle"
        aria-label={visible ? t('hidePassword') : t('showPassword')}
        aria-pressed={visible}
        disabled={disabled}
        onClick={() => setVisible((open) => !open)}
      >
        {visible ? <IconEyeOff /> : <IconEye />}
      </button>
    </div>
  )
}

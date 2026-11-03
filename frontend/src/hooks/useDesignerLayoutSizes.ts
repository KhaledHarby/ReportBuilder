import { useCallback, useEffect, type PointerEvent as ReactPointerEvent, type RefObject } from 'react'
import { useLocale } from '../i18n/LocaleProvider'

const STORAGE_DATASET = 'lumen-designer-dataset-pane'
const STORAGE_PREVIEW = 'lumen-designer-preview-pane'

const DATASET = { min: 200, max: 520, default: 280 }
const PREVIEW = { min: 320, max: 960, default: 480 }

function clamp(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, Math.round(value)))
}

function readSize(key: string, limits: { min: number; max: number; default: number }) {
  const raw = Number(localStorage.getItem(key))
  return Number.isFinite(raw) ? clamp(raw, limits.min, limits.max) : limits.default
}

function applySizes(stage: HTMLElement, dataset: number, preview: number) {
  stage.style.setProperty('--designer-dataset-pane', `${dataset}px`)
  stage.style.setProperty('--designer-preview-pane', `${preview}px`)
}

export function useDesignerLayoutSizes(stageRef: RefObject<HTMLElement | null>) {
  const { dir } = useLocale()
  const rtlSign = dir === 'rtl' ? -1 : 1

  useEffect(() => {
    const stage = stageRef.current
    if (!stage) {
      return
    }
    applySizes(
      stage,
      readSize(STORAGE_DATASET, DATASET),
      readSize(STORAGE_PREVIEW, PREVIEW),
    )
  }, [stageRef])

  const resizeDataset = useCallback(
    (event: ReactPointerEvent<HTMLButtonElement>) => {
      event.preventDefault()
      event.stopPropagation()
      const stage = stageRef.current
      if (!stage) {
        return
      }

      const handle = event.currentTarget
      const startX = event.clientX
      const startWidth = readSize(STORAGE_DATASET, DATASET)
      handle.setPointerCapture(event.pointerId)
      document.body.style.cursor = 'col-resize'
      document.body.style.userSelect = 'none'
      stage.classList.add('is-resizing')

      const onMove = (move: PointerEvent) => {
        const next = clamp(
          startWidth + (move.clientX - startX) * rtlSign,
          DATASET.min,
          DATASET.max,
        )
        stage.style.setProperty('--designer-dataset-pane', `${next}px`)
      }
      const onUp = () => {
        document.body.style.cursor = ''
        document.body.style.userSelect = ''
        stage.classList.remove('is-resizing')
        handle.removeEventListener('pointermove', onMove)
        handle.removeEventListener('pointerup', onUp)
        handle.removeEventListener('pointercancel', onUp)
        if (handle.hasPointerCapture(event.pointerId)) {
          handle.releasePointerCapture(event.pointerId)
        }
        const parsed = Number.parseInt(
          stage.style.getPropertyValue('--designer-dataset-pane'),
          10,
        )
        const preview = readSize(STORAGE_PREVIEW, PREVIEW)
        const dataset = Number.isFinite(parsed)
          ? clamp(parsed, DATASET.min, DATASET.max)
          : readSize(STORAGE_DATASET, DATASET)
        localStorage.setItem(STORAGE_DATASET, String(dataset))
        applySizes(stage, dataset, preview)
      }

      handle.addEventListener('pointermove', onMove)
      handle.addEventListener('pointerup', onUp)
      handle.addEventListener('pointercancel', onUp)
    },
    [rtlSign, stageRef],
  )

  const resizePreview = useCallback(
    (event: ReactPointerEvent<HTMLButtonElement>) => {
      event.preventDefault()
      event.stopPropagation()
      const stage = stageRef.current
      if (!stage) {
        return
      }

      const handle = event.currentTarget
      const startX = event.clientX
      const startWidth = readSize(STORAGE_PREVIEW, PREVIEW)
      handle.setPointerCapture(event.pointerId)
      document.body.style.cursor = 'col-resize'
      document.body.style.userSelect = 'none'
      stage.classList.add('is-resizing')

      const onMove = (move: PointerEvent) => {
        const next = clamp(
          startWidth + (startX - move.clientX) * rtlSign,
          PREVIEW.min,
          PREVIEW.max,
        )
        stage.style.setProperty('--designer-preview-pane', `${next}px`)
      }
      const onUp = () => {
        document.body.style.cursor = ''
        document.body.style.userSelect = ''
        stage.classList.remove('is-resizing')
        handle.removeEventListener('pointermove', onMove)
        handle.removeEventListener('pointerup', onUp)
        handle.removeEventListener('pointercancel', onUp)
        if (handle.hasPointerCapture(event.pointerId)) {
          handle.releasePointerCapture(event.pointerId)
        }
        const parsed = Number.parseInt(
          stage.style.getPropertyValue('--designer-preview-pane'),
          10,
        )
        const dataset = readSize(STORAGE_DATASET, DATASET)
        const preview = Number.isFinite(parsed)
          ? clamp(parsed, PREVIEW.min, PREVIEW.max)
          : readSize(STORAGE_PREVIEW, PREVIEW)
        localStorage.setItem(STORAGE_PREVIEW, String(preview))
        applySizes(stage, dataset, preview)
      }

      handle.addEventListener('pointermove', onMove)
      handle.addEventListener('pointerup', onUp)
      handle.addEventListener('pointercancel', onUp)
    },
    [rtlSign, stageRef],
  )

  return { resizeDataset, resizePreview }
}

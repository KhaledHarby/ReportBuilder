import { useEffect, useState } from 'react'

/**
 * Reports once an element has scrolled near the viewport, and stays true afterwards.
 * Used to defer requests for panes below the fold (the detail grid, the map) until the
 * user can actually see them, instead of firing them on every selection change.
 */
export function useOnceInView<T extends HTMLElement>(rootMargin = '240px') {
  const [node, setNode] = useState<T | null>(null)
  const [inView, setInView] = useState(false)

  useEffect(() => {
    if (inView || !node) {
      return
    }
    if (typeof IntersectionObserver === 'undefined') {
      setInView(true)
      return
    }
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) {
          setInView(true)
          observer.disconnect()
        }
      },
      { rootMargin },
    )
    observer.observe(node)
    return () => observer.disconnect()
  }, [node, inView, rootMargin])

  return { ref: setNode, inView }
}

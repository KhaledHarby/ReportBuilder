type SaveFilePicker = (options: {
  suggestedName?: string
  types?: Array<{ description?: string; accept: Record<string, string[]> }>
}) => Promise<FileSystemFileHandle>

export function bilingualExportName(english?: string | null, arabic?: string | null) {
  const en = (english ?? '').trim()
  const ar = (arabic ?? '').trim()
  if (en && ar && en !== ar) {
    return `${en} - ${ar}`
  }
  return en || ar
}

export function exportFileName(name: string | undefined, extension: string) {
  const cleaned = (name ?? '')
    .replace(/[\\/:*?"<>|]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .slice(0, 120)
  const base = cleaned || 'report'
  const ext = extension.replace(/^\./, '')
  return `${base}.${ext}`
}

export async function pickCsvFile(suggestedName: string) {
  const picker = (window as Window & { showSaveFilePicker?: SaveFilePicker }).showSaveFilePicker
  if (!picker) {
    return undefined
  }

  try {
    return await picker({
      suggestedName,
      types: [{ description: 'CSV', accept: { 'text/csv': ['.csv'] } }],
    })
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw error
    }
    return undefined
  }
}

export async function saveDownload(
  response: Response,
  fallbackName: string,
  file?: FileSystemFileHandle,
) {
  if (!response.ok) {
    const body = await response.json().catch(() => null)
    const detail =
      (body && (body.detail || body.title || body.message)) || response.statusText
    throw new Error(typeof detail === 'string' ? detail : 'Download failed')
  }

  if (file && response.body) {
    const writable = await file.createWritable()
    await response.body.pipeTo(writable)
    return
  }

  const blob = await response.blob()
  const fileName = fileNameFromDisposition(response.headers.get('Content-Disposition'), fallbackName)

  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

function fileNameFromDisposition(header: string | null, fallback: string) {
  if (!header) {
    return fallback
  }

  const encoded = header.match(/filename\*=(?:UTF-8''|utf-8'')([^;]+)/i)
  if (encoded?.[1]) {
    const raw = encoded[1].trim().replace(/^"|"$/g, '')
    try {
      return decodeURIComponent(raw)
    } catch {
      return raw
    }
  }

  const plain = header.match(/filename="([^"]+)"/i) ?? header.match(/filename=([^;]+)/i)
  return plain?.[1]?.trim() || fallback
}

import { jsPDF } from 'jspdf'
import PptxGenJS from 'pptxgenjs'
import { captureCharts, captureMapImage } from './analyticsCapture'

export type AnalyticsExportSnapshot = {
  title: string
  description?: string
  selections: string[]
  kpis: { label: string; value: string }[]
  labels?: {
    selections?: string
    allData?: string
    kpis?: string
    map?: string
  }
}

function fileStub(title: string) {
  const slug = title
    .toLowerCase()
    .replace(/[^a-z0-9\u0600-\u06ff]+/gi, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 48)
  const stamp = new Date().toISOString().slice(0, 10)
  return `${slug || 'analytics'}-${stamp}`
}

export async function exportAnalyticsPdf(snapshot: AnalyticsExportSnapshot) {
  const pdf = new jsPDF({ orientation: 'landscape', unit: 'pt', format: 'a4' })
  const pageW = pdf.internal.pageSize.getWidth()
  const pageH = pdf.internal.pageSize.getHeight()
  const margin = 36
  let y = margin

  pdf.setFont('helvetica', 'bold')
  pdf.setFontSize(20)
  pdf.text(snapshot.title, margin, y)
  y += 22
  pdf.setFont('helvetica', 'normal')
  pdf.setFontSize(10)
  pdf.setTextColor(90)
  pdf.text(new Date().toLocaleString(), margin, y)
  y += 16
  if (snapshot.description) {
    pdf.setTextColor(60)
    pdf.text(pdf.splitTextToSize(snapshot.description, pageW - margin * 2), margin, y)
    y += 28
  }

  pdf.setTextColor(20)
  pdf.setFont('helvetica', 'bold')
  pdf.setFontSize(12)
  pdf.text(snapshot.labels?.selections ?? 'Selections', margin, y)
  y += 16
  pdf.setFont('helvetica', 'normal')
  pdf.setFontSize(10)
  const selectionLine = snapshot.selections.length
    ? snapshot.selections.join('  ·  ')
    : snapshot.labels?.allData ?? 'All data'
  pdf.text(pdf.splitTextToSize(selectionLine, pageW - margin * 2), margin, y)
  y += 28

  if (snapshot.kpis.length > 0) {
    pdf.setFont('helvetica', 'bold')
    pdf.setFontSize(12)
    pdf.text(snapshot.labels?.kpis ?? 'KPIs', margin, y)
    y += 14
    const colW = (pageW - margin * 2) / Math.min(snapshot.kpis.length, 4)
    snapshot.kpis.forEach((kpi, index) => {
      const col = index % 4
      const row = Math.floor(index / 4)
      const x = margin + col * colW
      const top = y + row * 42
      pdf.setFont('helvetica', 'normal')
      pdf.setFontSize(9)
      pdf.setTextColor(90)
      pdf.text(kpi.label, x, top)
      pdf.setFont('helvetica', 'bold')
      pdf.setFontSize(14)
      pdf.setTextColor(20)
      pdf.text(kpi.value, x, top + 16)
    })
    y += Math.ceil(snapshot.kpis.length / 4) * 42 + 10
  }

  const charts = captureCharts()
  for (const chart of charts) {
    if (y > pageH - 220) {
      pdf.addPage()
      y = margin
    }
    pdf.setFont('helvetica', 'bold')
    pdf.setFontSize(12)
    pdf.setTextColor(20)
    pdf.text(chart.title, margin, y)
    y += 10
    const width = pageW - margin * 2
    const height = 180
    pdf.addImage(chart.dataUrl, 'PNG', margin, y, width, height)
    y += height + 18
  }

  const map = captureMapImage()
  if (map) {
    if (y > pageH - 240) {
      pdf.addPage()
      y = margin
    }
    pdf.setFont('helvetica', 'bold')
    pdf.setFontSize(12)
    pdf.text(snapshot.labels?.map ?? 'Map', margin, y)
    y += 10
    pdf.addImage(map, 'PNG', margin, y, pageW - margin * 2, 200)
  }

  pdf.save(`${fileStub(snapshot.title)}.pdf`)
}

export async function exportAnalyticsPptx(snapshot: AnalyticsExportSnapshot) {
  const deck = new PptxGenJS()
  deck.author = 'Report Builder'
  deck.title = snapshot.title
  deck.layout = 'LAYOUT_WIDE'

  const title = deck.addSlide()
  title.addText(snapshot.title, {
    x: 0.6,
    y: 1.6,
    w: 12,
    h: 0.8,
    fontSize: 32,
    bold: true,
    color: '17262d',
  })
  if (snapshot.description) {
    title.addText(snapshot.description, {
      x: 0.6,
      y: 2.5,
      w: 12,
      h: 0.8,
      fontSize: 14,
      color: '5c7079',
    })
  }
  title.addText(
    snapshot.selections.length
      ? snapshot.selections.join('  ·  ')
      : snapshot.labels?.allData ?? 'All data',
    {
    x: 0.6,
    y: 3.6,
    w: 12,
    h: 0.6,
    fontSize: 12,
    color: '005470',
  })
  title.addText(new Date().toLocaleString(), {
    x: 0.6,
    y: 6.4,
    w: 12,
    h: 0.3,
    fontSize: 10,
    color: '8aa0a8',
  })

  if (snapshot.kpis.length > 0) {
    const kpiSlide = deck.addSlide()
    kpiSlide.addText(snapshot.labels?.kpis ?? 'KPIs', {
      x: 0.5,
      y: 0.3,
      w: 12,
      h: 0.4,
      fontSize: 18,
      bold: true,
      color: '17262d',
    })
    snapshot.kpis.forEach((kpi, index) => {
      const x = 0.5 + (index % 4) * 3.1
      const y = 1.1 + Math.floor(index / 4) * 1.6
      kpiSlide.addShape('roundRect', {
        x,
        y,
        w: 2.9,
        h: 1.3,
        fill: { color: 'F4F8F9' },
        rectRadius: 0.1,
      })
      kpiSlide.addText(kpi.label, { x: x + 0.15, y: y + 0.15, w: 2.6, h: 0.35, fontSize: 11, color: '5c7079' })
      kpiSlide.addText(kpi.value, { x: x + 0.15, y: y + 0.5, w: 2.6, h: 0.55, fontSize: 20, bold: true, color: '17262d' })
    })
  }

  for (const chart of captureCharts()) {
    const slide = deck.addSlide()
    slide.addText(chart.title, { x: 0.5, y: 0.3, w: 12.3, h: 0.4, fontSize: 18, bold: true, color: '17262d' })
    slide.addImage({ data: chart.dataUrl, x: 0.5, y: 0.9, w: 12.3, h: 5.8 })
  }

  const map = captureMapImage()
  if (map) {
    const slide = deck.addSlide()
    slide.addText(snapshot.labels?.map ?? 'Map', {
      x: 0.5,
      y: 0.3,
      w: 12.3,
      h: 0.4,
      fontSize: 18,
      bold: true,
      color: '17262d',
    })
    slide.addImage({ data: map, x: 0.5, y: 0.9, w: 12.3, h: 5.8 })
  }

  await deck.writeFile({ fileName: `${fileStub(snapshot.title)}.pptx` })
}

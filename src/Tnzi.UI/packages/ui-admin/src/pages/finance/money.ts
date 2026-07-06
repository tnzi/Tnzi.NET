import { h, type VNode } from 'vue'

/**
 * Shared money formatting for the finance pages.
 *
 * With a currency code, renders a localized currency string. Without one,
 * degrades to a plain 2-decimal number rather than assuming a `$` default —
 * a fixed `USD` fallback mislabels every non-USD document as dollars.
 */
export function fmtMoney(amount?: number | null, currency?: string | null): string {
  if (amount === null || amount === undefined) return '—'
  if (!currency) return fmtAmount(amount)
  try {
    return new Intl.NumberFormat(undefined, {
      style: 'currency',
      currency,
    }).format(amount)
  } catch {
    return `${currency} ${amount.toFixed(2)}`
  }
}

/** Plain 2-decimal amount without a currency symbol (report tables). */
export function fmtAmount(amount?: number | null): string {
  if (amount === null || amount === undefined) return '—'
  return new Intl.NumberFormat(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(amount)
}

/** Tabular-nums cell for amount columns. */
export function amountCell(text: string, emphasis = false): VNode {
  return h(
    'span',
    { style: `font-variant-numeric: tabular-nums;${emphasis ? ' font-weight: 600;' : ''}` },
    text,
  )
}

/** Local date (00:00) → ISO string the backend DateTime binder accepts. */
export function tsToIsoDate(ts: number): string {
  const d = new Date(ts)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

/**
 * Backend date-only value (UTC midnight ISO) → local-midnight timestamp for
 * date pickers. Going through `new Date(iso).getTime()` instead would shift
 * the calendar day for viewers west of UTC (and round-trip a day earlier
 * through tsToIsoDate on save).
 */
export function isoDateToLocalTs(iso: string): number {
  const y = Number(iso.slice(0, 4))
  const m = Number(iso.slice(5, 7))
  const d = Number(iso.slice(8, 10))
  return new Date(y, m - 1, d).getTime()
}

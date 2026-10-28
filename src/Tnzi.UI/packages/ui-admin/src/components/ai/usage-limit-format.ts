/**
 * Formatting and draft helpers shared by the usage-limit panels
 * (`TUsageLimitDefaultsPanel`, `TUserUsageLimitsPanel`, `TUsageLimitPolicyForm`).
 *
 * Amounts are API-equivalent USD: what the same usage would cost at the
 * providers' list prices, so a turn on a large model and a turn on a small one
 * count by what they would cost rather than by raw tokens.
 */
import type { SaveUsagePolicyDto, UsageLimitSettingDto, UsagePolicyDto } from '@tnzi/core/services/ai'
import { UsageLimitMode } from '@tnzi/core/services/ai'

export type UsageTranslate = (key: string, params?: Record<string, unknown>) => string

const MINUTE_MS = 60_000

/**
 * "$1,234.56". A positive amount under a cent reads "<$0.01" rather than "$0.00":
 * a user who has used something has not used nothing.
 */
export function formatUsd(value: number | null | undefined): string {
  const n = typeof value === 'number' && Number.isFinite(value) ? value : 0
  if (n > 0 && n < 0.01) return '<$0.01'
  return n.toLocaleString('en-US', { style: 'currency', currency: 'USD', minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

/** A window's length: "5 hours", "7 days", "90 min". */
export function formatWindowLength(minutes: number | null | undefined, t: UsageTranslate): string {
  const m = typeof minutes === 'number' && Number.isFinite(minutes) && minutes > 0 ? minutes : 0
  if (m > 0 && m % (24 * 60) === 0) {
    const days = m / (24 * 60)
    return days === 1 ? t('duration.day') : t('duration.days', { count: days })
  }
  if (m > 0 && m % 60 === 0) {
    const hours = m / 60
    return hours === 1 ? t('duration.hour') : t('duration.hours', { count: hours })
  }
  return t('duration.minutes', { count: m })
}

/**
 * When a window resets: a countdown within a day ("Resets in 2 h 13 min"),
 * otherwise the local date and time. Null when the window has not started.
 */
export function formatReset(iso: string | null | undefined, t: UsageTranslate, now: Date = new Date()): string | null {
  if (!iso) return null
  const at = new Date(iso)
  if (Number.isNaN(at.getTime())) return null
  const ms = at.getTime() - now.getTime()
  if (ms < 24 * 60 * MINUTE_MS) {
    return t('user.resetsIn', { time: formatCountdown(Math.max(0, ms), t) })
  }
  return t('user.resetsAt', {
    time: at.toLocaleString(undefined, { weekday: 'short', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' }),
  })
}

/** "2 h 13 min" with minutes rounded up (a reset 61 seconds away is "2 min", never "1 min"). */
export function formatCountdown(ms: number, t: UsageTranslate): string {
  if (ms < MINUTE_MS) return t('countdown.lessThanMinute')
  const total = Math.ceil(ms / MINUTE_MS)
  const h = Math.floor(total / 60)
  const m = total % 60
  if (h === 0) return t('countdown.m', { m })
  if (m === 0) return t('countdown.h', { h })
  return t('countdown.hm', { h, m })
}

/** A share 0-100 for a bar (the server may report a little over 100: the turn that crossed was allowed to finish). */
export function barPercent(value: number | null | undefined): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) return 0
  return Math.max(0, Math.min(100, value))
}

/** An editable copy of a saved policy. */
export function toDraft(policy: UsagePolicyDto | null | undefined): SaveUsagePolicyDto {
  return {
    sessionLimit: copySetting(policy?.sessionLimit),
    weeklyLimit: copySetting(policy?.weeklyLimit),
    allowedModels: policy?.allowedModels ? [...policy.allowedModels] : null,
    defaultModel: policy?.defaultModel ?? null,
    note: policy?.note ?? null,
  }
}

function copySetting(setting: UsageLimitSettingDto | null | undefined): UsageLimitSettingDto {
  return { mode: setting?.mode ?? UsageLimitMode.Inherit, amountUsd: setting?.amountUsd ?? null }
}

/** Two drafts say the same thing (an amount on a non-limited item does not count; model order does not either). */
export function samePolicy(a: SaveUsagePolicyDto, b: SaveUsagePolicyDto): boolean {
  const setting = (s: UsageLimitSettingDto) => `${s.mode}:${s.mode === UsageLimitMode.Limited ? Number(s.amountUsd ?? -1) : ''}`
  const models = (m: string[] | null | undefined) => (m ? [...m].sort().join('|') : '<inherit>')
  return (
    setting(a.sessionLimit) === setting(b.sessionLimit)
    && setting(a.weeklyLimit) === setting(b.weeklyLimit)
    && models(a.allowedModels) === models(b.allowedModels)
    && (a.defaultModel || null) === (b.defaultModel || null)
    && (a.note || '').trim() === (b.note || '').trim()
  )
}

/**
 * What a save would be refused for, as a translation key, or null. Mirrors the
 * server's rules so the operator hears it before the round trip: an amount for
 * every limited item, a non-empty model list when one is chosen, and a default
 * model the user may actually use.
 */
export function policyProblem(draft: SaveUsagePolicyDto, allowedForDefault: string[] | null): string | null {
  for (const s of [draft.sessionLimit, draft.weeklyLimit]) {
    if (s.mode === UsageLimitMode.Limited && (typeof s.amountUsd !== 'number' || !Number.isFinite(s.amountUsd) || s.amountUsd < 0)) {
      return 'policy.amountRequired'
    }
  }
  if (draft.allowedModels && draft.allowedModels.length === 0) return 'policy.modelsRequired'
  if (draft.defaultModel && allowedForDefault && allowedForDefault.length > 0 && !allowedForDefault.includes(draft.defaultModel)) {
    return 'policy.defaultNotAllowed'
  }
  return null
}

/** The payload to save: trimmed note, no amount on items that are not limited. */
export function toPayload(draft: SaveUsagePolicyDto): SaveUsagePolicyDto {
  const clean = (s: UsageLimitSettingDto): UsageLimitSettingDto => ({
    mode: s.mode,
    amountUsd: s.mode === UsageLimitMode.Limited ? s.amountUsd ?? null : null,
  })
  return {
    sessionLimit: clean(draft.sessionLimit),
    weeklyLimit: clean(draft.weeklyLimit),
    allowedModels: draft.allowedModels ? [...draft.allowedModels] : null,
    defaultModel: draft.defaultModel || null,
    note: draft.note?.trim() ? draft.note.trim() : null,
  }
}

/**
 * Pure model behind the User Center "Notifications" section: server rows ->
 * an editable draft -> the minimal set of upserts that saves it.
 *
 * Kept out of the SFC so the rules that decide WHAT gets written (which rows,
 * in which zone, and when nothing should be written at all) are unit-testable
 * without mounting naive-ui.
 *
 * Time zones follow three rules:
 *
 * 1. Every channel keeps the zone its stored quiet hours are read in. A row an
 *    operator saved in another zone, or a row from before zones existed (null =
 *    UTC), is shown in that zone, marked as such, and never re-labelled by a
 *    save that does not touch its quiet hours.
 * 2. Changing the shared zone CONVERTS each set window to the new zone, keeping
 *    the instants it names today. Relabelling instead would move every message
 *    by the offset between the two zones.
 * 3. A save sends a zone only for the quiet hours it is setting. A channel
 *    switched off, or a cap changed, carries its row's own stored zone back
 *    unchanged, so a zone this host cannot resolve never blocks it.
 */
import type {
  NotificationPreferenceDto,
  SetNotificationPreferenceDto,
} from '@tnzi/core/services/notification'
import { convertTimeOfDay } from '../../utils/time-zones'

/**
 * The channels a person can switch here: the four that actually deliver.
 * The preference vocabulary also names InApp / Webhook, but no sender reads
 * those rows, so a switch for them would change nothing.
 */
export const SELF_SERVICE_CHANNELS = ['Email', 'Sms', 'Push', 'Fax'] as const
export type SelfServiceChannel = (typeof SELF_SERVICE_CHANNELS)[number]

/** What the quiet-hours selects start from when a person switches them on. */
export const DEFAULT_QUIET_START = '22:00:00'
export const DEFAULT_QUIET_END = '07:00:00'

/** The zone a stored row's quiet hours are read in when it carries none. */
const STORED_DEFAULT_ZONE = 'UTC'

/** One channel's editable state. */
export interface ChannelDraft {
  channel: SelfServiceChannel
  /**
   * The channel name as the stored row spells it. The upsert key is
   * (channel, category), so a row an operator saved as "email" must be
   * updated as "email", not shadowed by a second "Email" row.
   */
  wireChannel: string
  isEnabled: boolean
  quietHours: boolean
  /** "HH:mm:ss" wall-clock time in {@link ChannelDraft.timeZone}. */
  quietHoursStart: string | null
  quietHoursEnd: string | null
  /**
   * The zone this channel's quiet hours are read in. Stored quiet hours keep
   * their row's zone (UTC when the row has none); a channel without quiet
   * hours follows the shared zone.
   */
  timeZone: string
  /** The row's zone exactly as stored (null = none / no row); sent back when this save does not set quiet hours. */
  storedTimeZone: string | null
  /** null = no limit. */
  maxFrequencyPerHour: number | null
}

export interface PreferencesDraft {
  /** The zone new quiet hours are entered in; changing it converts every set window. */
  timeZone: string
  channels: ChannelDraft[]
}

/** One choice in a quiet-hours select; `value` is "HH:mm:ss", the shape the server reads and returns. */
export type QuietHourOption = { label: string; value: string }

const QUIET_HOUR_STEP_MINUTES = 15

function pad2(n: number): string {
  return String(n).padStart(2, '0')
}

/**
 * Choices for the quiet-hours selects: every quarter hour, plus any stored
 * value that is off that grid (an operator may have saved 22:10), so a select
 * always shows what is saved instead of rendering blank and then saving
 * something else.
 *
 * A select rather than a time picker on purpose: two clicks per time, and a
 * quarter hour is all the precision "don't disturb me at night" needs. Zone
 * offsets are whole quarter hours too, so a converted window stays on the grid.
 */
export function quietHourOptions(extra: ReadonlyArray<string | null | undefined> = []): QuietHourOption[] {
  const values = new Set<string>()
  for (let minutes = 0; minutes < 24 * 60; minutes += QUIET_HOUR_STEP_MINUTES) {
    values.add(`${pad2(Math.floor(minutes / 60))}:${pad2(minutes % 60)}:00`)
  }
  for (const value of extra) {
    if (value) values.add(value)
  }
  return [...values]
    .sort()
    .map((value) => ({ value, label: /^\d\d:\d\d:00$/.test(value) ? value.slice(0, 5) : value }))
}

/** A draft that cannot be saved as it stands, per channel. */
export interface DraftProblem {
  channel: SelfServiceChannel
  /** `incomplete`: one of the two times is missing; `empty`: start equals end, so the window never applies. */
  kind: 'incomplete' | 'empty'
}

/** The channel-wide row (category null) for a channel, matched case-insensitively. */
export function channelRow(
  rows: readonly NotificationPreferenceDto[],
  channel: string,
): NotificationPreferenceDto | undefined {
  const wanted = channel.toLowerCase()
  return rows.find((r) => !r.category && r.channel.toLowerCase() === wanted)
}

/** Rows that apply to one category on one channel; shown as rules, not edited as channels. */
export function categoryRules(rows: readonly NotificationPreferenceDto[]): NotificationPreferenceDto[] {
  return rows.filter((r) => !!r.category)
}

function hasQuietHours(row: NotificationPreferenceDto | undefined): boolean {
  return !!(row?.quietHoursStart && row?.quietHoursEnd)
}

/**
 * Build the draft from the caller's rows.
 *
 * The shared zone starts from a zone the person chose before (the first
 * channel with zoned quiet hours, else any row that carries a zone), else the
 * browser's. A zone-less row does not set it: those are UTC rows from before
 * zones existed, and they keep showing as UTC on their own channel.
 */
export function buildDraft(rows: readonly NotificationPreferenceDto[], browserZone: string): PreferencesDraft {
  const channelRows = SELF_SERVICE_CHANNELS.map((c) => channelRow(rows, c))
  const chosen =
    channelRows.find((r) => hasQuietHours(r) && !!r?.quietHoursTimeZone)?.quietHoursTimeZone ??
    rows.map((r) => r.quietHoursTimeZone).find((z): z is string => !!z)
  const shared = chosen ?? browserZone

  const channels = SELF_SERVICE_CHANNELS.map((channel, i): ChannelDraft => {
    const row = channelRows[i]
    const quiet = hasQuietHours(row)
    return {
      channel,
      wireChannel: row?.channel ?? channel,
      isEnabled: row?.isEnabled ?? true,
      quietHours: quiet,
      quietHoursStart: quiet ? (row?.quietHoursStart ?? null) : null,
      quietHoursEnd: quiet ? (row?.quietHoursEnd ?? null) : null,
      timeZone: quiet ? row?.quietHoursTimeZone || STORED_DEFAULT_ZONE : shared,
      storedTimeZone: row?.quietHoursTimeZone ?? null,
      maxFrequencyPerHour: row?.maxFrequencyPerHour ?? null,
    }
  })

  return { timeZone: shared, channels }
}

/** A detached copy, so the "as loaded" snapshot never moves with the form. */
export function cloneDraft(draft: PreferencesDraft): PreferencesDraft {
  return { timeZone: draft.timeZone, channels: draft.channels.map((c) => ({ ...c })) }
}

/**
 * The same channel with its quiet hours moved to `zone`, keeping the instants
 * they name on the day `at` falls on. A channel without quiet hours just takes
 * the zone. When a time cannot be converted (zone unknown to this browser) the
 * channel is returned unchanged rather than relabelled.
 */
export function moveChannelToZone(channel: ChannelDraft, zone: string, at: Date = new Date()): ChannelDraft {
  if (channel.timeZone === zone) return channel
  if (!channel.quietHours || !channel.quietHoursStart || !channel.quietHoursEnd) return { ...channel, timeZone: zone }

  const start = convertTimeOfDay(channel.quietHoursStart, channel.timeZone, zone, at)
  const end = convertTimeOfDay(channel.quietHoursEnd, channel.timeZone, zone, at)
  if (start === null || end === null) return channel
  return { ...channel, quietHoursStart: start, quietHoursEnd: end, timeZone: zone }
}

/** The draft with a new shared zone: every channel moves to it (see {@link moveChannelToZone}). */
export function changeSharedZone(draft: PreferencesDraft, zone: string, at: Date = new Date()): PreferencesDraft {
  return { timeZone: zone, channels: draft.channels.map((c) => moveChannelToZone(c, zone, at)) }
}

/** The part of a channel draft that is actually stored; times and zone only count while quiet hours are on. */
function storedShape(c: ChannelDraft): string {
  return JSON.stringify([
    c.isEnabled,
    c.quietHours ? c.quietHoursStart : null,
    c.quietHours ? c.quietHoursEnd : null,
    c.quietHours ? c.timeZone : null,
    c.maxFrequencyPerHour ?? null,
  ])
}

/**
 * The zone to send for a channel. Quiet hours being set carry their zone; when
 * that is the zone the row already had, the stored value goes back verbatim
 * (a legacy null stays null rather than becoming "UTC"). Without quiet hours
 * the row's own stored zone goes back, so this save cannot fail on a zone the
 * person never touched.
 */
function zoneToSend(c: ChannelDraft): string | null {
  if (!c.quietHours) return c.storedTimeZone
  return c.timeZone === (c.storedTimeZone || STORED_DEFAULT_ZONE) ? c.storedTimeZone : c.timeZone
}

function toInput(c: ChannelDraft): SetNotificationPreferenceDto {
  return {
    channel: c.wireChannel,
    isEnabled: c.isEnabled,
    quietHoursStart: c.quietHours ? (c.quietHoursStart ?? undefined) : undefined,
    quietHoursEnd: c.quietHours ? (c.quietHoursEnd ?? undefined) : undefined,
    quietHoursTimeZone: zoneToSend(c) ?? undefined,
    maxFrequencyPerHour: c.maxFrequencyPerHour ?? undefined,
  }
}

/**
 * The channels whose stored shape differs between `initial` and `current`.
 * Only these are written, so an untouched channel keeps its row exactly as it
 * is, zone included.
 */
export function changedChannels(initial: PreferencesDraft, current: PreferencesDraft): ChannelDraft[] {
  return current.channels.filter((c) => {
    const before = initial.channels.find((b) => b.channel === c.channel)
    return !before || storedShape(before) !== storedShape(c)
  })
}

/** The upserts that turn `initial` into `current`, and nothing more. */
export function pendingUpserts(initial: PreferencesDraft, current: PreferencesDraft): SetNotificationPreferenceDto[] {
  return changedChannels(initial, current).map(toInput)
}

/**
 * Quiet hours that cannot be saved as entered, among the channels about to be
 * written. A stored row the person is not changing is not their problem to fix
 * before they may save another channel.
 */
export function findProblems(channels: readonly ChannelDraft[]): DraftProblem[] {
  const problems: DraftProblem[] = []
  for (const c of channels) {
    if (!c.quietHours) continue
    if (!c.quietHoursStart || !c.quietHoursEnd) problems.push({ channel: c.channel, kind: 'incomplete' })
    else if (c.quietHoursStart === c.quietHoursEnd) problems.push({ channel: c.channel, kind: 'empty' })
  }
  return problems
}

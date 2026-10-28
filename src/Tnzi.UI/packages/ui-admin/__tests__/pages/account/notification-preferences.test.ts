import { describe, it, expect } from 'vitest'
import type { NotificationPreferenceDto } from '@tnzi/core/services/notification'
import {
  buildDraft,
  categoryRules,
  changeSharedZone,
  changedChannels,
  cloneDraft,
  findProblems,
  moveChannelToZone,
  pendingUpserts,
  quietHourOptions,
} from '../../../src/pages/account/notification-preferences'

const BROWSER = 'America/Toronto'
const SUMMER = new Date('2026-07-15T12:00:00Z')
const WINTER = new Date('2026-01-15T12:00:00Z')

function row(over: Partial<NotificationPreferenceDto>): NotificationPreferenceDto {
  return { id: 'r', userId: 'u1', channel: 'Email', isEnabled: true, ...over }
}

function channel(draft: ReturnType<typeof buildDraft>, name: string) {
  return draft.channels.find((c) => c.channel === name)!
}

describe('buildDraft', () => {
  it('starts every delivering channel at the defaults, in the browser zone, when nothing is stored', () => {
    const draft = buildDraft([], BROWSER)
    expect(draft.timeZone).toBe(BROWSER)
    expect(draft.channels.map((c) => c.channel)).toEqual(['Email', 'Sms', 'Push', 'Fax'])
    expect(draft.channels.every((c) => c.isEnabled && !c.quietHours && c.maxFrequencyPerHour === null)).toBe(true)
    expect(draft.channels.every((c) => c.timeZone === BROWSER && c.storedTimeZone === null)).toBe(true)
  })

  it('reads channel-wide rows case-insensitively and keeps the stored spelling for the upsert', () => {
    const draft = buildDraft([row({ channel: 'sms', isEnabled: false })], BROWSER)
    const sms = channel(draft, 'Sms')
    expect(sms.isEnabled).toBe(false)
    // Writing "Sms" would add a second row beside the stored "sms" one.
    expect(sms.wireChannel).toBe('sms')
  })

  it('★ keeps zone-less stored quiet hours in UTC on their own channel, without making UTC the shared zone', () => {
    const draft = buildDraft(
      [row({ quietHoursStart: '03:00:00', quietHoursEnd: '11:00:00', quietHoursTimeZone: null })],
      BROWSER,
    )
    expect(draft.timeZone).toBe(BROWSER)
    expect(channel(draft, 'Email')).toMatchObject({ timeZone: 'UTC', storedTimeZone: null, quietHoursStart: '03:00:00' })
  })

  it('takes the zone of stored zoned quiet hours as the shared zone', () => {
    const draft = buildDraft(
      [row({ quietHoursStart: '22:00:00', quietHoursEnd: '07:00:00', quietHoursTimeZone: 'Asia/Tokyo' })],
      BROWSER,
    )
    expect(draft.timeZone).toBe('Asia/Tokyo')
    expect(channel(draft, 'Email')).toMatchObject({ quietHours: true, timeZone: 'Asia/Tokyo' })
  })

  it('★ a channel stored in another zone keeps that zone instead of being shown in the shared one', () => {
    const draft = buildDraft(
      [
        row({ channel: 'Email', quietHoursStart: '22:00:00', quietHoursEnd: '07:00:00', quietHoursTimeZone: 'Asia/Tokyo' }),
        row({ channel: 'Sms', quietHoursStart: '21:00:00', quietHoursEnd: '06:00:00', quietHoursTimeZone: 'Europe/London' }),
      ],
      BROWSER,
    )
    expect(draft.timeZone).toBe('Asia/Tokyo')
    expect(channel(draft, 'Sms')).toMatchObject({ timeZone: 'Europe/London', quietHoursStart: '21:00:00' })
  })

  it('remembers a zone chosen before even when no channel has quiet hours now', () => {
    const draft = buildDraft([row({ quietHoursTimeZone: 'Europe/London' })], BROWSER)
    expect(draft.timeZone).toBe('Europe/London')
  })

  it('leaves category rows out of the channel switches and lists them as rules', () => {
    const rows = [row({ id: 'c1', category: 'Marketing', isEnabled: false })]
    expect(buildDraft(rows, BROWSER).channels[0]!.isEnabled).toBe(true)
    expect(categoryRules(rows).map((r) => r.id)).toEqual(['c1'])
  })
})

describe('pendingUpserts', () => {
  it('writes nothing for an untouched draft', () => {
    const initial = buildDraft([row({ isEnabled: false })], BROWSER)
    expect(pendingUpserts(initial, cloneDraft(initial))).toEqual([])
  })

  it('★ switching a channel off sends no zone when it has none to set (a browser zone the server lacks cannot block it)', () => {
    const initial = buildDraft([], 'America/Ciudad_Juarez')
    const current = cloneDraft(initial)
    channel(current, 'Sms').isEnabled = false

    expect(pendingUpserts(initial, current)).toEqual([
      { channel: 'Sms', isEnabled: false, quietHoursStart: undefined, quietHoursEnd: undefined, quietHoursTimeZone: undefined, maxFrequencyPerHour: undefined },
    ])
  })

  it('★ switching off a channel whose row carries a zone sends that stored zone back, not the shared one', () => {
    const rows = [row({ channel: 'Push', quietHoursTimeZone: 'Europe/London' })]
    const initial = changeSharedZone(buildDraft(rows, BROWSER), 'Asia/Tokyo', SUMMER)
    const current = cloneDraft(initial)
    channel(current, 'Push').isEnabled = false

    expect(pendingUpserts(initial, current)).toEqual([
      expect.objectContaining({ channel: 'Push', isEnabled: false, quietHoursTimeZone: 'Europe/London' }),
    ])
  })

  it('★ touching only the switch of a channel kept in its own zone writes its window back in that zone, unchanged', () => {
    const rows = [
      row({ channel: 'Email', quietHoursStart: '22:00:00', quietHoursEnd: '07:00:00', quietHoursTimeZone: 'Asia/Tokyo' }),
      row({ channel: 'Sms', quietHoursStart: '21:00:00', quietHoursEnd: '06:00:00', quietHoursTimeZone: 'Europe/London' }),
    ]
    const initial = buildDraft(rows, BROWSER)
    const current = cloneDraft(initial)
    channel(current, 'Sms').isEnabled = false

    expect(pendingUpserts(initial, current)).toEqual([
      expect.objectContaining({ channel: 'Sms', quietHoursStart: '21:00:00', quietHoursEnd: '06:00:00', quietHoursTimeZone: 'Europe/London' }),
    ])
  })

  it('★ a legacy zone-less row stays zone-less when only its cap changes', () => {
    const rows = [row({ quietHoursStart: '03:00:00', quietHoursEnd: '11:00:00', quietHoursTimeZone: null })]
    const initial = buildDraft(rows, BROWSER)
    const current = cloneDraft(initial)
    channel(current, 'Email').maxFrequencyPerHour = 5

    const [email] = pendingUpserts(initial, current)
    expect(email).toMatchObject({ quietHoursStart: '03:00:00', quietHoursEnd: '11:00:00', maxFrequencyPerHour: 5 })
    expect(email!.quietHoursTimeZone).toBeUndefined()
  })

  it('sends quiet-hours times only while quiet hours are on (turning them off clears them)', () => {
    const rows = [row({ quietHoursStart: '22:00:00', quietHoursEnd: '07:00:00', quietHoursTimeZone: BROWSER })]
    const initial = buildDraft(rows, BROWSER)
    const current = cloneDraft(initial)
    channel(current, 'Email').quietHours = false

    const [email] = pendingUpserts(initial, current)
    expect(email).toMatchObject({ channel: 'Email', quietHoursStart: undefined, quietHoursEnd: undefined, quietHoursTimeZone: BROWSER })
  })

  it('does not count edits to hidden times as a change while quiet hours are off', () => {
    const initial = buildDraft([], BROWSER)
    const current = cloneDraft(initial)
    channel(current, 'Email').quietHoursStart = '23:00:00'
    expect(pendingUpserts(initial, current)).toEqual([])
  })
})

describe('changeSharedZone (converting, not relabelling)', () => {
  it('★ moves a legacy UTC window to the new zone keeping today\'s instants', () => {
    const rows = [row({ quietHoursStart: '03:00:00', quietHoursEnd: '11:00:00', quietHoursTimeZone: null })]
    const initial = buildDraft(rows, BROWSER)

    const summer = changeSharedZone(initial, 'America/Toronto', SUMMER)
    expect(channel(summer, 'Email')).toMatchObject({ quietHoursStart: '23:00:00', quietHoursEnd: '07:00:00', timeZone: 'America/Toronto' })
    expect(pendingUpserts(initial, summer)).toEqual([
      expect.objectContaining({ channel: 'Email', quietHoursStart: '23:00:00', quietHoursEnd: '07:00:00', quietHoursTimeZone: 'America/Toronto' }),
    ])

    // Same window in January: Toronto is an hour further from UTC.
    expect(channel(changeSharedZone(initial, 'America/Toronto', WINTER), 'Email')).toMatchObject({
      quietHoursStart: '22:00:00',
      quietHoursEnd: '06:00:00',
    })
  })

  it('converts between two named zones too', () => {
    const rows = [row({ quietHoursStart: '22:00:00', quietHoursEnd: '07:00:00', quietHoursTimeZone: 'Asia/Tokyo' })]
    const moved = changeSharedZone(buildDraft(rows, BROWSER), 'America/Toronto', SUMMER)
    expect(channel(moved, 'Email')).toMatchObject({ quietHoursStart: '09:00:00', quietHoursEnd: '18:00:00' })
  })

  it('a channel without quiet hours just follows the zone and is not written', () => {
    const initial = buildDraft([row({ channel: 'Push' })], BROWSER)
    const moved = changeSharedZone(initial, 'Asia/Tokyo', SUMMER)
    expect(channel(moved, 'Push').timeZone).toBe('Asia/Tokyo')
    expect(pendingUpserts(initial, moved)).toEqual([])
  })

  it('leaves a window alone rather than relabel it when the zone cannot be converted', () => {
    const draft = buildDraft([row({ quietHoursStart: '22:00:00', quietHoursEnd: '07:00:00', quietHoursTimeZone: 'Asia/Tokyo' })], BROWSER)
    const email = channel(draft, 'Email')
    expect(moveChannelToZone(email, 'Mars/Olympus', SUMMER)).toBe(email)
  })
})

describe('findProblems', () => {
  it('flags quiet hours with a missing time and with start equal to end', () => {
    const draft = buildDraft([], BROWSER)
    draft.channels[0] = { ...draft.channels[0]!, quietHours: true, quietHoursStart: '22:00:00', quietHoursEnd: null }
    draft.channels[1] = { ...draft.channels[1]!, quietHours: true, quietHoursStart: '22:00:00', quietHoursEnd: '22:00:00' }

    expect(findProblems(draft.channels)).toEqual([
      { channel: 'Email', kind: 'incomplete' },
      { channel: 'Sms', kind: 'empty' },
    ])
  })

  it('ignores times on a channel whose quiet hours are off', () => {
    const draft = buildDraft([], BROWSER)
    draft.channels[0] = { ...draft.channels[0]!, quietHours: false, quietHoursStart: '22:00:00', quietHoursEnd: null }
    expect(findProblems(draft.channels)).toEqual([])
  })

  it('★ a stored empty window on a channel the person is not changing does not block saving another', () => {
    const rows = [row({ quietHoursStart: '22:00:00', quietHoursEnd: '22:00:00', quietHoursTimeZone: BROWSER })]
    const initial = buildDraft(rows, BROWSER)
    const current = cloneDraft(initial)
    channel(current, 'Fax').isEnabled = false

    expect(findProblems(changedChannels(initial, current))).toEqual([])
  })
})

describe('quietHourOptions', () => {
  it('offers every quarter hour, labelled HH:mm and valued HH:mm:ss', () => {
    const options = quietHourOptions()
    expect(options).toHaveLength(96)
    expect(options[0]).toEqual({ label: '00:00', value: '00:00:00' })
    expect(options.find((o) => o.value === '22:15:00')?.label).toBe('22:15')
  })

  it('keeps an off-grid stored value selectable instead of rendering blank', () => {
    const options = quietHourOptions(['22:10:00', '06:59:30', null])
    expect(options).toHaveLength(98)
    expect(options.find((o) => o.value === '22:10:00')?.label).toBe('22:10')
    expect(options.find((o) => o.value === '06:59:30')?.label).toBe('06:59:30')
    // Still in time order, so the stored value sits where it belongs.
    expect(options.map((o) => o.value)).toEqual([...options.map((o) => o.value)].sort())
  })
})

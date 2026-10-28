import { describe, expect, it } from 'vitest'
import { UsageLimitMode, type SaveUsagePolicyDto } from '@tnzi/core/services/ai'
import {
  barPercent,
  formatCountdown,
  formatUsd,
  formatWindowLength,
  policyProblem,
  samePolicy,
  toDraft,
  toPayload,
} from '../../../src/components/ai/usage-limit-format'

const t = (key: string, params?: Record<string, unknown>) =>
  `${key}${params ? `(${Object.entries(params).map(([k, v]) => `${k}=${v}`).join(',')})` : ''}`

const limited = (amountUsd: number | null) => ({ mode: UsageLimitMode.Limited, amountUsd })
const inherit = { mode: UsageLimitMode.Inherit, amountUsd: null }

function draft(overrides: Partial<SaveUsagePolicyDto> = {}): SaveUsagePolicyDto {
  return { sessionLimit: inherit, weeklyLimit: inherit, allowedModels: null, defaultModel: null, note: null, ...overrides }
}

describe('usage-limit-format', () => {
  it('formats USD, and a used amount under a cent is not "$0.00"', () => {
    expect(formatUsd(1234.5)).toBe('$1,234.50')
    expect(formatUsd(0)).toBe('$0.00')
    expect(formatUsd(0.004)).toBe('<$0.01')
    expect(formatUsd(null)).toBe('$0.00')
  })

  it('names window lengths in days, hours or minutes', () => {
    expect(formatWindowLength(300, t)).toBe('duration.hours(count=5)')
    expect(formatWindowLength(60, t)).toBe('duration.hour')
    expect(formatWindowLength(10080, t)).toBe('duration.days(count=7)')
    expect(formatWindowLength(90, t)).toBe('duration.minutes(count=90)')
  })

  it('counts down with minutes rounded up', () => {
    expect(formatCountdown(61_000, t)).toBe('countdown.m(m=2)')
    expect(formatCountdown(30_000, t)).toBe('countdown.lessThanMinute')
    expect(formatCountdown(2 * 3_600_000, t)).toBe('countdown.h(h=2)')
    expect(formatCountdown(2 * 3_600_000 + 13 * 60_000, t)).toBe('countdown.hm(h=2,m=13)')
  })

  it('clamps a bar to its track', () => {
    expect(barPercent(104)).toBe(100)
    expect(barPercent(-3)).toBe(0)
    expect(barPercent(undefined)).toBe(0)
  })

  it('an amount on an item that is not limited does not make a draft dirty', () => {
    expect(samePolicy(draft({ sessionLimit: { mode: UsageLimitMode.Unlimited, amountUsd: 5 } }), draft({ sessionLimit: { mode: UsageLimitMode.Unlimited, amountUsd: null } }))).toBe(true)
    expect(samePolicy(draft({ allowedModels: ['a', 'b'] }), draft({ allowedModels: ['b', 'a'] }))).toBe(true)
    expect(samePolicy(draft({ sessionLimit: limited(5) }), draft({ sessionLimit: limited(6) }))).toBe(false)
    expect(samePolicy(draft({ allowedModels: null }), draft({ allowedModels: [] }))).toBe(false)
  })

  it('finds what the server would refuse', () => {
    expect(policyProblem(draft({ sessionLimit: limited(null) }), null)).toBe('policy.amountRequired')
    expect(policyProblem(draft({ weeklyLimit: limited(-1) }), null)).toBe('policy.amountRequired')
    expect(policyProblem(draft({ allowedModels: [] }), null)).toBe('policy.modelsRequired')
    expect(policyProblem(draft({ defaultModel: 'x' }), ['a'])).toBe('policy.defaultNotAllowed')
    expect(policyProblem(draft({ defaultModel: 'x' }), [])).toBeNull()
    expect(policyProblem(draft({ sessionLimit: limited(0) }), null)).toBeNull()
  })

  it('the payload drops amounts on items that are not limited and trims the note', () => {
    expect(toPayload(draft({ weeklyLimit: { mode: UsageLimitMode.Unlimited, amountUsd: 9 }, note: '   ' }))).toEqual({
      sessionLimit: inherit,
      weeklyLimit: { mode: UsageLimitMode.Unlimited, amountUsd: null },
      allowedModels: null,
      defaultModel: null,
      note: null,
    })
  })

  it('a missing policy drafts as "follow" on every item', () => {
    expect(toDraft(null)).toEqual(draft())
  })
})

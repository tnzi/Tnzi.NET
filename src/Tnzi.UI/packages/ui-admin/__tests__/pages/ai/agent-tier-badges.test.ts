/**
 * Agent card tier badges read the backend's 1-5 scales the right way round.
 *
 * Quality is 1-5 with 5 the highest; latency and cost are 1-5 with 1 the
 * fastest / cheapest. The badges bucketed 1 / 2 / 3+ and coloured every scale
 * as "higher is better", so a default agent (3/3/3) read High on all three in
 * green, and the slowest, most expensive agent got green "High" latency and
 * cost badges while the fastest, cheapest one got yellow warnings.
 */
import { describe, it, expect } from 'vitest'
import { tierLabelKey, tierTagType } from '../../../src/pages/ai/agents/agent-config'

describe('agent tier badges', () => {
  it('buckets the 1-5 scale as 1-2 low, 3 medium, 4-5 high', () => {
    expect([1, 2, 3, 4, 5].map(tierLabelKey)).toEqual([
      'tier.low',
      'tier.low',
      'tier.medium',
      'tier.high',
      'tier.high',
    ])
  })

  it('colours a high quality tier as good', () => {
    expect(tierTagType(5, 'higherIsBetter')).toBe('success')
    expect(tierTagType(3, 'higherIsBetter')).toBe('info')
    expect(tierTagType(1, 'higherIsBetter')).toBe('warning')
  })

  it('colours a high latency or cost tier as a warning, a low one as good', () => {
    expect(tierTagType(5, 'lowerIsBetter')).toBe('warning')
    expect(tierTagType(3, 'lowerIsBetter')).toBe('info')
    expect(tierTagType(1, 'lowerIsBetter')).toBe('success')
  })
})

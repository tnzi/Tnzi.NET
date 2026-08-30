import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TAgingBar from '../../../src/components/finance/TAgingBar.vue'

const buckets = { current: 10, days1To30: 5, days31To60: 0, days61To90: 0, over90: 2, total: 17 }

describe('TAgingBar', () => {
  it('renders legend labels from the effective cut points', () => {
    // Finance:AgingBucketDays is configurable; the labels must follow the cuts
    // that produced the numbers, or the bar lies under a [7,14,21] deployment.
    const w = mount(TAgingBar, { props: { buckets: { ...buckets, agingBucketDays: [7, 14, 21] } } })
    const text = w.text()
    expect(text).toContain('1-7')
    expect(text).toContain('8-14')
    expect(text).toContain('15-21')
    expect(text).toContain('21+')
    expect(text).not.toContain('1-30')
  })

  it('keeps the historical labels when the cut points are absent', () => {
    const w = mount(TAgingBar, { props: { buckets } })
    expect(w.text()).toContain('1-30')
    expect(w.text()).toContain('90+')
  })
})

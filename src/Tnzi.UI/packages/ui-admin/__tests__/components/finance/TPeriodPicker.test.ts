import { describe, it, expect, vi, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import TPeriodPicker from '../../../src/components/finance/TPeriodPicker.vue'
import { useFinancePeriod } from '../../../src/headless/useFinancePeriod'

/**
 * The finance pages re-run their report on the picker's `change`. A relative
 * preset that moved on its own (a tab shown again on a later day) has to come
 * out the same way, or the numbers stay on a range the picker no longer shows.
 */
function setVisibility(state: 'visible' | 'hidden'): void {
  Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => state })
  document.dispatchEvent(new Event('visibilitychange'))
}

const stubs = { NSelect: true, NDatePicker: true, Select: true, DatePicker: true }

describe('TPeriodPicker', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it('★ reports a relative preset that rolled over to a new day as a change', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date(2026, 8, 30, 23, 0)) // Sep 30
    useFinancePeriod().setPreset('year-to-date')

    const wrapper = mount(TPeriodPicker, { global: { stubs } })
    // Mounting is not a change: nothing has run on a stale range yet.
    expect(wrapper.emitted('change')).toBeUndefined()

    vi.setSystemTime(new Date(2026, 9, 1, 8, 0)) // Oct 1, same tab
    setVisibility('visible')
    await nextTick()
    expect(wrapper.emitted('change')).toHaveLength(1)
    expect(useFinancePeriod().period.value).toEqual({ from: '2026-01-01', to: '2026-10-01' })

    // Shown again the same day: the range did not move, so the page re-runs nothing.
    setVisibility('visible')
    await nextTick()
    expect(wrapper.emitted('change')).toHaveLength(1)
    wrapper.unmount()
  })

  it('stays quiet for a custom range', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date(2026, 8, 30))
    useFinancePeriod().setRange({ from: '2026-09-01', to: '2026-09-15' })
    const wrapper = mount(TPeriodPicker, { global: { stubs } })

    vi.setSystemTime(new Date(2026, 9, 5))
    setVisibility('visible')
    await nextTick()
    expect(wrapper.emitted('change')).toBeUndefined()
    wrapper.unmount()
  })
})

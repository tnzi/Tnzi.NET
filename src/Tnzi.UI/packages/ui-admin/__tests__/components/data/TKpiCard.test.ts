import { EMPTY_DASH } from '../../../src/utils/placeholders'
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TKpiCard from '../../../src/components/data/TKpiCard.vue'

function mountCard(props: Record<string, unknown>, slots: Record<string, unknown> = {}) {
  return mount(TKpiCard, {
    props: { label: 'Label', value: 0, ...props },
    slots,
  })
}

describe('TKpiCard', () => {
  it('renders the label (12px muted, above) and a static numeric value when animated=false', () => {
    const w = mountCard({ label: 'Total Users', value: 42, animated: false })
    expect(w.find('.t-stat-card__label').text()).toBe('Total Users')
    expect(w.find('.t-stat-card__value').text()).toContain('42')
  })

  it('animates numeric values through NNumberAnimation by default', () => {
    const w = mountCard({ label: 'Total', value: 42 })
    expect(w.findComponent({ name: 'NumberAnimation' }).exists()).toBe(true)
  })

  it('does not use NNumberAnimation for string values (rendered verbatim)', () => {
    const w = mountCard({ label: 'Active', value: 'Yes' })
    expect(w.findComponent({ name: 'NumberAnimation' }).exists()).toBe(false)
    expect(w.find('.t-stat-card__value').text()).toContain('Yes')
  })

  it('renders an placeholder for null values and hides the suffix', () => {
    const w = mountCard({ label: 'Rate', value: null, suffix: '%' })
    expect(w.find('.t-stat-card__value').text()).toContain(EMPTY_DASH)
    expect(w.find('.t-stat-card__suffix').exists()).toBe(false)
  })

  it('renders the suffix next to a present value', () => {
    const w = mountCard({ label: 'Rate', value: 88, suffix: '%', animated: false })
    expect(w.find('.t-stat-card__suffix').text()).toBe('%')
  })

  it('preserves decimal places through NNumberAnimation (precision derived from the value)', () => {
    const w = mountCard({ label: 'Avg', value: 99.95 })
    const anim = w.findComponent({ name: 'NumberAnimation' })
    expect(anim.props('precision')).toBe(2)
    expect(anim.props('to')).toBe(99.95)
  })

  it('applies the tone class to the value', () => {
    const w = mountCard({ label: 'Errors', value: 3, tone: 'error', animated: false })
    expect(w.find('.t-stat-card__value').classes()).toContain('t-stat-card__value--error')
  })

  it('defaults to the default tone', () => {
    const w = mountCard({ label: 'Plain', value: 1, animated: false })
    expect(w.find('.t-stat-card__value').classes()).toContain('t-stat-card__value--default')
  })

  it('renders the icon block only when an icon is given', () => {
    const withIcon = mountCard({ label: 'Users', value: 1, icon: 'mdi:account', tone: 'success', animated: false })
    const block = withIcon.find('.t-stat-card__icon')
    expect(block.exists()).toBe(true)
    expect(block.classes()).toContain('t-stat-card__icon--success')
    expect(withIcon.findComponent({ name: 'TSvgIcon' }).props('icon')).toBe('mdi:account')

    const withoutIcon = mountCard({ label: 'Users', value: 1, animated: false })
    expect(withoutIcon.find('.t-stat-card__icon').exists()).toBe(false)
  })

  it('renders the #extra slot after the value (e.g. a status tag)', () => {
    const w = mountCard(
      { label: 'Locked', value: 2, animated: false },
      { extra: '<span class="extra-tag">Action needed</span>' },
    )
    expect(w.find('.t-stat-card__value .extra-tag').text()).toBe('Action needed')
  })
})

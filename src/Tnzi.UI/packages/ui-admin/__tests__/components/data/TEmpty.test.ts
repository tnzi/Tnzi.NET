import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TEmpty from '../../../src/components/data/TEmpty.vue'

describe('TEmpty', () => {
  it('renders the default icon and the "No data" fallback text', () => {
    const w = mount(TEmpty)
    expect(w.find('.t-empty__text').text()).toBe('No data')
    expect(w.findComponent({ name: 'TSvgIcon' }).props('icon')).toBe('mdi:inbox-outline')
  })

  it('renders custom (already-translated) text', () => {
    const w = mount(TEmpty, { props: { text: '暂无数据' } })
    expect(w.find('.t-empty__text').text()).toBe('暂无数据')
  })

  it('falls back to "No data" when text is an empty string', () => {
    const w = mount(TEmpty, { props: { text: '' } })
    expect(w.find('.t-empty__text').text()).toBe('No data')
  })

  it('accepts a custom icon', () => {
    const w = mount(TEmpty, { props: { icon: 'mdi:database-off-outline' } })
    expect(w.findComponent({ name: 'TSvgIcon' }).props('icon')).toBe('mdi:database-off-outline')
  })

  it('applies the size modifier class and scales the icon', () => {
    const small = mount(TEmpty, { props: { size: 'small' } })
    expect(small.find('.t-empty').classes()).toContain('t-empty--small')
    expect(small.findComponent({ name: 'TSvgIcon' }).props('size')).toBe(28)

    const large = mount(TEmpty, { props: { size: 'large' } })
    expect(large.find('.t-empty').classes()).toContain('t-empty--large')
    expect(large.findComponent({ name: 'TSvgIcon' }).props('size')).toBe(56)
  })

  it('defaults to medium', () => {
    const w = mount(TEmpty)
    expect(w.find('.t-empty').classes()).toContain('t-empty--medium')
    expect(w.findComponent({ name: 'TSvgIcon' }).props('size')).toBe(40)
  })

  it('renders trailing slot content (e.g. a call-to-action)', () => {
    const w = mount(TEmpty, { slots: { default: '<button class="cta">Create</button>' } })
    expect(w.find('.cta').exists()).toBe(true)
  })
})

import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TGrid from '../../../src/components/layout/TGrid.vue'

describe('TGrid', () => {
  it('renders default slot', () => {
    const wrapper = mount(TGrid, { slots: { default: '<div class="c">a</div>' } })
    expect(wrapper.find('.c').exists()).toBe(true)
  })

  it('applies display grid', () => {
    const wrapper = mount(TGrid)
    expect(wrapper.attributes('style')).toContain('display: grid')
  })

  it('uses 12 columns by default', () => {
    const wrapper = mount(TGrid)
    const style = wrapper.attributes('style') ?? ''
    expect(style).toContain('grid-template-columns: repeat(12, minmax(0, 1fr))')
  })

  it('accepts custom columns count', () => {
    const wrapper = mount(TGrid, { props: { cols: 4 } })
    expect(wrapper.attributes('style')).toContain('grid-template-columns: repeat(4, minmax(0, 1fr))')
  })

  it('applies gap prop', () => {
    const wrapper = mount(TGrid, { props: { gap: '24px' } })
    expect(wrapper.attributes('style')).toContain('gap: 24px')
  })

  it('applies default gap 16px when not provided', () => {
    const wrapper = mount(TGrid)
    expect(wrapper.attributes('style')).toContain('gap: 16px')
  })
})

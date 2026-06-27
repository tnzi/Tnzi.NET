import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { h } from 'vue'
import TMasterDetailLayout from '../../../src/components/layout/TMasterDetailLayout.vue'

describe('TMasterDetailLayout', () => {
  it('renders both master and detail slots', () => {
    const w = mount(TMasterDetailLayout, {
      slots: {
        master: () => h('div', { class: 'my-master' }, 'tree'),
        detail: () => h('div', { class: 'my-detail' }, 'panel'),
      },
    })
    expect(w.find('.t-master-detail__master .my-master').exists()).toBe(true)
    expect(w.find('.t-master-detail__detail .my-detail').exists()).toBe(true)
  })

  it('exposes width / gap / stacked-height as CSS vars (number → px)', () => {
    const w = mount(TMasterDetailLayout, {
      props: { masterWidth: 320, gap: 16, masterMobileMaxHeight: '50vh' },
    })
    const style = w.find('.t-master-detail').attributes('style') ?? ''
    expect(style).toContain('--t-md-master-w: 320px')
    expect(style).toContain('--t-md-gap: 16px')
    expect(style).toContain('--t-md-master-mh: 50vh')
  })

  it('passes a string masterWidth through verbatim', () => {
    const w = mount(TMasterDetailLayout, { props: { masterWidth: 'minmax(240px, 30%)' } })
    const style = w.find('.t-master-detail').attributes('style') ?? ''
    expect(style).toContain('--t-md-master-w: minmax(240px, 30%)')
  })

  it('drops the divider when bordered=false', () => {
    const bordered = mount(TMasterDetailLayout)
    expect(bordered.find('.t-master-detail').classes()).not.toContain('t-master-detail--plain')
    const plain = mount(TMasterDetailLayout, { props: { bordered: false } })
    expect(plain.find('.t-master-detail').classes()).toContain('t-master-detail--plain')
  })

  it('toggles the detail scroll class via detailScroll', () => {
    const on = mount(TMasterDetailLayout)
    expect(on.find('.t-master-detail__detail').classes()).toContain('t-master-detail__detail--scroll')
    const off = mount(TMasterDetailLayout, { props: { detailScroll: false } })
    expect(off.find('.t-master-detail__detail').classes()).not.toContain('t-master-detail__detail--scroll')
  })
})

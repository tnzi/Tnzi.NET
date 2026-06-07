import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TContentPage from '../../../src/components/layout/TContentPage.vue'

const stubs = {
  Popover: { name: 'Popover', template: '<div><slot name="trigger" /><slot /></div>' },
  SvgIcon: true,
}

describe('TContentPage', () => {
  it('renders the body slot', () => {
    const w = mount(TContentPage, { props: { title: 'X' }, slots: { default: '<div class="body">B</div>' }, global: { stubs } })
    expect(w.find('.body').text()).toBe('B')
  })

  it('renders a TPageHeader when a title is given', () => {
    const w = mount(TContentPage, { props: { title: 'My Page' }, global: { stubs } })
    expect(w.find('.t-page-header__title').text()).toBe('My Page')
  })

  it('hides the header when showHeader=false even with a title', () => {
    const w = mount(TContentPage, { props: { title: 'My Page', showHeader: false }, global: { stubs } })
    expect(w.find('.t-page-header').exists()).toBe(false)
  })

  it('renders header when only an #actions slot is provided (auto)', () => {
    const w = mount(TContentPage, { slots: { actions: '<button class="a" />' }, global: { stubs } })
    expect(w.find('.t-page-header').exists()).toBe(true)
    expect(w.find('.t-page-header__actions .a').exists()).toBe(true)
  })

  it('applies the scroll modifier class', () => {
    const w = mount(TContentPage, { props: { title: 'X', scroll: 'fill' }, global: { stubs } })
    expect(w.find('.t-content-page--scroll-fill').exists()).toBe(true)
  })
})

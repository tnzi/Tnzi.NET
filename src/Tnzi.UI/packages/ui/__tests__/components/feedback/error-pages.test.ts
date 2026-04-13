import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TErrorPage from '../../../src/components/feedback/TErrorPage.vue'
import T404 from '../../../src/components/feedback/T404.vue'
import T500 from '../../../src/components/feedback/T500.vue'
import T403 from '../../../src/components/feedback/T403.vue'

describe('TErrorPage', () => {
  it('renders code and title props', () => {
    const wrapper = mount(TErrorPage, {
      props: { code: '404', title: 'Not Found', description: 'Gone' },
    })
    expect(wrapper.text()).toContain('404')
    expect(wrapper.text()).toContain('Not Found')
    expect(wrapper.text()).toContain('Gone')
  })
  it('renders action slot', () => {
    const wrapper = mount(TErrorPage, {
      props: { code: '500', title: 'Error' },
      slots: { action: '<button class="a">Retry</button>' },
    })
    expect(wrapper.find('button.a').exists()).toBe(true)
  })
  it('renders illustration slot', () => {
    const wrapper = mount(TErrorPage, {
      props: { code: '404', title: 'X' },
      slots: { illustration: '<svg class="art" />' },
    })
    expect(wrapper.find('svg.art').exists()).toBe(true)
  })
})

describe('T404', () => {
  it('displays 404 code', () => { expect(mount(T404).text()).toContain('404') })
  it('uses default title "Page Not Found"', () => { expect(mount(T404).text()).toContain('Page Not Found') })
  it('accepts custom title override', () => {
    expect(mount(T404, { props: { title: '页面不存在' } }).text()).toContain('页面不存在')
  })
})

describe('T500', () => {
  it('displays 500 code', () => { expect(mount(T500).text()).toContain('500') })
  it('uses default title "Server Error"', () => { expect(mount(T500).text()).toContain('Server Error') })
})

describe('T403', () => {
  it('displays 403 code', () => { expect(mount(T403).text()).toContain('403') })
  it('uses default title "Forbidden"', () => { expect(mount(T403).text()).toContain('Forbidden') })
})

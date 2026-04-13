import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TAdminFooter from '../../../src/components/layout/TAdminFooter.vue'

describe('TAdminFooter', () => {
  it('renders copyright text', () => {
    const wrapper = mount(TAdminFooter, {
      props: { copyright: '© 2026 Tnzi' },
    })
    expect(wrapper.text()).toContain('© 2026 Tnzi')
  })

  it('renders links', () => {
    const wrapper = mount(TAdminFooter, {
      props: {
        links: [
          { label: 'Docs', href: 'https://tnzi.cc/docs' },
          { label: 'GitHub', href: 'https://github.com/tnzi/Tnzi.NET' },
        ],
      },
    })
    const anchors = wrapper.findAll('a')
    expect(anchors).toHaveLength(2)
    expect(anchors[0].text()).toBe('Docs')
    expect(anchors[0].attributes('href')).toBe('https://tnzi.cc/docs')
    expect(anchors[1].text()).toBe('GitHub')
  })

  it('renders default slot content', () => {
    const wrapper = mount(TAdminFooter, {
      slots: { default: '<span class="extra">extra</span>' },
    })
    expect(wrapper.find('.extra').exists()).toBe(true)
  })

  it('renders nothing visible when no copyright, links, or slot provided', () => {
    const wrapper = mount(TAdminFooter)
    expect(wrapper.find('.t-admin-footer').exists()).toBe(true)
    expect(wrapper.find('.t-admin-footer__copyright').exists()).toBe(false)
    expect(wrapper.find('.t-admin-footer__links').exists()).toBe(false)
  })

  it('opens external links in a new tab with noreferrer', () => {
    const wrapper = mount(TAdminFooter, {
      props: {
        links: [{ label: 'External', href: 'https://example.com', external: true }],
      },
    })
    const anchor = wrapper.find('a')
    expect(anchor.attributes('target')).toBe('_blank')
    expect(anchor.attributes('rel')).toBe('noopener noreferrer')
  })
})

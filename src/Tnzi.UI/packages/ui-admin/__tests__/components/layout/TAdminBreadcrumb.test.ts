import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TAdminBreadcrumb from '../../../src/components/layout/TAdminBreadcrumb.vue'

describe('TAdminBreadcrumb', () => {
  it('renders provided items in order', () => {
    const wrapper = mount(TAdminBreadcrumb, {
      props: {
        items: [
          { label: 'Home', to: '/' },
          { label: 'Identity', to: '/identity' },
          { label: 'Users', to: '/identity/users' },
        ],
      },
      global: {
        stubs: {
          Breadcrumb: { template: '<div class="nb"><slot /></div>' },
          BreadcrumbItem: { template: '<span class="nbi"><slot /></span>' },
        },
      },
    })
    const items = wrapper.findAll('.nbi')
    expect(items).toHaveLength(3)
    expect(items[0].text()).toBe('Home')
    expect(items[1].text()).toBe('Identity')
    expect(items[2].text()).toBe('Users')
  })

  it('emits click with item when an item is clicked', async () => {
    const wrapper = mount(TAdminBreadcrumb, {
      props: {
        items: [
          { label: 'Home', to: '/' },
          { label: 'Users', to: '/identity/users' },
        ],
      },
      global: {
        stubs: {
          Breadcrumb: { template: '<div><slot /></div>' },
          BreadcrumbItem: { template: '<span class="nbi" @click="$emit(\'click\')"><slot /></span>' },
        },
      },
    })
    await wrapper.findAll('.nbi')[1].trigger('click')
    expect(wrapper.emitted('itemClick')).toBeTruthy()
    expect(wrapper.emitted('itemClick')?.[0]).toEqual([
      { label: 'Users', to: '/identity/users' },
    ])
  })

  it('filters out items with `hidden: true`', () => {
    const wrapper = mount(TAdminBreadcrumb, {
      props: {
        items: [
          { label: 'Home', to: '/' },
          { label: 'Hidden', to: '/hidden', hidden: true },
          { label: 'Visible', to: '/visible' },
        ],
      },
      global: {
        stubs: {
          Breadcrumb: { template: '<div><slot /></div>' },
          BreadcrumbItem: { template: '<span class="nbi"><slot /></span>' },
        },
      },
    })
    const items = wrapper.findAll('.nbi')
    expect(items).toHaveLength(2)
    expect(items.map((i) => i.text())).toEqual(['Home', 'Visible'])
  })

  it('accepts an i18n resolver function for labels', () => {
    const wrapper = mount(TAdminBreadcrumb, {
      props: {
        items: [{ label: 'menu.home', to: '/' }],
        translate: (key: string) => ({ 'menu.home': 'Accueil' } as Record<string, string>)[key] ?? key,
      },
      global: {
        stubs: {
          Breadcrumb: { template: '<div><slot /></div>' },
          BreadcrumbItem: { template: '<span class="nbi"><slot /></span>' },
        },
      },
    })
    expect(wrapper.find('.nbi').text()).toBe('Accueil')
  })

  it('renders nothing when items are empty', () => {
    const wrapper = mount(TAdminBreadcrumb, {
      props: { items: [] },
      global: {
        stubs: {
          Breadcrumb: { template: '<div class="nb"><slot /></div>' },
          BreadcrumbItem: true,
        },
      },
    })
    expect(wrapper.find('.nb').exists()).toBe(false)
  })
})

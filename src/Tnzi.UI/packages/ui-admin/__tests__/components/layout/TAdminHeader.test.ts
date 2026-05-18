import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import TAdminHeader from '../../../src/components/layout/TAdminHeader.vue'
import { useAdminAppStore } from '../../../src/stores/useAdminAppStore'

vi.mock('@vueuse/core', async () => {
  const actual = await vi.importActual<typeof import('@vueuse/core')>('@vueuse/core')
  return {
    ...actual,
    useFullscreen: () => ({
      isFullscreen: { value: false },
      toggle: vi.fn(),
    }),
  }
})

describe('TAdminHeader', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('renders no logo block when no logo slot is supplied (Phase I.7.7+)', () => {
    // Phase I.7.7: the header no longer auto-renders `title` as a fallback
    // logo — the logo lives in the sidebar (`TAdminSidebar`) in vertical
    // layout modes, and consumers wanting the title in the header opt in
    // via the `#logo` slot.
    const wrapper = mount(TAdminHeader, { props: { title: 'Tnzi Admin' } })
    expect(wrapper.find('.t-admin-header__logo').exists()).toBe(false)
  })

  it('logo slot renders when consumer supplies it', () => {
    const wrapper = mount(TAdminHeader, {
      props: { title: 'X' },
      slots: { logo: '<div class="custom-logo">LOGO</div>' },
    })
    expect(wrapper.find('.custom-logo').exists()).toBe(true)
    expect(wrapper.find('.t-admin-header__logo').exists()).toBe(true)
    expect(wrapper.find('.t-admin-header__logo').text()).not.toContain('X')
  })

  it('menu toggler flips appStore.siderCollapse', async () => {
    const store = useAdminAppStore()
    expect(store.siderCollapse).toBe(false)
    const wrapper = mount(TAdminHeader, { props: { showToggler: true } })
    await wrapper.find('.t-admin-header__toggler').trigger('click')
    expect(store.siderCollapse).toBe(true)
  })

  it('search button emits openSearch when clicked', async () => {
    const wrapper = mount(TAdminHeader, { props: { showSearch: true } })
    await wrapper.find('.t-admin-header__search').trigger('click')
    expect(wrapper.emitted('openSearch')).toBeTruthy()
  })

  it('theme button emits openThemeDrawer when clicked', async () => {
    const wrapper = mount(TAdminHeader, { props: { showThemeBtn: true } })
    await wrapper.find('.t-admin-header__theme').trigger('click')
    expect(wrapper.emitted('openThemeDrawer')).toBeTruthy()
  })

  it('reload button triggers appStore.reloadPage', async () => {
    const store = useAdminAppStore()
    let called = false
    store.reloadPage = async () => {
      called = true
    }
    const wrapper = mount(TAdminHeader, { props: { showReload: true } })
    await wrapper.find('.t-admin-header__reload').trigger('click')
    expect(called).toBe(true)
  })

  it('locale switcher emits localeChange', async () => {
    const wrapper = mount(TAdminHeader, { props: { showLangSwitch: true } })
    // The component exposes setLocale fn via defineExpose for testability
    ;(wrapper.vm as unknown as { setLocale: (l: string) => void }).setLocale('zh-cn')
    expect(wrapper.emitted('localeChange')).toBeTruthy()
    expect(wrapper.emitted('localeChange')?.[0]).toEqual(['zh-cn'])
  })

  it('hides togglers by prop flags', () => {
    const wrapper = mount(TAdminHeader, {
      props: {
        showToggler: false,
        showSearch: false,
        showFullscreen: false,
        showThemeBtn: false,
        showLangSwitch: false,
        showReload: false,
      },
    })
    expect(wrapper.find('.t-admin-header__toggler').exists()).toBe(false)
    expect(wrapper.find('.t-admin-header__search').exists()).toBe(false)
    expect(wrapper.find('.t-admin-header__fullscreen').exists()).toBe(false)
    expect(wrapper.find('.t-admin-header__theme').exists()).toBe(false)
    expect(wrapper.find('.t-admin-header__lang').exists()).toBe(false)
    expect(wrapper.find('.t-admin-header__reload').exists()).toBe(false)
  })

  it('renders user and notification slots', () => {
    const wrapper = mount(TAdminHeader, {
      slots: {
        user: '<div class="user-slot">U</div>',
        notification: '<div class="notif-slot">N</div>',
      },
    })
    expect(wrapper.find('.user-slot').exists()).toBe(true)
    expect(wrapper.find('.notif-slot').exists()).toBe(true)
  })
})

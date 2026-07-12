import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { THEME_CONTEXT_KEY, createThemeContext, mergeThemeSettings } from '@tnzi/ui'
import ExceptionView from '../../../src/pages/exception/ExceptionView.vue'
import { useAdminAppStore } from '../../../src/stores/useAdminAppStore'

// ExceptionView reads the concrete error from `route.meta.exceptionType` and
// wires the CTAs to vue-router — mock both so we can drive the type + assert
// navigation without a full router.
const push = vi.fn(() => Promise.resolve())
const back = vi.fn()
let metaType: string | undefined = '403'

vi.mock('vue-router', () => ({
  useRoute: () => ({ meta: { exceptionType: metaType } }),
  useRouter: () => ({ push, back }),
}))

function themeProvide() {
  const ctx = createThemeContext(mergeThemeSettings({}))
  return { [THEME_CONTEXT_KEY as unknown as symbol]: ctx }
}

function mountView() {
  return mount(ExceptionView, { global: { provide: themeProvide() } })
}

describe('ExceptionView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    push.mockClear()
    back.mockClear()
    metaType = '403'
  })

  it('renders the 403 preset from meta.exceptionType with a localized subtitle', () => {
    metaType = '403'
    const wrapper = mountView()
    expect(wrapper.find('.t-exception-page__title').text()).toBe('403')
    expect(wrapper.find('.t-exception-page__subtitle').text()).toContain('permission')
  })

  it('renders 404 and 500 presets driven by meta', () => {
    metaType = '404'
    expect(mountView().find('.t-exception-page__title').text()).toBe('404')
    metaType = '500'
    expect(mountView().find('.t-exception-page__title').text()).toBe('500')
  })

  it('falls back to 404 when exceptionType is missing/unknown', () => {
    metaType = undefined
    expect(mountView().find('.t-exception-page__title').text()).toBe('404')
    metaType = 'nonsense'
    expect(mountView().find('.t-exception-page__title').text()).toBe('404')
  })

  it('renders a single "Back to home" CTA (no redundant "Go back")', () => {
    const wrapper = mountView()
    expect(wrapper.findAll('button')).toHaveLength(1)
  })

  it('primary CTA navigates to the dashboard', async () => {
    const wrapper = mountView()
    await wrapper.findAll('button')[0]!.trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'dashboard' })
  })

  it('resolves the Chinese subtitle when the app locale is zh-cn', () => {
    metaType = '404'
    useAdminAppStore().setLocale('zh-cn')
    const wrapper = mountView()
    expect(wrapper.find('.t-exception-page__subtitle').text()).toContain('不存在')
  })
})

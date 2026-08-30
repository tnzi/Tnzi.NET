import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { THEME_CONTEXT_KEY, createThemeContext, mergeThemeSettings } from '@tnzi/ui'
import ExceptionView from '../../../src/pages/exception/ExceptionView.vue'
import { useAdminAppStore } from '../../../src/stores/useAdminAppStore'
import {
  registerAdminLocales,
  resetAdminLocalesForTest,
} from '../../../src/i18n/locale-registry'

// ExceptionView reads the concrete error from `route.meta.exceptionType` and
// wires the CTAs to vue-router - mock both so we can drive the type + assert
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

/**
 * These pages used to resolve through a private `tr()` that read the bundled
 * dictionary and never `messageOverrides`. A permissioned console sends people
 * to 403 routinely - a coordinator following a stale link to a page their role
 * cannot open landed on an English page in an otherwise translated shell.
 */
describe('ExceptionView honours consumer overrides', () => {
  // A separate top-level describe does NOT inherit the pinia reset above, so
  // without this the French store leaks forward and the last two specs read a
  // dictionary the test before them registered.
  beforeEach(() => {
    setActivePinia(createPinia())
    push.mockClear()
    metaType = '404'
  })
  afterEach(() => {
    resetAdminLocalesForTest()
  })

  function useFrench(overrides: Record<string, unknown>): void {
    registerAdminLocales([
      { code: 'en', label: 'English' },
      { code: 'fr', label: 'Français' },
    ])
    const store = useAdminAppStore()
    store.extendLocaleMessages({ fr: overrides })
    store.setLocale('fr')
  }

  it('renders the override for 403 title, subtitle and CTA', () => {
    metaType = '403'
    useFrench({
      admin: {
        exception: {
          actions: { home: "Retour à l'accueil" },
          '403': { title: '403', subtitle: "Vous n'avez pas accès à cette page." },
        },
      },
    })

    const wrapper = mountView()
    expect(wrapper.find('.t-exception-page__subtitle').text()).toBe(
      "Vous n'avez pas accès à cette page.",
    )
    expect(wrapper.findAll('button')[0]!.text()).toBe("Retour à l'accueil")
  })

  it('renders the override for 404 and 500 too', () => {
    const dict = {
      admin: {
        exception: {
          '404': { title: '404', subtitle: 'Page introuvable.' },
          '500': { title: '500', subtitle: 'Erreur du serveur.' },
        },
      },
    }
    metaType = '404'
    useFrench(dict)
    expect(mountView().find('.t-exception-page__subtitle').text()).toBe('Page introuvable.')

    metaType = '500'
    expect(mountView().find('.t-exception-page__subtitle').text()).toBe('Erreur du serveur.')
  })

  it('overrides win over a bundled string the dictionary already has', () => {
    // The subtle half of the defect: keys MISSING from the bundle happened to
    // work (the walk fell through to the fallback), so only an override of a
    // key the bundle already carries proves the override is consulted first.
    metaType = '404'
    const store = useAdminAppStore()
    store.extendLocaleMessages({
      en: { admin: { exception: { '404': { subtitle: 'Nothing here.' } } } },
    })

    expect(mountView().find('.t-exception-page__subtitle').text()).toBe('Nothing here.')
  })

  it('is unchanged for a consumer with no override', () => {
    metaType = '404'
    // Bundled English, exactly as before: NOT TExceptionPage's own preset,
    // which is what a missed lookup would leave on screen.
    expect(mountView().find('.t-exception-page__subtitle').text()).toContain(
      'the page you visited',
    )
  })
})

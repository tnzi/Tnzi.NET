/**
 * The "a consumer registers a third locale" path.
 *
 * Every assertion here corresponds to a place that used to enforce exactly two
 * languages, and four of the five failed SILENTLY - the dictionary vanished,
 * or the wrong dictionary was installed, or the language simply was not
 * offered, with no error and no warning. Silent is why they survived: an app
 * with a complete French dictionary rendered English chrome and read like a
 * translation gap rather than a bug.
 */
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { mount, flushPromises } from '@vue/test-utils'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { defineComponent, h } from 'vue'
import { enUS, frFR, dateFrFR } from 'naive-ui'
import {
  registerAdminLocales,
  registerAdminLocale,
  getRegisteredAdminLocales,
  isAdminLocaleRegistered,
  getDefaultAdminLocale,
  resetAdminLocalesForTest,
} from '../../src/i18n/locale-registry'
import { getLocaleMessages, loadLocaleMessages, setLocaleMessages } from '../../src/i18n/messages'
import { translatePageKey, translateChromeKey } from '../../src/i18n/translate'
import { useAdminAppStore } from '../../src/stores/useAdminAppStore'
import { useAdminLocale } from '../../src/headless/useAdminLocale'
import TAdminAutoBreadcrumb from '../../src/components/layout/TAdminAutoBreadcrumb.vue'
import TAdminHeader from '../../src/components/layout/TAdminHeader.vue'
import TDesktopTray from '../../src/components/desktop/TDesktopTray.vue'
import THeaderBanner from '../../src/components/dashboard/THeaderBanner.vue'

/** Stand-in for a consumer's own dictionary - shape only, no real translation. */
const FR: Record<string, unknown> = {
  admin: {
    common: { save: 'Enregistrer' },
    modules: { dashboard: { title: 'Tableau de bord' } },
  },
}

beforeEach(() => {
  setActivePinia(createPinia())
})

afterEach(() => {
  // The registry is module-level state and outlives a mount, so a spec that
  // registers `fr` would otherwise leak it into every spec after it.
  resetAdminLocalesForTest()
})

describe('locale registry', () => {
  it('offers the two bundled locales when a consumer registers nothing', () => {
    expect(getRegisteredAdminLocales().map((l) => l.code)).toEqual(['en', 'zh-cn'])
    expect(getDefaultAdminLocale()).toBe('en')
  })

  it('replaces the bundled set rather than merging with it', () => {
    // The whole point: an English/French product must not show a 中文 entry.
    registerAdminLocales([
      { code: 'en', label: 'English' },
      { code: 'fr', label: 'Français' },
    ])

    expect(getRegisteredAdminLocales().map((l) => l.code)).toEqual(['en', 'fr'])
    expect(isAdminLocaleRegistered('zh-cn')).toBe(false)
  })

  it('registerAdminLocale adds one without dropping what is already there', () => {
    registerAdminLocale({ code: 'fr', label: 'Français' })
    expect(getRegisteredAdminLocales().map((l) => l.code)).toEqual(['en', 'zh-cn', 'fr'])
  })

  it('installs an eagerly-supplied dictionary at registration', () => {
    registerAdminLocales([{ code: 'fr', label: 'Français', messages: FR }])
    expect(getLocaleMessages('fr')).toBe(FR)
  })

  it('defaults the Intl tag to the code, keeping the bundled two on their region tags', () => {
    registerAdminLocales([
      { code: 'en', label: 'English' },
      { code: 'zh-cn', label: '中文' },
      { code: 'fr', label: 'Français' },
      { code: 'pt', label: 'Português', intlTag: 'pt-BR' },
    ])
    const tags = Object.fromEntries(getRegisteredAdminLocales().map((l) => [l.code, l.intlTag]))
    expect(tags).toEqual({ en: 'en-US', 'zh-cn': 'zh-CN', fr: 'fr', pt: 'pt-BR' })
  })
})

describe('message registry', () => {
  it('does NOT install the English pack under an unknown locale code', async () => {
    // The old loader was a ternary: anything that was not 'zh-cn' imported
    // `en`. So `loadLocaleMessages('fr')` filed the ENGLISH dictionary under
    // 'fr' and a French app rendered English chrome.
    await loadLocaleMessages('fr')
    expect(getLocaleMessages('fr')).toBeUndefined()
  })

  it('uses a registered lazy loader for a locale the framework does not bundle', async () => {
    registerAdminLocales([
      { code: 'en', label: 'English' },
      { code: 'fr', label: 'Français', loadMessages: async () => FR },
    ])
    await loadLocaleMessages('fr')
    expect(getLocaleMessages('fr')).toBe(FR)
  })
})

describe('extendLocaleMessages', () => {
  it('merges a dictionary keyed by a locale the framework does not ship', () => {
    // It used to check for `en` and `'zh-cn'` BY NAME and merge only those, so
    // `{ fr }` was dropped on the floor - while `createAdminApp({ locales })`
    // is the documented way to supply consumer messages.
    const store = useAdminAppStore()
    store.extendLocaleMessages({ fr: FR })

    expect(store.messageOverrides.fr).toEqual(FR)
  })

  it('accumulates across calls for an unbundled locale', () => {
    const store = useAdminAppStore()
    store.extendLocaleMessages({ fr: { admin: { common: { save: 'Enregistrer' } } } })
    store.extendLocaleMessages({ fr: { admin: { common: { cancel: 'Annuler' } } } })

    expect(store.messageOverrides.fr).toEqual({
      admin: { common: { save: 'Enregistrer', cancel: 'Annuler' } },
    })
  })

  it('still merges the two bundled locales', () => {
    const store = useAdminAppStore()
    store.extendLocaleMessages({ en: { a: 1 }, 'zh-cn': { b: 2 } })
    expect(store.messageOverrides.en).toEqual({ a: 1 })
    expect(store.messageOverrides['zh-cn']).toEqual({ b: 2 })
  })
})

describe('selecting an unbundled locale', () => {
  it('resolves keys from the consumer dictionary with no cast at the call site', () => {
    const store = useAdminAppStore()
    store.extendLocaleMessages({ fr: FR })
    store.setLocale('fr')

    expect(translatePageKey('', 'admin.common.save')).toBe('Enregistrer')
  })

  it('drops a persisted locale the application no longer offers', () => {
    const store = useAdminAppStore()
    store.setLocale('zh-cn')
    registerAdminLocales([
      { code: 'en', label: 'English' },
      { code: 'fr', label: 'Français' },
    ])

    store.ensureLocaleRegistered()

    // Not stranded in a language with no dictionary and no switcher entry.
    expect(store.locale).toBe('en')
  })

  it('keeps a persisted locale that is still offered', () => {
    registerAdminLocales([
      { code: 'en', label: 'English' },
      { code: 'fr', label: 'Français' },
    ])
    const store = useAdminAppStore()
    store.setLocale('fr')
    store.ensureLocaleRegistered()
    expect(store.locale).toBe('fr')
  })
})

describe('useAdminLocale', () => {
  it('lists exactly the registered locales, marking the active one', () => {
    registerAdminLocales([
      { code: 'en', label: 'English' },
      { code: 'fr', label: 'Français' },
    ])
    const store = useAdminAppStore()
    store.setLocale('fr')

    const { options } = useAdminLocale()
    expect(options.value).toEqual([
      { code: 'en', label: 'English', active: false },
      { code: 'fr', label: 'Français', active: true },
    ])
  })

  it('reports no choice when the app registers a single language', () => {
    registerAdminLocales([{ code: 'en', label: 'English' }])
    expect(useAdminLocale().hasChoice.value).toBe(false)
  })

  it('ignores a code the application does not offer', () => {
    registerAdminLocales([{ code: 'en', label: 'English' }])
    const locales = useAdminLocale()
    locales.setLocale('zh-cn')
    expect(locales.current.value).toBe('en')
  })

  it("uses the registration's naive bundle so naive's own strings follow", () => {
    registerAdminLocales([
      { code: 'fr', label: 'Français', naive: { locale: frFR, dateLocale: dateFrFR } },
    ])
    useAdminAppStore().setLocale('fr')

    expect(useAdminLocale().naive.value.locale).toBe(frFR)
    expect(useAdminLocale().naive.value.dateLocale).toBe(dateFrFR)
  })

  it('falls back to English when a locale registers no naive bundle', () => {
    registerAdminLocales([{ code: 'fr', label: 'Français' }])
    useAdminAppStore().setLocale('fr')
    expect(useAdminLocale().naive.value.locale).toBe(enUS)
  })

  it('resolves the bundled zh-cn naive bundle without any registration', () => {
    useAdminAppStore().setLocale('zh-cn')
    // Naive shipped this all along; the shell simply never bound it, so
    // pagination and the date picker stayed English even in Chinese.
    expect(useAdminLocale().naive.value.locale).not.toBe(enUS)
  })
})

/**
 * The switchers themselves, not just the composable behind them.
 *
 * Asserting `useAdminLocale().options` alone leaves the actual gap open: either
 * component could go back to a hard-coded two-item array and every other test
 * here would stay green. Verified by mutation - putting the literal list back
 * in the header turns the first of these red.
 *
 * The menu teleports, so these read the options the component hands NDropdown
 * (registered under naive's own name, `Dropdown`) rather than the open menu.
 */
describe('the rendered language switchers', () => {
  function menuOf(wrapper: ReturnType<typeof mount>): Array<{ key: string; label: string }> {
    const dropdown = wrapper.findComponent({ name: 'Dropdown' })
    const options = (dropdown.props('options') ?? []) as Array<{ key: string; label: string }>
    return options.map((o) => ({ key: o.key, label: o.label }))
  }

  it('header lists exactly the registered locales', () => {
    registerAdminLocales([
      { code: 'en', label: 'English' },
      { code: 'fr', label: 'Français' },
    ])

    const wrapper = mount(TAdminHeader, { props: { showLangSwitch: true } })

    expect(menuOf(wrapper)).toEqual([
      { key: 'en', label: 'English' },
      { key: 'fr', label: 'Français' },
    ])
  })

  it('desktop tray lists exactly the registered locales', () => {
    registerAdminLocales([
      { code: 'en', label: 'English' },
      { code: 'fr', label: 'Français' },
    ])

    const wrapper = mount(TDesktopTray, {
      global: {
        stubs: {
          NPopover: { template: '<div><slot name="trigger" /><slot /></div>' },
          NTooltip: { template: '<div><slot name="trigger" /><slot /></div>' },
          NDatePicker: true,
        },
      },
    })

    expect(menuOf(wrapper).map((o) => o.key)).toEqual(['en', 'fr'])
  })

  it('header hides the switcher when only one language is offered', () => {
    registerAdminLocales([{ code: 'en', label: 'English' }])

    const wrapper = mount(TAdminHeader, { props: { showLangSwitch: true } })

    expect(wrapper.find('.t-admin-header__lang').exists()).toBe(false)
  })

  it('header still shows both bundled languages when nothing is registered', () => {
    const wrapper = mount(TAdminHeader, { props: { showLangSwitch: true } })

    expect(menuOf(wrapper).map((o) => o.key)).toEqual(['en', 'zh-cn'])
  })
})

/**
 * The dashboard greeting banner's clock.
 *
 * It took a `locale` prop defaulting to the literal `'en'` and the widget
 * wrapper passed nothing, so the most prominent line on the workbench home
 * screen kept an English date on an otherwise fully translated console - while
 * the desktop taskbar clock, reading the same tag, was right. Two clocks in
 * one shell, one correct.
 *
 * Asserted against real `Intl` output (no mock): mocking the formatter here
 * would test that a string was threaded through, not that the date is French.
 */
describe('dashboard banner clock follows the active locale', () => {
  // The banner seeds its ticker from `new Date()`, so the clock has to be
  // pinned or these assertions would be about when the suite happened to run.
  // Fake timers rather than writing `wrapper.vm.now`: `<script setup>` bindings
  // are not writable through the vm, and that silent no-op is exactly what made
  // the first version of these tests read the wall clock instead.
  beforeEach(() => {
    vi.useFakeTimers({ now: new Date('2026-08-23T22:30:00') })
  })
  afterEach(() => {
    vi.useRealTimers()
  })

  function bannerTime(props: Record<string, unknown> = {}): string {
    return mount(THeaderBanner, { props }).find('.t-header-banner__time').text()
  }

  it('renders French day and month names, and 24-hour time', () => {
    registerAdminLocales([
      { code: 'en', label: 'English' },
      { code: 'fr', label: 'Français' },
    ])
    useAdminAppStore().setLocale('fr')

    const text = bannerTime()
    expect(text).toContain('dimanche')
    expect(text).toContain('août')
    // The hour cycle comes from the locale - the component's `hour`/`minute`
    // options do not have to change for French to get 22:30 instead of 10:30 PM.
    expect(text).toContain('22:30')
    expect(text).not.toContain('PM')
  })

  it('honours a consumer intlTag that differs from the code', () => {
    // fr-CA writes the time as "22 h 30"; a consumer registering that tag has
    // already told the framework the answer.
    registerAdminLocales([{ code: 'fr', label: 'Français', intlTag: 'fr-CA' }])
    useAdminAppStore().setLocale('fr')

    expect(bannerTime()).toContain('22 h 30')
  })

  it('lets an explicit locale prop win over the active locale', () => {
    registerAdminLocales([
      { code: 'en', label: 'English' },
      { code: 'fr', label: 'Français' },
    ])
    useAdminAppStore().setLocale('fr')

    const text = bannerTime({ locale: 'en-US' })
    expect(text).toContain('Sunday')
    expect(text).toContain('10:30 PM')
  })

  it('is unchanged for a consumer on en', () => {
    expect(bannerTime()).toBe('Sunday, Aug 23, 2026, 10:30 PM')
  })
})

/**
 * The reported regression, verbatim: with an override registered for a key the
 * BUNDLED dictionary already has, the sidebar menu label localised and the
 * header breadcrumb stayed English, because the breadcrumb resolved through a
 * second, override-blind copy of the lookup.
 */
describe('consumer overrides reach every chrome path', () => {
  const Blank = defineComponent({ render: () => h('div') })

  function makeRouter(): Router {
    return createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: '/admin',
          name: 'admin-root',
          component: Blank,
          children: [
            {
              path: 'dashboard',
              name: 'dashboard',
              component: Blank,
              meta: { title: 'tnzi.admin.modules.dashboard.title' },
            },
          ],
        },
      ],
    })
  }

  it('translateChromeKey prefers the override over the bundled string', () => {
    const store = useAdminAppStore()
    store.extendLocaleMessages({ en: { admin: { modules: { dashboard: { title: 'Overview' } } } } })

    expect(translateChromeKey('admin.modules.dashboard.title')).toBe('Overview')
    // Same answer as the path that already honoured overrides.
    expect(translatePageKey('', 'admin.modules.dashboard.title')).toBe('Overview')
  })

  it('the built-in dashboard breadcrumb shows the override, not the bundled English', async () => {
    setLocaleMessages('en', { admin: { modules: { dashboard: { title: 'Dashboard' } } } })
    const store = useAdminAppStore()
    store.extendLocaleMessages({ en: { admin: { modules: { dashboard: { title: 'Overview' } } } } })

    const router = makeRouter()
    await router.push('/admin/dashboard')
    await router.isReady()

    const wrapper = mount(TAdminAutoBreadcrumb, {
      // Exactly what AdminShellRoot passes down as `defaultTranslate`.
      props: { showIcon: false, translate: (k: string) => translateChromeKey(k) },
      global: {
        plugins: [router],
        stubs: {
          Breadcrumb: { template: '<div class="nb"><slot /></div>' },
          BreadcrumbItem: { template: '<span class="nbi"><slot /></span>' },
          TSvgIcon: true,
        },
      },
    })
    await flushPromises()

    expect(wrapper.findAll('.nbi').map((i) => i.text())).toEqual(['Overview'])
  })
})

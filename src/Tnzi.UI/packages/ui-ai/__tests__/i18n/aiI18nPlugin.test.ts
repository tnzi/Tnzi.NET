/**
 * `createAiI18nPlugin`: the catalogue installed on an app (`app.use`), with no root component of
 * the app's own, and switched at runtime in every app it is installed in.
 */
import { describe, expect, it } from 'vitest'
import { createApp, defineComponent, defineCustomElement, h, nextTick } from 'vue'
import { createAiI18nPlugin, matchAiLocale, useAiI18n, useAiLocale } from '../../src/i18n/index'
import { useControlsText } from '../../src/components/controls/controls-text'
import { en } from '../../src/locales/en'
import { zhCn } from '../../src/locales/zh-cn'

const Probe = defineComponent({
  setup() {
    const t = useAiI18n()
    const controls = useControlsText()
    const locale = useAiLocale()
    return () => h('p', { 'data-locale': locale.value ?? '' }, `${t.value.common.cancel}|${controls.value.retry}`)
  },
})

function mount(plugin: ReturnType<typeof createAiI18nPlugin>): HTMLElement {
  const el = document.createElement('div')
  document.body.appendChild(el)
  createApp(Probe).use(plugin).mount(el)
  return el
}

describe('matchAiLocale', () => {
  const keys = ['en', 'zh-CN']
  it('matches the tag, then shorter tags, then the same language', () => {
    expect(matchAiLocale('zh-CN', keys)).toBe('zh-CN')
    expect(matchAiLocale('zh_cn', keys)).toBe('zh-CN')
    expect(matchAiLocale('en-GB', keys)).toBe('en')
    expect(matchAiLocale('zh-Hant-TW', keys)).toBe('zh-CN')
    expect(matchAiLocale('fr-FR', keys)).toBeNull()
    expect(matchAiLocale('', keys)).toBeNull()
  })
})

describe('createAiI18nPlugin', () => {
  it('provides the catalogue of the starting language to the app and to the controls', () => {
    const el = mount(createAiI18nPlugin({ messages: { en, 'zh-CN': zhCn }, locale: 'zh-CN' }))
    expect(el.textContent).toBe(`${zhCn.common.cancel}|${zhCn.common.retry}`)
    expect(el.querySelector('p')?.dataset.locale).toBe('zh-CN')
  })

  it('switches every app it is installed in at runtime', async () => {
    const plugin = createAiI18nPlugin({ messages: { en, 'zh-CN': zhCn }, locale: 'en-US' })
    const first = mount(plugin)
    const second = mount(plugin)
    expect(first.textContent).toBe(`${en.common.cancel}|${en.common.retry}`)

    plugin.setLocale('zh-Hans-CN')
    await nextTick()
    for (const el of [first, second]) {
      expect(el.textContent).toBe(`${zhCn.common.cancel}|${zhCn.common.retry}`)
      expect(el.querySelector('p')?.dataset.locale).toBe('zh-Hans-CN')
    }
    expect(plugin.locale.value).toBe('zh-Hans-CN')
  })

  it('falls back to the fallback catalogue for a language it has none of, and takes packs added later', async () => {
    const plugin = createAiI18nPlugin({ messages: { en }, locale: 'zh-CN' })
    const el = mount(plugin)
    expect(el.textContent).toBe(`${en.common.cancel}|${en.common.retry}`)
    plugin.setMessages('zh-CN', zhCn)
    await nextTick()
    expect(el.textContent).toBe(`${zhCn.common.cancel}|${zhCn.common.retry}`)
  })

  it('ignores an empty language', () => {
    const plugin = createAiI18nPlugin({ locale: 'en' })
    plugin.setLocale('  ')
    expect(plugin.locale.value).toBe('en')
  })

  it('reaches a custom element through configureApp', async () => {
    const plugin = createAiI18nPlugin({ messages: { en, 'zh-CN': zhCn }, locale: 'en' })
    const Element = defineCustomElement(Probe, { shadowRoot: false, configureApp: (app) => app.use(plugin) })
    customElements.define('tz-i18n-probe', Element)
    const el = document.createElement('tz-i18n-probe')
    document.body.appendChild(el)
    await nextTick()
    expect(el.textContent).toBe(`${en.common.cancel}|${en.common.retry}`)
    plugin.setLocale('zh-CN')
    await nextTick()
    expect(el.textContent).toBe(`${zhCn.common.cancel}|${zhCn.common.retry}`)
  })
})

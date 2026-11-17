/**
 * TLandingPage layout: only the hero (plan, headline, subline, composer) is centred; the chips and
 * footer live in a band below it that shares the free height equally with an empty band above.
 * That split is what keeps the composer in place when the chips change. happy-dom does no layout,
 * so these pin the structure and the flex rules the guarantee rests on; the positions themselves
 * were measured in Chromium (see the package docs).
 */
import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { afterEach, describe, expect, it } from 'vitest'
import { createApp, h, nextTick, ref, type App } from 'vue'

const { default: TLandingPage } = await import('../../src/components/chat/TLandingPage.vue')

let app: App | null = null
let host: HTMLElement | null = null

function mount(render: () => unknown): HTMLElement {
  host = document.createElement('div')
  document.body.appendChild(host)
  app = createApp({ render: render as never })
  app.mount(host)
  return host
}

afterEach(() => {
  app?.unmount()
  host?.remove()
  app = null
  host = null
})

const childClasses = (el: Element) => Array.from(el.children).map((c) => c.className.split(' ')[0])

describe('TLandingPage layout', () => {
  it('puts the hero between an empty band above and the chips + footer below', () => {
    const el = mount(() =>
      h(
        TLandingPage,
        { chips: [{ label: 'One' }, { label: 'Two' }] },
        { plan: () => h('span', 'Pro'), subline: () => 'Sub', footer: () => h('p', { class: 'foot' }, 'f') },
      ),
    )
    const root = el.querySelector('.t-landing')!
    expect(childClasses(root)).toEqual(['t-landing__above', 't-landing__hero', 't-landing__below'])

    const hero = root.querySelector('.t-landing__hero')!
    expect(childClasses(hero)).toEqual(['t-landing__plan', 't-landing__headline', 't-landing__subline', 't-thread-composer-wrap'])

    const below = root.querySelector('.t-landing__below')!
    expect(childClasses(below)).toEqual(['t-landing__chips', 't-landing__footer'])
    expect(root.querySelector('.t-landing__above')!.getAttribute('aria-hidden')).toBe('true')
  })

  it('keeps both bands, empty, when there are no chips and no footer', () => {
    const el = mount(() => h(TLandingPage, {}))
    const root = el.querySelector('.t-landing')!
    expect(childClasses(root)).toEqual(['t-landing__above', 't-landing__hero', 't-landing__below'])
    expect(root.querySelector('.t-landing__below')!.children).toHaveLength(0)
  })

  it('changes only the band below when chips arrive late', async () => {
    const chips = ref<{ label: string }[]>([])
    const el = mount(() => h(TLandingPage, { chips: chips.value }))
    const hero = el.querySelector('.t-landing__hero')!
    const composer = el.querySelector('.t-thread-composer-wrap')!

    chips.value = [{ label: 'A' }, { label: 'B' }, { label: 'C' }]
    await nextTick()

    expect(el.querySelector('.t-landing__hero')).toBe(hero)
    expect(el.querySelector('.t-thread-composer-wrap')).toBe(composer)
    expect(el.querySelectorAll('.t-landing__below .t-landing__suggestion')).toHaveLength(3)
  })

  it('keeps the same structure in the compact (phone) layout', () => {
    const el = mount(() => h(TLandingPage, { compact: true, chips: [{ label: 'One' }] }))
    const root = el.querySelector('.t-landing')!
    expect(root.classList.contains('t-landing--compact')).toBe(true)
    expect(childClasses(root)).toEqual(['t-landing__above', 't-landing__hero', 't-landing__below'])
  })

  /* The guarantee is the CSS: equal growth from a zero basis for the two bands, a band below that
     may be smaller than its content, a hero that neither grows nor shrinks, and no centring of the scroller itself (which would clip the top
     of an overflowing page). Pinned on the source because happy-dom computes no layout. */
  it('splits the free height evenly and never centres the scroller', () => {
    const here = dirname(fileURLToPath(import.meta.url))
    const source = readFileSync(resolve(here, '../../src/components/chat/TLandingPage.vue'), 'utf8').replace(/\r\n/g, '\n')
    const css = source.slice(source.indexOf('<style scoped>'))
    const rule = (selector: string) => {
      const match = css.match(new RegExp(`\\n${selector.replace('.', '\\.')} \\{([^}]*)\\}`))
      return match?.[1] ?? ''
    }
    expect(rule('.t-landing__above')).toMatch(/flex: 1 0 0;/)
    expect(rule('.t-landing__below')).toMatch(/flex: 1 0 0;/)
    // Chips that need more than their half run on below the window instead of lifting the hero
    expect(rule('.t-landing__below')).toMatch(/min-height: 0;/)
    expect(rule('.t-landing__hero')).toMatch(/flex: none;/)
    expect(rule('.t-landing')).toMatch(/overflow-y: auto;/)
    expect(rule('.t-landing')).not.toMatch(/justify-content/)
  })
})

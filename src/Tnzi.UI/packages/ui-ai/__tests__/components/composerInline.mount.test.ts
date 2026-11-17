/**
 * `TThreadComposer` `inline`: one row (left cluster, text, right cluster) in a
 * pill, for a composer in a card. The class is mounted for real; the row itself
 * is pinned on the declared styles (happy-dom does no layout).
 */
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { afterEach, describe, expect, it } from 'vitest'
import { createApp, h, nextTick, type App } from 'vue'
import { cssRule } from '../support/css-rule'

const { default: TThreadComposer } = await import('../../src/components/chat/TThreadComposer.vue')

let app: App | null = null
let host: HTMLElement | null = null

function mount(props: Record<string, unknown>): HTMLElement {
  host = document.createElement('div')
  document.body.appendChild(host)
  app = createApp({ render: () => h(TThreadComposer, { modelValue: '', ...props }, { left: () => h('button', { class: 'picker' }, 'Agent') }) })
  app.mount(host)
  return host
}

afterEach(() => {
  app?.unmount()
  host?.remove()
  app = null
  host = null
})

const file = 'components/chat/TThreadComposer.vue'

describe('TThreadComposer inline', () => {
  it('is off by default', async () => {
    const el = mount({})
    await nextTick()
    expect(el.querySelector('.t-thread-composer-wrap')!.classList.contains('is-inline')).toBe(false)
  })

  it('marks the wrapper, keeping the same parts in the same order', async () => {
    const el = mount({ inline: true, sticky: false })
    await nextTick()
    const wrap = el.querySelector('.t-thread-composer-wrap')!
    expect(wrap.classList.contains('is-inline')).toBe(true)
    expect(wrap.querySelector('.t-thread-composer__left .picker')).not.toBeNull()
    expect(wrap.querySelector('.t-thread-composer__input')).not.toBeNull()
    expect(wrap.querySelector('.t-thread-composer__right .t-thread-composer__send')).not.toBeNull()
  })

  it('lays the clusters around the text in one pill-shaped row', () => {
    expect(cssRule(file, '.t-thread-composer-wrap.is-inline')).toMatch(/padding-top: 0;/)
    const box = cssRule(file, '.t-thread-composer-wrap.is-inline .t-thread-composer')
    expect(box).toMatch(/display: flex;/)
    expect(box).toMatch(/flex-wrap: wrap;/)
    expect(box).toMatch(/align-items: flex-end;/)
    expect(box).toMatch(/padding: 3px 5px;/)
    expect(box).toMatch(/border-radius: 22px;/)
    expect(cssRule(file, '.t-thread-composer-wrap.is-inline .t-thread-composer__bar')).toMatch(/display: contents;/)
    expect(cssRule(file, '.t-thread-composer-wrap.is-inline .t-thread-composer__left')).toMatch(/order: 1;/)
    const input = cssRule(file, '.t-thread-composer-wrap.is-inline .t-thread-composer__input')
    expect(input).toMatch(/order: 2;/)
    expect(input).toMatch(/flex: 1 1 0;/)
    expect(input).toMatch(/padding: 6px;/)
    expect(cssRule(file, '.t-thread-composer-wrap.is-inline .t-thread-composer__right')).toMatch(/order: 3;\s*margin-left: 0;/)
  })

  it('gives attachments and notices a line of their own, and a phone 16px text', () => {
    const lines = cssRule(file, '.t-thread-composer-wrap.is-inline .t-thread-composer__files,\n.t-thread-composer-wrap.is-inline .t-thread-composer__reject')
    expect(lines).toMatch(/flex: 1 1 100%;/)
    expect(lines).toMatch(/margin: 4px 6px 6px;/)
    expect(cssRule(file, '.t-thread-composer-wrap.is-inline .t-thread-composer__reject')).toMatch(/padding: 6px 6px 6px 10px;/)
    expect(cssRule(file, '  .t-thread-composer-wrap.is-inline .t-thread-composer__input')).toMatch(/font-size: 16px;/)
  })

  it('comes after the compact rules, so a phone keeps the row', () => {
    const source = readFileSync(resolve(__dirname, '../../src', file), 'utf8')
    expect(source.indexOf('.is-inline .t-thread-composer {')).toBeGreaterThan(source.indexOf('.is-compact .t-thread-composer {'))
  })
})

/**
 * TComposerModelPicker with thinking efforts: the trigger reads "model · effort", the
 * menu lists the models and then the current model's efforts as a segmented row (its
 * default marked), choosing an effort keeps the menu open, choosing a model closes it,
 * and an effort the locale does not name shows as its raw value.
 */
import { afterEach, describe, expect, it } from 'vitest'
import { createApp, h, nextTick, ref, type App } from 'vue'

const { default: TComposerModelPicker } = await import('../../src/components/chat/TComposerModelPicker.vue')

const MODELS = [
  { id: 'opus', displayName: 'Opus 5.5', efforts: ['low', 'medium', 'high', 'xhigh', 'max'], defaultEffort: 'medium' },
  { id: 'sonnet', displayName: 'Sonnet 5.5', efforts: ['low', 'medium', 'high'], defaultEffort: 'high' },
  { id: 'haiku', displayName: 'Haiku 4.5' },
  { id: 'kimi', displayName: 'Kimi', efforts: ['off', 'on'], defaultEffort: 'on' },
]

let app: App | null = null
let host: HTMLElement | null = null

afterEach(() => {
  app?.unmount()
  host?.remove()
  app = null
  host = null
})

async function mount(model: string | null, effort: string | null, models: typeof MODELS = MODELS) {
  const state = { model: ref(model), effort: ref(effort), modelEvents: [] as string[], effortEvents: [] as string[] }
  host = document.createElement('div')
  document.body.appendChild(host)
  app = createApp({
    render: () =>
      h(TComposerModelPicker, {
        models,
        modelValue: state.model.value,
        effort: state.effort.value,
        'onUpdate:modelValue': (id: string) => {
          state.modelEvents.push(id)
          state.model.value = id
        },
        'onUpdate:effort': (value: string) => {
          state.effortEvents.push(value)
          state.effort.value = value
        },
      }),
  })
  app.mount(host)
  await nextTick()
  return state
}

const trigger = () => host!.querySelector<HTMLButtonElement>('.t-composer-model-picker__trigger')!
const effortRow = () => host!.querySelector<HTMLElement>('[data-effort-row]')
const effortButtons = () => [...host!.querySelectorAll<HTMLButtonElement>('[data-effort-row] .t-segmented__option')]
const isOpen = () => trigger().getAttribute('aria-expanded') === 'true'

async function openMenu() {
  trigger().click()
  await nextTick()
  await nextTick()
}

describe('TComposerModelPicker efforts', () => {
  it('reads "model · effort" on the trigger, or the model alone without efforts', async () => {
    await mount('opus', 'xhigh')
    expect(trigger().textContent).toContain('Opus 5.5 · Extra high')
    app!.unmount()
    host!.remove()

    await mount('haiku', null)
    expect(trigger().textContent?.trim()).toBe('Haiku 4.5')
  })

  it('an effort the model does not offer is not shown as chosen', async () => {
    await mount('sonnet', 'max')
    expect(trigger().textContent?.trim()).toBe('Sonnet 5.5')
  })

  it("lists only the current model's efforts, marking its default", async () => {
    await mount('sonnet', 'high')
    await openMenu()

    expect(effortRow()).not.toBeNull()
    expect(effortButtons().map((b) => b.textContent?.trim())).toEqual(['Low', 'Medium', 'HighDefault'])
    const marked = effortButtons().filter((b) => b.querySelector('.t-segmented__dot--muted'))
    expect(marked.map((b) => b.dataset.value)).toEqual(['high'])
    expect(host!.querySelector('.t-composer-model-picker__effort-hint')?.textContent).toContain('longer')
  })

  it('choosing an effort keeps the menu open; choosing a model closes it', async () => {
    const state = await mount('opus', 'medium')
    await openMenu()

    effortButtons().find((b) => b.dataset.value === 'max')!.click()
    await nextTick()
    expect(state.effortEvents).toEqual(['max'])
    expect(isOpen()).toBe(true)
    expect(trigger().textContent).toContain('Opus 5.5 · Max')

    host!.querySelector<HTMLButtonElement>('[role="menuitemradio"]:nth-of-type(2)')!.click()
    await nextTick()
    expect(state.modelEvents).toEqual(['sonnet'])
    expect(isOpen()).toBe(false)
  })

  it('the effort row fills the menu at desktop width too, for five efforts and for two', async () => {
    expect(window.innerWidth).toBeGreaterThanOrEqual(768)
    await mount('opus', 'low')
    await openMenu()
    expect(host!.querySelector('[data-effort-row] .t-segmented')!.classList.contains('t-segmented--grow')).toBe(true)
    app!.unmount()
    host!.remove()

    await mount('kimi', 'on')
    await openMenu()
    expect(host!.querySelector('[data-effort-row] .t-segmented')!.classList.contains('t-segmented--grow')).toBe(true)
  })

  it('a model without efforts has no effort row', async () => {
    await mount('haiku', null)
    await openMenu()
    expect(effortRow()).toBeNull()
  })

  it("names Kimi's off / on switch as two cells", async () => {
    await mount('kimi', 'on')
    expect(trigger().textContent).toContain('Kimi · On')
    await openMenu()
    expect(effortButtons().map((b) => b.dataset.value)).toEqual(['off', 'on'])
    expect(host!.querySelector('.t-composer-model-picker__effort-hint')?.textContent).toBe('Thinks before answering')
  })

  it('shows an effort the locale does not name as it is, without a hint', async () => {
    await mount('odd', 'turbo', [{ id: 'odd', displayName: 'Odd', efforts: ['turbo', 'eco'], defaultEffort: 'eco' }])
    expect(trigger().textContent).toContain('Odd · turbo')
    await openMenu()
    expect(effortButtons().map((b) => b.textContent?.trim())).toEqual(['turbo', 'ecoDefault'])
    expect(host!.querySelector('.t-composer-model-picker__effort-hint')).toBeNull()
  })

  it('a single model with efforts shows no model list, only the effort row', async () => {
    await mount('opus', 'low', [MODELS[0]!])
    await openMenu()
    expect(host!.querySelectorAll('[role="menuitemradio"]')).toHaveLength(0)
    expect(effortButtons()).toHaveLength(5)
  })

  it('arrow keys in the effort row move the effort, not the menu focus', async () => {
    const state = await mount('opus', 'low')
    await openMenu()
    const low = effortButtons().find((b) => b.dataset.value === 'low')!
    low.focus()
    const event = new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true })
    let reachedMenu = false
    host!.querySelector('[role="menu"]')!.addEventListener('keydown', () => { reachedMenu = true })
    low.dispatchEvent(event)
    await nextTick()
    expect(state.effortEvents).toEqual(['medium'])
    expect(reachedMenu).toBe(false)
  })
})

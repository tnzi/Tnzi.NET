import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import TPageHeader from '../../../src/components/layout/TPageHeader.vue'
import TRowActions from '../../../src/components/crud/TRowActions.vue'
import type { RowAction } from '../../../src/headless/row-actions'

/* Narrow-screen behaviour is driven by `useBreakpoint().isSm`; happy-dom does
   no media queries, so the house pattern is to mock the module (same as
   TListShell / TChatWindow) and flip the ref per test. */
const bp = vi.hoisted(() => ({ isSm: { value: false } }))
vi.mock('../../../src/headless/useBreakpoint', () => ({ useBreakpoint: () => bp }))

const stubs = {
  Popover: { name: 'Popover', template: '<div><slot name="trigger" /><slot /></div>' },
  SvgIcon: true,
}

function mountHeader(props = {}, slots = {}) {
  const global: Record<string, unknown> = { stubs }
  return mount(TPageHeader, { props, slots, global })
}

async function mountHeaderWithRouter(props = {}) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/u', name: 'identity.users', meta: { title: 'tnzi.admin.modules.identity.users.title' }, component: { template: '<div/>' } }],
  })
  await router.push('/u')
  await router.isReady()
  const w = mount(TPageHeader, { props, global: { stubs, plugins: [router] } })
  await flushPromises()
  return w
}

describe('TPageHeader', () => {
  it('renders the title prop', () => {
    const w = mountHeader({ title: 'User Management' })
    expect(w.find('.t-page-header__title').text()).toBe('User Management')
  })

  it('shows the ⓘ help popover only when help is set', () => {
    expect(mountHeader({ title: 'X' }).find('.t-page-header__help').exists()).toBe(false)
    expect(mountHeader({ title: 'X', help: 'Explains X' }).find('.t-page-header__help').exists()).toBe(true)
  })

  it('lets #title slot replace the whole left region', () => {
    const w = mountHeader({ title: 'X' }, { title: '<div class="custom-title">Avatar+Name</div>' })
    expect(w.find('.custom-title').exists()).toBe(true)
    expect(w.find('.t-page-header__title').exists()).toBe(false)
  })

  it('renders the #actions slot', () => {
    const w = mountHeader({ title: 'X' }, { actions: '<button class="act">Go</button>' })
    expect(w.find('.t-page-header__actions .act').exists()).toBe(true)
  })

  // The bar is three columns. The CSS side of the contract (only the centre one
  // is flexible, the bar does not wrap) lives in page-header-geometry.test.ts;
  // these pin the DOM the CSS is written against.
  it('lays the bar out as three sibling columns', () => {
    const w = mountHeader({ title: 'X', back: true }, { actions: '<button class="a">A</button>' })
    expect(w.find('.t-page-header__bar > .t-page-header__back').exists()).toBe(true)
    expect(w.find('.t-page-header__bar > .t-page-header__main').exists()).toBe(true)
    expect(w.find('.t-page-header__bar > .t-page-header__actions').exists()).toBe(true)
  })

  it('puts the back control OUTSIDE the identity, in its own column', () => {
    // Inside the identity its width would count toward what sizes the title,
    // and a rich `#title` could swallow the affordance the page asked for.
    const w = mountHeader({ title: 'X', back: true })
    expect(w.find('.t-page-header__main .t-page-header__back').exists()).toBe(false)
    expect(w.find('.t-page-header__left .t-page-header__back').exists()).toBe(false)
  })

  it('puts #extra in the SAME column as the identity, under it', () => {
    // This is what makes the subtitle line up with the title without any
    // re-applied indent, and what keeps it out of the row that sizes #actions.
    const w = mountHeader({ title: 'X', back: true }, { extra: '<span class="meta">FILE-1</span>' })
    expect(w.find('.t-page-header__main > .t-page-header__extra .meta').exists()).toBe(true)
    // Same parent as the identity row, and after it.
    const main = w.find('.t-page-header__main').element
    const children = Array.from(main.children).map((el) => el.className)
    expect(children).toEqual(['t-page-header__left', 't-page-header__extra'])
  })

  it('renders no subtitle row when the page supplies no #extra', () => {
    const w = mountHeader({ title: 'X', back: true })
    expect(w.find('.t-page-header__extra').exists()).toBe(false)
    expect(w.find('.t-page-header__main > .t-page-header__left').exists()).toBe(true)
  })

  it('keeps the identity column when there is no back control', () => {
    const w = mountHeader({ title: 'X' }, { extra: '<span class="meta">M</span>' })
    expect(w.find('.t-page-header__back').exists()).toBe(false)
    expect(w.find('.t-page-header__main > .t-page-header__extra .meta').exists()).toBe(true)
  })

  // --- narrow-screen budget: the header is outside the scroll container, so
  // every row it grows by is subtracted from the readable area outright. ---

  describe('narrow screens', () => {
    beforeEach(() => { bp.isSm.value = true })
    afterEach(() => { bp.isSm.value = false })

    const twoActions: RowAction<void>[] = [
      { key: 'save', label: 'Save', type: 'primary', onClick: () => {} },
      { key: 'archive', label: 'Archive', onClick: () => {} },
    ]

    it('moves every declarative action into the More menu', async () => {
      const w = mountHeader({ title: 'X', actions: twoActions })
      await w.vm.$nextTick()
      const rowActions = w.findComponent(TRowActions)
      expect(rowActions.exists()).toBe(true)
      // maxInline 0 => nothing inline, everything reachable through the menu.
      expect(rowActions.props('maxInline')).toBe(0)
    })

    it('treats collapsed actions as an inline cluster, so they keep the title row', () => {
      // Without this the phone rule gives the action group a full-width basis
      // and the single "More" button claims an entire row of the header.
      const w = mountHeader({ title: 'X', actions: twoActions })
      expect(w.classes()).toContain('t-page-header--inline-actions')
    })

    it('does NOT claim the row for a slot-based action cluster', () => {
      // Slot content may be a search field rather than buttons (TListShell);
      // the framework cannot collapse it, so it keeps its own row.
      const w = mountHeader({ title: 'X' }, { actions: '<button class="a">A</button>' })
      expect(w.classes()).not.toContain('t-page-header--inline-actions')
    })

    it('collapses #extra behind a chevron, and opening it reveals the row', async () => {
      const w = mountHeader({ title: 'X' }, { extra: '<span class="meta">FILE-1</span>' })
      const toggle = w.find('.t-page-header__extra-toggle')
      expect(toggle.exists()).toBe(true)
      expect(toggle.attributes('aria-expanded')).toBe('false')
      // Hidden, but discoverable and reachable - not silently dropped.
      expect(w.find('.t-page-header__extra').exists()).toBe(false)

      await toggle.trigger('click')
      expect(w.find('.t-page-header__extra .meta').exists()).toBe(true)
      expect(w.find('.t-page-header__extra-toggle').attributes('aria-expanded')).toBe('true')
    })

    it('keeps the subtitle when the page opts out of collapsing', () => {
      const w = mountHeader({ title: 'X', extraCollapse: false }, { extra: '<span class="meta">M</span>' })
      expect(w.find('.t-page-header__extra-toggle').exists()).toBe(false)
      expect(w.find('.t-page-header__extra .meta').exists()).toBe(true)
    })
  })

  it('keeps declarative actions inline when there is room', () => {
    const w = mountHeader({
      title: 'X',
      actions: [{ key: 'save', label: 'Save', onClick: () => {} }] as RowAction<void>[],
    })
    expect(w.findComponent(TRowActions).props('maxInline')).toBe(2)
    expect(w.classes()).not.toContain('t-page-header--inline-actions')
  })

  it('lets an #actions slot win over declarative actions', () => {
    // Backward compatibility: pages that already hand-render their cluster keep
    // rendering it, and nothing of theirs moves into a menu.
    const w = mountHeader(
      { title: 'X', actions: [{ key: 'save', label: 'Save', onClick: () => {} }] as RowAction<void>[] },
      { actions: '<button class="mine">Mine</button>' },
    )
    expect(w.find('.t-page-header__actions .mine').exists()).toBe(true)
    expect(w.findComponent(TRowActions).exists()).toBe(false)
  })

  it('shows #extra with no toggle at desktop widths', () => {
    const w = mountHeader({ title: 'X' }, { extra: '<span class="meta">M</span>' })
    expect(w.find('.t-page-header__extra .meta').exists()).toBe(true)
    expect(w.find('.t-page-header__extra-toggle').exists()).toBe(false)
  })

  it('falls back to route.meta.title when no title prop', async () => {
    const w = await mountHeaderWithRouter({ translate: (k: string) => (k.endsWith('.title') ? 'Users' : k) })
    await w.vm.$nextTick()
    expect(w.find('.t-page-header__title').text()).toBe('Users')
  })
})

/**
 * Nav badges as they actually render.
 *
 * The pure display rule is covered in `unit/nav-badge.test.ts`; what these
 * lock is the wiring - which naive slot each surface uses, and which of the
 * TWO chip shapes it gets. Both matter and neither shows up in a snapshot or
 * in typecheck:
 *   - naive drops a menu row's trailing `extra` region to `opacity: 0` the
 *     moment the menu collapses, which is exactly the rail where a count is
 *     most useful, so the collapsed rail must use the icon instead;
 *   - a slotless `NBadge` is a `<sup>` sized for a corner overlay (raised off
 *     the label's line, 18px against a 14px label, glued to the last letter).
 *     An expanded row must therefore NOT be an `NBadge`, and a collapsed rail
 *     must still be one. "A badge exists" would pass either way.
 */
import { describe, it, expect, beforeEach } from 'vitest'
import fs from 'node:fs'
import path from 'node:path'
import { h } from 'vue'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import TAdminSidebar from '../../../src/components/layout/TAdminSidebar.vue'
import TDetailLayout from '../../../src/components/detail/TDetailLayout.vue'
import { useAdminRouteStore, type AdminRouteRecord } from '../../../src/stores/useAdminRouteStore'
import { useAdminAppStore } from '../../../src/stores/useAdminAppStore'

/** Renders each option's `icon` / `extra` render functions so we can read them. */
const menuStub = {
  name: 'Menu',
  props: ['options', 'collapsed', 'value', 'mode', 'indent', 'collapsedWidth', 'collapsedIconSize', 'expandedKeys', 'inverted'],
  setup(props: { options: Array<Record<string, unknown>> }) {
    return () =>
      h(
        'ul',
        { class: 'n-menu-stub' },
        (props.options ?? []).map((o) =>
          h('li', { 'data-key': String(o.key), 'data-has-extra': o.extra ? '1' : '0' }, [
            h('span', { class: 'row-icon' }, o.icon ? [(o.icon as () => unknown)()] : []),
            h('span', { class: 'row-extra' }, o.extra ? [(o.extra as () => unknown)()] : []),
          ]),
        ),
      )
  },
}

const routes: AdminRouteRecord[] = [
  { name: 'staff', path: '/staff', meta: { title: 'Staff', icon: 'mdi:account', order: 1 } },
  { name: 'files', path: '/files', meta: { title: 'Files', icon: 'mdi:file', order: 2 } },
]

function mountSidebar() {
  return mount(TAdminSidebar, { global: { stubs: { Menu: menuStub, SvgIcon: true } } })
}

function row(wrapper: ReturnType<typeof mountSidebar>, key: string) {
  return wrapper.find(`[data-key="${key}"]`)
}

describe('sidebar menu badges', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('paints the count at the row trailing edge when the sider is open', () => {
    const store = useAdminRouteStore()
    store.setConstantRoutes(routes)
    store.setMenuBadge('staff', 3)
    const w = mountSidebar()
    expect(row(w, 'staff').find('.row-extra').text()).toBe('3')
  })

  it('uses the inline chip, not naive corner-overlay superscript machinery', () => {
    const store = useAdminRouteStore()
    store.setConstantRoutes(routes)
    store.setMenuBadge('staff', 3)
    const w = mountSidebar()
    const extra = row(w, 'staff').find('.row-extra')
    expect(extra.find('span.t-nav-badge').exists()).toBe(true)
    // An `NBadge` here would render `<sup class="n-badge-sup">`: raised off the
    // label's line, 18px tall, glued to the last letter. That is the corner
    // shape and it belongs on the collapsed rail, not on an expanded row.
    expect(extra.find('.n-badge').exists()).toBe(false)
    expect(extra.find('sup').exists()).toBe(false)
  })

  it('leaves `extra` unset entirely for entries with no badge', () => {
    const store = useAdminRouteStore()
    store.setConstantRoutes(routes)
    store.setMenuBadge('staff', 3)
    const w = mountSidebar()
    // Not merely empty - absent, so naive renders no extra region at all.
    expect(row(w, 'files').attributes('data-has-extra')).toBe('0')
    expect(row(w, 'staff').attributes('data-has-extra')).toBe('1')
  })

  it('renders NOTHING for a zero count', () => {
    const store = useAdminRouteStore()
    store.setConstantRoutes(routes)
    store.setMenuBadge('staff', 0)
    const w = mountSidebar()
    expect(row(w, 'staff').attributes('data-has-extra')).toBe('0')
  })

  it('honours the framework-wide cap of 99', () => {
    const store = useAdminRouteStore()
    store.setConstantRoutes(routes)
    store.setMenuBadge('staff', 250)
    const w = mountSidebar()
    expect(row(w, 'staff').find('.row-extra').text()).toBe('99+')
  })

  it('moves the count onto the icon when the sider is collapsed', () => {
    const store = useAdminRouteStore()
    store.setConstantRoutes(routes)
    store.setMenuBadge('staff', 5)
    useAdminAppStore().setSiderCollapse(true)
    const w = mountSidebar()
    // `extra` is invisible on a collapsed rail, so it must NOT be used there.
    expect(row(w, 'staff').attributes('data-has-extra')).toBe('0')
    const icon = row(w, 'staff').find('.row-icon')
    expect(icon.text()).toContain('5')
    // ...and it keeps the naive corner overlay, which is right for an icon.
    expect(icon.find('.n-badge').exists()).toBe(true)
    expect(icon.find('span.t-nav-badge').exists()).toBe(false)
  })

  it('follows the count at runtime without remounting', async () => {
    const store = useAdminRouteStore()
    store.setConstantRoutes(routes)
    const w = mountSidebar()
    expect(row(w, 'staff').attributes('data-has-extra')).toBe('0')
    store.setMenuBadge('staff', 2)
    await w.vm.$nextTick()
    expect(row(w, 'staff').find('.row-extra').text()).toBe('2')
    store.setMenuBadge('staff', null)
    await w.vm.$nextTick()
    expect(row(w, 'staff').attributes('data-has-extra')).toBe('0')
  })
})

/**
 * The tabs strip runs against REAL naive components.
 *
 * It used to be stubbed, and the stub invoked the `tab` prop itself - so it
 * asserted what the prop was supposed to produce rather than what naive did
 * with it. Naive was in fact dropping the label entirely and painting an empty
 * comment node, and this test could not see it. `NMenu` stays stubbed because
 * the side nav's counts live in render functions that only a stub can read.
 */
describe('detail section badges', () => {
  const sections = [
    { key: 'profile', label: 'Profile', icon: 'mdi:account' },
    { key: 'attendance', label: 'Attendance', icon: 'mdi:calendar', badge: 4 },
  ]

  function mountDetail(layout: 'side' | 'tabs') {
    return mount(TDetailLayout, {
      props: { layout, sections, activeSection: 'profile', title: 'Staff' },
      slots: { default: '<div class="body" />' },
      global: { stubs: { Menu: menuStub, SvgIcon: true, Popover: true } },
    })
  }

  it('side nav paints the count at the row trailing edge', () => {
    const w = mountDetail('side')
    expect(w.find('[data-key="attendance"] .row-extra').text()).toBe('4')
    expect(w.find('[data-key="attendance"] .row-extra span.t-nav-badge').exists()).toBe(true)
    expect(w.find('[data-key="attendance"] .row-extra .n-badge').exists()).toBe(false)
    expect(w.find('[data-key="profile"]').attributes('data-has-extra')).toBe('0')
  })

  it('tabs strip paints the count beside the label', () => {
    const w = mountDetail('tabs')
    expect(w.find('[data-name="attendance"]').text()).toContain('Attendance')
    expect(w.find('[data-name="attendance"]').text()).toContain('4')
    expect(w.find('[data-name="attendance"] span.t-nav-badge').exists()).toBe(true)
    // A badge-less tab keeps the plain string naive rendered before.
    expect(w.find('[data-name="profile"]').text()).toBe('Profile')
  })
})

/**
 * The three things the consuming app actually complained about are CSS
 * properties, and happy-dom applies no stylesheet - so they are pinned by
 * reading the rule. Deliberately loose on the VALUES (tuning 6px to 8px is
 * somebody's call, not a regression) and strict on the properties BEING
 * DECLARED, because dropping one is what puts the chip back where it was.
 */
describe('the inline chip rule', () => {
  const css = fs.readFileSync(path.resolve(__dirname, '../../../src/styles/polish.css'), 'utf8')
  const rule = css.slice(css.indexOf('.t-nav-badge {'))
  const block = rule.slice(0, rule.indexOf('}'))

  it('is declared once, shared by every expanded surface', () => {
    expect(css.match(/^\.t-nav-badge \{/gm)?.length).toBe(1)
  })

  it('sets vertical-align, so it sits on the label line instead of above it', () => {
    // The bug was inheriting `<sup>`'s UA `vertical-align: super`.
    expect(block).toMatch(/vertical-align:\s*(?!super)\S+/)
  })

  it('carries its own leading gap, so it is not glued to the label', () => {
    const margin = block.match(/margin-left:\s*(\d+(?:\.\d+)?)px/)
    expect(margin).not.toBeNull()
    expect(Number(margin![1])).toBeGreaterThan(0)
  })

  it('is smaller than the 14px label - a count, not an alert', () => {
    const font = block.match(/font-size:\s*(\d+(?:\.\d+)?)px/)
    const height = block.match(/height:\s*(\d+(?:\.\d+)?)px/)
    expect(font).not.toBeNull()
    expect(height).not.toBeNull()
    expect(Number(font![1])).toBeLessThan(14)
    // naive's corner chip is 18px; anything at or above that reads as an alert.
    expect(Number(height![1])).toBeLessThan(18)
  })
})

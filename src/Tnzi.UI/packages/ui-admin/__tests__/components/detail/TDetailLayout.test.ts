import { describe, it, expect } from 'vitest'
import fs from 'node:fs'
import path from 'node:path'
import { h } from 'vue'
import { mount } from '@vue/test-utils'
import TDetailLayout from '../../../src/components/detail/TDetailLayout.vue'
import TPageHeader from '../../../src/components/layout/TPageHeader.vue'

/**
 * NTabs is deliberately NOT stubbed. The tab strip's whole job is to render a
 * LABEL, and a stub can only ever tell us that a strip exists - which is what
 * the previous version of this test asserted while every real label rendered
 * blank. See the `tabs layout` cases below.
 */
const stubs = {
  Popover: { name: 'Popover', template: '<div><slot name="trigger" /><slot /></div>' },
  Menu: { name: 'Menu', props: ['value', 'options'], template: '<div class="n-menu-stub" />' },
  SvgIcon: true,
}
const sections = [
  { key: 'basic', label: 'Basic' },
  { key: 'perms', label: 'Perms', group: 'security' },
]

describe('TDetailLayout', () => {
  it('plain layout renders header + body + footer slots', () => {
    const w = mount(TDetailLayout, {
      props: { layout: 'plain', title: 'Edit' },
      slots: { default: '<div class="body">B</div>', footer: '<div class="ft">F</div>' },
      global: { stubs },
    })
    expect(w.find('.t-page-header__title').text()).toBe('Edit')
    expect(w.find('.body').text()).toBe('B')
    expect(w.find('.ft').text()).toBe('F')
  })

  it('forwards #extra to the header, under the title in the identity column', () => {
    const w = mount(TDetailLayout, {
      props: { layout: 'plain', title: 'Edit' },
      slots: {
        default: '<div class="body" />',
        extra: '<span class="meta">FILE-2024-0912 - Litigation - Acme Corp</span>',
      },
      global: { stubs },
    })
    // Its own row inside the identity column (so it lines up with the title)...
    expect(w.find('.t-page-header__main > .t-page-header__extra .meta').exists()).toBe(true)
    // ...and NOT in the identity row itself, whose width sizes the title.
    expect(w.find('.t-page-header__left .meta').exists()).toBe(false)
  })

  it('renders no extra row when the page supplies no #extra', () => {
    const w = mount(TDetailLayout, {
      props: { layout: 'plain', title: 'Edit' },
      slots: { default: '<div class="body" />', actions: '<button class="act" />' },
      global: { stubs },
    })
    expect(w.find('.t-page-header__extra').exists()).toBe(false)
    expect(w.find('.t-page-header__bar .act').exists()).toBe(true)
  })

  /* The failure this guards is invisible to typecheck, build AND a stubbed
     mount: the strip renders, the tabs are clickable, `data-name` is right -
     every label is just empty. So the assertion has to be the text. */
  describe('tabs layout', () => {
    /* Three shapes on purpose: icon only, icon + badge, and neither. The last
       one is the regression guard for "an adornment nobody asked for" - it
       must stay a bare label with no placeholder. */
    const tabSections = [
      { key: 'basic', label: 'Basic', icon: 'mdi:information-outline' },
      { key: 'perms', label: 'Perms', badge: 3, icon: 'mdi:shield-outline' },
      { key: 'plain', label: 'Plain' },
    ]

    function mountTabs(extraStubs: Record<string, unknown> = {}) {
      return mount(TDetailLayout, {
        props: { layout: 'tabs', title: 'X', sections: tabSections, activeSection: 'basic' },
        slots: { default: '<div class="body" />' },
        global: { stubs: { ...stubs, ...extraStubs } },
      })
    }

    function tab(w: ReturnType<typeof mountTabs>, key: string) {
      return w.find(`.n-tabs-tab[data-name="${key}"]`)
    }

    it('renders one tab per section, each showing its label', () => {
      const labels = mountTabs().findAll('.n-tabs-tab__label')
      expect(labels).toHaveLength(3)
      expect(labels[0].text()).toBe('Basic')
      expect(labels[1].text()).toBe('Perms3')
      expect(labels[2].text()).toBe('Plain')
    })

    it('keeps the label AND the trailing chip on a badged section', () => {
      const badged = tab(mountTabs(), 'perms')
      expect(badged.find('.t-nav-badge').text()).toBe('3')
      // the chip must be additive - a badge that ate its own label is the bug
      expect(badged.find('.t-detail-layout__tab-label').text()).toContain('Perms')
    })

    /* `icon` is one `DetailSection` field read by BOTH layouts. When only the
       side NMenu honoured it, moving a page from `side` to `tabs` silently
       dropped the glyph and nothing said so - `layout` picks a shape, it does
       not pick which fields mean something. Real TSvgIcon here (the shared
       `SvgIcon` stub key does not match it), so this also proves the element
       survives naive's own render path. */
    it('paints the section icon ahead of the label', () => {
      const w = mountTabs()
      expect(tab(w, 'basic').find('.t-detail-layout__tab-icon').exists()).toBe(true)
      expect(tab(w, 'perms').find('.t-detail-layout__tab-icon').exists()).toBe(true)
    })

    it('carries the icon the section actually declared, per tab', () => {
      const w = mountTabs({ TSvgIcon: true })
      expect(tab(w, 'basic').find('.t-detail-layout__tab-icon').attributes('icon')).toBe(
        'mdi:information-outline',
      )
      expect(tab(w, 'perms').find('.t-detail-layout__tab-icon').attributes('icon')).toBe(
        'mdi:shield-outline',
      )
    })

    it('leaves an icon-less section as a bare label, with no placeholder', () => {
      const plain = tab(mountTabs(), 'plain')
      expect(plain.find('.t-detail-layout__tab-icon').exists()).toBe(false)
      // Not merely empty - absent. No wrapper span either: naive keeps the
      // plain string it always built for a section with no adornments.
      expect(plain.find('.t-detail-layout__tab-label').exists()).toBe(false)
      expect(plain.find('.n-tabs-tab__label').text()).toBe('Plain')
    })

    it('marks the active section and emits on click', async () => {
      const w = mountTabs()
      expect(tab(w, 'basic').classes()).toContain('n-tabs-tab--active')
      await tab(w, 'perms').trigger('click')
      expect(w.emitted('update:activeSection')?.[0]).toEqual(['perms'])
    })
  })

  /**
   * The tab label's own styling is NOT scoped, and that is load-bearing.
   * `tabLabel()` builds those vnodes but naive's `Tab` renders them, so Vue
   * stamps ITS scope id (none) on them - a scoped rule compiles, ships and
   * matches nothing. The former scoped `white-space: nowrap` did exactly that.
   * happy-dom applies no stylesheet, so this is pinned by reading the source.
   */
  describe('the tab label rule', () => {
    const dir = path.resolve(__dirname, '../../../src')
    const polish = fs.readFileSync(path.join(dir, 'styles/polish.css'), 'utf8')
    const sfc = fs.readFileSync(path.join(dir, 'components/detail/TDetailLayout.vue'), 'utf8')
    const scoped = sfc.slice(sfc.indexOf('<style scoped>'))

    it.each(['.t-detail-layout__tab-label', '.t-detail-layout__tab-icon'])(
      'declares %s globally, never in the scoped block',
      (selector) => {
        expect(polish).toContain(`${selector} {`)
        // A DECLARATION, not a mention - the scoped block carries a comment
        // pointing at polish.css, and that has to stay legal.
        const declares = new RegExp(`${selector.replace('.', '\\.')}\\s*[,{]`)
        expect(scoped).not.toMatch(declares)
      },
    )

    it('centres the label on its line rather than the text baseline', () => {
      const block = polish.slice(polish.indexOf('.t-detail-layout__tab-label {'))
      expect(block.slice(0, block.indexOf('}'))).toMatch(/display:\s*inline-flex/)
    })

    it('spaces the icon from the label without a container gap', () => {
      const icon = polish.slice(polish.indexOf('.t-detail-layout__tab-icon {'))
      expect(icon.slice(0, icon.indexOf('}'))).toMatch(/margin-right:\s*\d/)
      // A `gap` on the wrapper would double the chip's own `margin-left`.
      const label = polish.slice(polish.indexOf('.t-detail-layout__tab-label {'))
      expect(label.slice(0, label.indexOf('}'))).not.toMatch(/\bgap:/)
    })
  })

  it('side layout renders the left NMenu', () => {
    const w = mount(TDetailLayout, {
      props: { layout: 'side', title: 'X', sections, activeSection: 'basic' },
      slots: { default: '<div class="body" />' },
      global: { stubs },
    })
    expect(w.find('.n-menu-stub').exists()).toBe(true)
    expect(w.find('.t-detail-layout--side').exists()).toBe(true)
  })

  it('exposes the active section icon as a slot prop (menu icon ⇒ panel title icon)', () => {
    const iconSections = [
      { key: 'basic', label: 'Basic', icon: 'mdi:information-outline' },
      { key: 'perms', label: 'Perms', icon: 'mdi:shield-outline', group: 'security' },
    ]
    const w = mount(TDetailLayout, {
      props: { layout: 'side', sections: iconSections, activeSection: 'perms' },
      slots: {
        default: (p: { sectionIcon?: string }) => h('span', { class: 'icon-probe' }, p.sectionIcon ?? ''),
      },
      global: { stubs },
    })
    expect(w.find('.icon-probe').text()).toBe('mdi:shield-outline')
  })

  it('section icon slot prop is empty when the active section has no icon', () => {
    const w = mount(TDetailLayout, {
      props: { layout: 'side', sections, activeSection: 'basic' },
      slots: {
        default: (p: { sectionIcon?: string }) => h('span', { class: 'icon-probe' }, p.sectionIcon ?? ''),
      },
      global: { stubs },
    })
    expect(w.find('.icon-probe').text()).toBe('')
  })

  it('emits update:activeSection when a section is selected', async () => {
    const w = mount(TDetailLayout, {
      props: { layout: 'side', title: 'X', sections, activeSection: 'basic' },
      slots: { default: '<div class="body" />' },
      global: { stubs },
    })
    ;(w.vm as unknown as { onSection: (k: string) => void }).onSection('perms')
    await w.vm.$nextTick()
    expect(w.emitted('update:activeSection')?.[0]).toEqual(['perms'])
  })

  it('omits the header when showHeader=false', () => {
    const w = mount(TDetailLayout, {
      props: { layout: 'plain', title: 'Edit', showHeader: false },
      slots: { default: '<div class="body" />' },
      global: { stubs },
    })
    expect(w.find('.t-page-header').exists()).toBe(false)
    expect(w.find('.body').exists()).toBe(true)
  })

  it('exposes scrollToSection that activates the target section', async () => {
    const w = mount(TDetailLayout, {
      props: { layout: 'side', title: 'X', sections, activeSection: 'basic' },
      slots: { default: '<div class="body" />' },
      global: { stubs },
    })
    const vm = w.vm as unknown as { scrollToSection?: (k: string) => Promise<void> }
    expect(typeof vm.scrollToSection).toBe('function')
    await vm.scrollToSection?.('perms')
    expect(w.emitted('update:activeSection')?.[0]).toEqual(['perms'])
  })

  /* Title tier. Anything the layout renders below its own header is ONE REGION
     of the page - which is what makes a TListShell dropped into a `side` panel
     line up with a TDetailSection picked from the same menu. The sizes
     themselves are asserted in ../layout/title-tier.test.ts. */
  describe('title tier', () => {
    /** The layout's own header, plus a header slotted into its panel/body. */
    function mountWithSlottedHeader(layout: 'plain' | 'side') {
      return mount(TDetailLayout, {
        props: { layout, title: 'Agent Foo', sections, activeSection: 'basic' },
        slots: { default: () => h(TPageHeader, { title: 'Skills' }) },
        global: { stubs },
      })
    }

    it('keeps its OWN header at the page tier', () => {
      const w = mountWithSlottedHeader('plain')
      const headers = w.findAll('.t-page-header')
      expect(headers[0].classes()).not.toContain('t-page-header--section')
    })

    it.each(['plain', 'side'] as const)(
      'gives a header slotted into the %s body the section tier, with no prop',
      (layout) => {
        const w = mountWithSlottedHeader(layout)
        const slotted = w.findAll('.t-page-header').at(-1)
        expect(slotted?.classes()).toContain('t-page-header--section')
        // ...and it really is a second header, not the layout's own one.
        expect(w.findAll('.t-page-header')).toHaveLength(2)
      },
    )
  })
})

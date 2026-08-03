import { describe, it, expect } from 'vitest'
import { ref } from 'vue'
import { mount } from '@vue/test-utils'
import { THint } from '@tnzi/ui'
import TDetailSection from '../../../src/components/detail/TDetailSection.vue'
import { DETAIL_ACTIVE_SECTION_ICON } from '../../../src/components/detail/active-section-icon'

// Template uses <TSvgIcon> (local import name); its component `name` is SvgIcon.
// Stub both keys so the match holds regardless of how VTU resolves it.
const iconStub = { name: 'SvgIcon', props: ['icon'], template: '<i class="svg-icon" :data-icon="icon" />' }
const stubs = { TSvgIcon: iconStub, SvgIcon: iconStub }

const iconOf = (w: ReturnType<typeof mount>) => w.find('.svg-icon').attributes('data-icon')

describe('TDetailSection title icon', () => {
  it('renders an explicit icon prop before the title', () => {
    const w = mount(TDetailSection, { props: { title: 'X', icon: 'mdi:star' }, global: { stubs } })
    expect(iconOf(w)).toBe('mdi:star')
  })

  it('falls back to the page-provided active-section icon (custom section auto-mirror)', () => {
    const w = mount(TDetailSection, {
      props: { title: 'X' },
      global: { stubs, provide: { [DETAIL_ACTIVE_SECTION_ICON as symbol]: ref('mdi:shield-outline') } },
    })
    expect(iconOf(w)).toBe('mdi:shield-outline')
  })

  it('explicit icon prop wins over the provided fallback', () => {
    const w = mount(TDetailSection, {
      props: { title: 'X', icon: 'mdi:star' },
      global: { stubs, provide: { [DETAIL_ACTIVE_SECTION_ICON as symbol]: ref('mdi:shield-outline') } },
    })
    expect(iconOf(w)).toBe('mdi:star')
  })

  it('renders no icon when neither prop nor provider is present', () => {
    const w = mount(TDetailSection, { props: { title: 'X' }, global: { stubs } })
    expect(w.find('.svg-icon').exists()).toBe(false)
  })
})

describe('TDetailSection hint placement', () => {
  const HINT = 'Vacation pay accrues as wages are earned and is owed whether or not the time is ever taken.'

  it('defaults to the muted line under the title (existing callers unchanged)', () => {
    const w = mount(TDetailSection, { props: { title: 'X', hint: HINT }, global: { stubs } })
    expect(w.find('.t-detail-section__hint').text()).toBe(HINT)
    expect(w.findComponent(THint).exists()).toBe(false)
  })

  it('moves the hint beside the title as an icon + popover when hintMode="popover"', () => {
    const w = mount(TDetailSection, {
      props: { title: 'X', hint: HINT, hintMode: 'popover' },
      global: { stubs },
    })
    // The standing paragraph is gone - that is the whole point of the mode.
    expect(w.find('.t-detail-section__hint').exists()).toBe(false)
    const hint = w.findComponent(THint)
    expect(hint.exists()).toBe(true)
    expect(hint.props('content')).toBe(HINT)
    // Screen readers still get the copy without hovering (THint labels the
    // trigger with its content).
    expect(w.find('.t-detail-section__title-row .t-hint').attributes('aria-label')).toBe(HINT)
  })

  it('keeps the hint trigger OUT of the heading so it stays out of the heading name', () => {
    const w = mount(TDetailSection, {
      props: { title: 'Vacation Pay', hint: HINT, hintMode: 'popover' },
      global: { stubs },
    })
    expect(w.find('h3 .t-hint').exists()).toBe(false)
    expect(w.find('h3').text()).toBe('Vacation Pay')
  })

  it('renders nothing hint-shaped when hint is absent in either mode', () => {
    for (const hintMode of ['inline', 'popover'] as const) {
      const w = mount(TDetailSection, { props: { title: 'X', hintMode }, global: { stubs } })
      expect(w.find('.t-detail-section__hint').exists()).toBe(false)
      expect(w.findComponent(THint).exists()).toBe(false)
    }
  })
})

describe('TDetailSection #titleExtra', () => {
  it('renders slot content on the heading row', () => {
    const w = mount(TDetailSection, {
      props: { title: 'X' },
      slots: { titleExtra: '<span class="probe-chip">3 pending</span>' },
      global: { stubs },
    })
    expect(w.find('.t-detail-section__title-row .probe-chip').text()).toBe('3 pending')
  })

  it('coexists with the default inline hint', () => {
    const w = mount(TDetailSection, {
      props: { title: 'X', hint: 'one line' },
      slots: { titleExtra: '<span class="probe-chip">3</span>' },
      global: { stubs },
    })
    expect(w.find('.probe-chip').exists()).toBe(true)
    expect(w.find('.t-detail-section__hint').text()).toBe('one line')
  })

  it('adds no markup when the slot is unused', () => {
    const w = mount(TDetailSection, { props: { title: 'X' }, global: { stubs } })
    expect(w.find('.t-detail-section__title-row').element.children.length).toBe(1)
  })
})

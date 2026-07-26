import { describe, it, expect } from 'vitest'
import { ref } from 'vue'
import { mount } from '@vue/test-utils'
import TDetailSection from '../../../src/components/detail/TDetailSection.vue'
import { DETAIL_ACTIVE_SECTION_ICON } from '../../../src/components/detail/activeSectionIcon'

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

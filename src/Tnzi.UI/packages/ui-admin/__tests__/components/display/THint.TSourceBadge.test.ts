import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { TSvgIcon } from '@tnzi/ui/components'
import TSourceBadge from '../../../src/components/display/TSourceBadge.vue'
import THint from '../../../src/components/display/THint.vue'

describe('THint', () => {
  it('renders a trigger button carrying the type modifier class', () => {
    const wrapper = mount(THint, { props: { type: 'help', content: 'hi' } })
    const btn = wrapper.find('button.t-hint')
    expect(btn.exists()).toBe(true)
    expect(btn.classes()).toContain('t-hint--help')
  })

  it('maps type → default glyph (info default, help = question mark, warning = exclamation)', () => {
    expect(mount(THint, {}).findComponent(TSvgIcon).props('icon')).toBe('mdi:information-outline')
    expect(mount(THint, { props: { type: 'help' } }).findComponent(TSvgIcon).props('icon')).toBe(
      'mdi:help-circle-outline',
    )
    expect(mount(THint, { props: { type: 'warning' } }).findComponent(TSvgIcon).props('icon')).toBe(
      'mdi:alert-circle-outline',
    )
  })

  it('icon prop overrides the type glyph', () => {
    const wrapper = mount(THint, { props: { type: 'warning', icon: 'mdi:shield-alert' } })
    expect(wrapper.findComponent(TSvgIcon).props('icon')).toBe('mdi:shield-alert')
  })
})

describe('TSourceBadge', () => {
  // Primary wire form: PascalCase member name string (JsonStringEnumConverter).
  const memberNameCases: Array<[string, string]> = [
    ['FileSystem', 'File'],
    ['Database', 'Database'],
    ['Plugin', 'Plugin'],
    ['Managed', 'Managed'],
    ['Project', 'Project'],
    ['Workspace', 'Workspace'],
    ['Configuration', 'Config'],
    ['EmbeddedResource', 'Built-in'],
  ]
  it.each(memberNameCases)('maps member name %s → label %s', (value, label) => {
    const wrapper = mount(TSourceBadge, { props: { value } })
    expect(wrapper.text()).toContain(label)
  })

  // Legacy numeric compatibility: raw SkillSource ordinals still map correctly.
  const numericCases: Array<[number, string]> = [
    [0, 'File'],
    [1, 'Database'],
    [2, 'Plugin'],
    [3, 'Managed'],
    [4, 'Project'],
  ]
  it.each(numericCases)('maps legacy ordinal %s → label %s', (value, label) => {
    const wrapper = mount(TSourceBadge, { props: { value } })
    expect(wrapper.text()).toContain(label)
  })

  it('falls back to Database for an unknown value', () => {
    const wrapper = mount(TSourceBadge, { props: { value: 'somethingUnknown' } })
    expect(wrapper.text()).toContain('Database')
  })

  it('resolves the label via the translate prop when provided', () => {
    const translate = (key: string) => (key === 'admin.shared.source.filesystem' ? '文件' : '')
    const wrapper = mount(TSourceBadge, { props: { value: 'FileSystem', translate } })
    expect(wrapper.text()).toContain('文件')
  })

  it('honours an explicit label override', () => {
    const wrapper = mount(TSourceBadge, { props: { value: 'Database', label: 'Custom' } })
    expect(wrapper.text()).toContain('Custom')
  })
})

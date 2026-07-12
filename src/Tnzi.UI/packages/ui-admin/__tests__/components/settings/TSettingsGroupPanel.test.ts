import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import type { SettingsCenterFieldDto, SettingsCenterGroupDto } from '@tnzi/core/services/system'
import TSettingsGroupPanel from '../../../src/components/settings/TSettingsGroupPanel.vue'

function makeGroup(fields: Partial<SettingsCenterFieldDto>[]): SettingsCenterGroupDto {
  return {
    key: 'demo',
    moduleName: 'Demo',
    displayName: 'Demo Group',
    order: 0,
    canEdit: true,
    fields: fields.map((f) => ({
      key: 'Demo:X',
      label: 'X',
      type: 'String',
      isEncrypted: false,
      isReadOnly: false,
      isRequired: false,
      isOverridden: false,
      isSet: false,
      ...f,
    })) as SettingsCenterFieldDto[],
  }
}

function mountPanel(group: SettingsCenterGroupDto) {
  const saveGroup = vi.fn(async () => group)
  const resetGroup = vi.fn(async () => group)
  const wrapper = mount(TSettingsGroupPanel, {
    props: { group, saveGroup, resetGroup },
  })
  return { wrapper, saveGroup }
}

describe('TSettingsGroupPanel client-side validation', () => {
  it('marks required fields with an asterisk', () => {
    const { wrapper } = mountPanel(makeGroup([{ key: 'Demo:R', label: 'R', isRequired: true }]))
    expect(wrapper.find('.t-settings-field__required').exists()).toBe(true)
  })

  it('blocks save and shows an inline error when a required field is cleared', async () => {
    const group = makeGroup([{ key: 'Demo:R', label: 'R', isRequired: true, value: 'was-set' }])
    const { wrapper, saveGroup } = mountPanel(group)

    await wrapper.find('input').setValue('')
    await (wrapper.vm as unknown as { onSave: () => Promise<void> }).onSave()

    expect(saveGroup).not.toHaveBeenCalled()
    expect(wrapper.find('.t-settings-field__error').exists()).toBe(true)
  })

  it('blocks save when a pattern-constrained value does not match', async () => {
    const group = makeGroup([{ key: 'Demo:Url', label: 'Url', pattern: 'https?://.+' }])
    const { wrapper, saveGroup } = mountPanel(group)

    await wrapper.find('input').setValue('not-a-url')
    await (wrapper.vm as unknown as { onSave: () => Promise<void> }).onSave()

    expect(saveGroup).not.toHaveBeenCalled()
    expect(wrapper.find('.t-settings-field__error').exists()).toBe(true)
  })

  it('saves when the pattern matches and clears the error on input', async () => {
    const group = makeGroup([{ key: 'Demo:Url', label: 'Url', pattern: 'https?://.+' }])
    const { wrapper, saveGroup } = mountPanel(group)

    await wrapper.find('input').setValue('https://ok.example')
    await (wrapper.vm as unknown as { onSave: () => Promise<void> }).onSave()

    expect(saveGroup).toHaveBeenCalledWith('demo', { 'Demo:Url': 'https://ok.example' })
    expect(wrapper.find('.t-settings-field__error').exists()).toBe(false)
  })
})

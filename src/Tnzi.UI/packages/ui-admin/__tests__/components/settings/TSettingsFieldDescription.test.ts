import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import type { SettingsCenterFieldDto } from '@tnzi/core/services/system'
import TSettingsField from '../../../src/components/settings/TSettingsField.vue'
import { useAdminAppStore } from '../../../src/stores/useAdminAppStore'

/**
 * 设置项描述的 i18n 解析。
 *
 * ★ 描述的 i18n 键是从**标签键派生**的（+`Desc`），后端不带第二个特性字段。
 * 这一组守的是那条派生规则以及它的回退：键不存在时必须原样显示后端的英文描述 ——
 * 那是全部部署今天的行为，回退一旦失灵就会在界面上渲染出一个 i18n 键。
 */
function makeField(overrides: Partial<SettingsCenterFieldDto>): SettingsCenterFieldDto {
  return {
    key: 'Demo:X',
    label: 'X',
    type: 'String',
    isEncrypted: false,
    isReadOnly: false,
    isRequired: false,
    isOverridden: false,
    isSet: false,
    ...overrides,
  } as SettingsCenterFieldDto
}

function hintOf(field: SettingsCenterFieldDto): string {
  const wrapper = mount(TSettingsField, { props: { field, value: null } })
  const hints = wrapper.findAll('.t-settings-field__hint')
  return hints.length > 0 ? hints[hints.length - 1]!.text() : ''
}

describe('TSettingsField description i18n', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('★ falls back to the backend text when no locale entry exists', () => {
    const hint = hintOf(
      makeField({
        i18nKey: 'admin.modules.system.settings.fields.thisKeyDoesNotExistAnywhere',
        description: 'Backend supplied English description',
      }),
    )
    expect(hint).toBe('Backend supplied English description')
  })

  it('falls back to the backend text when the field carries no i18n key at all', () => {
    const hint = hintOf(makeField({ i18nKey: null, description: 'No key here' }))
    expect(hint).toBe('No key here')
  })

  it('renders nothing when the backend supplied no description', () => {
    const wrapper = mount(TSettingsField, {
      props: { field: makeField({ description: null }), value: null },
    })
    expect(wrapper.find('.t-settings-field__hint').exists()).toBe(false)
  })

  /**
   * ★★ 这一条是真正证明「派生规则会被解析」的用例。
   * 只测回退是不够的：把派生那行删掉，纯回退的用例照样全绿 —— 实测过。
   */
  it('★ resolves {labelKey}Desc from the dictionary when an entry exists', () => {
    useAdminAppStore().extendLocaleMessages({
      en: {
        admin: {
          modules: {
            system: {
              settings: { fields: { demoDerived: 'Label', demoDerivedDesc: 'TRANSLATED' } },
            },
          },
        },
      },
    })

    const hint = hintOf(
      makeField({
        i18nKey: 'admin.modules.system.settings.fields.demoDerived',
        description: 'Backend English',
      }),
    )

    expect(hint).toBe('TRANSLATED')
  })

  it('★ uses the Desc key, never the label key', () => {
    useAdminAppStore().extendLocaleMessages({
      en: {
        admin: {
          modules: {
            system: { settings: { fields: { demoLabelOnly: 'THE LABEL' } } },
          },
        },
      },
    })

    // 只有标签键存在、没有 Desc 键 → 必须回退到后端描述，而不是把标签当描述显示。
    const hint = hintOf(
      makeField({
        i18nKey: 'admin.modules.system.settings.fields.demoLabelOnly',
        description: 'Backend English',
      }),
    )

    expect(hint).toBe('Backend English')
  })
})

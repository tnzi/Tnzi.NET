import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { NPopconfirm } from 'naive-ui'
import type { SettingsCenterGroupDto } from '@tnzi/core/services/system'
import type { AdminSettingsConfig } from '../../../src/plugin/settingsConfig'

const getDefinitions = vi.fn()
const saveGroup = vi.fn()
const resetGroup = vi.fn()
// Parameters.vue (the lazy "advanced" section) consumes bridge.settings.* — the
// mock must stay complete so activating that section never throws.
const settingsFetch = vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 }))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))
vi.mock('../../../src/services/bridges/system-bridge', () => ({
  createSystemBridge: () => ({
    settingsCenter: { getDefinitions, saveGroup, resetGroup },
    settings: {
      fetch: settingsFetch,
      create: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
    },
    menus: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn(), reorder: vi.fn() },
    accessLogs: { fetch: vi.fn() },
    scheduledJobs: { fetch: vi.fn(), trigger: vi.fn() },
  }),
}))

// Imports of mocked-module consumers must come AFTER the vi.mock calls so the
// factories see initialised spies (same ordering as AgentDetail.test.ts).
import TSettingsPage from '../../../src/components/settings/TSettingsPage.vue'
import TDetailLayout from '../../../src/components/detail/TDetailLayout.vue'
import { ADMIN_SETTINGS_CONFIG_KEY } from '../../../src/plugin/settingsConfig'

const demoGroup: SettingsCenterGroupDto = {
  key: 'system-general',
  moduleName: 'System',
  displayName: 'General',
  i18nKey: null,
  description: null,
  icon: 'mdi:web',
  order: 0,
  fields: [
    {
      key: 'System:SiteName', label: 'Site Name', i18nKey: null, description: null, type: 'String',
      isEncrypted: false, isReadOnly: false, isRequired: false, min: null, max: null, options: null,
      value: 'Tnzi.NET', defaultValue: 'Tnzi.NET', isOverridden: false, isSet: false,
    },
    {
      key: 'System:BoolFlag', label: 'Bool Flag', i18nKey: null, description: null, type: 'Boolean',
      isEncrypted: false, isReadOnly: false, isRequired: false, min: null, max: null, options: null,
      value: 'false', defaultValue: 'false', isOverridden: false, isSet: false,
    },
  ],
}

const clone = <T>(v: T): T => structuredClone(v)

// Minimal complete field DTO factory for the search/subsection/duration cases.
function makeField(
  over: Partial<SettingsCenterGroupDto['fields'][number]> & { key: string; label: string },
): SettingsCenterGroupDto['fields'][number] {
  return {
    key: over.key, label: over.label, i18nKey: null, description: null, type: 'String',
    isEncrypted: false, isReadOnly: false, isRequired: false, min: null, max: null, options: null,
    subsection: null, value: null, defaultValue: null, isOverridden: false, isSet: false, ...over,
  }
}

const budgetGroup: SettingsCenterGroupDto = {
  key: 'ai-budget', moduleName: 'AI', displayName: 'Budget', i18nKey: null, description: null,
  icon: null, order: 1,
  fields: [makeField({ key: 'AI:Budget:CacheTtl', label: 'Cache TTL' })],
}

const retryGroup: SettingsCenterGroupDto = {
  key: 'ai-retry', moduleName: 'AI', displayName: 'Retry', i18nKey: null, description: null,
  icon: null, order: 2,
  fields: [
    makeField({ key: 'AI:Retry:BaseDelay', label: 'Base Delay', type: 'Duration', value: '00:00:05' }),
    makeField({ key: 'AI:Retry:MaxJitter', label: 'Max Jitter', type: 'Duration', subsection: 'Backoff' }),
  ],
}

function findButton(wrapper: ReturnType<typeof mount>, label: string) {
  return wrapper.findAll('button').find((b) => b.text() === label)
}

describe('Settings page (TSettingsPage integration)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    getDefinitions.mockReset()
    saveGroup.mockReset()
    resetGroup.mockReset()
    getDefinitions.mockResolvedValue([clone(demoGroup)])
    // Echo the saved overrides back as the server would: changed keys become
    // overridden with the new value.
    saveGroup.mockImplementation(async (_groupKey: string, changed: Record<string, string | null>) => ({
      ...clone(demoGroup),
      fields: demoGroup.fields.map((f) =>
        f.key in changed
          ? { ...clone(f), value: changed[f.key], isOverridden: true, isSet: true }
          : clone(f),
      ),
    }))
    resetGroup.mockResolvedValue(clone(demoGroup))
  })

  it('loads definitions on mount and renders the group nav + fields', async () => {
    const wrapper = mount(TSettingsPage)
    await flushPromises()

    expect(getDefinitions).toHaveBeenCalledTimes(1)
    // Group label in the left nav / active panel title.
    expect(wrapper.text()).toContain('General')
    // First schema group auto-activates and renders its fields.
    expect(wrapper.text()).toContain('Site Name')
    expect(wrapper.text()).toContain('Bool Flag')
  })

  it('saves only changed fields and shows the Modified tag from the response', async () => {
    const wrapper = mount(TSettingsPage)
    await flushPromises()
    expect(wrapper.text()).not.toContain('Modified')

    // The only <input> in the group panel is the String field (Boolean → NSwitch).
    const input = wrapper.find('.t-settings-group input')
    expect(input.exists()).toBe(true)
    await input.setValue('New')

    const save = findButton(wrapper, 'Save')
    expect(save).toBeTruthy()
    await save!.trigger('click')
    await flushPromises()

    expect(saveGroup).toHaveBeenCalledTimes(1)
    expect(saveGroup.mock.calls[0]?.[0]).toBe('system-general')
    // BoolFlag is untouched — payload contains the changed field only.
    expect(saveGroup.mock.calls[0]?.[1]).toEqual({ 'System:SiteName': 'New' })

    // The resolved group (isOverridden: true) re-hydrates the panel.
    expect(wrapper.text()).toContain('Modified')
  })

  it('discard restores the original value without calling saveGroup', async () => {
    const wrapper = mount(TSettingsPage)
    await flushPromises()

    const input = wrapper.find('.t-settings-group input')
    await input.setValue('Changed')

    const discard = findButton(wrapper, 'Discard')
    expect(discard).toBeTruthy()
    await discard!.trigger('click')

    expect((wrapper.find('.t-settings-group input').element as HTMLInputElement).value).toBe('Tnzi.NET')
    expect(saveGroup).not.toHaveBeenCalled()
  })

  it('restore defaults calls resetGroup for the active group', async () => {
    const wrapper = mount(TSettingsPage)
    await flushPromises()

    // Drive the NPopconfirm confirmation the way its action button would —
    // emitting positive-click invokes the @positive-click="onReset" binding
    // without simulating the teleported popover in happy-dom.
    const popconfirm = wrapper.findComponent(NPopconfirm)
    expect(popconfirm.exists()).toBe(true)
    popconfirm.vm.$emit('positive-click')
    await flushPromises()

    expect(resetGroup).toHaveBeenCalledTimes(1)
    expect(resetGroup.mock.calls[0]?.[0]).toBe('system-general')
  })

  it('renders consumer-provided custom sections and switches to them', async () => {
    const config: AdminSettingsConfig = {
      sections: [
        {
          key: 'biz',
          label: 'Biz Section',
          component: { template: '<div class="biz-panel">biz</div>' },
        },
      ],
    }
    const wrapper = mount(TSettingsPage, {
      global: { provide: { [ADMIN_SETTINGS_CONFIG_KEY]: config } },
    })
    await flushPromises()

    // The custom section label is only in the left nav until activated.
    expect(wrapper.text()).toContain('Biz Section')
    expect(wrapper.find('.biz-panel').exists()).toBe(false)

    const layout = wrapper.findComponent(TDetailLayout)
    ;(layout.vm as unknown as { onSection: (k: string) => void }).onSection('custom:biz')
    await flushPromises()

    expect(wrapper.find('.biz-panel').exists()).toBe(true)
  })

  it('hideGroups removes the built-in schema group from the nav', async () => {
    const config: AdminSettingsConfig = { hideGroups: ['system-general'] }
    const wrapper = mount(TSettingsPage, {
      global: { provide: { [ADMIN_SETTINGS_CONFIG_KEY]: config } },
    })
    await flushPromises()

    expect(wrapper.text()).not.toContain('General')
    expect(wrapper.text()).not.toContain('Site Name')
    // The advanced parameters section is still offered.
    expect(wrapper.text()).toContain('Parameters')
  })

  it('global search filters the left nav to matching groups (deep-link engine keeps full list)', async () => {
    getDefinitions.mockResolvedValue([clone(demoGroup), clone(budgetGroup)])
    const wrapper = mount(TSettingsPage)
    await flushPromises()

    const layout = wrapper.findComponent(TDetailLayout)
    const keys = () => (layout.props('sections') as { key: string }[]).map((s) => s.key)
    expect(keys()).toContain('system-general')
    expect(keys()).toContain('ai-budget')

    // Type into the nav-header search box → only the matching group stays in the nav.
    const search = wrapper.find('.t-detail-layout__nav-header input')
    expect(search.exists()).toBe(true)
    await search.setValue('Budget')
    await flushPromises()

    expect(keys()).toEqual(['ai-budget'])
    // Clearing restores the full nav.
    await search.setValue('')
    await flushPromises()
    expect(keys()).toContain('system-general')
    expect(keys()).toContain('ai-budget')
  })

  it('renders subsection fields in a collapsible section and duration inputs with a format hint', async () => {
    getDefinitions.mockResolvedValue([clone(retryGroup)])
    const wrapper = mount(TSettingsPage)
    await flushPromises()

    // Default-area duration field.
    expect(wrapper.text()).toContain('Base Delay')
    // Duration placeholder hint.
    expect(wrapper.find('.t-settings-group input[placeholder="d.hh:mm:ss / hh:mm:ss"]').exists()).toBe(true)
    // Subsection title + its field (NCollapse default-expanded).
    expect(wrapper.text()).toContain('Backoff')
    expect(wrapper.text()).toContain('Max Jitter')
  })

  it('rejects an invalid duration client-side without calling saveGroup', async () => {
    getDefinitions.mockResolvedValue([clone(retryGroup)])
    const wrapper = mount(TSettingsPage)
    await flushPromises()

    const input = wrapper.find('.t-settings-group input[placeholder="d.hh:mm:ss / hh:mm:ss"]')
    await input.setValue('not-a-duration')

    const save = findButton(wrapper, 'Save')
    await save!.trigger('click')
    await flushPromises()

    expect(saveGroup).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('valid duration')
  })

  it('still renders the page (advanced section intact) when getDefinitions rejects', async () => {
    getDefinitions.mockRejectedValueOnce(new Error('boom'))
    const wrapper = mount(TSettingsPage)
    await flushPromises()

    expect(wrapper.find('.t-detail-layout').exists()).toBe(true)
    // No schema groups, but the advanced parameters entry remains navigable.
    expect(wrapper.text()).toContain('Parameters')
    expect(wrapper.text()).not.toContain('General')
  })
})

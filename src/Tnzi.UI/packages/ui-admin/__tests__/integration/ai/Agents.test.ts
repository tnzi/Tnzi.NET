import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { EMPTY_DASH } from '../../../src/utils/placeholders'
import { translatePageKey } from '../../../src/pages/_shared/translate'

/**
 * Agents integration test - production-grade card page (TCardPage) with a health
 * KPI strip, per-card tier badges, clone, and a drill-in detail route.
 *
 * Mocks the ai-bridge boundary (NOT @tnzi/core/services/ai directly - the page
 * convention mocks at the bridge so pages stay decoupled from API factory
 * shapes) + vue-router's useRouter (push spy for the View action). Mirrors the
 * Personas.test.ts card-grid pattern. Stub naming uses the un-prefixed Naive
 * component names (`Card`, not `NCard`).
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

const agentFetch = vi.fn(async () => ({
  items: [
    {
      id: 'a1',
      name: 'Writer',
      description: 'Generates marketing copy',
      provider: 'openai',
      model: 'gpt-4o',
      isEnabled: true,
      qualityTier: 2,
      latencyTier: 1,
      costTier: 2,
      executionMode: 0,
      persona: 'You speak with empathy.',
      creationTime: '2026-04-01T00:00:00Z',
    },
    {
      id: 'a2',
      name: 'Reviewer',
      description: 'Reviews drafts',
      provider: 'anthropic',
      model: 'claude-3-5-sonnet',
      isEnabled: false,
      qualityTier: 3,
      latencyTier: 2,
      costTier: 3,
      executionMode: 2,
      persona: null,
      creationTime: '2026-04-02T00:00:00Z',
    },
  ],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 20,
}))
const getHealth = vi.fn(async () => ({
  totalAgents: 2,
  healthyAgents: 1,
  unhealthyAgents: 1,
  disabledAgents: 1,
  unhealthyDetails: [],
}))
const agentClone = vi.fn(async (id: string) => ({ id: `${id}-copy`, name: 'Writer (copy)' }))
const agentUpdate = vi.fn(async (id: string, data: unknown) => ({ id, ...(data as object) }))
const getProviderOptions = vi.fn(async () => [
  { id: 'p1', name: 'DeepSeek', providerType: 'deepseek', defaultModel: 'deepseek-v4-pro' },
])

vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    agents: {
      fetch: agentFetch,
      getHealth,
      clone: agentClone,
      create: vi.fn(async (data: unknown) => ({ id: 'a3', ...(data as object) })),
      update: agentUpdate,
      delete: vi.fn(async () => undefined),
    },
    providers: {
      getOptions: getProviderOptions,
    },
  }),
}))

const routerPush = vi.fn(() => Promise.resolve())
vi.mock('vue-router', () => ({
  useRouter: () => ({ push: routerPush }),
  useRoute: () => ({ query: {}, params: {}, meta: {}, path: '/admin/ai/agents' }),
}))

import Agents from '../../../src/pages/ai/agents/Agents.vue'

const stubs = {
  Card: { name: 'Card', props: ['title'], template: '<div class="n-card-stub"><slot /></div>' },
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  Pagination: { name: 'Pagination', template: '<div class="n-pagination-stub" />' },
  Tag: { name: 'Tag', props: ['type'], template: '<span class="n-tag-stub"><slot /></span>' },
  Input: {
    name: 'Input',
    props: ['value'],
    emits: ['update:value'],
    template: '<input class="n-input-stub" :value="value" @input="$emit(\'update:value\', $event.target.value)" />',
  },
  Select: { name: 'Select', props: ['value', 'options'], emits: ['update:value'], template: '<select class="n-select-stub" />' },
  Button: { name: 'Button', props: ['loading'], template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: {
    name: 'Modal',
    props: ['show'],
    emits: ['update:show'],
    template: '<div v-if="show" class="n-modal-stub"><slot /><slot name="footer" /></div>',
  },
  Popover: { name: 'Popover', template: '<div><slot name="trigger" /><slot /></div>' },
  Popconfirm: {
    name: 'Popconfirm',
    emits: ['positive-click'],
    template: '<div><slot name="trigger" /><slot /></div>',
  },
  Checkbox: { name: 'Checkbox', template: '<input type="checkbox" />' },
  Form: { name: 'Form', template: '<form><slot /></form>' },
  FormItem: { name: 'FormItem', template: '<div class="form-item"><slot /></div>' },
}

interface AgentRow { id: string; name: string }
interface AgentsVm {
  openDetail: (item: AgentRow) => void
  cloneOne: (item: AgentRow) => Promise<void>
}

describe('Agents page (production card grid)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    agentFetch.mockClear()
    getHealth.mockClear()
    agentClone.mockClear()
    routerPush.mockClear()
  })

  it('mounts and fetches agents + health on mount', async () => {
    mount(Agents, { global: { stubs } })
    await flushPromises()
    expect(agentFetch).toHaveBeenCalledTimes(1)
    expect(getHealth).toHaveBeenCalledTimes(1)
  })

  it('renders one card per agent', async () => {
    const wrapper = mount(Agents, { global: { stubs } })
    await flushPromises()
    expect(wrapper.findAll('.t-entity-card')).toHaveLength(2)
  })

  it('cards show agent names and the page title', async () => {
    const wrapper = mount(Agents, { global: { stubs } })
    await flushPromises()
    expect(wrapper.text()).toContain('Writer')
    expect(wrapper.text()).toContain('Reviewer')
    expect(wrapper.text()).toContain('Agents')
  })

  it('renders the health KPI strip from getHealth', async () => {
    const wrapper = mount(Agents, { global: { stubs } })
    await flushPromises()
    // 4 KPI cards (total/healthy/unhealthy/disabled) - values 2/1/1/1.
    expect(wrapper.findAll('.ai-agent-page__kpi')).toHaveLength(4)
    expect(wrapper.text()).toContain('Total')
  })

  // A refused or failed health read is not a fleet of zero agents with none
  // unhealthy: the strip shows dashes instead of a reassuring row of zeros.
  it('★ a health summary that could not be read shows dashes, not zeros', async () => {
    getHealth.mockRejectedValueOnce(new Error('Forbidden'))
    const wrapper = mount(Agents, { global: { stubs } })
    await flushPromises()
    const values = wrapper.findAll('.ai-agent-page__kpi .t-stat-card__value').map((v) => v.text())
    expect(values).toEqual([EMPTY_DASH, EMPTY_DASH, EMPTY_DASH, EMPTY_DASH])
  })

  it('View calls router.push to the agent detail route', async () => {
    const wrapper = mount(Agents, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as AgentsVm
    vm.openDetail({ id: 'a1', name: 'Writer' })
    // By NAME so the link follows any basePath / history-base prefix.
    expect(routerPush).toHaveBeenCalledWith({ name: 'ai.agents.detail', params: { id: 'a1' } })
  })

  // The list row carries grants, persona and execution config that the modal
  // never shows. Sending them back reverted whatever the detail page (or
  // another admin) had changed since the list loaded.
  it('the Edit modal sends the fields it shows, not the whole list row', async () => {
    agentUpdate.mockClear()
    const wrapper = mount(Agents, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      crud: {
        openEdit: (row: unknown) => void
        submit: () => Promise<unknown>
        formModal: { formData: { value: Record<string, unknown> } }
      }
    }
    vm.crud.openEdit({
      id: 'a1', name: 'Writer', description: 'Old', provider: 'openai', model: 'gpt-4o', isEnabled: true,
      executionMode: 0, instructions: 'Be brief', temperature: 0.7, maxTokens: 2048, timeoutSeconds: 60,
      skillSlugs: ['stale-skill'], knowledgeBaseIds: ['kb-1'], toolGroups: ['web'], persona: 'Stale persona',
      executionConfig: { maxRounds: 3 }, domains: ['writing'], roles: ['writer'],
      qualityTier: 3, latencyTier: 3, costTier: 3, creationTime: '2026-04-01T00:00:00Z',
    })
    vm.crud.formModal.formData.value.description = 'Generates release notes'
    await vm.crud.submit()

    expect(agentUpdate).toHaveBeenCalledTimes(1)
    const dto = agentUpdate.mock.calls[0]![1] as Record<string, unknown>
    expect(dto.description).toBe('Generates release notes')
    expect(dto.name).toBe('Writer')
    for (const notShown of ['skillSlugs', 'knowledgeBaseIds', 'toolGroups', 'persona', 'executionConfig', 'domains', 'roles', 'qualityTier', 'id', 'creationTime']) {
      expect(dto).not.toHaveProperty(notShown)
    }
  })

  // The runtime resolves an agent's provider by the configured provider's NAME.
  // A built-in list of type slugs ("openai", "anthropic") matches none of those
  // names unless one happens to be configured under it, so an agent created
  // from it saved fine and failed at run time.
  it('the provider field offers the configured provider names', async () => {
    const wrapper = mount(Agents, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as { agentFormSchemaWithProviders: Array<{ key: string; type: string; options?: Array<{ value: unknown }> }> }
    const field = vm.agentFormSchemaWithProviders.find((f) => f.key === 'provider')!
    expect(field.type).toBe('select')
    expect(field.options?.map((o) => o.value)).toEqual(['DeepSeek'])
  })

  it('the provider hints ask for a configured provider name, not a type slug', () => {
    for (const key of ['form.providerPlaceholder', 'detail.providerPlaceholder']) {
      expect(translatePageKey('ai.agents', key)).not.toMatch(/openai|anthropic/i)
    }
  })

  it('with no readable provider list the provider is typed, never picked from a guessed list', async () => {
    getProviderOptions.mockRejectedValueOnce(new Error('Forbidden'))
    const wrapper = mount(Agents, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      agentFormSchemaWithProviders: Array<{ key: string; type: string; options?: unknown[]; placeholderKey?: string }>
      searchFields: Array<{ key: string; type: string; options?: unknown[] }>
    }
    const field = vm.agentFormSchemaWithProviders.find((f) => f.key === 'provider')!
    expect(field.type).toBe('text')
    expect(field.options ?? []).toEqual([])
    expect(field.placeholderKey).toBe('form.providerPlaceholder')
    const filter = vm.searchFields.find((f) => f.key === 'provider')!
    expect(filter.type).toBe('text')
    expect(filter.options ?? []).toEqual([])
  })

  it('Clone calls bridge.agents.clone and refreshes', async () => {
    const wrapper = mount(Agents, { global: { stubs } })
    await flushPromises()
    agentFetch.mockClear()
    getHealth.mockClear()
    const vm = wrapper.vm as unknown as AgentsVm
    await vm.cloneOne({ id: 'a1', name: 'Writer' })
    await flushPromises()
    expect(agentClone).toHaveBeenCalledWith('a1')
    expect(agentFetch).toHaveBeenCalled()
  })
})

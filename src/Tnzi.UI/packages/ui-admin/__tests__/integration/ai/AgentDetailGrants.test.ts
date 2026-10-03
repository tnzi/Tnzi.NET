import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { NSwitch } from 'naive-ui'

/**
 * AgentDetail resource sections x agent grants.
 *
 * The agent's `skillSlugs` / `toolGroups` / `knowledgeBaseIds` carry ENABLED
 * grants only; the grant list adds disabled ones and the grant ids. These tests
 * pin the behaviour that depends on the difference: a switched-off grant is still
 * shown (and can be switched back on), removing it deletes it by id instead of
 * sending an agent update it is not part of, the write affordances follow
 * `ai.agent.update`, and a failed grant-list read never renders a switch whose
 * state the page does not know.
 */

const AGENT = {
  id: 'agent-1',
  name: 'Writer',
  provider: 'openai',
  model: 'gpt-4o',
  isEnabled: true,
  executionMode: 0,
  qualityTier: 2,
  latencyTier: 1,
  costTier: 2,
  persona: null,
  creationTime: '2026-04-01T00:00:00Z',
  skillSlugs: ['write'],
  toolGroups: ['web'],
  knowledgeBaseIds: [] as string[],
}

const GRANTS = {
  toolGroups: [{ id: 'g-web', key: 'web', isEnabled: true, priority: 0 }],
  toolNames: [],
  skills: [
    { id: 'g-write', key: 'write', isEnabled: true, priority: 0 },
    { id: 'g-old', key: 'old', isEnabled: false, priority: 0 },
  ],
  knowledgeBases: [],
}

const getByIdMock = vi.fn(async () => ({ ...AGENT }))
const updateMock = vi.fn(async (_id: string, data: object) => ({ ...AGENT, ...data }))
const emptyPage = { items: [], totalCount: 0, pageIndex: 1, pageSize: 100 }
const skillsFetchMock = vi.fn(async () => ({
  items: [
    { id: 's1', slug: 'write', name: 'Write', enabled: true },
    { id: 's2', slug: 'old', name: 'Old Skill', enabled: true },
    { id: 's3', slug: 'fresh', name: 'Fresh', enabled: true },
  ],
  totalCount: 3,
  pageIndex: 1,
  pageSize: 100,
}))

const grantMock = {
  listForAgent: vi.fn(async () => structuredClone(GRANTS)),
  usedBy: vi.fn(async () => [] as unknown[]),
  setEnabled: vi.fn(async () => undefined),
  remove: vi.fn(async () => undefined),
}

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/agent-grant-bridge', () => ({ createAgentGrantBridge: () => grantMock }))
vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    agents: {
      getById: getByIdMock,
      update: updateMock,
      getToolGroups: vi.fn(async () => [{ name: 'web', toolCount: 2, toolNames: ['web_search', 'web_fetch'] }]),
      getVersions: vi.fn(async () => ({ ...emptyPage })),
      validate: vi.fn(),
      getMemory: vi.fn(async () => ({ ...emptyPage })),
    },
    agentRuns: { fetch: vi.fn(async () => ({ ...emptyPage })) },
    providers: { getOptions: vi.fn(async () => []), listModels: vi.fn(async () => ({ models: [] })) },
    knowledge: { fetch: vi.fn(async () => ({ ...emptyPage })) },
    skills: { fetch: skillsFetchMock },
  }),
}))
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { id: 'agent-1' }, query: {} }),
  useRouter: () => ({
    replace: vi.fn(async () => undefined),
    push: vi.fn(async () => undefined),
    resolve: (to: { name: string }) => ({ path: `/admin/${String(to.name).replace(/\./g, '/')}` }),
  }),
  RouterLink: { name: 'RouterLink', props: ['to'], template: '<a><slot /></a>' },
}))

import AgentDetail from '../../../src/pages/ai/agents/AgentDetail.vue'
import AgentResourcePicker from '../../../src/pages/ai/agents/sections/AgentResourcePicker.vue'
import { useAdminAuthStore } from '../../../src/stores/useAdminAuthStore'

async function openSkills(): Promise<VueWrapper> {
  const wrapper = mount(AgentDetail)
  await flushPromises()
  ;(wrapper.vm as unknown as { setSection: (k: string) => void }).setSection('skills')
  await flushPromises()
  return wrapper
}

function picker(wrapper: VueWrapper) {
  return wrapper.findComponent(AgentResourcePicker)
}

describe('AgentDetail - grant states in the resource sections', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    grantMock.listForAgent.mockImplementation(async () => structuredClone(GRANTS))
  })

  it('lists a switched-off grant next to the active one, each with its switch', async () => {
    const wrapper = await openSkills()
    expect(grantMock.listForAgent).toHaveBeenCalledWith('agent-1')

    const items = picker(wrapper).props('assigned') as Array<{ value: string; enabled?: boolean }>
    expect(items.map((i) => [i.value, i.enabled])).toEqual([
      ['write', true],
      ['old', false],
    ])
    expect(wrapper.text()).toContain('Switched off')
    expect(wrapper.findAllComponents(NSwitch)).toHaveLength(2)
    // A granted-but-disabled skill is not offered again in the Add list.
    const available = picker(wrapper).props('available') as Array<{ value: string }>
    expect(available.map((i) => i.value)).toEqual(['fresh'])
  })

  it('switching a grant back on addresses it by grant id, then reloads agent and grants', async () => {
    const wrapper = await openSkills()
    getByIdMock.mockClear()
    grantMock.listForAgent.mockClear()

    const off = wrapper.findAllComponents(NSwitch)[1]!
    off.vm.$emit('update:value', true)
    await flushPromises()

    expect(grantMock.setEnabled).toHaveBeenCalledWith('skill', 'g-old', true)
    expect(getByIdMock).toHaveBeenCalledTimes(1)
    expect(grantMock.listForAgent).toHaveBeenCalledTimes(1)
    expect(updateMock).not.toHaveBeenCalled()
  })

  it('removing a switched-off grant deletes it by id; removing an active one updates the agent', async () => {
    const wrapper = await openSkills()

    picker(wrapper).vm.$emit('remove', 'old')
    await flushPromises()
    expect(grantMock.remove).toHaveBeenCalledWith('skill', 'g-old')
    expect(updateMock).not.toHaveBeenCalled()

    picker(wrapper).vm.$emit('remove', 'write')
    await flushPromises()
    expect(updateMock).toHaveBeenCalledWith('agent-1', { skillSlugs: [] })
    expect(grantMock.remove).toHaveBeenCalledTimes(1)
  })

  it('assigning sends the active list plus the new key (the switched-off grant is not part of it)', async () => {
    const wrapper = await openSkills()
    picker(wrapper).vm.$emit('assign', 'fresh')
    await flushPromises()
    expect(updateMock).toHaveBeenCalledWith('agent-1', { skillSlugs: ['write', 'fresh'] })
  })

  it('hides every write affordance without ai.agent.update', async () => {
    useAdminAuthStore().setUserInfo({ id: 'op', username: 'viewer', roles: [], permissions: ['ai.agent.view'] } as never)
    const wrapper = await openSkills()

    expect(picker(wrapper).props('canEdit')).toBe(false)
    expect(wrapper.findAllComponents(NSwitch)).toHaveLength(0)
    expect(wrapper.text()).not.toContain('Add skill')
    expect(wrapper.text()).not.toContain('Remove')
    // Reading who else uses a skill stays available to a viewer.
    expect(wrapper.text()).toContain('Used by')
  })

  it('a failed grant-list read shows why and renders no switch rather than a guessed state', async () => {
    grantMock.listForAgent.mockRejectedValue(new Error('grants refused'))
    const wrapper = await openSkills()

    expect(wrapper.text()).toContain('grants refused')
    expect(wrapper.findAllComponents(NSwitch)).toHaveLength(0)
    // Falls back to the agent's active list, so the section is not blank.
    const items = picker(wrapper).props('assigned') as Array<{ value: string; enabled?: boolean }>
    expect(items).toEqual([expect.objectContaining({ value: 'write', enabled: undefined })])
  })
})

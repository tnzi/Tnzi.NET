import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * A switch the create form does not seed renders OFF, while a create that leaves
 * the field out (or a payload adapter that fills it with `?? true`) is enabled
 * on the server. The operator saw "disabled" and created a live agent, MCP
 * server, provider, skill or workflow. Each create form now starts where the
 * server does, enums included.
 */

// Every bridge read answers an empty result that works both as a list and as
// a paged list, so each page mounts without a hand-written mock per method.
function emptyAnswer() {
  return Object.assign([] as unknown[], { items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })
}

function anyBridge(): unknown {
  return new Proxy(
    {},
    {
      get: () =>
        new Proxy(
          {},
          { get: () => vi.fn(async () => emptyAnswer()) },
        ),
    },
  )
}

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))
vi.mock('../../../src/services/bridges/ai-bridge', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../../src/services/bridges/ai-bridge')>()
  return { ...original, createAiBridge: () => anyBridge() }
})
vi.mock('../../../src/services/bridges/agent-grant-bridge', () => ({ createAgentGrantBridge: () => anyBridge() }))
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: {}, query: {} }),
  useRouter: () => ({
    push: vi.fn(async () => undefined),
    replace: vi.fn(async () => undefined),
    resolve: (to: { name: string }) => ({ path: `/admin/${String(to.name)}` }),
  }),
  RouterLink: { name: 'RouterLink', props: ['to'], template: '<a><slot /></a>' },
}))

type CrudVm = {
  crud: {
    openCreate: () => void
    formModal: { formData: { value: Record<string, unknown> | null } }
  }
}

async function createFormOf(load: () => Promise<{ default: unknown }>, crudName = 'crud') {
  const page = (await load()).default
  const wrapper = mount(page as never)
  await flushPromises()
  const crud = (wrapper.vm as unknown as Record<string, CrudVm['crud']>)[crudName]!
  crud.openCreate()
  await flushPromises()
  const data = { ...(crud.formModal.formData.value ?? {}) }
  wrapper.unmount()
  return data
}

describe('AI create forms start where the server does', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('agents: enabled, single execution mode', async () => {
    const data = await createFormOf(() => import('../../../src/pages/ai/agents/Agents.vue'))
    expect(data).toMatchObject({ isEnabled: true, executionMode: 'Single' })
  })

  it('MCP servers: enabled, priority 0', async () => {
    const data = await createFormOf(() => import('../../../src/pages/ai/mcp/McpServers.vue'), 'serverCrud')
    expect(data).toMatchObject({ isEnabled: true, priority: 0 })
  })

  it('providers: enabled, priority 0', async () => {
    const data = await createFormOf(() => import('../../../src/pages/ai/providers/Providers.vue'))
    expect(data).toMatchObject({ isEnabled: true, priority: 0 })
  })

  it('skills: enabled, user scope, priority 0', async () => {
    const data = await createFormOf(() => import('../../../src/pages/ai/skills/Skills.vue'))
    expect(data).toMatchObject({ enabled: true, scope: 'User', priority: 0 })
  })

  it('workflows: enabled, sequential execution mode', async () => {
    const data = await createFormOf(() => import('../../../src/pages/ai/workflows/Workflows.vue'))
    expect(data).toMatchObject({ isEnabled: true, executionMode: 'Sequential' })
  })
})

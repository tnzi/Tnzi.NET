import { describe, it, expect, vi } from 'vitest'
import { createAgentGrantBridge } from '../../../src/services/bridges/agent-grant-bridge'
import type { HttpClient } from '@tnzi/core/http'

// ★ 反向查询的读也必须在拒绝时抛：页面据此显示「被 N 个智能体使用」，
//   拒绝若被读成 [] 就是「没人依赖它」，而操作员下一步正是删掉它。

const REFUSAL = 'Permission denied: ai.agent.view'

const ok = <T>(data: T) => ({ data, succeeded: true, success: true, code: 200, message: '' })
const refused = () => ({ data: null, succeeded: false, success: false, code: 403, message: REFUSAL })

function client(overrides: Partial<Record<'get' | 'post' | 'delete', ReturnType<typeof vi.fn>>> = {}) {
  return {
    get: overrides.get ?? vi.fn(async () => ok([])),
    post: overrides.post ?? vi.fn(async () => ok(null)),
    put: vi.fn(),
    delete: overrides.delete ?? vi.fn(async () => ok(null)),
  } as unknown as HttpClient & { get: ReturnType<typeof vi.fn> }
}

describe('agent-grant-bridge', () => {
  it('usedBy routes each resource type to its reverse endpoint', async () => {
    const c = client()
    const bridge = createAgentGrantBridge({ client: c })
    await bridge.usedBy('skill', 's')
    await bridge.usedBy('knowledge', 'k')
    await bridge.usedBy('tool', 't')
    const urls = c.get.mock.calls.map((call: unknown[]) => call[0])
    expect(urls).toEqual([
      '/admin/agents/grants/reverse/skill',
      '/admin/agents/grants/reverse/knowledge',
      '/admin/agents/grants/reverse/tool',
    ])
  })

  it('usedBy returns the agents on success', async () => {
    const agents = [{ agentId: 'a1', agentName: 'Writer', agentIsEnabled: true }]
    const bridge = createAgentGrantBridge({ client: client({ get: vi.fn(async () => ok(agents)) }) })
    await expect(bridge.usedBy('skill', 's')).resolves.toEqual(agents)
  })

  it('usedBy throws on a refused read instead of answering "no agents"', async () => {
    const bridge = createAgentGrantBridge({ client: client({ get: vi.fn(async () => refused()) }) })
    await expect(bridge.usedBy('knowledge', 'kb')).rejects.toThrow(REFUSAL)
  })

  it('listForAgent throws on a refused read and fills missing groups on success', async () => {
    const refusing = createAgentGrantBridge({ client: client({ get: vi.fn(async () => refused()) }) })
    await expect(refusing.listForAgent('a1')).rejects.toThrow(REFUSAL)

    const partial = createAgentGrantBridge({
      client: client({ get: vi.fn(async () => ok({ skills: [{ id: 'g', key: 's', isEnabled: false, priority: 0 }] })) }),
    })
    await expect(partial.listForAgent('a1')).resolves.toEqual({
      toolGroups: [],
      toolNames: [],
      skills: [{ id: 'g', key: 's', isEnabled: false, priority: 0 }],
      knowledgeBases: [],
    })
  })

  it.each([
    ['setEnabled', (b: ReturnType<typeof createAgentGrantBridge>) => b.setEnabled('skill', 'g1', false)],
    ['remove', (b: ReturnType<typeof createAgentGrantBridge>) => b.remove('tool', 'g1')],
  ])('%s rejects with the backend reason on a refused envelope', async (_name, call) => {
    const bridge = createAgentGrantBridge({
      client: client({ post: vi.fn(async () => refused()), delete: vi.fn(async () => refused()) }),
    })
    await expect(call(bridge)).rejects.toThrow(REFUSAL)
  })

  it('without a client every call rejects rather than pretending success', async () => {
    const bridge = createAgentGrantBridge({})
    await expect(bridge.usedBy('skill', 's')).rejects.toThrow()
    await expect(bridge.setEnabled('skill', 'g', true)).rejects.toThrow()
  })
})

import { describe, it, expect, vi } from 'vitest'
import { createPermissionBridge } from '../../../src/services/bridges/permission-bridge'

// A failed envelope as `normalizeApiResult` produces it for a refused request.
const refused = (message: string) => ({ succeeded: false, success: false, code: 403, message, data: undefined })

function clientAnswering(answer: unknown) {
  return {
    get: vi.fn(async () => answer),
    post: vi.fn(async () => answer),
    put: vi.fn(async () => answer),
    delete: vi.fn(async () => answer),
  }
}

describe('permission bridge reads', () => {
  // `HttpClient` resolves a refusal instead of rejecting; the unchecked unwrap
  // turned it into `undefined`, so the Evaluate tab cleared its result and
  // the rule views read as "no rules", with no error anywhere.
  it('evaluate rejects a refused request with the server message', async () => {
    const bridge = createPermissionBridge({ client: clientAnswering(refused('Forbidden')) as never })
    await expect(bridge.evaluate({ toolName: 'write_file' } as never)).rejects.toThrow('Forbidden')
  })

  it('getRules and getPersistedRules reject a refused read', async () => {
    const bridge = createPermissionBridge({ client: clientAnswering(refused('Forbidden')) as never })
    await expect(bridge.getRules()).rejects.toThrow('Forbidden')
    await expect(bridge.getPersistedRules()).rejects.toThrow('Forbidden')
  })

  it('returns what a successful read carries', async () => {
    const rules = [{ id: 'r1', toolPattern: 'x', behavior: 'Allow', scope: 'System', priority: 1 }]
    const bridge = createPermissionBridge({
      client: clientAnswering({ succeeded: true, success: true, code: 200, data: rules }) as never,
    })
    await expect(bridge.getPersistedRules()).resolves.toEqual(rules)
  })
})

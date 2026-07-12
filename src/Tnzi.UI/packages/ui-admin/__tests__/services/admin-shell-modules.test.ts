import { describe, it, expect, vi } from 'vitest'
import {
  fetchAdminShellModules,
  normalizeModuleName,
} from '../../src/services/admin-shell-modules'

function client(resp: unknown, throws = false) {
  return {
    get: vi.fn(async () => {
      if (throws) throw new Error('network')
      return resp
    }),
  } as unknown as Parameters<typeof fetchAdminShellModules>[0]
}

describe('normalizeModuleName', () => {
  it('lowercases and replaces dots with dashes', () => {
    expect(normalizeModuleName('AI.Skills')).toBe('ai-skills')
    expect(normalizeModuleName('Identity')).toBe('identity')
    expect(normalizeModuleName('ai-skills')).toBe('ai-skills')
  })
})

describe('fetchAdminShellModules', () => {
  it('returns a normalized set of ENABLED module names', async () => {
    const set = await fetchAdminShellModules(
      client({
        data: {
          modules: [
            { name: 'Identity', isEnabled: true },
            { name: 'Finance', isEnabled: true },
            { name: 'AI.Skills', isEnabled: true },
          ],
        },
      }),
    )
    expect(set).toEqual(new Set(['identity', 'finance', 'ai-skills']))
  })

  it('excludes disabled modules', async () => {
    const set = await fetchAdminShellModules(
      client({
        data: {
          modules: [
            { name: 'Identity', isEnabled: true },
            { name: 'Finance', isEnabled: false },
          ],
        },
      }),
    )
    expect(set).toEqual(new Set(['identity']))
  })

  it('returns null on a malformed payload (fail-open)', async () => {
    expect(await fetchAdminShellModules(client({ data: null }))).toBeNull()
    expect(await fetchAdminShellModules(client({ data: {} }))).toBeNull()
    expect(await fetchAdminShellModules(client({}))).toBeNull()
  })

  it('returns null when the request throws (endpoint unavailable / 403)', async () => {
    expect(await fetchAdminShellModules(client(null, true))).toBeNull()
  })

  it('returns an empty set when the signal is known but nothing is enabled', async () => {
    const set = await fetchAdminShellModules(client({ data: { modules: [] } }))
    expect(set).toEqual(new Set())
  })
})

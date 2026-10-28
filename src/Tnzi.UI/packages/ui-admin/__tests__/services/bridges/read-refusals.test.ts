import { describe, it, expect, vi } from 'vitest'
import type { HttpClient } from '@tnzi/core/http'
import { createChannelsBridge } from '../../../src/services/bridges/channels-bridge'
import { createSandboxBridge } from '../../../src/services/bridges/sandbox-bridge'
import { createAiBridge, createWorkspaceAgentsBridge } from '../../../src/services/bridges/ai-bridge'
import { createAuthorizationBridge } from '../../../src/services/bridges/authorization-bridge'
import { createPayrollBridge } from '../../../src/services/bridges/payroll-bridge'
import { readOrAbsent } from '../../../src/services/_mappers'

/**
 * Reads of an optional module's admin endpoints answer "absent" only when the
 * module is absent (404 / 501). A refusal (403) or a server failure (5xx) is a
 * different statement, and the pages that read "absent" as "this module is not
 * loaded" would otherwise tell an operator to install a module that is there.
 * Likewise a list read that is refused rejects instead of reading as empty.
 */

const envelope = (code: number, data: unknown, message = '') => ({
  data,
  succeeded: code < 400,
  success: code < 400,
  code,
  message,
})

function clientAnswering(code: number, data: unknown = null, message = ''): HttpClient {
  const answer = vi.fn(async () => envelope(code, data, message))
  return { get: answer, post: answer, put: answer, delete: answer } as unknown as HttpClient
}

describe('readOrAbsent', () => {
  it('returns the data of a successful envelope', () => {
    expect(readOrAbsent(envelope(200, [1, 2]), [])).toEqual([1, 2])
  })

  it.each([404, 501])('returns the absent value for %s', (code) => {
    expect(readOrAbsent(envelope(code, null, 'Not here'), 'absent')).toBe('absent')
  })

  it.each([400, 403, 500])('throws the server reason for %s', (code) => {
    expect(() => readOrAbsent(envelope(code, null, `Refused ${code}`), 'absent')).toThrow(`Refused ${code}`)
  })

  it('passes a bare payload through', () => {
    expect(readOrAbsent({ enabled: true }, null)).toEqual({ enabled: true })
  })
})

describe('channels bridge reads', () => {
  it('answer empty when the module is not mapped (404)', async () => {
    const bridge = createChannelsBridge({ client: clientAnswering(404) })
    await expect(bridge.channels.getStatus()).resolves.toBeNull()
    await expect(bridge.channels.getAdapters()).resolves.toEqual([])
    await expect(bridge.gateway.getStatus()).resolves.toBeNull()
    await expect(bridge.gateway.getConnections()).resolves.toEqual([])
    await expect(bridge.gateway.getBindings()).resolves.toEqual([])
  })

  it('reject a refused read with the server reason', async () => {
    const bridge = createChannelsBridge({ client: clientAnswering(403, null, 'Forbidden: ai.channel.view') })
    await expect(bridge.channels.getStatus()).rejects.toThrow('Forbidden: ai.channel.view')
    await expect(bridge.channels.getAdapters()).rejects.toThrow('Forbidden: ai.channel.view')
    await expect(bridge.gateway.getStatus()).rejects.toThrow('Forbidden: ai.channel.view')
    await expect(bridge.gateway.getConnections()).rejects.toThrow('Forbidden: ai.channel.view')
    await expect(bridge.gateway.getBindings()).rejects.toThrow('Forbidden: ai.channel.view')
  })
})

describe('sandbox bridge read', () => {
  it('answers null when the module is not mapped (404)', async () => {
    await expect(createSandboxBridge({ client: clientAnswering(404) }).getStatus()).resolves.toBeNull()
  })

  it('rejects a server failure with its reason', async () => {
    const bridge = createSandboxBridge({ client: clientAnswering(500, null, 'Sandbox provider crashed') })
    await expect(bridge.getStatus()).rejects.toThrow('Sandbox provider crashed')
  })

  it('returns the status on success', async () => {
    const bridge = createSandboxBridge({ client: clientAnswering(200, { enabled: true }) })
    await expect(bridge.getStatus()).resolves.toEqual({ enabled: true })
  })
})

describe('workspace agents list', () => {
  it('rejects a refused read instead of reading as no agents', async () => {
    const bridge = createWorkspaceAgentsBridge({ client: clientAnswering(403, null, 'Forbidden: ai.agent.view') })
    await expect(bridge.list()).rejects.toThrow('Forbidden: ai.agent.view')
  })

  it('returns the discovered agents on success', async () => {
    const bridge = createWorkspaceAgentsBridge({ client: clientAnswering(200, [{ agentId: 'w1' }]) })
    await expect(bridge.list()).resolves.toEqual([{ agentId: 'w1' }])
  })
})

/**
 * Array reads behind pages that already say "the read failed" in a catch. With
 * the unchecked unwrap a refusal resolved as an empty list (or undefined), so
 * those catches never ran: a refused entity-role read made the matrix toggle-off
 * a silent no-op while the page reported success, a failed per-module read made
 * the cascade-disable preview count 0 affected functions, and the pay modal of a
 * run offered no payslips instead of the reason.
 */
describe('array reads the pages catch', () => {
  const refused = () => clientAnswering(403, null, 'Forbidden: read')

  it('authorization reads reject a refusal', async () => {
    const bridge = createAuthorizationBridge({ client: refused() })
    await expect(bridge.functionModules.getAll()).rejects.toThrow('Forbidden: read')
    await expect(bridge.functionModules.previewCascade('m1')).rejects.toThrow('Forbidden: read')
    await expect(bridge.permissions.getByModule('m1')).rejects.toThrow('Forbidden: read')
    await expect(bridge.roleFunctions.superAdminRoles()).rejects.toThrow('Forbidden: read')
    await expect(bridge.entityInfos.getAll()).rejects.toThrow('Forbidden: read')
    await expect(bridge.entityRoles.getByEntityInfo('e1')).rejects.toThrow('Forbidden: read')
    await expect(bridge.entityRoles.getForUser('u1')).rejects.toThrow('Forbidden: read')
  })

  it('a matrix toggle whose lookup is refused does not report a change it never made', async () => {
    const write = vi.fn(async () => envelope(200, null))
    const client = {
      get: vi.fn(async () => envelope(403, null, 'Forbidden: read')),
      post: write,
      put: write,
      delete: write,
    } as unknown as HttpClient
    const bridge = createAuthorizationBridge({ client })
    await expect(
      bridge.entityRoles.setCell({ entityInfoId: 'e1', roleId: 'r1', operation: 'Read', enable: false } as never),
    ).rejects.toThrow('Forbidden: read')
    await expect(
      bridge.entityRoles.setCell({ entityInfoId: 'e1', roleId: 'r1', operation: 'Read', enable: true } as never),
    ).rejects.toThrow('Forbidden: read')
    expect(write).not.toHaveBeenCalled()
  })

  it('the cascade preview does not count an unreadable module as empty', async () => {
    const answers: Record<string, unknown> = {
      list: envelope(200, [{ id: 'm1', name: 'Root' }, { id: 'm2', name: 'Child', parentId: 'm1' }]),
    }
    const get = vi.fn(async (url: string) =>
      url.includes('m2') ? envelope(500, null, 'Module read failed') : (answers.list as object),
    )
    const client = { get, post: get, put: get, delete: get } as unknown as HttpClient
    const bridge = createAuthorizationBridge({ client })
    await expect(bridge.functionModules.previewCascade('m1')).rejects.toThrow('Module read failed')
  })

  it('payroll reads reject a refusal', async () => {
    const bridge = createPayrollBridge({ client: refused() })
    await expect(bridge.employees.assignments('e1')).rejects.toThrow('Forbidden: read')
    await expect(bridge.runs.payslips('r1')).rejects.toThrow('Forbidden: read')
    await expect(bridge.runs.inputs('r1')).rejects.toThrow('Forbidden: read')
    await expect(bridge.countryPacks.fetch({ pageIndex: 1, pageSize: 20 } as never)).rejects.toThrow('Forbidden: read')
  })

  it('AI array reads reject a refusal', async () => {
    const bridge = createAiBridge({ client: refused() })
    await expect(bridge.agents.getToolGroups()).rejects.toThrow('Forbidden: read')
    await expect(bridge.skills.search('x')).rejects.toThrow('Forbidden: read')
    await expect(bridge.skills.getPopular(10)).rejects.toThrow('Forbidden: read')
    await expect(bridge.skillCategories.getTree()).rejects.toThrow('Forbidden: read')
    await expect(bridge.skillCategories.getSkills('c1')).rejects.toThrow('Forbidden: read')
    await expect(bridge.knowledge.searchTest('kb1', { query: 'q', topK: 5 } as never)).rejects.toThrow('Forbidden: read')
  })

  it('the same reads still return the list on success', async () => {
    const auth = createAuthorizationBridge({ client: clientAnswering(200, [{ id: 'x', roleId: 'r1', operation: 'Read' }]) })
    await expect(auth.entityRoles.getByEntityInfo('e1')).resolves.toHaveLength(1)
    const payroll = createPayrollBridge({ client: clientAnswering(200, [{ id: 'p1' }]) })
    await expect(payroll.runs.payslips('r1')).resolves.toEqual([{ id: 'p1' }])
    const ai = createAiBridge({ client: clientAnswering(200, [{ name: 'g' }]) })
    await expect(ai.agents.getToolGroups()).resolves.toEqual([{ name: 'g' }])
  })
})

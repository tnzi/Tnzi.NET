import { describe, it, expect, vi } from 'vitest'
import { createIdentityBridge } from '../../../src/services/bridges/identity-bridge'

/**
 * The invitee side of invitations. Anonymous: it is built straight off the
 * HttpClient (the person holding the link has no account yet), so the bridge
 * takes the client and wires `useInvitationApi` itself.
 */
function clientWith(handlers: { get?: unknown; post?: unknown }) {
  return {
    get: vi.fn(handlers.get as never),
    post: vi.fn(handlers.post as never),
    put: vi.fn(),
    delete: vi.fn(),
  } as never
}

describe('identity-bridge invitationAcceptance', () => {
  it('preview returns the preview payload for a usable token', async () => {
    const bridge = createIdentityBridge({
      client: clientWith({
        get: async () => ({ succeeded: true, code: 200, data: { userName: 'newhire', maskedEmail: 'n***@example.com', expiresAt: '2026-09-19T00:00:00Z' } }),
      }),
    })
    const preview = await bridge.invitationAcceptance.preview('tok')
    expect(preview?.userName).toBe('newhire')
  })

  it('preview answers null for a dead link and for a dead network - one situation to the invitee', async () => {
    const refused = createIdentityBridge({
      client: clientWith({ get: async () => ({ succeeded: false, code: 404, message: 'Invalid or expired invitation.' }) }),
    })
    await expect(refused.invitationAcceptance.preview('tok')).resolves.toBeNull()

    const down = createIdentityBridge({
      client: clientWith({ get: async () => { throw new Error('network down') } }),
    })
    await expect(down.invitationAcceptance.preview('tok')).resolves.toBeNull()
  })

  it('accept rejects on a refused envelope instead of resolving to nothing', async () => {
    const bridge = createIdentityBridge({
      client: clientWith({
        post: async () => ({ succeeded: false, code: 400, message: 'Password does not meet the policy.' }),
      }),
    })
    await expect(bridge.invitationAcceptance.accept({ token: 'tok', password: 'weak' })).rejects.toThrow(
      /Password does not meet the policy/,
    )
  })

  it('accept passes the result through, including completed:false with remaining steps', async () => {
    const bridge = createIdentityBridge({
      client: clientWith({
        post: async () => ({ succeeded: true, code: 200, data: { completed: false, remainingSteps: ['SetPassword'] } }),
      }),
    })
    const result = await bridge.invitationAcceptance.accept({ token: 'tok' })
    expect(result.completed).toBe(false)
    expect(result.remainingSteps).toEqual(['SetPassword'])
  })

  it('rejects with a clear error when no client is wired (mock-api test path)', async () => {
    const stub = vi.fn() as never
    const bridge = createIdentityBridge({ userApi: stub, roleApi: stub, tenantApi: stub, loginLogApi: stub })
    await expect(bridge.invitationAcceptance.accept({ token: 'tok' })).rejects.toThrow(/invitationAcceptance\.accept/)
    await expect(bridge.invitationAcceptance.preview('tok')).resolves.toBeNull()
  })
})

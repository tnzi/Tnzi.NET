import { describe, it, expect, vi } from 'vitest'
import { useAuthApi } from '../../src/services/identity/api'

function mockClient() {
  return {
    get: vi.fn(async () => ({ success: true, code: 200, data: {} })),
    post: vi.fn(async () => ({ success: true, code: 200, data: {} })),
    put: vi.fn(async () => ({ success: true, code: 200, data: {} })),
    delete: vi.fn(async () => ({ success: true, code: 200, data: undefined })),
  }
}

// A pending-action temp token is enough to change the account's password for its
// ten-minute lifetime, and reading the challenge hands out the authenticator secret.
// Request paths land in request and access logs, so the token goes in the body.
// It also used to be standard Base64 in a path segment, where a '/' arrived as a
// literal %2F and the lookup failed for about half of all tokens.
describe('useAuthApi pending actions', () => {
  const token = 'abc/def+ghi='

  function urlsOf(c: ReturnType<typeof mockClient>) {
    return [c.get, c.post].flatMap((fn) => fn.mock.calls.map((call) => String((call as unknown[])[0])))
  }

  it('describe posts the token in the body of /auth/pending-actions/describe', async () => {
    const c = mockClient()
    const api = useAuthApi(c as never)

    await api.describePendingActions(token)

    expect(c.post).toHaveBeenCalledWith('/auth/pending-actions/describe', { tempToken: token })
    expect(c.get).not.toHaveBeenCalled()
    expect(urlsOf(c).some((u) => u.includes('abc'))).toBe(false)
  })

  it('send-email-code posts the token in the body of /auth/pending-actions/send-email-code', async () => {
    const c = mockClient()
    const api = useAuthApi(c as never)

    await api.sendPendingActionEmailCode(token)

    expect(c.post).toHaveBeenCalledWith('/auth/pending-actions/send-email-code', { tempToken: token })
    expect(urlsOf(c).some((u) => u.includes('abc'))).toBe(false)
  })
})

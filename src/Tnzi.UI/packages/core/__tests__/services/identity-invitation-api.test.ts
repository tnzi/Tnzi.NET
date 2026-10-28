import { describe, it, expect, vi } from 'vitest'
import { useInvitationApi } from '../../src/services/identity/api'

function mockClient() {
  return {
    get: vi.fn(async () => ({ success: true, code: 200, data: {} })),
    post: vi.fn(async () => ({ success: true, code: 200, data: {} })),
    put: vi.fn(async () => ({ success: true, code: 200, data: {} })),
    delete: vi.fn(async () => ({ success: true, code: 200, data: undefined })),
  }
}

describe('useInvitationApi (invitee side)', () => {
  // Request paths land in request and access logs, and previewing does not consume
  // the token: a token read from a log could be used to accept the invitation first.
  it('preview sends the token in the body of POST /invitations/preview, never in the URL', async () => {
    const c = mockClient()
    const api = useInvitationApi(c as never)

    await api.preview('secret-token')

    expect(c.post).toHaveBeenCalledWith('/invitations/preview', { token: 'secret-token' })
    expect(c.get).not.toHaveBeenCalled()
    const urls = [c.get, c.post].flatMap((fn) => fn.mock.calls.map((call) => String((call as unknown[])[0])))
    expect(urls.some((u) => u.includes('secret-token'))).toBe(false)
  })

  it('accept posts the token in the body of POST /invitations/accept', async () => {
    const c = mockClient()
    const api = useInvitationApi(c as never)

    await api.accept({ token: 'secret-token', password: 'P@ssw0rd!' })

    expect(c.post).toHaveBeenCalledWith('/invitations/accept', { token: 'secret-token', password: 'P@ssw0rd!' }, expect.anything())
  })
})

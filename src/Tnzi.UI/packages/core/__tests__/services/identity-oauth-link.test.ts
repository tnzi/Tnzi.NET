import { describe, it, expect, vi } from 'vitest'
import { parseOAuthLinkCompletion, useProfileApi } from '../../src/services/identity/api'

function mockClient() {
  return {
    get: vi.fn(async () => ({ success: true, code: 200, data: {} })),
    post: vi.fn(async () => ({ success: true, code: 200, data: undefined })),
    put: vi.fn(async () => ({ success: true, code: 200, data: {} })),
    delete: vi.fn(async () => ({ success: true, code: 200, data: undefined })),
  }
}

// The OAuth callback no longer links: it stages the provider identity and hands
// back a completion code that the signed-in page redeems. A link token is a URL
// parameter and the callback is anonymous, so linking there let anyone attach a
// victim's provider identity to their own account with one link.
describe('OAuth link completion', () => {
  it('completeOAuthLink posts the code in the body to the profile complete endpoint', async () => {
    const c = mockClient()
    const api = useProfileApi(c as never)

    await api.completeOAuthLink('github', 'code-123')

    expect(c.post).toHaveBeenCalledWith('/users/profile/linked-accounts/github/complete', { code: 'code-123' })
  })

  it('parseOAuthLinkCompletion reads the code and provider the callback put on the fragment', () => {
    expect(parseOAuthLinkCompletion('#linkCode=abc%2B%2F%3D&linkedProvider=github')).toEqual({ provider: 'github', code: 'abc+/=' })
    expect(parseOAuthLinkCompletion('linkCode=abc&linkedProvider=google')).toEqual({ provider: 'google', code: 'abc' })
  })

  it('parseOAuthLinkCompletion ignores fragments that are not a link completion', () => {
    expect(parseOAuthLinkCompletion('')).toBeNull()
    expect(parseOAuthLinkCompletion('#section-2')).toBeNull()
    expect(parseOAuthLinkCompletion('#accessToken=at&refreshToken=rt')).toBeNull()
    expect(parseOAuthLinkCompletion('#linkCode=abc')).toBeNull()
    expect(parseOAuthLinkCompletion('#linkedProvider=github')).toBeNull()
  })
})

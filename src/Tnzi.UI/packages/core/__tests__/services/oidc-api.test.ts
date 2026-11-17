import { describe, it, expect, vi } from 'vitest'
import { useAdminOidcApi, useOidcApi, useOidcSessionApi } from '../../src/services/oidc/api'
import {
  buildOidcConsentDeniedUrl,
  continueOidcAuthorization,
  validateOidcReturnUrl,
} from '../../src/services/oidc/continue'
import { OidcClientType, OidcConsentType } from '../../src/services/oidc/metadata'

function mockClient(postResult: unknown = { succeeded: true, success: true, code: 204, data: undefined }) {
  return {
    get: vi.fn(async () => ({ succeeded: true, success: true, code: 200, data: null })),
    post: vi.fn(async () => postResult),
    put: vi.fn(async () => ({ succeeded: true, success: true, code: 200, data: null })),
    delete: vi.fn(async () => ({ succeeded: true, success: true, code: 200, data: undefined })),
  }
}

const PROVIDER = 'https://id.example.test/'
const AUTHORIZE = 'https://id.example.test/connect/authorize?client_id=app&state=s1'

describe('useAdminOidcApi', () => {
  it('lists clients against the admin route with the query as params', async () => {
    const c = mockClient()
    await useAdminOidcApi(c as never).getClients({ pageIndex: 1, pageSize: 20, isEnabled: true })
    expect(c.get).toHaveBeenCalledWith('/admin/oidc/clients', { params: { pageIndex: 1, pageSize: 20, isEnabled: true } })
  })

  it('sends enum members by name when creating a client', async () => {
    const c = mockClient()
    await useAdminOidcApi(c as never).createClient({
      displayName: 'App',
      clientType: OidcClientType.Public,
      consentType: OidcConsentType.Implicit,
      redirectUris: ['https://app.example.test/cb'],
    })
    expect(c.post).toHaveBeenCalledWith('/admin/oidc/clients', expect.objectContaining({ clientType: 'Public', consentType: 'Implicit' }))
  })

  it('toggles, rotates and deletes on the per-client sub-routes', async () => {
    const c = mockClient()
    const api = useAdminOidcApi(c as never)
    await api.setClientEnabled('c-1', false)
    await api.rotateClientSecret('c-1')
    await api.deleteClient('c-1')
    expect(c.put).toHaveBeenCalledWith('/admin/oidc/clients/c-1/enabled', { enabled: false })
    expect(c.post).toHaveBeenCalledWith('/admin/oidc/clients/c-1/rotate-secret')
    expect(c.delete).toHaveBeenCalledWith('/admin/oidc/clients/c-1')
  })

  it('revokes a grant by id', async () => {
    const c = mockClient()
    await useAdminOidcApi(c as never).revokeGrant('g-1')
    expect(c.delete).toHaveBeenCalledWith('/admin/oidc/grants/g-1')
  })
})

describe('useOidcApi', () => {
  it('encodes the client id in user routes', async () => {
    const c = mockClient()
    const api = useOidcApi(c as never)
    await api.revokeMyGrants('my app')
    await api.getConsentInfo('my app', 'openid email')
    expect(c.delete).toHaveBeenCalledWith('/oidc/grants/my%20app')
    expect(c.get).toHaveBeenCalledWith('/oidc/consents/my%20app', { params: { scope: 'openid email' } })
  })
})

describe('useOidcSessionApi', () => {
  it('posts to connect/session under the provider, with credentials', async () => {
    const c = mockClient()
    await useOidcSessionApi(c as never, 'https://id.example.test/base').establish()
    expect(c.post).toHaveBeenCalledWith('https://id.example.test/base/connect/session', undefined, { withCredentials: true })
  })
})

describe('validateOidcReturnUrl', () => {
  it.each([
    [AUTHORIZE, AUTHORIZE],
    ['https://evil.example.test/connect/authorize?x=1', null],
    ['https://id.example.test/connect/token', null],
    ['/connect/authorize', null],
    ['javascript:alert(1)', null],
    [null, null],
  ])('%s -> %s', (input, expected) => {
    expect(validateOidcReturnUrl(input, PROVIDER)).toBe(expected)
  })

  it('respects a provider mounted under a path', () => {
    expect(validateOidcReturnUrl('https://id.example.test/idp/connect/authorize', 'https://id.example.test/idp')).not.toBeNull()
    expect(validateOidcReturnUrl('https://id.example.test/connect/authorize', 'https://id.example.test/idp')).toBeNull()
  })
})

describe('continueOidcAuthorization', () => {
  it('establishes the bridge, then navigates back to the authorize request', async () => {
    const c = mockClient()
    const navigate = vi.fn()
    await continueOidcAuthorization({ client: c as never, providerBaseUrl: PROVIDER, returnUrl: AUTHORIZE, navigate })
    expect(c.post).toHaveBeenCalledTimes(1)
    expect(navigate).toHaveBeenCalledWith(AUTHORIZE)
  })

  it('refuses a foreign return URL without calling the provider', async () => {
    const c = mockClient()
    const navigate = vi.fn()
    await expect(
      continueOidcAuthorization({ client: c as never, providerBaseUrl: PROVIDER, returnUrl: 'https://evil.example.test/connect/authorize', navigate }),
    ).rejects.toThrow()
    expect(c.post).not.toHaveBeenCalled()
    expect(navigate).not.toHaveBeenCalled()
  })

  it('does not navigate when the bridge is refused', async () => {
    const c = mockClient({ succeeded: false, success: false, code: 401, message: 'The session is no longer valid.' })
    const navigate = vi.fn()
    await expect(
      continueOidcAuthorization({ client: c as never, providerBaseUrl: PROVIDER, returnUrl: AUTHORIZE, navigate }),
    ).rejects.toThrow()
    expect(navigate).not.toHaveBeenCalled()
  })
})

describe('buildOidcConsentDeniedUrl', () => {
  it('appends the decision parameter', () => {
    expect(buildOidcConsentDeniedUrl(AUTHORIZE)).toBe(`${AUTHORIZE}&tnzi_consent=denied`)
  })
})

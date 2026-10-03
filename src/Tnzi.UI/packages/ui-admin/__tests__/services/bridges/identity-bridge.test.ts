import { describe, it, expect, vi } from 'vitest'
import { createIdentityBridge } from '../../../src/services/bridges/identity-bridge'

function mockUserApi() {
  return {
    getList: vi.fn(async () => ({
      items: [{ id: '1', userName: 'alice', email: 'a@a' }],
      totalCount: 1,
      pageIndex: 1,
      pageSize: 20,
      totalPages: 1,
      hasPreviousPage: false,
      hasNextPage: false,
    })),
    create: vi.fn(async (data: unknown) => ({ ...(data as object), id: 'new' })),
    update: vi.fn(async (id: string, data: unknown) => ({ id, ...(data as object) })),
    deleteMany: vi.fn(async () => undefined),
    exportCsv: vi.fn(async () => new Blob(['id,userName\n1,alice'], { type: 'text/csv' })),
    importCsv: vi.fn(async () => ({ successCount: 1, failureCount: 0, errors: [] })),
  } as any
}

function mockRoleApi() {
  return {
    getPagedList: vi.fn(async () => ({
      items: [],
      totalCount: 0,
      pageIndex: 1,
      pageSize: 20,
      totalPages: 0,
      hasPreviousPage: false,
      hasNextPage: false,
    })),
    create: vi.fn(async () => ({ id: 'r1' })),
    update: vi.fn(async () => ({})),
    deleteMany: vi.fn(async () => undefined),
  } as any
}

function mockTenantApi() {
  return {
    getPagedList: vi.fn(async () => ({
      items: [],
      totalCount: 0,
      pageIndex: 1,
      pageSize: 20,
      totalPages: 0,
      hasPreviousPage: false,
      hasNextPage: false,
    })),
    create: vi.fn(async () => ({ id: 't1' })),
    update: vi.fn(async () => ({})),
    delete: vi.fn(async () => undefined),
  } as any
}

function mockLoginLogApi() {
  return {
    getList: vi.fn(async () => ({
      items: [],
      totalCount: 0,
      pageIndex: 1,
      pageSize: 20,
      totalPages: 0,
      hasPreviousPage: false,
      hasNextPage: false,
    })),
  } as any
}

// organizationApi + sessionApi are optional - the bridge degrades the
// organizations / sessions sub-contracts to lazy-rejecting stubs when they're
// absent, so existing 4-api tests don't need to mock them. (See
// identity-bridge.ts createIdentityBridge for the guard.)
function makeBridge(overrides: Record<string, unknown> = {}) {
  return createIdentityBridge({
    userApi: mockUserApi(),
    roleApi: mockRoleApi(),
    tenantApi: mockTenantApi(),
    loginLogApi: mockLoginLogApi(),
    ...overrides,
  })
}

describe('identity-bridge', () => {
  /**
   * `POST invitations/accept` issues the session. In cookie delivery mode the
   * refresh token travels only as `Set-Cookie` on that response, which a
   * cross-origin SPA keeps only when the request carried credentials. Every
   * other token-issuing call goes through `createTnziClient`, which wires the
   * flag from the delivery mode; this one is built by the bridge and has to be
   * told.
   */
  describe('invitationAcceptance.accept and the delivery mode', () => {
    const client = () => ({
      get: vi.fn(async () => ({ succeeded: true, success: true, code: 200, data: null })),
      post: vi.fn(async () => ({ succeeded: true, success: true, code: 200, data: { completed: true } })),
    })

    it('carries credentials in cookie mode', async () => {
      const http = client()
      const bridge = createIdentityBridge({ client: http as never, withCredentials: true })
      await bridge.invitationAcceptance.accept({ token: 't', password: 'p' } as never)
      expect(http.post).toHaveBeenCalledWith(
        '/invitations/accept',
        { token: 't', password: 'p' },
        expect.objectContaining({ skipAuthRefresh: true, withCredentials: true }),
      )
    })

    it('does not in bearer mode (the default)', async () => {
      const http = client()
      const bridge = createIdentityBridge({ client: http as never })
      await bridge.invitationAcceptance.accept({ token: 't', password: 'p' } as never)
      const options = (http.post.mock.calls[0] as unknown[])[2] as Record<string, unknown>
      expect(options.skipAuthRefresh).toBe(true)
      expect(options.withCredentials).toBeFalsy()
    })
  })

  // `getTotpSetup` reads like a query but POSTs `two-factor/totp/setup`, which resets
  // the authenticator key; with TOTP disabled by configuration the backend refuses it
  // with a 400. The `get` prefix keeps it out of the write gate's verb list, and on
  // bare unwrap the refusal surfaced as `Cannot read properties of undefined
  // (reading 'sharedKey')` instead of the server's reason.
  // The two-factor panels render from these reads. On bare unwrap a refused
  // envelope (503 with two-factor switched off, 403) resolved to `null`, the
  // panel emitted `loaded` with it and the host crashed on `status.methods`.
  it('two-factor status and sign-in policy reads reject with the server message on a failed envelope', async () => {
    const refused = { succeeded: false, success: false, code: 503, data: null, message: 'Two-factor authentication is disabled' }
    const profileApi = { getTwoFactorStatus: vi.fn(async () => refused) }
    const userSecurityApi = {
      getTwoFactorStatus: vi.fn(async () => refused),
      getSignInPolicy: vi.fn(async () => ({ ...refused, code: 403, message: 'Forbidden' })),
    }
    const bridge = makeBridge({ profileApi, userSecurityApi })

    await expect(bridge.me.getTwoFactorStatus()).rejects.toThrow('Two-factor authentication is disabled')
    await expect(bridge.userSecurity.getTwoFactorStatus('u1')).rejects.toThrow('Two-factor authentication is disabled')
    await expect(bridge.userSecurity.getSignInPolicy('u1')).rejects.toThrow('Forbidden')
  })

  it('me.getTotpSetup rejects with the server message on a failed envelope', async () => {
    const profileApi = {
      getTotpSetup: vi.fn(async () => ({
        succeeded: false, success: false, code: 400, data: null, message: 'Authenticator (TOTP) two-factor is not enabled',
      })),
    }
    const bridge = makeBridge({ profileApi })
    await expect(bridge.me.getTotpSetup()).rejects.toThrow('Authenticator (TOTP) two-factor is not enabled')
  })

  // `HttpClient.download` RESOLVES a failed envelope (`{ succeeded: false, data:
  // undefined, message }`) on any non-2xx, and bare unwrap handed that back as
  // `undefined`: the Users list export did nothing at all (TListShell only
  // downloads when a Blob came back and useCrudPage only toasts thrown errors),
  // and the account page wrote a file whose body was the string `undefined`
  // and toasted success.
  it('users.export rejects with the server message when download resolves a failed envelope', async () => {
    const userApi = mockUserApi()
    userApi.exportCsv = vi.fn(async () => ({
      succeeded: false, success: false, code: 429, data: undefined, message: 'Rate limit exceeded, retry later',
    }))
    const bridge = makeBridge({ userApi })
    await expect(
      bridge.users.export!({ pageIndex: 1, pageSize: 20, searchText: '', sortField: undefined, sortOrder: null, filters: {} }),
    ).rejects.toThrow('Rate limit exceeded')
  })

  // The role matrices (RoleFunctions / EntityRoles) spread the result of
  // `roles.getAll()`. A refused envelope bare-unwrapped to `null`, and the
  // spread threw "null is not iterable" into the page's catch, which toasted
  // it verbatim in place of the server's permission message.
  it('roles.getAll rejects with the server message on a refused envelope', async () => {
    const roleApi = mockRoleApi()
    roleApi.getAll = vi.fn(async () => ({
      succeeded: false, success: false, code: 403, data: null, message: 'Permission denied: identity.role.view',
    }))
    const bridge = makeBridge({ roleApi })
    await expect(bridge.roles.getAll()).rejects.toThrow('Permission denied: identity.role.view')
  })

  it('me.exportPersonalData rejects with the server message on a failed envelope', async () => {
    const profileApi = {
      exportPersonalData: vi.fn(async () => ({
        succeeded: false, success: false, code: 500, data: undefined, message: 'Export is temporarily unavailable',
      })),
    }
    const bridge = makeBridge({ profileApi })
    await expect(bridge.me.exportPersonalData()).rejects.toThrow('Export is temporarily unavailable')
  })

  it('me.exportPersonalData hands back the payload of a succeeded envelope', async () => {
    const payload = { profile: { userName: 'alice' }, exportedAt: '2026-09-12T00:00:00Z' }
    const profileApi = {
      exportPersonalData: vi.fn(async () => ({ succeeded: true, success: true, code: 200, data: payload })),
    }
    const bridge = makeBridge({ profileApi })
    expect(await bridge.me.exportPersonalData()).toEqual(payload)
  })

  it('sessions.cleanExpired rejects with the server message on a failed envelope', async () => {
    const sessionApi = {
      cleanExpired: vi.fn(async () => ({ succeeded: false, success: false, code: 403, data: null, message: 'cleanup refused' })),
    }
    const bridge = makeBridge({ sessionApi })
    await expect(bridge.sessions.cleanExpired(30)).rejects.toThrow('cleanup refused')
  })

  it('users.fetch calls userApi.getList with mapped query', async () => {
    const userApi = mockUserApi()
    const bridge = createIdentityBridge({
      userApi,
      roleApi: mockRoleApi(),
      tenantApi: mockTenantApi(),
      loginLogApi: mockLoginLogApi(),
    })
    const result = await bridge.users.fetch({
      pageIndex: 2,
      pageSize: 10,
      searchText: 'alice',
      sortField: 'name',
      sortOrder: 'asc',
      filters: { active: true },
    })
    expect(userApi.getList).toHaveBeenCalledWith(
      expect.objectContaining({
        pageIndex: 2,
        pageSize: 10,
        keyword: 'alice',
        sortBy: 'name',
        // The wire name is `sortDescending`, not `sortOrder`: no backend query
        // DTO declares a direction string, so the old key was dropped on
        // arrival and server-side sorting silently never happened.
        sortDescending: false,
        active: true,
      }),
    )
    expect(result.items).toHaveLength(1)
    expect(result.totalCount).toBe(1)
  })

  it('users.fetch coerces isLockedOut/isEmailConfirmed string filters to booleans', async () => {
    const userApi = mockUserApi()
    const bridge = createIdentityBridge({
      userApi,
      roleApi: mockRoleApi(),
      tenantApi: mockTenantApi(),
      loginLogApi: mockLoginLogApi(),
    })
    await bridge.users.fetch({
      pageIndex: 1,
      pageSize: 20,
      searchText: '',
      filters: { isLockedOut: 'true', isEmailConfirmed: 'false' },
    })
    // String 'true'/'false' from the search NSelect must become real booleans
    // so the backend bool? binds instead of 400-ing on a JSON string.
    expect(userApi.getList).toHaveBeenCalledWith(
      expect.objectContaining({ isLockedOut: true, isEmailConfirmed: false }),
    )
  })

  it('users.fetch drops empty-string boolean filters (unset select = no filter)', async () => {
    const userApi = mockUserApi()
    const bridge = createIdentityBridge({
      userApi,
      roleApi: mockRoleApi(),
      tenantApi: mockTenantApi(),
      loginLogApi: mockLoginLogApi(),
    })
    await bridge.users.fetch({
      pageIndex: 1,
      pageSize: 20,
      searchText: '',
      filters: { isLockedOut: '' },
    })
    const arg = userApi.getList.mock.calls[0][0]
    expect('isLockedOut' in arg).toBe(false)
  })

  it('loginLogs.fetch coerces isSuccess string filter to a boolean', async () => {
    const loginLogApi = mockLoginLogApi()
    const bridge = createIdentityBridge({
      userApi: mockUserApi(),
      roleApi: mockRoleApi(),
      tenantApi: mockTenantApi(),
      loginLogApi,
    })
    await bridge.loginLogs.fetch({
      pageIndex: 1,
      pageSize: 20,
      searchText: '',
      filters: { isSuccess: 'false' },
    })
    expect(loginLogApi.getList).toHaveBeenCalledWith(
      expect.objectContaining({ isSuccess: false }),
    )
  })

  it('users.create delegates to userApi.create', async () => {
    const userApi = mockUserApi()
    const bridge = createIdentityBridge({
      userApi,
      roleApi: mockRoleApi(),
      tenantApi: mockTenantApi(),
      loginLogApi: mockLoginLogApi(),
    })
    const result = await bridge.users.create({ userName: 'Bob' } as any)
    expect(userApi.create).toHaveBeenCalledWith({ userName: 'Bob' })
    expect((result as any).id).toBe('new')
  })

  // The failed-envelope case for create / update: every earlier "rejects" in this
  // file was a not-wired stub, and the mocks return bare objects, so ensureOk had
  // never seen a refusal here. A 409 on create used to resolve `null` and the
  // list page toasted "created" over a row that did not exist.
  it('users.create / update and roles.create reject with the server message on a failed envelope', async () => {
    const refused = { succeeded: false, success: false, code: 409, data: null, message: 'User name is already taken' }
    const userApi = { ...mockUserApi(), create: vi.fn(async () => refused), update: vi.fn(async () => refused) }
    const roleApi = { ...mockRoleApi(), create: vi.fn(async () => ({ ...refused, message: 'Role code is already taken' })) }
    const bridge = makeBridge({ userApi, roleApi })
    await expect(bridge.users.create({ userName: 'Bob' } as never)).rejects.toThrow('User name is already taken')
    await expect(bridge.users.update('1', { userName: 'Bob' } as never)).rejects.toThrow('User name is already taken')
    await expect(bridge.roles.create({ name: 'Admin' } as never)).rejects.toThrow('Role code is already taken')
  })

  it('users.update delegates to userApi.update', async () => {
    const userApi = mockUserApi()
    const bridge = createIdentityBridge({
      userApi,
      roleApi: mockRoleApi(),
      tenantApi: mockTenantApi(),
      loginLogApi: mockLoginLogApi(),
    })
    await bridge.users.update('7', { userName: 'renamed' } as any)
    expect(userApi.update).toHaveBeenCalledWith('7', { userName: 'renamed' })
  })

  it('users.delete delegates to userApi.deleteMany', async () => {
    const userApi = mockUserApi()
    const bridge = createIdentityBridge({
      userApi,
      roleApi: mockRoleApi(),
      tenantApi: mockTenantApi(),
      loginLogApi: mockLoginLogApi(),
    })
    await bridge.users.delete(['1', '2'])
    expect(userApi.deleteMany).toHaveBeenCalledWith(['1', '2'])
  })

  it('users.export wraps CSV in Blob; users.import calls importCsv', async () => {
    const userApi = mockUserApi()
    const bridge = createIdentityBridge({
      userApi,
      roleApi: mockRoleApi(),
      tenantApi: mockTenantApi(),
      loginLogApi: mockLoginLogApi(),
    })
    const blob = await bridge.users.export!({
      pageIndex: 1,
      pageSize: 20,
      searchText: '',
      filters: {},
    })
    expect(blob).toBeInstanceOf(Blob)
    await bridge.users.import!(new File(['x'], 'x.csv'))
    expect(userApi.importCsv).toHaveBeenCalled()
  })

  it('exposes roles sub-contract with fetch/create/update/delete', async () => {
    const bridge = makeBridge()
    expect(typeof bridge.roles.fetch).toBe('function')
    expect(typeof bridge.roles.create).toBe('function')
    expect(typeof bridge.roles.update).toBe('function')
    expect(typeof bridge.roles.delete).toBe('function')
  })

  // -- sessions.list (global paged session list, GET /admin/sessions) --------

  function mockSessionApi() {
    return {
      getSessions: vi.fn(async () => ({
        items: [
          {
            id: 's1',
            userId: 'u1',
            userName: 'alice',
            isRevoked: false,
            creationTime: '2026-01-01T00:00:00Z',
            lastActivityTime: '2026-01-01T01:00:00Z',
          },
        ],
        totalCount: 1,
        pageIndex: 1,
        pageSize: 20,
        totalPages: 1,
        hasPreviousPage: false,
        hasNextPage: false,
      })),
      getUserSessions: vi.fn(async () => []),
      revokeSession: vi.fn(async () => undefined),
      revokeAllSessions: vi.fn(async () => undefined),
      updateActivityTime: vi.fn(async () => undefined),
      cleanExpired: vi.fn(async () => 0),
      getStatistics: vi.fn(async () => ({ activeSessionCount: 0, onlineUserCount: 0, topDevices: [] })),
      getActiveUsers: vi.fn(async () => []),
    } as any
  }

  it('sessions.list calls sessionApi.getSessions with filters and returns paged items incl. userName', async () => {
    const sessionApi = mockSessionApi()
    const bridge = makeBridge({ sessionApi })
    const result = await bridge.sessions.list({ userId: 'u1', includeRevoked: true, pageIndex: 2, pageSize: 50 })
    expect(sessionApi.getSessions).toHaveBeenCalledWith({
      userId: 'u1',
      includeRevoked: true,
      pageIndex: 2,
      pageSize: 50,
    })
    expect(result.items).toHaveLength(1)
    expect(result.items[0]?.userName).toBe('alice')
    expect(result.totalCount).toBe(1)
  })

  it('sessions.list maps null userId to undefined (global, unfiltered list)', async () => {
    const sessionApi = mockSessionApi()
    const bridge = makeBridge({ sessionApi })
    await bridge.sessions.list({ userId: null, includeRevoked: false, pageIndex: 1, pageSize: 20 })
    expect(sessionApi.getSessions).toHaveBeenCalledWith(
      expect.objectContaining({ userId: undefined }),
    )
  })

  it('sessions.list rejects with a clear error when no sessionApi is wired', async () => {
    const bridge = makeBridge()
    await expect(bridge.sessions.list()).rejects.toThrow(/sessions\.list/)
  })

  it('getAuthConfig returns null when no authApi is wired (fail-open)', async () => {
    const bridge = makeBridge()
    await expect(bridge.getAuthConfig()).resolves.toBeNull()
  })

  it('getAuthConfig unwraps the auth config from authApi', async () => {
    const authApi = { getConfig: vi.fn(async () => ({ allowEmailLogin: true, oAuthProviders: [] })) } as any
    const bridge = makeBridge({ authApi })
    const config = await bridge.getAuthConfig()
    expect(authApi.getConfig).toHaveBeenCalled()
    expect(config?.allowEmailLogin).toBe(true)
  })

  it('getAuthConfig swallows probe errors and returns null (fail-open)', async () => {
    const authApi = { getConfig: vi.fn(async () => { throw new Error('boom') }) } as any
    const bridge = makeBridge({ authApi })
    await expect(bridge.getAuthConfig()).resolves.toBeNull()
  })

  it('invitations.create issues an invitation through the admin api', async () => {
    const invitationApi = {
      create: vi.fn(async () => ({
        userId: 'u1',
        userName: 'newhire',
        acceptUrl: 'https://admin.example.com/accept-invitation?token=abc',
        expiresAt: '2026-09-08T00:00:00Z',
      })),
      resend: vi.fn(),
      revoke: vi.fn(),
    } as any
    const bridge = makeBridge({ invitationApi })

    const invited = await bridge.invitations.create({ userName: 'newhire', email: 'n@example.com' })

    expect(invitationApi.create).toHaveBeenCalledWith({ userName: 'newhire', email: 'n@example.com' })
    // acceptUrl is readable only on this response - the server keeps just the hash.
    expect(invited.acceptUrl).toContain('token=abc')
  })

  it('invitations.resend passes the optional lifetime through', async () => {
    const invitationApi = {
      create: vi.fn(),
      resend: vi.fn(async () => ({ userId: 'u1', userName: 'n', acceptUrl: 'x', expiresAt: 'y' })),
      revoke: vi.fn(),
    } as any
    const bridge = makeBridge({ invitationApi })

    await bridge.invitations.resend('u1', 24)

    expect(invitationApi.resend).toHaveBeenCalledWith('u1', 24)
  })

  it('invitations.* reject with a clear error when no invitationApi is wired', async () => {
    const bridge = makeBridge()
    await expect(bridge.invitations.create({ userName: 'x' })).rejects.toThrow(/invitations\.create/)
    await expect(bridge.invitations.revoke('u1')).rejects.toThrow(/invitations\.revoke/)
  })

  it('oauthLoginUrl returns empty string when no client is wired', () => {
    const bridge = makeBridge()
    expect(bridge.oauthLoginUrl('github')).toBe('')
  })

  // Linking a provider from the User Center used to navigate straight to the anonymous
  // OAuth login URL; the callback never learns who is signed in, so a provider email that
  // differs from ours created a brand-new account. The link token is what carries the
  // identity across the two anonymous hops, and it must end up on the URL.
  it('oauthLoginUrl carries the link token as a query parameter', () => {
    const client = {
      resolveUrl: vi.fn((url: string, params?: Record<string, string>) => {
        const qs = params ? '?' + new URLSearchParams(params).toString() : ''
        return `https://api.example${url}${qs}`
      }),
    } as any
    const bridge = makeBridge({ client })

    const url = bridge.oauthLoginUrl('github', 'https://app.example/account', 'tok123')

    expect(url).toContain('/oauth/github/login')
    expect(url).toContain('linkToken=tok123')
    expect(url).toContain('returnUrl=')
  })

  it('me.issueOAuthLinkToken unwraps the token envelope', async () => {
    const profileApi = {
      issueOAuthLinkToken: vi.fn(async () => ({
        succeeded: true, success: true, code: 200,
        data: { token: 'tok123', provider: 'github', expiresAt: '2026-09-12T00:05:00Z' },
      })),
    }
    const bridge = makeBridge({ profileApi })

    const issued = await bridge.me.issueOAuthLinkToken('github')

    expect(profileApi.issueOAuthLinkToken).toHaveBeenCalledWith('github')
    expect(issued.token).toBe('tok123')
    expect(issued.provider).toBe('github')
  })
})

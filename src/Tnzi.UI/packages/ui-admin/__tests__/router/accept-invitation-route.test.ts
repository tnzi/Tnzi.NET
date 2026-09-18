import { describe, it, expect } from 'vitest'
import { createRouter, createMemoryHistory } from 'vue-router'
import { defaultAdminRoutes } from '../../src/router/routes'

/**
 * The backend's `DefaultInvitationUrlGenerator` builds the invite link as
 * `{System:FrontendUrl}/accept-invitation?token=...` when no
 * `Identity:Invitation:AcceptUrlTemplate` is configured. The route literal here
 * is that contract's other half: until 2026-09-12 no package shipped it, so the
 * default link landed on the 404 route while the admin half (invite / resend /
 * revoke) looked complete.
 */
describe('accept-invitation route', () => {
  const router = createRouter({ history: createMemoryHistory(), routes: defaultAdminRoutes })

  it('resolves the default invite link to the acceptance page, not 404', () => {
    const resolved = router.resolve('/accept-invitation?token=abc')
    expect(resolved.name).toBe('accept-invitation')
    expect(resolved.query.token).toBe('abc')
  })

  it('is reachable anonymously - the invitee has no account to sign in with yet', () => {
    const record = defaultAdminRoutes.find((r) => r.name === 'accept-invitation')
    expect(record?.meta?.requiresAuth).toBe(false)
  })
})

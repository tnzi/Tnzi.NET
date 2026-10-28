import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * One user's admin page.
 *
 * Locks two things the page decides on its own:
 * - Saving the profile replaces the user's detail record (the backend maps the
 *   whole DTO onto it), so the avatar the form does not edit must be sent back
 *   or every profile save deletes the user's avatar.
 * - A section appears only for an admin whose role can read it, and its write
 *   action follows the permission the endpoint actually checks. Sessions and
 *   sign-in history are separate grants from `user.view`; showing them anyway
 *   crashed the sessions list and rendered the history as "no sign-ins".
 */
const h = vi.hoisted(() => ({
  getById: vi.fn(),
  update: vi.fn(),
  listForUser: vi.fn(),
  fetchLogs: vi.fn(),
  getAllRoles: vi.fn(),
}))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({
    query: {}, params: { id: 'u1' }, path: '/admin/identity/users/u1',
    fullPath: '/admin/identity/users/u1', hash: '', name: 'identity.users.detail', meta: {}, matched: [],
  }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn(), resolve: () => ({ path: '/admin/identity/users', matched: [] }) }),
}))

vi.mock('../../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: () => ({
    users: { getById: h.getById, update: h.update, lock: vi.fn(), unlock: vi.fn(), resetPassword: vi.fn() },
    sessions: { listForUser: h.listForUser, revoke: vi.fn(), revokeAllForUser: vi.fn() },
    loginLogs: { fetch: h.fetchLogs },
    roles: { getAll: h.getAllRoles },
  }),
}))

import Page from '../../../src/pages/identity/UserDetail.vue'
import UserSessionsSection from '../../../src/pages/identity/sections/UserSessionsSection.vue'
import UserLoginLogsSection from '../../../src/pages/identity/sections/UserLoginLogsSection.vue'
import UserRolesSection from '../../../src/pages/identity/sections/UserRolesSection.vue'
import { useAdminAuthStore } from '../../../src/stores/useAdminAuthStore'

const user = {
  id: 'u1', userName: 'jane@example.test', email: 'jane@example.test', phoneNumber: null,
  nickname: null, firstName: 'Jane', lastName: 'Doe', gender: 0, birthday: null, address: null, website: null, bio: null,
  avatar: 'https://cdn.example.test/jane.png', avatarId: 'f-avatar',
  isLockedOut: false, twoFactorEnabled: false, isEmailConfirmed: true, roles: ['Staff'], creationTime: '2026-01-01T00:00:00Z',
}

function signInWith(permissions: string[]) {
  useAdminAuthStore().setUserInfo({ id: 'admin', username: 'admin', displayName: 'Admin', roles: [], permissions })
}

type PageVm = {
  sections: Array<{ key: string }>
  canRevokeSessions: boolean
  form: Record<string, unknown>
  save: () => Promise<void>
}

describe('UserDetail page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    h.getById.mockReset().mockResolvedValue(user)
    h.update.mockReset().mockResolvedValue(user)
    h.listForUser.mockReset().mockResolvedValue([])
    h.fetchLogs.mockReset().mockResolvedValue({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20, totalPages: 0, hasPreviousPage: false, hasNextPage: false })
  })

  it('sends the avatar back when the profile is saved', async () => {
    signInWith(['user.view', 'user.update'])
    const wrapper = mount(Page, { props: { id: 'u1' } })
    await flushPromises()
    const vm = wrapper.vm as unknown as PageVm

    // The page hands its live form object to the bridge and reloads into it
    // after the save, so snapshot what was actually sent.
    let sent: Record<string, unknown> | null = null
    h.update.mockImplementation(async (_id: string, data: Record<string, unknown>) => {
      sent = { ...data }
      return user
    })
    vm.form.lastName = 'Doe-Smith'
    await vm.save()

    expect(h.update).toHaveBeenCalledTimes(1)
    expect(sent).toEqual(expect.objectContaining({
      lastName: 'Doe-Smith',
      avatarId: 'f-avatar',
      avatarUrl: 'https://cdn.example.test/jane.png',
    }))
    wrapper.unmount()
  })

  // The birthday picker answers with an epoch number; `birthday` binds to
  // DateTime, so a number in the body fails the save (400) and a birthday
  // could never be set or changed here. The picked day goes out as a date.
  it('sends a picked birthday as a calendar date', async () => {
    signInWith(['user.view', 'user.update'])
    const wrapper = mount(Page, { props: { id: 'u1' } })
    await flushPromises()
    const vm = wrapper.vm as unknown as PageVm

    let sent: Record<string, unknown> | null = null
    h.update.mockImplementation(async (_id: string, data: Record<string, unknown>) => {
      sent = { ...data }
      return user
    })
    vm.form.birthday = new Date(1990, 4, 1).getTime()
    await vm.save()

    expect(sent).toEqual(expect.objectContaining({ birthday: '1990-05-01' }))
    wrapper.unmount()
  })

  // An invited account is locked out by design until the invitee accepts; it is
  // "not arrived yet", not "disabled". The users list shows it as Invited and
  // offers no lock / unlock; the user page must not undo that, and Unlock here
  // would release an account that has no password and already carries its roles.
  it('★ shows an invited account as invited and offers no lock or unlock', async () => {
    signInWith(['user.view', 'user.update'])
    h.getById.mockResolvedValue({ ...user, isLockedOut: true, pendingActions: 'InvitationPending', isEmailConfirmed: false })
    const wrapper = mount(Page, { props: { id: 'u1' } })
    await flushPromises()

    const vm = wrapper.vm as unknown as PageVm & { badges: Array<{ label: string }>; canToggleLock: boolean }
    const labels = vm.badges.map((b) => b.label)
    expect(labels).toContain('Invited')
    expect(labels).not.toContain('Locked')
    expect(vm.canToggleLock).toBe(false)
    expect(wrapper.text()).not.toContain('Unlock')
    wrapper.unmount()
  })

  it('offers lock / unlock on an account that is not waiting on an invitation', async () => {
    signInWith(['user.view', 'user.update'])
    h.getById.mockResolvedValue({ ...user, isLockedOut: true, pendingActions: 'None' })
    const wrapper = mount(Page, { props: { id: 'u1' } })
    await flushPromises()

    const vm = wrapper.vm as unknown as PageVm & { badges: Array<{ label: string }>; canToggleLock: boolean }
    expect(vm.badges.map((b) => b.label)).toContain('Locked')
    expect(vm.canToggleLock).toBe(true)
    wrapper.unmount()
  })

  it('hides roles, sessions and sign-in history from a role that cannot read them', async () => {
    signInWith(['user.view', 'user.update'])
    const wrapper = mount(Page, { props: { id: 'u1' } })
    await flushPromises()

    const keys = (wrapper.vm as unknown as PageVm).sections.map((s) => s.key)
    expect(keys).not.toContain('roles')
    expect(keys).not.toContain('sessions')
    expect(keys).not.toContain('loginLogs')
    expect(keys).toContain('profile')
    wrapper.unmount()
  })

  it('shows each section for the grant that reads it', async () => {
    signInWith(['user.view', 'role.view', 'session.view', 'identity.loginLog.view'])
    const wrapper = mount(Page, { props: { id: 'u1' } })
    await flushPromises()

    const keys = (wrapper.vm as unknown as PageVm).sections.map((s) => s.key)
    expect(keys).toContain('roles')
    expect(keys).toContain('sessions')
    expect(keys).toContain('loginLogs')
    wrapper.unmount()
  })

  it('offers revoking sessions on session.delete, not on user.update', async () => {
    signInWith(['user.view', 'user.update', 'session.view'])
    const withoutDelete = mount(Page, { props: { id: 'u1' } })
    await flushPromises()
    expect((withoutDelete.vm as unknown as PageVm).canRevokeSessions).toBe(false)
    withoutDelete.unmount()

    setActivePinia(createPinia())
    signInWith(['user.view', 'session.view', 'session.delete'])
    const withDelete = mount(Page, { props: { id: 'u1' } })
    await flushPromises()
    expect((withDelete.vm as unknown as PageVm).canRevokeSessions).toBe(true)
    withDelete.unmount()
  })
})

describe('user activity sections when the read is refused', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('the sessions section survives a refused load and does not claim the user has none', async () => {
    h.listForUser.mockReset().mockRejectedValue(new Error('Missing permission session.view'))
    const wrapper = mount(UserSessionsSection, { props: { userId: 'u1', canRevoke: false, t: (k: string) => k } })
    await flushPromises()

    expect(wrapper.text()).not.toContain('detail.sessions.empty')
    wrapper.unmount()
  })

  it('the roles section does not read "no roles exist" when the role list is refused', async () => {
    h.getAllRoles.mockReset().mockRejectedValue(new Error('Missing permission role.view'))
    const wrapper = mount(UserRolesSection, { props: { userId: 'u1', userRoleNames: ['Staff'], canEdit: true, t: (k: string) => k } })
    await flushPromises()

    expect(wrapper.text()).not.toContain('actions.noRolesAvailable')
    wrapper.unmount()
  })

  it('the sign-in history does not read "no sign-ins" when the load is refused', async () => {
    h.fetchLogs.mockReset().mockRejectedValue(new Error('Missing permission identity.loginLog.view'))
    const wrapper = mount(UserLoginLogsSection, { props: { userId: 'u1', t: (k: string) => k } })
    await flushPromises()

    expect(wrapper.text()).not.toContain('detail.loginLogs.empty')
    wrapper.unmount()
  })
})

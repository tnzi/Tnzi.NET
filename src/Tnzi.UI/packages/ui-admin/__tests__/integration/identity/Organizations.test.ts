import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import Organizations from '../../../src/pages/identity/Organizations.vue'
import { useAdminAuthStore } from '../../../src/stores/useAdminAuthStore'

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ meta: {}, query: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
}))

// The members panel could remove a user from an organization but nothing in the
// admin could put anyone into one: the bridge's `assignToOrganization` had no
// caller, so an org tree could be built but never populated from the UI.
const bridge = vi.hoisted(() => ({
  getTree: vi.fn(async () => [
    { id: 'o1', name: 'Head Office', code: 'HQ', sortOrder: 0, isEnabled: true, children: [] },
  ]),
  getUsers: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 10 })),
  usersFetch: vi.fn(async () => ({
    items: [{ id: 'u1', userName: 'alice', email: 'alice@example.com' }],
    totalCount: 1,
    pageIndex: 1,
    pageSize: 20,
  })),
  assignToOrganization: vi.fn(async (_userId: string, _orgId: string) => undefined),
  removeFromOrganization: vi.fn(async () => undefined),
}))

vi.mock('../../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: () => ({
    organizations: {
      getTree: bridge.getTree,
      getUsers: bridge.getUsers,
      create: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
    },
    users: {
      fetch: bridge.usersFetch,
      assignToOrganization: bridge.assignToOrganization,
      removeFromOrganization: bridge.removeFromOrganization,
    },
  }),
}))

async function mountWithOrgSelected() {
  const wrapper = mount(Organizations)
  await flushPromises()
  const vm = wrapper.vm as unknown as { onSelect: (keys: string[]) => void }
  vm.onSelect(['o1'])
  await flushPromises()
  return wrapper
}

describe('Organizations page members panel', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    bridge.assignToOrganization.mockClear()
    bridge.getUsers.mockClear()
  })

  it('adds the picked users to the selected organization and reloads the members', async () => {
    const wrapper = await mountWithOrgSelected()
    expect(wrapper.find('.t-org-page__add-members').exists()).toBe(true)

    const vm = wrapper.vm as unknown as { onPickMembers: (v: unknown) => void; addMembers: () => Promise<void> }
    vm.onPickMembers(['u1', 'u2'])
    const loadsBefore = bridge.getUsers.mock.calls.length
    await vm.addMembers()
    await flushPromises()

    expect(bridge.assignToOrganization.mock.calls).toEqual([['u1', 'o1'], ['u2', 'o1']])
    expect(bridge.getUsers.mock.calls.length).toBeGreaterThan(loadsBefore)
  })

  it('keeps adding the rest when one account is refused', async () => {
    bridge.assignToOrganization.mockImplementationOnce(async () => {
      throw new Error('Only a super administrator can modify a super administrator account')
    })
    const wrapper = await mountWithOrgSelected()
    const vm = wrapper.vm as unknown as { onPickMembers: (v: unknown) => void; addMembers: () => Promise<void> }
    vm.onPickMembers(['u1', 'u2'])
    await vm.addMembers()

    expect(bridge.assignToOrganization.mock.calls).toEqual([['u1', 'o1'], ['u2', 'o1']])
  })

  it('hides the add control from admins who cannot update users', async () => {
    const auth = useAdminAuthStore()
    auth.setUserInfo({ id: 'a1', username: 'viewer', roles: [], permissions: ['identity.view', 'organization.view'] } as never)
    const wrapper = await mountWithOrgSelected()

    expect(wrapper.find('.t-org-page__add-members').exists()).toBe(false)
  })
})

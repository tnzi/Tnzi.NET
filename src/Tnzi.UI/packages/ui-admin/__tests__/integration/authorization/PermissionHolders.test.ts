import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import { PermissionCategory, PermissionRoleGrantState, type PermissionHoldersDto } from '@tnzi/core/services/authorization'

/**
 * Who holds one permission code. The page must keep the three things a reviewer
 * would otherwise misread: super admins (no grant row, still holders), dormant
 * grants (grant nothing today, come back on re-enable) and explicit denies.
 */

const push = vi.fn(() => Promise.resolve())
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: {}, query: {} }),
  useRouter: () => ({ push, replace: vi.fn(() => Promise.resolve()) }),
}))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

const holdersRead = vi.fn()
vi.mock('../../../src/services/bridges/authorization-bridge', () => ({
  createAuthorizationBridge: () => ({ permissions: { holders: holdersRead } }),
}))

import PermissionHolders from '../../../src/pages/authorization/PermissionHolders.vue'
import { useAdminAuthStore } from '../../../src/stores/useAdminAuthStore'

function holdersOf(overrides: Partial<PermissionHoldersDto> = {}): PermissionHoldersDto {
  return {
    functionId: 'f1',
    code: 'catalog.product.export',
    name: 'Export Products',
    category: PermissionCategory.Business,
    isEnabled: true,
    isRetired: false,
    isInEffect: true,
    userNamesHidden: false,
    roles: [
      { id: 'r-root', name: 'Root', state: PermissionRoleGrantState.SuperAdmin, memberCount: 1 },
      { id: 'r-staff', name: 'Staff', state: PermissionRoleGrantState.Effective, memberCount: 2 },
      { id: 'r-legacy', name: 'Legacy', state: PermissionRoleGrantState.Dormant, memberCount: 4 },
      { id: 'r-auditor', name: 'Auditor', state: PermissionRoleGrantState.GrantDisabled, memberCount: 1 },
    ],
    users: [
      { id: 'u-alice', userName: 'alice', isSuperAdmin: false, isDirect: false, roles: [{ id: 'r-staff', name: 'Staff' }] },
      { id: 'u-carol', userName: 'carol', isSuperAdmin: false, isDirect: true, roles: [] },
      { id: 'u-erin', userName: 'erin', isSuperAdmin: true, isDirect: false, roles: [{ id: 'r-root', name: 'Root' }] },
    ],
    deniedUsers: [{ id: 'u-bob', userName: 'bob' }],
    ...overrides,
  }
}

async function mountFor(code = 'catalog.product.export') {
  const wrapper = mount(PermissionHolders, { props: { code } })
  await flushPromises()
  return wrapper
}

function usersSection(wrapper: ReturnType<typeof mount>) {
  return wrapper.find('[data-test="holders-users"]')
}

describe('PermissionHolders', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    push.mockClear()
    holdersRead.mockReset().mockResolvedValue(holdersOf())
  })

  it('reads the holders of the code it is given', async () => {
    await mountFor('catalog.product.export')
    expect(holdersRead).toHaveBeenCalledWith('catalog.product.export')
  })

  it('lists every role with how it relates to the code, flagging the ones that grant nothing', async () => {
    const wrapper = await mountFor()
    const rows = wrapper.findAll('[data-test="holders-roles"] li')
    expect(rows.map((r) => r.text())).toEqual([
      expect.stringContaining('Super admin (implicit)'),
      expect.stringContaining('Granted'),
      expect.stringContaining('Dormant'),
      expect.stringContaining('Grant disabled'),
    ])
    const inert = rows.map((r) => r.classes().includes('t-perm-holders__role--inert'))
    expect(inert).toEqual([false, false, true, true])
    // Only the roles that confer the code count as "granting" in the summary.
    expect(wrapper.text()).toContain('3 account(s) hold it, 2 role(s) grant it, 1 denied')
  })

  it('says how each account holds the code: super admin role, plain role, direct grant', async () => {
    const wrapper = await mountFor()
    const text = usersSection(wrapper).text()
    expect(text).toContain('alice')
    expect(text).toContain('Staff')
    expect(text).toContain('carol')
    expect(text).toContain('Direct grant')
    expect(text).toContain('erin')
    expect(text).toContain('Super admin (Root)')
  })

  it('shows explicitly denied accounts in their own section', async () => {
    const wrapper = await mountFor()
    const denied = wrapper.find('[data-test="holders-denied"]')
    expect(denied.exists()).toBe(true)
    expect(denied.text()).toContain('bob')
    expect(usersSection(wrapper).text()).not.toContain('bob')
  })

  it('explains that only super admins hold a code that is not in effect', async () => {
    holdersRead.mockResolvedValueOnce(holdersOf({
      isEnabled: false,
      isInEffect: false,
      roles: [
        { id: 'r-root', name: 'Root', state: PermissionRoleGrantState.SuperAdmin, memberCount: 1 },
        { id: 'r-staff', name: 'Staff', state: PermissionRoleGrantState.Dormant, memberCount: 2 },
      ],
      users: [{ id: 'u-erin', userName: 'erin', isSuperAdmin: true, isDirect: false, roles: [{ id: 'r-root', name: 'Root' }] }],
      deniedUsers: [],
    }))
    const wrapper = await mountFor()
    expect(wrapper.find('[data-test="holders-not-in-effect"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('Disabled')
    expect(wrapper.find('[data-test="holders-denied"]').exists()).toBe(false)
  })

  it('shows the server refusal instead of an empty list', async () => {
    holdersRead.mockRejectedValueOnce(new Error("Permission 'x.y' not found"))
    const wrapper = await mountFor('x.y')
    expect(wrapper.find('[data-test="holders-error"]').text()).toContain("Permission 'x.y' not found")
    expect(usersSection(wrapper).exists()).toBe(false)
  })

  it('filters the accounts by user name', async () => {
    const wrapper = await mountFor()
    const vm = wrapper.vm as unknown as { keyword: string }
    vm.keyword = 'car'
    await nextTick()
    const text = usersSection(wrapper).text()
    expect(text).toContain('carol')
    expect(text).not.toContain('alice')
  })

  it('opens the account page and the role permission editor', async () => {
    const wrapper = await mountFor()
    const userLink = usersSection(wrapper).findAll('button').find((b) => b.text() === 'alice')
    await userLink!.trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'identity.users.detail', params: { id: 'u-alice' } })

    const roleLink = wrapper.findAll('[data-test="holders-roles"] li')[1]!.find('button')
    await roleLink.trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'authorization.roleFunctions', query: { roleId: 'r-staff' } })
  })

  it('does not offer links the viewer cannot follow', async () => {
    const auth = useAdminAuthStore()
    auth.setUserInfo({ id: 'me', username: 'me', permissions: ['authorization.permission.view'] } as never)
    auth.setSuperUser(false)
    const wrapper = await mountFor()
    expect(usersSection(wrapper).findAll('button').some((b) => b.text() === 'alice')).toBe(false)
    expect(wrapper.findAll('[data-test="holders-roles"] button')).toHaveLength(0)
  })

  it('falls back to a short id when the caller may not see account names', async () => {
    holdersRead.mockResolvedValueOnce(holdersOf({
      userNamesHidden: true,
      users: [
        { id: 'aaaaaaaa-0000-0000-0000-000000000001', userName: '', isSuperAdmin: false, isDirect: true, roles: [] },
        { id: 'bbbbbbbb-0000-0000-0000-000000000002', userName: '', isSuperAdmin: true, isDirect: false, roles: [{ id: 'r-root', name: 'Root' }] },
      ],
      deniedUsers: [{ id: 'cccccccc-0000-0000-0000-000000000003', userName: '' }],
    }))
    const wrapper = await mountFor()
    expect(wrapper.find('[data-test="holders-names-hidden"]').exists()).toBe(true)
    const users = usersSection(wrapper).text()
    expect(users).toContain('aaaaaaaa…')
    expect(users).toContain('bbbbbbbb…')
    expect(wrapper.find('[data-test="holders-denied"]').text()).toContain('cccccccc…')

    // The filter still finds an account by its id.
    const vm = wrapper.vm as unknown as { keyword: string }
    vm.keyword = 'bbbb'
    await nextTick()
    expect(usersSection(wrapper).text()).toContain('bbbbbbbb…')
    expect(usersSection(wrapper).text()).not.toContain('aaaaaaaa…')
  })

  it('does not show the hidden-names note when names are present', async () => {
    const wrapper = await mountFor()
    expect(wrapper.find('[data-test="holders-names-hidden"]').exists()).toBe(false)
  })

  it('keeps the latest code when an earlier read answers last', async () => {
    let releaseFirst: (value: PermissionHoldersDto) => void = () => {}
    holdersRead
      .mockReset()
      .mockImplementationOnce(() => new Promise<PermissionHoldersDto>((resolve) => { releaseFirst = resolve }))
      .mockResolvedValueOnce(holdersOf({ code: 'second.code', name: 'Second' }))
    const wrapper = mount(PermissionHolders, { props: { code: 'first.code' } })
    await wrapper.setProps({ code: 'second.code' })
    await flushPromises()
    releaseFirst(holdersOf({ code: 'first.code', name: 'First' }))
    await flushPromises()
    expect(wrapper.text()).toContain('Second')
    expect(wrapper.text()).not.toContain('First')
  })
})

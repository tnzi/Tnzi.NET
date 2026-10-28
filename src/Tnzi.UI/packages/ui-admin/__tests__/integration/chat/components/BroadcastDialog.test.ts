import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

vi.mock('vue-router', () => ({
  useRoute: () => ({ meta: {}, query: {}, params: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
}))
vi.mock('../../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

const broadcast = vi.fn(async () => 3)
const broadcasts = vi.fn(async () => ({
  items: [
    { id: 'b1', content: 'hello all', targetType: 'All', targetSummary: 'All users', recipientCount: 5, senderId: null, senderName: 'admin', creationTime: '2026-01-01T00:00:00Z' },
  ],
  totalCount: 1,
  pageIndex: 1,
  pageSize: 5,
  totalPages: 1,
  hasPreviousPage: false,
  hasNextPage: false,
}))
vi.mock('../../../../src/services/bridges/chat-bridge', () => ({
  createChatBridge: () => ({ broadcast, broadcasts }),
}))

const rolesGetAll = vi.fn(async () => [
  { id: 'r1', name: 'Admin' },
  { id: 'r2', name: 'Editor' },
])
const usersFetch = vi.fn(async () => ({
  items: [{ id: 'u1', userName: 'alice', email: 'alice@x.com' }],
  totalCount: 1, pageIndex: 1, pageSize: 20, totalPages: 1, hasPreviousPage: false, hasNextPage: false,
}))
vi.mock('../../../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: () => ({
    roles: { getAll: rolesGetAll },
    users: { fetch: usersFetch },
  }),
}))

import BroadcastDialog from '../../../../src/pages/chat/components/BroadcastDialog.vue'
import { translatePageKey } from '../../../../src/pages/_shared/translate'

type DialogVm = {
  form: { content: string; targetMode: string; roleIds: string[]; userIds: string[] }
  handleSend: () => Promise<void>
}

describe('BroadcastDialog', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    broadcast.mockClear()
    broadcasts.mockClear()
    rolesGetAll.mockClear()
    usersFetch.mockClear()
  })

  it('loads broadcast history when opened', async () => {
    const wrapper = mount(BroadcastDialog, { props: { show: false } })
    await flushPromises()
    expect(broadcasts).not.toHaveBeenCalled()

    await wrapper.setProps({ show: true })
    await flushPromises()
    expect(broadcasts).toHaveBeenCalled()
  })

  it('sends to all users with all:true', async () => {
    const wrapper = mount(BroadcastDialog, { props: { show: true } })
    await flushPromises()
    const vm = wrapper.vm as unknown as DialogVm
    vm.form.content = 'Hello everyone'
    await vm.handleSend()
    await flushPromises()
    expect(broadcast).toHaveBeenCalledWith(
      expect.objectContaining({ content: 'Hello everyone', all: true }),
    )
  })

  it('sends "by role" with the selected roleIds', async () => {
    const wrapper = mount(BroadcastDialog, { props: { show: true } })
    await flushPromises()
    const vm = wrapper.vm as unknown as DialogVm
    vm.form.content = 'Role-targeted message'
    vm.form.targetMode = 'roles'
    vm.form.roleIds = ['r1', 'r2']
    await vm.handleSend()
    await flushPromises()
    expect(broadcast).toHaveBeenCalledWith(
      expect.objectContaining({ content: 'Role-targeted message', roleIds: ['r1', 'r2'] }),
    )
  })

  it('sends "by user" with the selected userIds', async () => {
    const wrapper = mount(BroadcastDialog, { props: { show: true } })
    await flushPromises()
    const vm = wrapper.vm as unknown as DialogVm
    vm.form.content = 'User-targeted message'
    vm.form.targetMode = 'users'
    vm.form.userIds = ['u1']
    await vm.handleSend()
    await flushPromises()
    expect(broadcast).toHaveBeenCalledWith(
      expect.objectContaining({ content: 'User-targeted message', userIds: ['u1'] }),
    )
  })

  it('blocks a "by role" send when no role is selected', async () => {
    const wrapper = mount(BroadcastDialog, { props: { show: true } })
    await flushPromises()
    const vm = wrapper.vm as unknown as DialogVm
    vm.form.content = 'Missing role target'
    vm.form.targetMode = 'roles'
    vm.form.roleIds = []
    await vm.handleSend()
    await flushPromises()
    expect(broadcast).not.toHaveBeenCalled()
  })

  it('the content rule speaks the active locale', async () => {
    const wrapper = mount(BroadcastDialog, { props: { show: true } })
    await flushPromises()
    const vm = wrapper.vm as unknown as { rules: { content: Array<{ message: string }> } }
    expect(vm.rules.content[0]!.message).toBe(translatePageKey('chat.broadcast', 'contentRequired'))
    expect(vm.rules.content[0]!.message).toBe('Enter the message to broadcast')
  })

  it('an empty message is held at the form instead of rejecting the send', async () => {
    // Render the modal body in place so the form (and its validation) is real.
    const wrapper = mount(BroadcastDialog, {
      props: { show: true },
      global: { stubs: { NModal: { template: '<div><slot /></div>' }, TOverlayTheme: { template: '<div><slot /></div>' } } },
    })
    await flushPromises()
    const vm = wrapper.vm as unknown as DialogVm
    vm.form.content = ''
    await expect(vm.handleSend()).resolves.toBeUndefined()
    expect(broadcast).not.toHaveBeenCalled()
  })

  it('a whitespace-only message is held at the form too (the server refuses it)', async () => {
    const wrapper = mount(BroadcastDialog, {
      props: { show: true },
      global: { stubs: { NModal: { template: '<div><slot /></div>' }, TOverlayTheme: { template: '<div><slot /></div>' } } },
    })
    await flushPromises()
    const vm = wrapper.vm as unknown as DialogVm
    vm.form.content = ' \n\t '
    await expect(vm.handleSend()).resolves.toBeUndefined()
    expect(broadcast).not.toHaveBeenCalled()

    // Positive control: the same form sends once the message has text in it.
    vm.form.content = '  Hello  '
    await vm.handleSend()
    await flushPromises()
    expect(broadcast).toHaveBeenCalledTimes(1)
  })
})

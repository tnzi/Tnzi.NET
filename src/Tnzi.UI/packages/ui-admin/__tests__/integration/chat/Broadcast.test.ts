import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'

const mockBroadcast = vi.fn(async () => 3)
const mockSuccess = vi.fn()
const mockError = vi.fn()

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('../../../src/services/bridges/chat-bridge', () => ({
  createChatBridge: () => ({ broadcast: mockBroadcast }),
}))

// useMessage() requires NMessageProvider in the tree — stub naive-ui.
vi.mock('naive-ui', async () => {
  const actual = await vi.importActual<typeof import('naive-ui')>('naive-ui')
  return {
    ...actual,
    useMessage: () => ({ success: mockSuccess, error: mockError, info: vi.fn(), warning: vi.fn() }),
  }
})

const stubs = {
  Input: {
    props: ['value', 'modelValue', 'type', 'rows', 'placeholder'],
    emits: ['update:value'],
    template: '<textarea class="stub-input" @input="$emit(\'update:value\', $event.target.value)" />',
  },
  Button: { template: '<button class="stub-btn" @click="$emit(\'click\')"><slot /></button>' },
  Form: {
    props: ['model', 'rules'],
    emits: [],
    template: '<form><slot /></form>',
    methods: { validate: () => Promise.resolve() },
  },
  FormItem: { template: '<div><slot /></div>' },
  RadioGroup: {
    props: ['value', 'modelValue'],
    emits: ['update:value'],
    template: '<div><slot /></div>',
  },
  Radio: { props: ['value'], template: '<span><slot /></span>' },
  Card: { template: '<div class="stub-card"><slot /></div>' },
}

describe('Broadcast page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    mockBroadcast.mockClear()
    mockSuccess.mockClear()
    mockError.mockClear()
  })

  it('mounts without throwing', async () => {
    const { default: Broadcast } = await import('../../../src/pages/chat/Broadcast.vue')
    const wrapper = mount(Broadcast, { global: { stubs } })
    await nextTick()
    expect(wrapper.exists()).toBe(true)
  })

  it('renders the send button', async () => {
    const { default: Broadcast } = await import('../../../src/pages/chat/Broadcast.vue')
    const wrapper = mount(Broadcast, { global: { stubs } })
    await nextTick()
    const buttons = wrapper.findAll('.stub-btn')
    expect(buttons.length).toBeGreaterThanOrEqual(1)
  })

  it('clicking send calls bridge.broadcast with form content and shows success', async () => {
    const { default: Broadcast } = await import('../../../src/pages/chat/Broadcast.vue')
    const wrapper = mount(Broadcast, { global: { stubs } })
    await nextTick()

    // Set content directly on the component's reactive form
    const vm = wrapper.vm as unknown as { form: { content: string; targetMode: string; targetIds: string } }
    vm.form.content = 'Hello everyone!'
    await nextTick()

    const btn = wrapper.find('.stub-btn')
    await btn.trigger('click')
    await flushPromises()

    expect(mockBroadcast).toHaveBeenCalledWith(
      expect.objectContaining({ content: 'Hello everyone!' }),
    )
    expect(mockSuccess).toHaveBeenCalled()
  })

  it('sends all:true when target mode is all (the default)', async () => {
    const { default: Broadcast } = await import('../../../src/pages/chat/Broadcast.vue')
    const wrapper = mount(Broadcast, { global: { stubs } })
    await nextTick()

    const vm = wrapper.vm as unknown as { form: { content: string; targetMode: string; targetIds: string } }
    vm.form.content = 'System-wide notice'
    await nextTick()

    await wrapper.find('.stub-btn').trigger('click')
    await flushPromises()

    expect(mockBroadcast).toHaveBeenCalledWith(
      expect.objectContaining({ content: 'System-wide notice', all: true }),
    )
    // 'all' mode must not also send empty target arrays
    const dto = (mockBroadcast.mock.calls[0] as unknown[])[0] as { roleIds?: unknown; userIds?: unknown }
    expect(dto.roleIds).toBeUndefined()
    expect(dto.userIds).toBeUndefined()
  })

  it('sends userIds when target mode is users', async () => {
    const { default: Broadcast } = await import('../../../src/pages/chat/Broadcast.vue')
    const wrapper = mount(Broadcast, { global: { stubs } })
    await nextTick()

    const vm = wrapper.vm as unknown as { form: { content: string; targetMode: string; targetIds: string } }
    vm.form.content = 'User broadcast'
    vm.form.targetMode = 'users'
    vm.form.targetIds = 'u1, u2'
    await nextTick()

    await wrapper.find('.stub-btn').trigger('click')
    await flushPromises()

    expect(mockBroadcast).toHaveBeenCalledWith(
      expect.objectContaining({ content: 'User broadcast', userIds: ['u1', 'u2'] }),
    )
  })

  it('sends roleIds when target mode is roles', async () => {
    const { default: Broadcast } = await import('../../../src/pages/chat/Broadcast.vue')
    const wrapper = mount(Broadcast, { global: { stubs } })
    await nextTick()

    const vm = wrapper.vm as unknown as { form: { content: string; targetMode: string; targetIds: string } }
    vm.form.content = 'Role broadcast'
    vm.form.targetMode = 'roles'
    vm.form.targetIds = 'admin, user'
    await nextTick()

    await wrapper.find('.stub-btn').trigger('click')
    await flushPromises()

    expect(mockBroadcast).toHaveBeenCalledWith(
      expect.objectContaining({ content: 'Role broadcast', roleIds: ['admin', 'user'] }),
    )
  })
})

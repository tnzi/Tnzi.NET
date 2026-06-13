import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * SandboxStatus integration test — a read-only operations view over
 * `/admin/sandbox/status`. The admin client + sandbox bridge are mocked so the
 * page mounts without a backend. Assertions cover: the status overview (enabled
 * / provider / data root + denylist counts), the three denylist sections with
 * per-section keyword filter, the disabled-module banner branch, and the
 * not-loaded (null status / 404) branch.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

const getStatus = vi.fn(async () => ({
  enabled: true,
  provider: 'LocalSandboxProvider',
  dataRoot: '/var/tnzi/sandbox',
  deniedCommands: ['rm', 'curl', 'wget', 'shutdown'],
  deniedPatterns: ['*.key', '*.pem', '/etc/**'],
  environmentBlacklist: ['AWS_SECRET_ACCESS_KEY', 'OPENAI_API_KEY'],
}))

vi.mock('../../../src/services/bridges/sandbox-bridge', () => ({
  createSandboxBridge: () => ({ getStatus }),
}))

import SandboxStatus from '../../../src/pages/ai/sandbox/SandboxStatus.vue'

const stubs = {
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Input: {
    name: 'Input',
    props: ['value'],
    emits: ['update:value'],
    template:
      '<input class="n-input-stub" :value="value" @input="$emit(\'update:value\', $event.target.value)" />',
  },
  Card: {
    name: 'Card',
    props: ['title'],
    template: '<div class="n-card-stub"><div class="card-title">{{ title }}</div><slot name="header-extra" /><slot /></div>',
  },
  Empty: { name: 'Empty', props: ['description'], template: '<div class="n-empty-stub">{{ description }}<slot name="icon" /><slot name="extra" /></div>' },
  Alert: { name: 'Alert', props: ['title'], template: '<div class="n-alert-stub">{{ title }}<slot /></div>' },
  Tag: { name: 'Tag', props: ['type'], template: '<span class="n-tag-stub" :data-type="type"><slot /></span>' },
}

type Vm = {
  refresh: () => Promise<void>
  status: { enabled: boolean } | null
  queries: Record<string, string>
  denylists: Array<{ key: string; items: string[]; filtered: string[] }>
}

describe('SandboxStatus page (status overview + filtered denylists)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    getStatus.mockClear()
    getStatus.mockResolvedValue({
      enabled: true,
      provider: 'LocalSandboxProvider',
      dataRoot: '/var/tnzi/sandbox',
      deniedCommands: ['rm', 'curl', 'wget', 'shutdown'],
      deniedPatterns: ['*.key', '*.pem', '/etc/**'],
      environmentBlacklist: ['AWS_SECRET_ACCESS_KEY', 'OPENAI_API_KEY'],
    })
  })

  it('fetches the status on mount', async () => {
    mount(SandboxStatus, { global: { stubs } })
    await flushPromises()
    expect(getStatus).toHaveBeenCalledTimes(1)
  })

  it('renders the status overview (provider + data root)', async () => {
    const wrapper = mount(SandboxStatus, { global: { stubs } })
    await flushPromises()
    expect(wrapper.text()).toContain('LocalSandboxProvider')
    expect(wrapper.text()).toContain('/var/tnzi/sandbox')
  })

  it('renders the three denylist sections with counts', async () => {
    const wrapper = mount(SandboxStatus, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as Vm

    // Three sections × the right items each.
    const byKey = Object.fromEntries(vm.denylists.map((d) => [d.key, d]))
    expect(byKey.deniedCommands.items).toHaveLength(4)
    expect(byKey.deniedPatterns.items).toHaveLength(3)
    expect(byKey.envBlacklist.items).toHaveLength(2)

    // Items render as chips.
    expect(wrapper.text()).toContain('shutdown')
    expect(wrapper.text()).toContain('*.pem')
    expect(wrapper.text()).toContain('OPENAI_API_KEY')
  })

  it('filters a single denylist by its keyword query', async () => {
    const wrapper = mount(SandboxStatus, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as Vm

    vm.queries.deniedCommands = 'w'
    await flushPromises()

    const cmds = vm.denylists.find((d) => d.key === 'deniedCommands')!
    // 'curl', 'wget', 'shutdown' contain 'w'; 'rm' does not.
    expect(cmds.filtered).toEqual(['wget', 'shutdown'])
    // Other sections are unaffected by this section's filter.
    const patterns = vm.denylists.find((d) => d.key === 'deniedPatterns')!
    expect(patterns.filtered).toHaveLength(3)
  })

  it('keyword filter is case-insensitive', async () => {
    const wrapper = mount(SandboxStatus, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as Vm

    vm.queries.envBlacklist = 'openai'
    await flushPromises()
    const env = vm.denylists.find((d) => d.key === 'envBlacklist')!
    expect(env.filtered).toEqual(['OPENAI_API_KEY'])
  })

  it('shows the disabled banner when the module is loaded but Enabled=false', async () => {
    getStatus.mockResolvedValueOnce({
      enabled: false,
      provider: 'LocalSandboxProvider',
      dataRoot: '/var/tnzi/sandbox',
      deniedCommands: ['rm'],
      deniedPatterns: [],
      environmentBlacklist: [],
    })
    const wrapper = mount(SandboxStatus, { global: { stubs } })
    await flushPromises()
    expect(wrapper.find('.n-alert-stub').exists()).toBe(true)
    expect(wrapper.text()).toContain('Sandbox module is disabled')
  })

  it('shows a friendly not-loaded prompt when the bridge returns null (404)', async () => {
    getStatus.mockResolvedValueOnce(null as never)
    const wrapper = mount(SandboxStatus, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as Vm
    expect(vm.status).toBeNull()
    expect(wrapper.text()).toContain('Sandbox module not loaded')
    // No denylist cards mount in the not-loaded state.
    expect(wrapper.find('.card-title').exists()).toBe(false)
  })

  it('treats a thrown bridge error as not-loaded', async () => {
    getStatus.mockRejectedValueOnce(new Error('404'))
    const wrapper = mount(SandboxStatus, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as Vm
    expect(vm.status).toBeNull()
    expect(wrapper.text()).toContain('Sandbox module not loaded')
  })
})

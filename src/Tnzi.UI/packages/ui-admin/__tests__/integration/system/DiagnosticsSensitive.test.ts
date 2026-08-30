import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import Diagnostics from '../../../src/pages/system/Diagnostics.vue'

// The sensitive-endpoints tab answers "what is this deployment exposing".
// Framework default controllers activate on their own, so before it existed the
// only way to know was to read each module's source - and the next framework
// version can add one without anything prompting an already-reviewed app to
// look again.

const listSensitive = vi.fn(async () => ({
  totalCount: 2,
  endpoints: [
    {
      name: 'storage.access-token',
      reason: 'Issues a credential that can leave the controlled environment.',
      route: 'files/{id}/access-token',
      httpMethod: 'GET',
      controller: 'Tnzi.Storage.Controllers.DefaultStorageController',
      module: 'Tnzi.Storage',
      isDefaultController: true,
      allowsAnonymous: false,
    },
    {
      name: 'storage.share-link',
      reason: 'Serves file content to an unauthenticated caller.',
      route: 'files/share/{token}/download',
      httpMethod: 'GET',
      controller: 'Tnzi.Storage.Controllers.DefaultStorageController',
      module: 'Tnzi.Storage',
      isDefaultController: true,
      allowsAnonymous: true,
    },
  ],
}))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))
vi.mock('../../../src/services/bridges/diagnostics-bridge', () => ({
  createDiagnosticsBridge: () => ({
    exceptions: {
      getSummary: vi.fn(async () => ({ totalCount: 0, topExceptions: [], byStatusCode: {}, byErrorCode: {} })),
      getRecent: vi.fn(async () => []),
      clear: vi.fn(),
    },
    controllers: { list: vi.fn(async () => ({ totalCount: 0, controllers: [] })) },
    modules: { list: vi.fn(async () => []) },
    sensitiveEndpoints: { list: listSensitive },
  }),
}))

const stubs = {
  Input: { props: ['value'], template: '<input />' },
  Button: { template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Select: { template: '<select />' },
  Popconfirm: { template: '<div><slot name="trigger" /></div>' },
  Card: { template: '<div><slot /></div>' },
  List: { template: '<div><slot /></div>' },
  ListItem: { template: '<div><slot /></div>' },
  Thing: { template: '<div><slot /></div>' },
  Empty: { template: '<div />' },
  Tag: { template: '<span><slot /></span>' },
  Alert: { template: '<div class="alert"><slot /></div>' },
}

describe('Diagnostics - sensitive endpoints tab', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    listSensitive.mockClear()
  })

  it('loads the sensitive endpoint report on mount', async () => {
    mount(Diagnostics, { global: { stubs } })
    await nextTick()
    await new Promise((r) => setTimeout(r, 10))

    expect(listSensitive).toHaveBeenCalledTimes(1)
  })

  /**
   * The reason column is what makes the list usable for a review - a list of
   * bare capability names cannot support one.
   */
  it('exposes each endpoint with its reason and access level', async () => {
    const wrapper = mount(Diagnostics, { global: { stubs } })
    await nextTick()
    await new Promise((r) => setTimeout(r, 10))

    const rows = (wrapper.vm as unknown as { filteredSensitive: Array<Record<string, unknown>> })
      .filteredSensitive
    expect(rows).toHaveLength(2)
    expect(rows[0].reason).toContain('leave the controlled environment')
    expect(rows.filter((r) => r.allowsAnonymous)).toHaveLength(1)
  })

  /**
   * Anonymously reachable sensitive endpoints are the rows to read first, so
   * the KPI flags their presence rather than burying them in the table.
   */
  it('counts the anonymously reachable ones separately', async () => {
    const wrapper = mount(Diagnostics, { global: { stubs } })
    await nextTick()
    await new Promise((r) => setTimeout(r, 10))

    expect((wrapper.vm as unknown as { anonymousSensitiveCount: number }).anonymousSensitiveCount).toBe(1)
  })

  it('filters by capability name, route or module', async () => {
    const wrapper = mount(Diagnostics, { global: { stubs } })
    await nextTick()
    await new Promise((r) => setTimeout(r, 10))

    const vm = wrapper.vm as unknown as {
      sensitiveFilter: string
      filteredSensitive: Array<{ name: string }>
    }
    vm.sensitiveFilter = 'share'
    await nextTick()
    expect(vm.filteredSensitive.map((r) => r.name)).toEqual(['storage.share-link'])
  })
})

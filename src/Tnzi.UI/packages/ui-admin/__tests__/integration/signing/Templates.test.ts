import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import Templates from '../../../src/pages/signing/Templates.vue'

const FULL_TEMPLATE = {
  id: 't1',
  name: 'Retainer agreement',
  category: 'Legal',
  source: 'Composed',
  pageCount: 2,
  fieldCount: 3,
  requiresWetSignature: false,
  isActive: true,
  version: 4,
  creationTime: '2026-08-01T00:00:00Z',
  bodyTemplate: 'Body with {{client.name}}',
  fields: [{ key: 'sig', label: 'Signature', type: 'Signature' }],
}

const mockGetById = vi.fn(async () => FULL_TEMPLATE)
const mockUpdate = vi.fn(async () => FULL_TEMPLATE)
const mockFetch = vi.fn(async () => ({
  // The list projection deliberately carries NO fields and no body.
  items: [
    {
      id: 't1',
      name: 'Retainer agreement',
      category: 'Legal',
      source: 'Composed',
      pageCount: 2,
      fieldCount: 3,
      requiresWetSignature: false,
      isActive: true,
      version: 4,
      creationTime: '2026-08-01T00:00:00Z',
    },
    {
      id: 't2',
      name: 'Blank draft',
      category: '',
      source: 'Uploaded',
      pageCount: 1,
      fieldCount: 0,
      requiresWetSignature: true,
      isActive: false,
      version: 1,
      creationTime: '2026-08-02T00:00:00Z',
    },
  ] as never[],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 20,
}))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), delete: vi.fn() }),
}))

vi.mock('../../../src/services/bridges/signing-bridge', () => ({
  createSigningBridge: () => ({
    requests: {
      fetch: vi.fn(),
      getById: vi.fn(),
      create: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
      send: vi.fn(),
      void: vi.fn(),
    },
    templates: {
      fetch: mockFetch,
      getById: mockGetById,
      create: vi.fn(),
      update: mockUpdate,
      delete: vi.fn(),
    },
  }),
}))

const stubs = {
  DataTable: { props: ['data'], template: '<div class="dt" />' },
  Pagination: { template: '<div />' },
  Input: { props: ['value'], template: '<input />' },
  Button: { template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { props: ['show'], template: '<div v-if="show"><slot /></div>' },
  Popover: { template: '<div><slot name="trigger" /></div>' },
  Checkbox: { template: '<input type="checkbox" />' },
  Form: { template: '<form><slot /></form>' },
  FormItem: { template: '<div><slot /></div>' },
  InputNumber: { template: '<input type="number" />' },
  Switch: { template: '<button />' },
  Select: { template: '<select />' },
  DatePicker: { template: '<input type="date" />' },
  Drawer: { props: ['show'], template: '<div v-if="show"><slot /></div>' },
  DrawerContent: { template: '<div><slot /></div>' },
}

async function mountPage() {
  const wrapper = mount(Templates, { global: { stubs } })
  await nextTick()
  await new Promise((r) => setTimeout(r, 10))
  return wrapper
}

describe('Signing Templates page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('renders each template as a tile with its readiness on the face', async () => {
    const wrapper = await mountPage()
    expect(mockFetch).toHaveBeenCalled()
    expect(wrapper.findAll('.t-entity-card')).toHaveLength(2)
    expect(wrapper.text()).toContain('Retainer agreement')
  })

  it('warns on a template with no placed fields - it renders a document nobody can sign', async () => {
    const wrapper = await mountPage()
    const warnings = wrapper.findAll('.st-card__warn')
    // Only the second fixture has fieldCount 0.
    expect(warnings).toHaveLength(1)
  })

  /**
   * ★ The regression this locks: opening the editor straight off a list row
   * would seed the form with `fields: undefined`, and the backend rebuilds the
   * field set wholesale - so saving would silently delete every placed field.
   */
  it('hydrates the full template before opening the editor', async () => {
    const wrapper = await mountPage()
    const vm = wrapper.vm as unknown as {
      openTemplate: (row: unknown) => Promise<void>
      crud: { formModal: { formData: { value: Record<string, unknown> | null } } }
    }
    await vm.openTemplate({ id: 't1', name: 'Retainer agreement' })
    expect(mockGetById).toHaveBeenCalledWith('t1')
    expect(vm.crud.formModal.formData.value?.fields).toHaveLength(1)
  })

  it('does not fall back to the list row when hydration fails', async () => {
    // Falling back would open the editor with `fields: undefined`; one Save
    // later the backend rebuilds the field set from that and the template is
    // stripped. Refusing to open is strictly better than opening a form that
    // destroys data on submit.
    mockGetById.mockRejectedValueOnce(new Error('network'))
    const wrapper = await mountPage()
    const vm = wrapper.vm as unknown as {
      openTemplate: (row: unknown) => Promise<void>
      crud: { formModal: { visible: { value: boolean } } }
    }
    await vm.openTemplate({ id: 't1' })
    expect(vm.crud.formModal.visible.value).toBe(false)
  })

  it('writes only the template contract, not the read-only projections it was seeded with', async () => {
    const wrapper = await mountPage()
    const vm = wrapper.vm as unknown as {
      openTemplate: (row: unknown) => Promise<void>
      crud: { submit: () => Promise<unknown> }
    }
    await vm.openTemplate({ id: 't1' })
    await vm.crud.submit()
    expect(mockUpdate).toHaveBeenCalled()
    const payload = mockUpdate.mock.calls[0]?.[1] as Record<string, unknown>
    expect(payload).toHaveProperty('fields')
    // `version` / `fieldCount` / `creationTime` are server-owned projections.
    expect(payload).not.toHaveProperty('version')
    expect(payload).not.toHaveProperty('fieldCount')
    expect(payload).not.toHaveProperty('creationTime')
  })
})

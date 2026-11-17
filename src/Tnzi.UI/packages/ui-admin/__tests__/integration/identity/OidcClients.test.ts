import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * OIDC client registry page - list, register (one-time secret), enable /
 * disable, rotate. The two behaviours that matter most:
 *   - the secret is shown once after create / rotate and dropped when closed;
 *   - the list fields (redirect URIs, scopes) round-trip through the text form.
 */

const clientsFetch = vi.fn(async () => ({
  items: [
    {
      id: 'c1', clientId: 'app-one', displayName: 'App One', clientType: 'Confidential', consentType: 'Explicit',
      isEnabled: true, requirePkce: true, redirectUris: ['https://a.example/cb'], postLogoutRedirectUris: [],
      scopes: ['profile', 'email'], creationTime: '2026-10-01T00:00:00Z',
    },
  ],
  totalCount: 1,
  pageIndex: 1,
  pageSize: 20,
}))
const clientsCreate = vi.fn(async () => ({ client: { id: 'c2', clientId: 'new-app' }, clientSecret: 'plain-secret' }))
const clientsUpdate = vi.fn(async (id: string) => ({ id }))
const clientsSetEnabled = vi.fn(async () => undefined)
const clientsRotate = vi.fn(async () => ({ client: { id: 'c1', clientId: 'app-one' }, clientSecret: 'rotated' }))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('../../../src/services/bridges/oidc-bridge', () => ({
  createOidcBridge: () => ({
    clients: {
      fetch: clientsFetch,
      create: clientsCreate,
      update: clientsUpdate,
      delete: vi.fn(async () => undefined),
      setEnabled: clientsSetEnabled,
      rotateSecret: clientsRotate,
    },
    grants: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
  }),
}))

const stubs = {
  DataTable:   { props: ['data'], template: '<div class="dt" :data-rows="data.length" />' },
  Pagination:  { template: '<div />' },
  Input:       { props: ['value'], template: '<input :value="value" />' },
  Button:      { template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal:       { props: ['show'], template: '<div v-if="show"><slot /></div>' },
  Popover:     { template: '<div><slot name="trigger" /></div>' },
  Checkbox:    { template: '<input type="checkbox" />' },
  Form:        { template: '<form><slot /></form>' },
  FormItem:    { template: '<div><slot /></div>' },
  InputNumber: { template: '<input type="number" />' },
  Switch:      { template: '<button />' },
  Select:      { template: '<select />' },
  DatePicker:  { template: '<input type="date" />' },
}

type Vm = {
  crud: {
    openCreate: () => void
    openEdit: (row: unknown) => void
    submit: () => Promise<unknown>
    formModal: { formData: { value: Record<string, unknown> | null } }
  }
  issued: { clientSecret?: string } | null
  secretShow: boolean
  rowActions: Array<{ key: string; show?: (row: unknown) => boolean; onClick?: (row: unknown) => unknown }>
  onSecretToggle: (show: boolean) => void
}

async function mountPage() {
  const { default: Page } = await import('../../../src/pages/identity/OidcClients.vue')
  const wrapper = mount(Page, { global: { stubs } })
  await flushPromises()
  return { wrapper, vm: wrapper.vm as unknown as Vm }
}

describe('OidcClients page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('lists the registered clients as tiles: name, client id, type, consent and scopes', async () => {
    const { wrapper } = await mountPage()
    expect(clientsFetch).toHaveBeenCalled()
    // A client is a thing, not a row to compare: tiles, no table.
    expect(wrapper.find('.t-entity-card').exists()).toBe(true)
    expect(wrapper.find('.dt').exists()).toBe(false)
    const tile = wrapper.find('.t-entity-card').text()
    expect(tile).toContain('App One')
    expect(tile).toContain('app-one')
    expect(tile).toContain('profile')
    expect(tile).toContain('email')
  })

  it('opening a tile edits the client, so there is no separate Edit action', async () => {
    const { wrapper, vm } = await mountPage()
    expect(vm.rowActions.map((a) => a.key)).not.toContain('edit')
    await wrapper.find('.t-entity-card').trigger('click')
    await flushPromises()
    expect(vm.crud.formModal.formData.value?.redirectUrisText).toBe('https://a.example/cb')
  })

  it('★ registering a client sends lists and shows the secret exactly once', async () => {
    const { vm } = await mountPage()
    vm.crud.openCreate()
    const form = vm.crud.formModal.formData.value!
    Object.assign(form, {
      displayName: 'New app',
      redirectUrisText: 'https://n.example/cb\n\n  https://n.example/cb2  ',
      scopesText: 'profile, email offline_access',
    })
    await vm.crud.submit()
    await flushPromises()

    expect(clientsCreate).toHaveBeenCalledWith(expect.objectContaining({
      displayName: 'New app',
      redirectUris: ['https://n.example/cb', 'https://n.example/cb2'],
      scopes: ['profile', 'email', 'offline_access'],
      clientType: 'Confidential',
      consentType: 'Explicit',
    }))
    expect(vm.secretShow).toBe(true)
    expect(vm.issued?.clientSecret).toBe('plain-secret')

    // Closing the overlay forgets the credential.
    vm.onSecretToggle(false)
    expect(vm.issued).toBeNull()
  })

  it('edits through the text form and sends lists back', async () => {
    const { vm } = await mountPage()
    const row = (clientsFetch.mock.results[0]!.value as Promise<{ items: unknown[] }>)
    const items = (await row).items
    vm.crud.openEdit(items[0])
    await flushPromises()
    const form = vm.crud.formModal.formData.value!
    expect(form.redirectUrisText).toBe('https://a.example/cb')
    expect(form.scopesText).toBe('profile email')

    form.redirectUrisText = 'https://a.example/cb\nhttps://a.example/cb2'
    await vm.crud.submit()
    await flushPromises()
    expect(clientsUpdate).toHaveBeenCalledWith('c1', expect.objectContaining({
      redirectUris: ['https://a.example/cb', 'https://a.example/cb2'],
      scopes: ['profile', 'email'],
    }))
  })

  it('offers disable on an enabled client and enable on a disabled one, never both', async () => {
    const { vm } = await mountPage()
    const enable = vm.rowActions.find((a) => a.key === 'enable')!
    const disable = vm.rowActions.find((a) => a.key === 'disable')!
    const on = { isEnabled: true, clientType: 'Confidential' }
    const off = { isEnabled: false, clientType: 'Confidential' }
    expect([enable.show!(on), disable.show!(on)]).toEqual([false, true])
    expect([enable.show!(off), disable.show!(off)]).toEqual([true, false])

    await disable.onClick!({ id: 'c1', ...on })
    await flushPromises()
    expect(clientsSetEnabled).toHaveBeenCalledWith('c1', false)
  })

  it('rotating is only offered to confidential clients and reveals the new secret', async () => {
    const { vm } = await mountPage()
    const rotate = vm.rowActions.find((a) => a.key === 'rotateSecret')!
    expect(rotate.show!({ isEnabled: true, clientType: 'Public' })).toBe(false)
    expect(rotate.show!({ isEnabled: true, clientType: 'Confidential' })).toBe(true)

    await rotate.onClick!({ id: 'c1', clientType: 'Confidential' })
    await flushPromises()
    expect(clientsRotate).toHaveBeenCalledWith('c1')
    expect(vm.issued?.clientSecret).toBe('rotated')
  })
})

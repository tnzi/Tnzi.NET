import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

// The personal-data export serialises whatever the bridge hands back and writes
// it to a file. If the bridge ever resolves a refusal to `undefined` again, the
// user receives `personal-data-<date>.json` whose body is the string `undefined`
// and a green "downloaded" toast - a GDPR export reported as delivered. The
// section therefore refuses to write a file around a missing payload, on top of
// the bridge rejecting the failed envelope.

const downloadBlob = vi.fn()
vi.mock('@tnzi/core', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@tnzi/core')>()),
  downloadBlob: (...args: unknown[]) => downloadBlob(...args),
}))

const messageApi = {
  success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn(),
  loading: vi.fn(), create: vi.fn(), destroyAll: vi.fn(),
}

import DangerSection from '../../../src/pages/account/sections/DangerSection.vue'
import {
  createUserCenterState,
  provideUserCenterContext,
} from '../../../src/pages/account/user-center-context'
import { makePageTranslator } from '../../../src/pages/_shared/translate'

function mountDanger(exportPersonalData: () => Promise<unknown>) {
  const me = {
    exportPersonalData,
    deactivate: vi.fn(async () => undefined),
    deleteAccount: vi.fn(async () => undefined),
  }
  const Host = {
    components: { DangerSection },
    setup() {
      const ctx = createUserCenterState({
        bridge: { me, getAuthConfig: vi.fn(async () => ({})), oauthLoginUrl: () => '' } as never,
        storage: { files: { previewUrl: (id: string) => `/preview/${id}` } } as never,
        authStore: { userInfo: null } as never,
        message: messageApi as never,
        t: makePageTranslator('account.userCenter'),
        config: {},
        logoutAndRedirect: () => undefined,
      })
      provideUserCenterContext(ctx)
      return {}
    },
    template: '<DangerSection />',
  }
  return mount(Host)
}

async function clickExport(wrapper: ReturnType<typeof mount>): Promise<void> {
  const button = wrapper.findAll('button').find((b) => b.text().includes('Export data'))
  expect(button, 'export button').toBeDefined()
  await button!.trigger('click')
  await flushPromises()
}

describe('DangerSection - personal data export', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('writes the payload to a file and toasts success', async () => {
    const wrapper = mountDanger(async () => ({ profile: { userName: 'alice' } }))
    await clickExport(wrapper)

    expect(downloadBlob).toHaveBeenCalledTimes(1)
    const [blob, name] = downloadBlob.mock.calls[0] as [Blob, string]
    expect(await blob.text()).toContain('"userName": "alice"')
    expect(name).toMatch(/^personal-data-\d{4}-\d{2}-\d{2}\.json$/)
    expect(messageApi.success).toHaveBeenCalled()
    expect(messageApi.error).not.toHaveBeenCalled()
  })

  it('a rejected export toasts the server message and writes no file', async () => {
    const wrapper = mountDanger(async () => {
      throw new Error('Export is temporarily unavailable')
    })
    await clickExport(wrapper)

    expect(downloadBlob).not.toHaveBeenCalled()
    expect(messageApi.success).not.toHaveBeenCalled()
    expect(messageApi.error).toHaveBeenCalledWith('Export is temporarily unavailable')
  })

  it('a missing payload is an error, not a file whose body is "undefined"', async () => {
    const wrapper = mountDanger(async () => undefined)
    await clickExport(wrapper)

    expect(downloadBlob).not.toHaveBeenCalled()
    expect(messageApi.success).not.toHaveBeenCalled()
    expect(messageApi.error).toHaveBeenCalledTimes(1)
  })
})

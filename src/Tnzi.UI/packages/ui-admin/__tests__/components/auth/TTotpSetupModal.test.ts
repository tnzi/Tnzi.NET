import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { defineComponent, h } from 'vue'

/**
 * `TTotpSetupModal` - authenticator enrolment as two callbacks.
 *
 * Opening must fetch a fresh key every time (the backend resets the key on
 * each setup call, so a secret kept from an abandoned attempt would never
 * verify), a refused setup closes with the reason rather than showing an
 * empty QR, and the first code goes through `confirmCode` before `enabled`
 * fires.
 */
const messageApi = { success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn(), loading: vi.fn(), create: vi.fn(), destroyAll: vi.fn() }

vi.mock('@tnzi/ui', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@tnzi/ui')>()),
  useSafeMessage: () => messageApi,
  TModalShell: defineComponent({
    name: 'TModalShell',
    props: ['show', 'title', 'width'],
    emits: ['update:show'],
    setup(props, { slots }) {
      return () =>
        props.show
          ? h('div', { class: 'modal' }, [slots.default?.(), h('div', { class: 'footer' }, slots.footer?.())])
          : h('div', { class: 'modal-closed' })
    },
  }),
}))

import TTotpSetupModal from '../../../src/components/auth/TTotpSetupModal.vue'

const fetchSetup = vi.fn(async () => ({ sharedKey: 'ABCD EFGH', authenticatorUri: 'otpauth://totp/x?secret=ABCDEFGH' }))
const confirmCode = vi.fn(async (_code: string) => undefined)

function mountModal(show = true) {
  return mount(TTotpSetupModal, {
    props: { show, fetchSetup, confirmCode },
    global: {
      stubs: {
        Spin: { template: '<div><slot /></div>' },
        QrCode: { props: ['value'], template: '<i class="qr" :data-value="value" />' },
      },
    },
  })
}

describe('TTotpSetupModal', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('fetches a fresh key on every open and renders the QR + secret', async () => {
    const w = mountModal(false)
    await flushPromises()
    expect(fetchSetup).not.toHaveBeenCalled()

    await w.setProps({ show: true })
    await flushPromises()
    expect(fetchSetup).toHaveBeenCalledTimes(1)
    expect(w.find('.qr').attributes('data-value')).toBe('otpauth://totp/x?secret=ABCDEFGH')
    expect(w.text()).toContain('ABCD EFGH')

    await w.setProps({ show: false })
    await w.setProps({ show: true })
    await flushPromises()
    expect(fetchSetup).toHaveBeenCalledTimes(2)
  })

  it('closes with the reason when the setup is refused', async () => {
    fetchSetup.mockRejectedValueOnce(new Error('An authenticator is already enabled.'))
    const w = mountModal()
    await flushPromises()

    expect(messageApi.error).toHaveBeenCalledWith('An authenticator is already enabled.')
    expect(w.emitted('update:show')).toEqual([[false]])
  })

  it('confirms with the typed code, emits enabled and closes; the host announces', async () => {
    const w = mountModal()
    await flushPromises()

    const confirm = w.findAll('button').find((b) => b.text() === 'Enable')!
    expect(confirm.attributes('disabled')).toBeDefined()

    await w.find('input').setValue(' 123456 ')
    await confirm.trigger('click')
    await flushPromises()

    expect(confirmCode).toHaveBeenCalledWith('123456')
    // One toast per enrolment, and it is the panel's (after its reload).
    expect(messageApi.success).not.toHaveBeenCalled()
    expect(w.emitted('enabled')).toHaveLength(1)
    expect(w.emitted('update:show')).toEqual([[false]])
  })

  it('keeps the dialog open and shows the error when the code is wrong', async () => {
    confirmCode.mockRejectedValueOnce(new Error('Invalid verification code'))
    const w = mountModal()
    await flushPromises()

    await w.find('input').setValue('000000')
    await w.findAll('button').find((b) => b.text() === 'Enable')!.trigger('click')
    await flushPromises()

    expect(messageApi.error).toHaveBeenCalledWith('Invalid verification code')
    expect(w.emitted('enabled')).toBeUndefined()
    expect(w.emitted('update:show')).toBeUndefined()
  })
})

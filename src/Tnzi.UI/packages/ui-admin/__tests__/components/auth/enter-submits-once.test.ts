import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { defineComponent, h } from 'vue'

/**
 * A form submits once per Enter or click.
 *
 * Enter in a form's last text field is the browser's implicit submission: the
 * form's `submit` fires on keydown, and the same key comes back up as `keyup`.
 * A submit button inside the form is clicked AND submits the form. A form that
 * listens on more than one of those paths sends the request two or three times:
 * a second "enable authenticator" with the same code, or a second and third
 * sign-in attempt counting towards the lockout.
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
      return () => (props.show ? h('div', { class: 'modal' }, [slots.default?.(), h('div', { class: 'footer' }, slots.footer?.())]) : null)
    },
  }),
}))

import TTotpSetupModal from '../../../src/components/auth/TTotpSetupModal.vue'
import TAdminLoginCard from '../../../src/components/auth/TAdminLoginCard.vue'

describe('TTotpSetupModal - Enter in the code box', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('confirms the code once: the form submit, not the key coming back up', async () => {
    const confirmCode = vi.fn(async (_code: string) => undefined)
    const w = mount(TTotpSetupModal, {
      props: {
        show: true,
        fetchSetup: async () => ({ sharedKey: 'K', authenticatorUri: 'otpauth://totp/x?secret=K' }),
        confirmCode,
      },
      global: { stubs: { Spin: { template: '<div><slot /></div>' }, QrCode: { template: '<i />' } } },
    })
    await flushPromises()

    const input = w.find('input')
    await input.setValue('123456')
    // What a browser does for Enter in the form's only field.
    await w.find('form').trigger('submit')
    await input.trigger('keyup', { key: 'Enter' })
    await flushPromises()

    expect(confirmCode).toHaveBeenCalledTimes(1)
    expect(w.emitted('enabled')).toHaveLength(1)
  })

  it('ignores a second confirm while the first is still answering', async () => {
    let release!: () => void
    const confirmCode = vi.fn(() => new Promise<void>((r) => { release = r }))
    const w = mount(TTotpSetupModal, {
      props: {
        show: true,
        fetchSetup: async () => ({ sharedKey: 'K', authenticatorUri: 'otpauth://totp/x?secret=K' }),
        confirmCode,
      },
      global: { stubs: { Spin: { template: '<div><slot /></div>' }, QrCode: { template: '<i />' } } },
    })
    await flushPromises()

    await w.find('input').setValue('123456')
    await w.find('form').trigger('submit')
    await w.find('form').trigger('submit')
    release()
    await flushPromises()

    expect(confirmCode).toHaveBeenCalledTimes(1)
  })
})

describe('TAdminLoginCard - one sign-in per action', () => {
  function mountCard(onLogin: (p: unknown) => Promise<void>) {
    return mount(TAdminLoginCard, {
      props: { onLogin, defaultUserName: 'admin', defaultPassword: 'secret' },
      attachTo: document.body,
    })
  }

  it('a click on Sign in sends one login request', async () => {
    const onLogin = vi.fn(async () => undefined)
    const w = mountCard(onLogin)

    await w.find('button[type="submit"]').trigger('click')
    await flushPromises()

    expect(onLogin).toHaveBeenCalledTimes(1)
    w.unmount()
  })

  it('Enter in the password box sends one login request', async () => {
    const onLogin = vi.fn(async () => undefined)
    const w = mountCard(onLogin)

    const password = w.find('input[type="password"]')
    // Implicit submission with a submit button present: the browser clicks it.
    await w.find('button[type="submit"]').trigger('click')
    await password.trigger('keyup', { key: 'Enter' })
    await flushPromises()

    expect(onLogin).toHaveBeenCalledTimes(1)
    w.unmount()
  })

  it('ignores a second submit while the first is still answering', async () => {
    let release!: () => void
    const onLogin = vi.fn(() => new Promise<void>((r) => { release = r }))
    const w = mountCard(onLogin)

    await w.find('form').trigger('submit')
    await w.find('form').trigger('submit')
    await flushPromises()
    release()
    await flushPromises()

    expect(onLogin).toHaveBeenCalledTimes(1)
    w.unmount()
  })
})

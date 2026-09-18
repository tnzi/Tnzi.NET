import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { defineComponent, h, reactive } from 'vue'

/**
 * `TStepUpModal` renders a core `StepUpPromptController`; the state machine is
 * tested in core, so this file checks the wiring only: each stage shows the
 * right controls, each control calls the right action, and the modal's own
 * close paths go through `cancel()` so `withStepUp` sees "not done".
 */

vi.mock('naive-ui', () => ({
  NButton: defineComponent({
    name: 'NButton',
    props: ['loading', 'disabled', 'block', 'text', 'type', 'size'],
    emits: ['click'],
    setup(props, { slots, emit, attrs }) {
      return () =>
        h(
          'button',
          { ...attrs, disabled: props.disabled || undefined, onClick: () => emit('click') },
          [slots.icon?.(), slots.default?.()],
        )
    },
  }),
  NSpin: defineComponent({ name: 'NSpin', setup: (_, { slots }) => () => h('div', slots.default?.()) }),
  NInputOtp: defineComponent({
    name: 'NInputOtp',
    props: ['value', 'length', 'allowInput', 'status', 'disabled', 'size'],
    emits: ['update:value', 'finish'],
    setup(props, { emit }) {
      return () =>
        h('input', {
          'data-test': 'otp',
          value: (props.value as string[]).join(''),
          onInput: (e: Event) => {
            const digits = (e.target as HTMLInputElement).value.split('')
            emit('update:value', digits)
            if (digits.length >= (props.length as number)) emit('finish')
          },
        })
    },
  }),
}))

vi.mock('@tnzi/ui', () => ({
  TModalShell: defineComponent({
    name: 'TModalShell',
    props: ['show', 'title', 'width', 'maskClosable'],
    emits: ['update:show'],
    setup(props, { slots, emit }) {
      return () =>
        props.show
          ? h('div', { class: 'modal', 'data-title': props.title }, [
              slots.default?.(),
              h('div', { class: 'footer' }, slots.footer?.()),
              h('button', { 'data-test': 'shell-close', onClick: () => emit('update:show', false) }),
            ])
          : h('div', { class: 'modal-closed' })
    },
  }),
  TSvgIcon: defineComponent({ name: 'TSvgIcon', props: ['icon', 'size'], render: () => h('i') }),
}))

import TStepUpModal from '../../../src/components/auth/TStepUpModal.vue'

function fakePrompt(over: Record<string, unknown> = {}) {
  return reactive({
    open: true,
    scope: 'identity.two-factor.manage',
    stage: 'choose',
    methods: ['passkey', 'totp', 'email'],
    method: null as string | null,
    sentTo: null as string | null,
    error: null as string | null,
    busy: false,
    canVerify: true,
    choose: vi.fn(async () => undefined),
    submitCode: vi.fn(async () => undefined),
    resendCode: vi.fn(async () => undefined),
    back: vi.fn(),
    cancel: vi.fn(),
    verify: vi.fn(),
    ...over,
  })
}

function mountModal(prompt: ReturnType<typeof fakePrompt>) {
  return mount(TStepUpModal, { props: { prompt: prompt as never } })
}

describe('TStepUpModal', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  /**
   * The controller's `verify()` settles only through choose / submitCode /
   * cancel, all of which come from this renderer. Navigating away from the
   * User Center mid-prompt unmounted the modal with the prompt still open,
   * so the calling write stayed pending (its `busy` spinner with it) until
   * the user found the prompt again. Unmounting is a cancel.
   */
  it('cancels an open prompt when it unmounts', () => {
    const prompt = fakePrompt()
    const wrapper = mountModal(prompt)
    wrapper.unmount()
    expect(prompt.cancel).toHaveBeenCalledTimes(1)
  })

  it('does not cancel on unmount when no prompt is open', () => {
    const prompt = fakePrompt({ open: false, stage: 'idle', methods: [] })
    const wrapper = mountModal(prompt)
    wrapper.unmount()
    expect(prompt.cancel).not.toHaveBeenCalled()
  })

  it('is closed while the prompt is closed', () => {
    const wrapper = mountModal(fakePrompt({ open: false, stage: 'idle', methods: [] }))
    expect(wrapper.find('.modal').exists()).toBe(false)
  })

  it('choose stage: one button per discovered method, in the controller order', async () => {
    const prompt = fakePrompt()
    const wrapper = mountModal(prompt)

    const buttons = ['passkey', 'totp', 'email'].map((m) => wrapper.find(`[data-test="t-step-up-method-${m}"]`))
    expect(buttons.every((b) => b.exists())).toBe(true)
    expect(wrapper.find('[data-test="t-step-up-method-sms"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="t-step-up-verify"]').exists()).toBe(false)

    await buttons[2]!.trigger('click')
    expect(prompt.choose).toHaveBeenCalledWith('email')
  })

  it('explains when the account has no way to verify', () => {
    const wrapper = mountModal(fakePrompt({ methods: [], canVerify: false }))
    expect(wrapper.find('[data-test="t-step-up-empty"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="t-step-up-empty"]').text()).toMatch(/passkey/i)
  })

  it('code stage: names where the code went, submits on the last digit, offers resend for email', async () => {
    const prompt = fakePrompt({ stage: 'code', method: 'email', sentTo: 'a***@example.com' })
    const wrapper = mountModal(prompt)

    expect(wrapper.find('[data-test="t-step-up-code"]').text()).toContain('a***@example.com')
    expect(wrapper.find('[data-test="t-step-up-resend"]').exists()).toBe(true)

    await wrapper.find('[data-test="otp"]').setValue('123456')
    await flushPromises()
    expect(prompt.submitCode).toHaveBeenCalledWith('123456')

    await wrapper.find('[data-test="t-step-up-resend"]').trigger('click')
    expect(prompt.resendCode).toHaveBeenCalledTimes(1)
  })

  it('code stage for TOTP: authenticator hint, no resend, back returns to the chooser', async () => {
    const prompt = fakePrompt({ stage: 'code', method: 'totp' })
    const wrapper = mountModal(prompt)

    expect(wrapper.find('[data-test="t-step-up-code"]').text()).toMatch(/authenticator/i)
    expect(wrapper.find('[data-test="t-step-up-resend"]').exists()).toBe(false)

    await wrapper.find('[data-test="t-step-up-back"]').trigger('click')
    expect(prompt.back).toHaveBeenCalledTimes(1)
  })

  it('the Verify button stays disabled until the code is complete', async () => {
    const prompt = fakePrompt({ stage: 'code', method: 'totp' })
    const wrapper = mountModal(prompt)
    const verify = () => wrapper.find('[data-test="t-step-up-verify"]')

    expect(verify().attributes('disabled')).toBeDefined()
    await wrapper.find('[data-test="otp"]').setValue('12345')
    expect(verify().attributes('disabled')).toBeDefined()
    expect(prompt.submitCode).not.toHaveBeenCalled()
  })

  it('★ stays on the code entry while the code is being verified (stage busy, method set)', () => {
    // Keyed on `method`, not `stage`: a submit flips the stage to `busy` and a
    // stage-keyed view would snap back to the chooser mid-request, which reads
    // as a rejection.
    const wrapper = mountModal(fakePrompt({ stage: 'busy', method: 'totp', busy: true }))
    expect(wrapper.find('[data-test="t-step-up-code"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="t-step-up-choose"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="t-step-up-verify"]').exists()).toBe(true)
  })

  it('shows the controller error with an alert role', () => {
    const wrapper = mountModal(fakePrompt({ stage: 'code', method: 'totp', error: 'Invalid or expired verification code' }))
    const error = wrapper.find('[data-test="t-step-up-error"]')
    expect(error.attributes('role')).toBe('alert')
    expect(error.text()).toBe('Invalid or expired verification code')
  })

  it('★ both close paths cancel the prompt, so withStepUp replays the challenge instead of hanging', async () => {
    const prompt = fakePrompt()
    const wrapper = mountModal(prompt)

    await wrapper.find('[data-test="t-step-up-cancel"]').trigger('click')
    expect(prompt.cancel).toHaveBeenCalledTimes(1)

    await wrapper.find('[data-test="shell-close"]').trigger('click')
    expect(prompt.cancel).toHaveBeenCalledTimes(2)
  })

  it('clears a half-typed code when the prompt reopens', async () => {
    const prompt = fakePrompt({ stage: 'code', method: 'totp' })
    const wrapper = mountModal(prompt)
    await wrapper.find('[data-test="otp"]').setValue('123')
    expect((wrapper.find('[data-test="otp"]').element as HTMLInputElement).value).toBe('123')

    prompt.open = false
    await flushPromises()
    prompt.open = true
    await flushPromises()

    expect((wrapper.find('[data-test="otp"]').element as HTMLInputElement).value).toBe('')
  })
})

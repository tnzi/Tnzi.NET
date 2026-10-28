import { describe, it, expect, vi, afterEach } from 'vitest'
import { h, nextTick, ref } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { useFormHostRegistration } from '@tnzi/ui/headless'
import { TSchemaForm } from '@tnzi/ui'
import TFormModal from '../../../src/components/crud/TFormModal.vue'
import { useFormModal } from '../../../src/headless/useFormModal'

/**
 * Stands in for a `TSchemaForm` rendered in the modal's slot: registers with
 * the nearest form host and answers `validate()` as told.
 */
function participant(valid: boolean, validate = vi.fn(async () => valid)) {
  return {
    component: {
      setup() {
        useFormHostRegistration({ validate })
        return () => h('div', { class: 'slotted-form' })
      },
    },
    validate,
  }
}

const modalStub = {
  name: 'Modal',
  props: ['show'],
  emits: ['update:show'],
  template:
    '<div v-if="show" class="n-modal-stub"><slot /><slot name="footer" /></div>',
}

// Stands in for the leave transition: naive's NModal keeps the body mounted
// until its transition finishes, so this stub renders the slots regardless of
// `show`. The `modalStub` above unmounts immediately and therefore cannot
// reproduce what a closing modal looks like.
const lingeringModalStub = {
  name: 'Modal',
  props: ['show'],
  template: '<div class="n-modal-stub"><slot /><slot name="footer" /></div>',
}

const buttonStub = {
  name: 'Button',
  template: '<button @click="$emit(\'click\')"><slot /></button>',
}

const stubs = {
  Modal: modalStub,
  Button: buttonStub,
}

function makeState(visible = true, mode: 'create' | 'edit' | 'view' = 'create') {
  return {
    visible: ref(visible),
    mode: ref(mode),
    formData: ref({ name: 'x' }),
    open: vi.fn(),
    close: vi.fn(),
    confirm: vi.fn(async () => ({ name: 'x' })),
  } as any
}

describe('TFormModal footer switch', () => {
  const footerSwitch = { key: 'isActive', label: 'Active', hint: 'Inactive records are hidden.' }

  function mountWith(mode: 'create' | 'edit' | 'view', extra: Record<string, unknown> = {}) {
    const state = makeState(true, mode)
    state.formData.value = { name: 'x', isActive: true }
    const wrapper = mount(TFormModal, {
      props: { state, title: 'Plan', footerSwitch: { ...footerSwitch, ...extra } },
      global: { stubs },
    })
    return { wrapper, state }
  }

  it('sits at the footer left, opposite the buttons, when editing', () => {
    const { wrapper } = mountWith('edit')
    const footer = wrapper.find('.t-form-modal__footer')
    // First in the footer row; its `margin-right: auto` pushes Cancel / Confirm to the right
    const children = Array.from(footer.element.children)
    expect(children[0].classList.contains('t-form-modal__switch')).toBe(true)
    expect(children.slice(1).map((c) => c.textContent?.trim())).toEqual(['admin.common.cancel', 'admin.common.confirm'])
    expect(wrapper.find('.t-form-modal__switch').text()).toContain('Active')
  })

  it('is absent on create and in view mode by default', () => {
    for (const mode of ['create', 'view'] as const) {
      const { wrapper } = mountWith(mode)
      expect(wrapper.find('.t-form-modal__switch').exists()).toBe(false)
    }
  })

  it('a page may opt in to show it on create too, never in view mode', () => {
    expect(mountWith('create', { modes: ['create', 'edit'] }).wrapper.find('.t-form-modal__switch').exists()).toBe(true)
    expect(mountWith('view', { modes: ['create', 'edit'] }).wrapper.find('.t-form-modal__switch').exists()).toBe(false)
  })

  it('clicking the label toggles the form field, and so does the switch', async () => {
    const { wrapper, state } = mountWith('edit')
    await wrapper.find('.t-form-modal__switch-label').trigger('click')
    expect(state.formData.value.isActive).toBe(false)

    wrapper.findComponent({ name: 'Switch' }).vm.$emit('update:value', true)
    await nextTick()
    expect(state.formData.value.isActive).toBe(true)
    expect(wrapper.findComponent({ name: 'Switch' }).props('value')).toBe(true)
  })

  /**
   * jsdom has no layout, so this cannot see where the switch lands; the real check is visual. It pins the rule
   * that makes it possible: the modal shell's footer is a `flex-end` row, so unless this footer grows to fill it,
   * the box shrink-wraps its children and the switch's `margin-right: auto` has nothing to push against (the
   * switch then renders beside Cancel, as it once did in a real browser).
   */
  it('the footer grows to fill the shell footer row, so the switch can sit at its left edge', () => {
    const source = readFileSync(resolve(__dirname, '../../../src/components/crud/TFormModal.vue'), 'utf8')
    const style = source.slice(source.indexOf('<style scoped>'))
    const rule = (selector: string) => {
      const start = style.indexOf(`${selector} {`)
      expect(start, selector).toBeGreaterThanOrEqual(0)
      return style.slice(start, style.indexOf('}', start))
    }
    expect(rule('.t-form-modal__footer')).toMatch(/flex-grow:\s*1/)
    expect(rule('.t-form-modal__switch')).toMatch(/margin-right:\s*auto/)
  })

  it('without the prop the footer is the buttons alone', () => {
    const wrapper = mount(TFormModal, { props: { state: makeState(true, 'edit'), title: 'Plan' }, global: { stubs } })
    expect(wrapper.find('.t-form-modal__switch').exists()).toBe(false)
    expect(wrapper.findAll('.t-form-modal__footer button')).toHaveLength(2)
  })
})

describe('TFormModal', () => {
  it('renders NModal with show from state.visible', () => {
    const state = makeState(true)
    const wrapper = mount(TFormModal, {
      props: { state, title: 'Edit' },
      global: { stubs },
    })
    expect(wrapper.find('.n-modal-stub').exists()).toBe(true)
  })

  it('renders default slot', () => {
    const state = makeState(true)
    const wrapper = mount(TFormModal, {
      props: { state, title: 'Edit' },
      global: { stubs },
      slots: { default: '<input class="form-field" />' },
    })
    expect(wrapper.find('.form-field').exists()).toBe(true)
  })

  it('Cancel button calls state.close()', async () => {
    const state = makeState(true)
    const wrapper = mount(TFormModal, {
      props: { state, title: 'Edit' },
      global: { stubs },
    })
    await wrapper.find('.t-form-modal__cancel').trigger('click')
    expect(state.close).toHaveBeenCalled()
  })

  it('Confirm button emits submit', async () => {
    const state = makeState(true, 'edit')
    const wrapper = mount(TFormModal, {
      props: { state, title: 'Edit' },
      global: { stubs },
    })
    await wrapper.find('.t-form-modal__confirm').trigger('click')
    expect(wrapper.emitted('submit')).toBeTruthy()
  })

  /**
   * The modal is the container that owns the Save button, so it is the one
   * that provides the form host: a slotted `TSchemaForm` registers with it and
   * Confirm validates before emitting `submit`. Without this the `required`
   * rule drew its message under the field while the request still went out
   * and the backend's 400 toast came back on top of it.
   */
  describe('form host', () => {
    it('does not emit submit while a slotted form is invalid', async () => {
      const state = makeState(true, 'edit')
      const form = participant(false)
      const wrapper = mount(TFormModal, {
        props: { state, title: 'Edit' },
        slots: { default: () => h(form.component) },
        global: { stubs },
      })
      await wrapper.find('.t-form-modal__confirm').trigger('click')
      await nextTick()
      expect(form.validate).toHaveBeenCalled()
      expect(wrapper.emitted('submit')).toBeUndefined()
      expect(state.close).not.toHaveBeenCalled()
    })

    it('emits submit once the slotted form validates', async () => {
      const state = makeState(true, 'edit')
      const form = participant(true)
      const wrapper = mount(TFormModal, {
        props: { state, title: 'Edit' },
        slots: { default: () => h(form.component) },
        global: { stubs },
      })
      await wrapper.find('.t-form-modal__confirm').trigger('click')
      await nextTick()
      // (The button stub emits `click` AND lets the native click through, so
      // the count is not asserted - the existing Confirm test has the same shape.)
      expect(wrapper.emitted('submit')).toBeTruthy()
    })
  })

  /**
   * A double-clicked Confirm on a create form posted the record twice: both
   * clicks passed validation and both emitted `submit` before the first save
   * closed the modal.
   */
  describe('one save per Confirm', () => {
    const loadingButtonStub = {
      name: 'Button',
      props: ['loading'],
      template: '<button :data-loading="loading ? \'yes\' : \'no\'" @click="$emit(\'click\')"><slot /></button>',
    }

    it('★ emits submit once when Confirm is clicked again while validation is pending', async () => {
      // Every validation waits on the same gate, so each click that got through
      // would emit once the gate opens.
      let release!: (valid: boolean) => void
      const gate = new Promise<boolean>((resolve) => (release = resolve))
      const form = participant(true, vi.fn(() => gate))
      const state = { ...makeState(true, 'create'), submitting: ref(false) }
      const wrapper = mount(TFormModal, {
        props: { state, title: 'New' },
        slots: { default: () => h(form.component) },
        global: { stubs },
      })
      const confirm = wrapper.find('.t-form-modal__confirm')
      await confirm.trigger('click')
      await confirm.trigger('click')
      release(true)
      await flushPromises()
      expect(wrapper.emitted('submit')).toHaveLength(1)
    })

    it('★ does not emit submit while the state reports a save in flight', async () => {
      const state = { ...makeState(true, 'create'), submitting: ref(true) }
      const wrapper = mount(TFormModal, {
        props: { state, title: 'New' },
        global: { stubs },
      })
      await wrapper.find('.t-form-modal__confirm').trigger('click')
      await nextTick()
      expect(wrapper.emitted('submit')).toBeUndefined()
    })

    it('shows the save in flight on the Confirm button', async () => {
      const state = { ...makeState(true, 'create'), submitting: ref(false) }
      const wrapper = mount(TFormModal, {
        props: { state, title: 'New' },
        global: { stubs: { ...stubs, Button: loadingButtonStub } },
      })
      const confirm = () => wrapper.find('.t-form-modal__confirm')
      expect(confirm().attributes('data-loading')).toBe('no')
      state.submitting.value = true
      await nextTick()
      expect(confirm().attributes('data-loading')).toBe('yes')
    })
  })

  it('hides confirm button in view mode', () => {
    const state = makeState(true, 'view')
    const wrapper = mount(TFormModal, {
      props: { state, title: 'View' },
      global: { stubs },
    })
    expect(wrapper.find('.t-form-modal__confirm').exists()).toBe(false)
  })

  describe('while the close transition runs', () => {
    const lingeringStubs = { Modal: lingeringModalStub, Button: buttonStub }

    function mountWithBody(mode: 'create' | 'edit' | 'view', record: { name: string } | null) {
      const state = useFormModal<{ name: string }>()
      state.open(mode, record)
      const wrapper = mount(TFormModal, {
        props: { state: state as never, title: 'Edit' },
        global: { stubs: lingeringStubs },
        slots: {
          default: (params: { formData: { name: string } | null }) =>
            h('span', { class: 'form-value' }, params.formData?.name ?? 'EMPTY'),
        },
      })
      return { state, wrapper }
    }

    it('keeps painting the values the body had', async () => {
      const { state, wrapper } = mountWithBody('edit', { name: 'Alice' })
      expect(wrapper.find('.form-value').text()).toBe('Alice')

      state.close()
      await nextTick()

      // The body is still on screen. Handing it a null record here would make it
      // repaint from empty values that are thrown away when it unmounts.
      expect(wrapper.find('.form-value').text()).toBe('Alice')
    })

    it('does not grow a confirm button on a closing view modal', async () => {
      const { state, wrapper } = mountWithBody('view', { name: 'Alice' })
      expect(wrapper.find('.t-form-modal__confirm').exists()).toBe(false)

      state.close()
      await nextTick()

      expect(wrapper.find('.t-form-modal__confirm').exists()).toBe(false)
    })

    it('paints the next record on the next open', async () => {
      const { state, wrapper } = mountWithBody('edit', { name: 'Alice' })
      state.close()
      await nextTick()

      state.open('edit', { name: 'Bob' })
      await nextTick()

      expect(wrapper.find('.form-value').text()).toBe('Bob')
    })
  })
  /**
   * The density the SHELL decides, seen from this modal: the default Cancel /
   * Confirm carry no `size`, and neither does the NForm a `TSchemaForm` builds,
   * so all of them used to fall back to naive's global `medium` while naive's
   * own dialogs rendered `small`. Mounted against the real NModal / NButton /
   * TSchemaForm (no stubs) because the size is resolved by naive from the
   * shell's provider, and a stub would only echo the test's own assumption.
   * NModal teleports, hence `attachTo` + document queries.
   */
  describe('control density (real shell)', () => {
    afterEach(() => {
      document.body.innerHTML = ''
    })

    it('renders the default footer buttons and a slotted TSchemaForm field small', () => {
      const state = makeState(true, 'edit')
      const wrapper = mount(TFormModal, {
        props: { state, title: 'Edit' },
        slots: {
          default: () =>
            h(TSchemaForm, {
              schema: [{ key: 'name', type: 'text', label: 'Name' }],
              model: { name: 'a' },
            }),
        },
        attachTo: document.body,
      })
      const confirm = document.querySelector('.t-form-modal__confirm')
      const cancel = document.querySelector('.t-form-modal__cancel')
      const field = document.querySelector('.t-form-schema--compact .n-input')
      expect(confirm?.classList.contains('n-button--small-type')).toBe(true)
      expect(cancel?.classList.contains('n-button--small-type')).toBe(true)
      expect(field?.classList.contains('n-input--small-size')).toBe(true)
      wrapper.unmount()
    })
  })
})

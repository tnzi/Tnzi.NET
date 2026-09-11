import { describe, it, expect, vi } from 'vitest'
import { h, nextTick, ref } from 'vue'
import { mount } from '@vue/test-utils'
import TFormModal from '../../../src/components/crud/TFormModal.vue'
import { useFormModal } from '../../../src/headless/useFormModal'

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
})

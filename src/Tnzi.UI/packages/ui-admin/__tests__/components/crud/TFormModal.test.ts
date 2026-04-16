import { describe, it, expect, vi } from 'vitest'
import { ref } from 'vue'
import { mount } from '@vue/test-utils'
import TFormModal from '../../../src/components/crud/TFormModal.vue'

const modalStub = {
  name: 'Modal',
  props: ['show'],
  emits: ['update:show'],
  template:
    '<div v-if="show" class="n-modal-stub"><slot /><slot name="footer" /></div>',
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
})

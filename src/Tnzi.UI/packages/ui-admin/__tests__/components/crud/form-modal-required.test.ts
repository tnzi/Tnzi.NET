import { describe, it, expect, vi } from 'vitest'
import { h, nextTick, ref } from 'vue'
import { mount } from '@vue/test-utils'
import TFormModal from '../../../src/components/crud/TFormModal.vue'
import TFormSchemaRenderer, { type FormSchemaItem } from '../../../src/pages/_shared/form-schema'

/**
 * The admin schemas declare `required`; `TSchemaForm` validates it; and the
 * container that owns Confirm has to ask before the request leaves. This test
 * runs the real renderer inside the real modal with naive's NForm, the way an
 * admin page assembles them, and pins that a blank required field keeps the
 * modal open with the message under the field and emits no `submit`.
 */
describe('TFormModal + TFormSchemaRenderer: a blank required field blocks Confirm', () => {
  const stubs = {
    Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show"><slot /><slot name="footer" /></div>' },
    Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  }
  const schema: FormSchemaItem[] = [{ key: 'name', label: 'Name', type: 'text', required: true }]

  function mountModal(model: Record<string, unknown>) {
    const state = {
      visible: ref(true),
      mode: ref('create'),
      formData: ref(model),
      open: vi.fn(),
      close: vi.fn(),
      confirm: vi.fn(async () => model),
    }
    const wrapper = mount(TFormModal, {
      props: { state: state as never, title: 'Create' },
      slots: {
        default: ({ formData }: { formData: Record<string, unknown> }) =>
          h(TFormSchemaRenderer, { schema, model: formData, readonly: false }),
      },
      global: { stubs },
    })
    return { wrapper, state }
  }

  it('keeps the modal open, shows the message, and emits no submit', async () => {
    const { wrapper, state } = mountModal({ name: '' })
    await wrapper.find('.t-form-modal__confirm').trigger('click')
    await vi.waitFor(() => expect(wrapper.find('.n-form-item-feedback').text()).toContain('required'))
    await nextTick()
    expect(wrapper.emitted('submit')).toBeUndefined()
    expect(state.close).not.toHaveBeenCalled()
  })

  it('emits submit once the field is filled', async () => {
    const { wrapper } = mountModal({ name: 'Alice' })
    await wrapper.find('.t-form-modal__confirm').trigger('click')
    await vi.waitFor(() => expect(wrapper.emitted('submit')).toBeTruthy())
  })
})

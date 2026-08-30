import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import SourceDocumentPicker from '../../../src/pages/signing/components/SourceDocumentPicker.vue'

const stubs = {
  Button: { template: `<button @click="$emit('click')"><slot /></button>` },
  Alert: { template: '<div class="alert"><slot /></div>' },
}

function mountPicker(fileName: string | null, upload = vi.fn()) {
  const wrapper = mount(SourceDocumentPicker, {
    props: { fileName, readonly: false, translate: (k: string) => k, upload },
    global: { stubs },
  })
  return { wrapper, upload }
}

/** Drive the hidden <input type="file"> the way the browser would. */
async function pick(wrapper: ReturnType<typeof mountPicker>['wrapper'], file: File) {
  const input = wrapper.find('input[type=file]')
  Object.defineProperty(input.element, 'files', { value: [file], configurable: true })
  await input.trigger('change')
  await new Promise((r) => setTimeout(r, 0))
}

describe('signing source document picker', () => {
  it('stores the file and emits the id the page needs for all three keys', async () => {
    const upload = vi.fn(async () => ({ id: 'file-1', fileName: 'nda.pdf' }))
    const { wrapper } = mountPicker(null, upload)

    await pick(wrapper, new File(['%PDF-1.7'], 'nda.pdf', { type: 'application/pdf' }))

    expect(upload).toHaveBeenCalled()
    expect(wrapper.emitted('change')?.[0]).toEqual([{ fileId: 'file-1', fileName: 'nda.pdf' }])
  })

  it('refuses a non-PDF without uploading it', async () => {
    const upload = vi.fn()
    const { wrapper } = mountPicker(null, upload)

    await pick(
      wrapper,
      new File(['x'], 'contract.docx', {
        type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      }),
    )

    // Accepting it would hand back a template that looks saved and fails later -
    // conversion is not wired, so the honest answer is up front.
    expect(upload).not.toHaveBeenCalled()
    expect(wrapper.emitted('change')).toBeUndefined()
    expect(wrapper.text()).toContain('form.sourceFilePdfOnly')
  })

  it('emits null on remove so the page clears every key together', async () => {
    const { wrapper } = mountPicker('nda.pdf')

    // The Remove button only renders once a file name is present.
    await wrapper.findAll('button')[0].trigger('click')

    expect(wrapper.emitted('change')?.[0]).toEqual([null])
  })
})

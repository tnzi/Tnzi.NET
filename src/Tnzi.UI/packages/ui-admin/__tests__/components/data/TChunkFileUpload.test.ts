import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import TChunkFileUpload from '../../../src/components/data/TChunkFileUpload.vue'

function createUploader() {
  return {
    initUpload: vi.fn(async (_meta: unknown) => ({ uploadId: 'upload-1' })),
    uploadChunk: vi.fn(async () => undefined),
    completeUpload: vi.fn(async () => ({ url: 'https://cdn/abc.bin' })),
  }
}

async function triggerUpload(wrapper: ReturnType<typeof mount>, file: File) {
  const input = wrapper.find('input[type=file]').element as HTMLInputElement
  Object.defineProperty(input, 'files', { value: [file], configurable: true })
  await wrapper.find('input[type=file]').trigger('change')
  await new Promise(r => setTimeout(r, 20))
}

describe('TChunkFileUpload', () => {
  it('renders file input', () => {
    const wrapper = mount(TChunkFileUpload, { props: { uploader: createUploader() } })
    expect(wrapper.find('input[type=file]').exists()).toBe(true)
  })

  it('uploads file in chunks and emits success', async () => {
    const uploader = createUploader()
    const wrapper = mount(TChunkFileUpload, { props: { uploader, chunkSize: 4 } })
    const file = new File(['0123456789ABCDEF'], 'test.bin')
    await triggerUpload(wrapper, file)

    expect(uploader.initUpload).toHaveBeenCalled()
    expect(uploader.uploadChunk).toHaveBeenCalledTimes(4)
    expect(uploader.completeUpload).toHaveBeenCalledWith('upload-1')
    expect(wrapper.emitted('success')?.[0]?.[0]).toEqual({ url: 'https://cdn/abc.bin' })
  })

  it('emits progress during upload', async () => {
    const uploader = createUploader()
    const wrapper = mount(TChunkFileUpload, { props: { uploader, chunkSize: 4 } })
    const file = new File(['01234567'], 'test.bin')
    await triggerUpload(wrapper, file)

    const events = wrapper.emitted('progress')
    expect(events).toBeTruthy()
    expect(events![events!.length - 1][0]).toBe(100)
  })

  it('emits error when uploader throws', async () => {
    const uploader = createUploader()
    uploader.uploadChunk = vi.fn(async () => { throw new Error('network') })
    const wrapper = mount(TChunkFileUpload, { props: { uploader, chunkSize: 4 } })
    const file = new File(['01234567'], 'test.bin')
    await triggerUpload(wrapper, file)

    expect(wrapper.emitted('error')).toBeTruthy()
  })
})

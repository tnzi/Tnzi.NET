import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { h } from 'vue'

// ---------------------------------------------------------------------------
// Mock cropperjs — happy-dom has no canvas/toBlob support
// ---------------------------------------------------------------------------
const mockToBlob = vi.fn((cb: (blob: Blob) => void) => {
  cb(new Blob(['cropped'], { type: 'image/jpeg' }))
})
// cropperjs v2 web-component API: configure the selection, then render it to a
// canvas via the async `$toCanvas()`.
const mockSelection = {
  aspectRatio: 1,
  initialCoverage: 0.9,
  $reset: vi.fn(() => mockSelection),
  $toCanvas: vi.fn(async () => ({ toBlob: mockToBlob })),
}
const mockCropperInstance = {
  getCropperSelection: vi.fn(() => mockSelection),
  getCropperImage: vi.fn(() => ({ $ready: vi.fn(async () => undefined) })),
  destroy: vi.fn(),
}
const MockCropper = vi.fn(() => mockCropperInstance)

vi.mock('cropperjs', () => ({
  default: MockCropper,
}))

// ---------------------------------------------------------------------------
// Stub NModal so the dialog renders without Naive UI provider tree
// ---------------------------------------------------------------------------
vi.mock('naive-ui', () => ({
  NModal: {
    name: 'NModal',
    props: ['show'],
    emits: ['update:show'],
     
    render(this: any) {
      if (!this.show) return h('div', { 'data-testid': 'modal-closed' })
      return h('div', { 'data-testid': 'modal-open' }, this.$slots.default?.())
    },
  },
  NButton: {
    name: 'NButton',
    props: ['type', 'disabled', 'loading'],
    emits: ['click'],
     
    render(this: any) {
      return h(
        'button',
        { disabled: this.disabled, onClick: () => this.$emit('click') },
        this.$slots.default?.(),
      )
    },
  },
  NSpin: {
    name: 'NSpin',
    props: ['show'],
     
    render(this: any) {
      return h('div', { 'data-testid': 'spin' }, this.$slots.default?.())
    },
  },
  NSpace: {
    name: 'NSpace',
     
    render(this: any) {
      return h('div', {}, this.$slots.default?.())
    },
  },
}))

import TImageUpload from '../TImageUpload.vue'

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeFile(name = 'photo.jpg', sizeMb = 1, type = 'image/jpeg'): File {
  const bytes = new Uint8Array(sizeMb * 1024 * 1024)
  return new File([bytes], name, { type })
}

function makeProps(overrides: Record<string, unknown> = {}) {
  return {
    upload: vi.fn().mockResolvedValue({ id: 'file-123', url: 'https://cdn.example.com/photo.jpg' }),
    ...overrides,
  }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('TImageUpload', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  // (a) oversized file → emit error, do NOT call upload
  it('(a) emits error and does not call upload when file exceeds maxSizeMb', async () => {
    const props = makeProps({ maxSizeMb: 2 })
    const wrapper = mount(TImageUpload, { props })

    const input = wrapper.find('input[type="file"]')
    expect(input.exists()).toBe(true)

    // Simulate file selection via the hidden input
    const oversizedFile = makeFile('big.jpg', 3) // 3 MB > 2 MB limit
    Object.defineProperty(input.element, 'files', {
      value: [oversizedFile],
      writable: false,
      configurable: true,
    })
    await input.trigger('change')
    await flushPromises()

    const errorEmit = wrapper.emitted('error')
    expect(errorEmit).toBeTruthy()
    if (errorEmit) {
      expect((errorEmit[0] as string[])[0]).toMatch(/size/i)
    }
    expect(props.upload).not.toHaveBeenCalled()
  })

  // (b) cropper=false + valid file → upload(File) → emit change/update:modelValue/update:fileId
  it('(b) with cropper=false calls upload with File and emits all events', async () => {
    const props = makeProps({ cropper: false })
    const wrapper = mount(TImageUpload, { props })

    const input = wrapper.find('input[type="file"]')
    const validFile = makeFile('photo.jpg', 1)
    Object.defineProperty(input.element, 'files', {
      value: [validFile],
      writable: false,
      configurable: true,
    })
    await input.trigger('change')
    await flushPromises()

    expect(props.upload).toHaveBeenCalledWith(validFile)

    const modelValueEmit = wrapper.emitted('update:modelValue')
    expect(modelValueEmit).toBeTruthy()
    if (modelValueEmit) {
      expect((modelValueEmit[0] as string[])[0]).toBe('https://cdn.example.com/photo.jpg')
    }

    const fileIdEmit = wrapper.emitted('update:fileId')
    expect(fileIdEmit).toBeTruthy()
    if (fileIdEmit) {
      expect((fileIdEmit[0] as (string | undefined)[])[0]).toBe('file-123')
    }

    const changeEmit = wrapper.emitted('change')
    expect(changeEmit).toBeTruthy()
    if (changeEmit) {
      const changePayload = (changeEmit[0] as { id?: string; url: string }[])[0]
      if (changePayload) {
        expect(changePayload.url).toBe('https://cdn.example.com/photo.jpg')
        expect(changePayload.id).toBe('file-123')
      }
    }

    expect(wrapper.emitted('error')).toBeFalsy()
  })

  // (c) upload rejects → emit error, no change
  it('(c) emits error when upload promise rejects and does not emit change', async () => {
    const props = makeProps({
      cropper: false,
      upload: vi.fn().mockRejectedValue(new Error('Network failure')),
    })
    const wrapper = mount(TImageUpload, { props })

    const input = wrapper.find('input[type="file"]')
    const validFile = makeFile('photo.jpg', 1)
    Object.defineProperty(input.element, 'files', {
      value: [validFile],
      writable: false,
      configurable: true,
    })
    await input.trigger('change')
    await flushPromises()

    const errorEmit = wrapper.emitted('error')
    expect(errorEmit).toBeTruthy()
    if (errorEmit) {
      expect((errorEmit[0] as string[])[0]).toMatch(/upload failed/i)
    }
    expect(wrapper.emitted('change')).toBeFalsy()
    expect(wrapper.emitted('update:modelValue')).toBeFalsy()
  })

  // (d) modelValue renders in preview img; shape='circle' applies circle class
  it('(d) renders modelValue in preview img and applies circle class for shape=circle', () => {
    const props = makeProps({
      modelValue: 'https://cdn.example.com/avatar.jpg',
      shape: 'circle',
      cropper: false,
    })
    const wrapper = mount(TImageUpload, { props })

    const img = wrapper.find('img')
    expect(img.exists()).toBe(true)
    expect(img.attributes('src')).toBe('https://cdn.example.com/avatar.jpg')

    const preview = wrapper.find('[data-testid="image-upload-preview"]')
    expect(preview.exists()).toBe(true)
    expect(preview.classes()).toContain('t-image-upload--circle')
  })

  // (e) shape='square' applies square class
  it('(e) applies square class for shape=square', () => {
    const props = makeProps({ shape: 'square', cropper: false })
    const wrapper = mount(TImageUpload, { props })

    const preview = wrapper.find('[data-testid="image-upload-preview"]')
    expect(preview.exists()).toBe(true)
    expect(preview.classes()).toContain('t-image-upload--square')
  })

  // (f) disabled prop prevents file input from being clickable
  it('(f) disabled prop makes preview non-interactive', () => {
    const props = makeProps({ disabled: true, cropper: false })
    const wrapper = mount(TImageUpload, { props })

    const preview = wrapper.find('[data-testid="image-upload-preview"]')
    expect(preview.exists()).toBe(true)
    expect(preview.classes()).toContain('t-image-upload--disabled')
  })

  // (g) wrong MIME type → emit error, no upload
  it('(g) emits error for wrong file type', async () => {
    const props = makeProps({ accept: 'image/*', cropper: false })
    const wrapper = mount(TImageUpload, { props })

    const input = wrapper.find('input[type="file"]')
    const wrongFile = new File(['pdf content'], 'document.pdf', { type: 'application/pdf' })
    Object.defineProperty(input.element, 'files', {
      value: [wrongFile],
      writable: false,
      configurable: true,
    })
    await input.trigger('change')
    await flushPromises()

    const errorEmit = wrapper.emitted('error')
    expect(errorEmit).toBeTruthy()
    if (errorEmit) {
      expect((errorEmit[0] as string[])[0]).toMatch(/type/i)
    }
    expect(props.upload).not.toHaveBeenCalled()
  })
})

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { h } from 'vue'

// ---------------------------------------------------------------------------
// Mock cropperjs - happy-dom has no canvas/toBlob support
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
// cropperjs v2: the <cropper-image> loads async (`$ready`) and must be
// contain-fitted + centred (`$center`) into the canvas before use.
const mockCropperImage = {
  $ready: vi.fn(async () => undefined),
  $center: vi.fn(),
}
const mockCropperInstance = {
  getCropperSelection: vi.fn(() => mockSelection),
  getCropperImage: vi.fn(() => mockCropperImage),
  destroy: vi.fn(),
}
// Must be a real `function` (not an arrow) so `new Cropper(...)` is constructable
// under vitest 4; returning an object from a constructor yields that object.
const MockCropper = vi.fn(function (this: unknown) {
  return mockCropperInstance
})

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
    // happy-dom has no object-URL support; the cropper flow needs it.
    globalThis.URL.createObjectURL = vi.fn(() => 'blob:mock-src')
    globalThis.URL.revokeObjectURL = vi.fn()
    mockSelection.aspectRatio = 0
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

  // (h) cropper flow: on image load, fit + centre the image into the canvas and
  //     configure the selection; on Confirm, render the crop and upload the blob.
  it('(h) fits/centres the image on load and uploads the cropped blob on confirm', async () => {
    const props = makeProps({ cropper: true, aspectRatio: 1 })
    const wrapper = mount(TImageUpload, { props })

    // Select a valid image → opens the cropper modal.
    const input = wrapper.find('input[type="file"]')
    const validFile = makeFile('photo.jpg', 1)
    Object.defineProperty(input.element, 'files', {
      value: [validFile],
      writable: false,
      configurable: true,
    })
    await input.trigger('change')
    await flushPromises()

    // The cropper <img> renders inside the (stubbed) open modal.
    const cropImg = wrapper.find('.t-image-upload__cropper-img')
    expect(cropImg.exists()).toBe(true)

    // Simulate the image finishing load → initCropperOnMount.
    await cropImg.trigger('load')
    await flushPromises()

    // Cropper built once; image waited-on, contain-fitted + centred; selection
    // configured with the requested aspect ratio and reset.
    expect(MockCropper).toHaveBeenCalledTimes(1)
    expect(mockCropperImage.$ready).toHaveBeenCalled()
    expect(mockCropperImage.$center).toHaveBeenCalledWith('contain')
    expect(mockSelection.aspectRatio).toBe(1)
    expect(mockSelection.$reset).toHaveBeenCalled()

    // Upload not fired until Confirm.
    expect(props.upload).not.toHaveBeenCalled()

    // Click Confirm → $toCanvas → toBlob → upload(blob).
    const confirmBtn = wrapper.findAll('button').find((b) => b.text() === 'Confirm')
    expect(confirmBtn).toBeTruthy()
    await confirmBtn!.trigger('click')
    await flushPromises()

    expect(mockSelection.$toCanvas).toHaveBeenCalled()
    expect(props.upload).toHaveBeenCalledTimes(1)
    const uploadArg = (props.upload as ReturnType<typeof vi.fn>).mock.calls[0]?.[0]
    expect(uploadArg).toBeInstanceOf(Blob)

    // Cropper cleaned up + object URL revoked.
    expect(mockCropperInstance.destroy).toHaveBeenCalled()
    expect(globalThis.URL.revokeObjectURL).toHaveBeenCalled()
  })

  // (i) title prop is applied to the preview area as a native tooltip
  it('(i) applies the title prop to the preview area', () => {
    const props = makeProps({ title: 'JPG/PNG, up to 5 MB', cropper: false })
    const wrapper = mount(TImageUpload, { props })
    const preview = wrapper.find('[data-testid="image-upload-preview"]')
    expect(preview.attributes('title')).toBe('JPG/PNG, up to 5 MB')
  })

  // (j) removable + value → remove control renders; click emits `remove` and clears
  it('(j) shows the remove control and emits remove on click when removable with a value', async () => {
    const props = makeProps({
      modelValue: 'https://cdn.example.com/avatar.jpg',
      removable: true,
      cropper: false,
    })
    const wrapper = mount(TImageUpload, { props })

    const removeBtn = wrapper.find('.t-image-upload__remove')
    expect(removeBtn.exists()).toBe(true)

    await removeBtn.trigger('click')

    expect(wrapper.emitted('remove')).toBeTruthy()
    const mv = wrapper.emitted('update:modelValue')
    expect(mv && (mv[0] as string[])[0]).toBe('')
    const fid = wrapper.emitted('update:fileId')
    expect(fid && (fid[0] as (string | undefined)[])[0]).toBeUndefined()
    // Removing must NOT open the file picker (click.stop).
    expect(props.upload).not.toHaveBeenCalled()
  })

  // (k) remove control is hidden without a value, when not removable, or when disabled
  it('(k) hides the remove control unless removable + value + enabled', () => {
    const noValue = mount(TImageUpload, { props: makeProps({ removable: true, cropper: false }) })
    expect(noValue.find('.t-image-upload__remove').exists()).toBe(false)

    const notRemovable = mount(TImageUpload, {
      props: makeProps({ modelValue: 'https://cdn.example.com/a.jpg', cropper: false }),
    })
    expect(notRemovable.find('.t-image-upload__remove').exists()).toBe(false)

    const disabled = mount(TImageUpload, {
      props: makeProps({ modelValue: 'https://cdn.example.com/a.jpg', removable: true, disabled: true, cropper: false }),
    })
    expect(disabled.find('.t-image-upload__remove').exists()).toBe(false)
  })

  // (l) custom width/height (rectangle) is applied to the preview box as inline style
  it('(l) applies custom width/height to the preview box (number → px, string verbatim)', () => {
    const numeric = mount(TImageUpload, { props: makeProps({ width: 240, height: 120, shape: 'square', cropper: false }) })
    const box = numeric.find('[data-testid="image-upload-preview"]')
    expect(box.attributes('style')).toContain('width: 240px')
    expect(box.attributes('style')).toContain('height: 120px')
    expect(box.classes()).toContain('t-image-upload--square')

    const stringSize = mount(TImageUpload, { props: makeProps({ width: '50%', height: '10rem', cropper: false }) })
    const box2 = stringSize.find('[data-testid="image-upload-preview"]')
    expect(box2.attributes('style')).toContain('width: 50%')
    expect(box2.attributes('style')).toContain('height: 10rem')
  })

  // (m) placeholder text prop + #placeholder slot customise the empty state
  it('(m) renders the placeholder text prop, and the #placeholder slot overrides it', () => {
    const withText = mount(TImageUpload, { props: makeProps({ placeholder: 'Upload logo', cropper: false }) })
    expect(withText.find('.t-image-upload__placeholder-text').text()).toBe('Upload logo')
    // default `+` glyph is not shown when a placeholder is provided
    expect(withText.find('.t-image-upload__plus').exists()).toBe(false)

    const withSlot = mount(TImageUpload, {
      props: makeProps({ placeholder: 'ignored', cropper: false }),
      slots: { placeholder: '<div class="custom-ph">Drop image here</div>' },
    })
    expect(withSlot.find('.custom-ph').text()).toBe('Drop image here')
    // slot wins over both the text prop and the default glyph
    expect(withSlot.find('.t-image-upload__placeholder-text').exists()).toBe(false)
    expect(withSlot.find('.t-image-upload__plus').exists()).toBe(false)
  })

  // (n) objectFit prop is applied to the preview image
  it('(n) applies objectFit to the loaded image (default cover, overridable to contain)', () => {
    const cover = mount(TImageUpload, { props: makeProps({ modelValue: 'https://cdn.example.com/a.jpg', cropper: false }) })
    expect(cover.find('.t-image-upload__img').attributes('style')).toContain('object-fit: cover')

    const contain = mount(TImageUpload, {
      props: makeProps({ modelValue: 'https://cdn.example.com/a.jpg', objectFit: 'contain', cropper: false }),
    })
    expect(contain.find('.t-image-upload__img').attributes('style')).toContain('object-fit: contain')
  })
})

import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import TAttachmentPanel from '../../src/components/data/TAttachmentPanel.vue'
import TCommentThread from '../../src/components/data/TCommentThread.vue'

/**
 * The two generic collaboration primitives.
 *
 * They are entity-agnostic on purpose, so what is worth locking is the
 * behaviour that is easy to get subtly wrong: the two-step upload, and not
 * throwing away what someone typed when a post fails.
 */
const stubs = {
  Alert: { name: 'Alert', template: '<div class="n-alert-stub"><slot /></div>' },
  Popconfirm: { name: 'Popconfirm', template: '<div><slot name="trigger" /></div>' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Input: {
    name: 'Input',
    props: ['value'],
    template: '<textarea :value="value" @input="$emit(\'update:value\', $event.target.value)" />',
  },
}

describe('TAttachmentPanel', () => {
  it('uploads then links, in that order', async () => {
    const calls: string[] = []
    const upload = vi.fn(async (f: File) => {
      calls.push('upload')
      return { fileId: 'f1', fileName: f.name, contentType: f.type, fileSize: f.size }
    })
    const attach = vi.fn(async () => { calls.push('attach') })

    const wrapper = mount(TAttachmentPanel, {
      props: { items: [], upload, attach },
      global: { stubs },
    })

    const file = new File(['x'], 'invoice.pdf', { type: 'application/pdf' })
    await (wrapper.vm as unknown as { send: (f: File[]) => Promise<void> }).send([file])
    await flushPromises()

    // Storage first, then the link - the owning module never sees the bytes.
    expect(calls).toEqual(['upload', 'attach'])
    expect(attach).toHaveBeenCalledWith(expect.objectContaining({ fileId: 'f1', fileName: 'invoice.pdf' }))
    expect(wrapper.emitted('changed')).toBeTruthy()
  })

  it('uploads several files one at a time so a server-side cap is attributable', async () => {
    const order: string[] = []
    const upload = vi.fn(async (f: File) => {
      order.push(`up:${f.name}`)
      return { fileId: f.name, fileName: f.name, contentType: f.type, fileSize: 1 }
    })
    const attach = vi.fn(async (l: { fileId: string }) => { order.push(`link:${l.fileId}`) })

    const wrapper = mount(TAttachmentPanel, { props: { items: [], upload, attach }, global: { stubs } })
    await (wrapper.vm as unknown as { send: (f: File[]) => Promise<void> }).send([
      new File(['a'], 'a.pdf'), new File(['b'], 'b.pdf'),
    ])

    expect(order).toEqual(['up:a.pdf', 'link:a.pdf', 'up:b.pdf', 'link:b.pdf'])
  })

  it('surfaces an upload failure instead of failing silently', async () => {
    const upload = vi.fn(async () => { throw new Error('storage is full') })
    const attach = vi.fn()

    const wrapper = mount(TAttachmentPanel, { props: { items: [], upload, attach }, global: { stubs } })
    await (wrapper.vm as unknown as { send: (f: File[]) => Promise<void> }).send([new File(['a'], 'a.pdf')])
    await flushPromises()

    expect(wrapper.text()).toContain('storage is full')
    expect(attach).not.toHaveBeenCalled()
    expect(wrapper.emitted('changed')).toBeFalsy()
  })

  it('hides the remove affordance when the viewer cannot remove', () => {
    const items = [{ id: 'a1', fileId: 'f1', fileName: 'x.pdf', fileSize: 10 }]
    const withRemove = mount(TAttachmentPanel, { props: { items, canRemove: true }, global: { stubs } })
    const without = mount(TAttachmentPanel, { props: { items, canRemove: false }, global: { stubs } })

    expect(withRemove.findAllComponents({ name: 'Popconfirm' }).length).toBe(1)
    expect(without.findAllComponents({ name: 'Popconfirm' }).length).toBe(0)
  })
})

describe('TCommentThread', () => {
  it('keeps the draft when posting fails', async () => {
    const post = vi.fn(async () => { throw new Error('offline') })
    const wrapper = mount(TCommentThread, { props: { items: [], post }, global: { stubs } })

    const vm = wrapper.vm as unknown as { draft: string; submitComment: () => Promise<void> }
    vm.draft = 'a thought worth keeping'
    await vm.submitComment()
    await flushPromises()

    // Losing what someone typed is the least forgivable thing a comment box does.
    expect(vm.draft).toBe('a thought worth keeping')
    expect(wrapper.text()).toContain('offline')
  })

  it('clears the draft once the post lands', async () => {
    const post = vi.fn(async () => undefined)
    const wrapper = mount(TCommentThread, { props: { items: [], post }, global: { stubs } })

    const vm = wrapper.vm as unknown as { draft: string; submitComment: () => Promise<void> }
    vm.draft = 'noted'
    await vm.submitComment()
    await flushPromises()

    expect(vm.draft).toBe('')
    expect(post).toHaveBeenCalledWith('noted')
    expect(wrapper.emitted('changed')).toBeTruthy()
  })

  it('trusts the server on who may delete each comment', () => {
    const items = [
      { id: 'c1', body: 'mine', creationTime: '2026-07-25T00:00:00Z', canDelete: true },
      { id: 'c2', body: 'theirs', creationTime: '2026-07-25T00:01:00Z', canDelete: false },
    ]
    const wrapper = mount(TCommentThread, { props: { items }, global: { stubs } })

    // One delete affordance, not two: the client never re-derives authorship.
    expect(wrapper.findAllComponents({ name: 'Popconfirm' }).length).toBe(1)
  })
})

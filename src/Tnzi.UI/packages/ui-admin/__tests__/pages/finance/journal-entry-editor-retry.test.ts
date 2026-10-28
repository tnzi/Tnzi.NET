import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import JournalEntryEditor from '../../../src/pages/finance/components/JournalEntryEditor.vue'

/**
 * "Save & post" on a new entry is two writes: create the draft, then post it.
 * When the post failed (a closed period, a missing rate) the editor stayed in
 * create mode, so the retry created a second draft, and every further retry
 * another one: drafts someone could later post by mistake. The document
 * editors already continue on the draft their first save created.
 */
type EditorVm = {
  postingDateTs: number
  lines: Array<{ accountId: string | null; debit: number | null; credit: number | null; memo: string }>
  save: (post: boolean) => Promise<void>
}

function mountNew() {
  const createDraft = vi.fn(async () => ({ id: 'je-new', status: 'Draft', lines: [] }))
  const updateDraft = vi.fn(async (id: string) => ({ id, status: 'Draft', lines: [] }))
  const post = vi.fn(async (_id: string) => ({}))
  const bridge = { journals: { createDraft, updateDraft, post } }
  const wrapper = mount(JournalEntryEditor, {
    props: {
      entry: null,
      accountOptions: [{ label: 'Cash', value: 'a-cash' }, { label: 'Revenue', value: 'a-rev' }],
      bridge: bridge as never,
    },
  })
  const vm = wrapper.vm as unknown as EditorVm
  vm.lines = [
    { accountId: 'a-cash', debit: 100, credit: null, memo: '' },
    { accountId: 'a-rev', debit: null, credit: 100, memo: '' },
  ]
  return { wrapper, vm, createDraft, updateDraft, post }
}

describe('JournalEntryEditor - a failed post is retried on the same draft', () => {
  it('★ the retry updates the draft the first attempt created instead of creating another', async () => {
    const { wrapper, vm, createDraft, updateDraft, post } = mountNew()
    await flushPromises()
    post.mockRejectedValueOnce(new Error('The period is closed.'))

    await vm.save(true)
    expect(createDraft).toHaveBeenCalledTimes(1)
    expect(wrapper.emitted('saved')).toBeUndefined()
    // The host is told, so it can show the draft as the record being edited.
    expect(wrapper.emitted('drafted')?.[0]?.[0]).toMatchObject({ id: 'je-new' })

    await vm.save(true)
    expect(createDraft).toHaveBeenCalledTimes(1)
    expect(updateDraft).toHaveBeenCalledWith('je-new', expect.anything())
    expect(post).toHaveBeenLastCalledWith('je-new')
    expect(wrapper.emitted('saved')).toHaveLength(1)
  })

  it('★ a second click while a save is in flight does not start another save', async () => {
    const { vm, createDraft } = mountNew()
    await flushPromises()
    let release: (v: unknown) => void = () => {}
    createDraft.mockImplementationOnce(() => new Promise((r) => { release = r }) as never)

    const first = vm.save(false)
    const second = vm.save(true)
    release({ id: 'je-new', status: 'Draft', lines: [] })
    await Promise.all([first, second])

    expect(createDraft).toHaveBeenCalledTimes(1)
  })
})

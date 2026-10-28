import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * A document's attachments and comments are read on open. A refused or failed
 * read must be said out loud: the empty lists it leaves behind would otherwise
 * read as "nothing attached, nobody commented".
 */
const h = vi.hoisted(() => ({
  attachments: vi.fn(),
  comments: vi.fn(),
  error: vi.fn(),
}))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))
vi.mock('../../../src/services/bridges/storage-bridge', () => ({
  createStorageBridge: () => ({ files: { upload: vi.fn() } }),
}))
vi.mock('../../../src/services/bridges/finance-bridge', () => ({
  createFinanceBridge: () => ({
    collaboration: { attachments: h.attachments, comments: h.comments },
  }),
}))
vi.mock('../../../src/pages/_shared/safe-message', () => ({
  useSafeMessage: () => ({ success: vi.fn(), error: h.error, warning: vi.fn(), info: vi.fn() }),
}))

import DocumentCollaborationPanel from '../../../src/pages/finance/components/DocumentCollaborationPanel.vue'
import { useAdminAuthStore } from '../../../src/stores/useAdminAuthStore'

describe('DocumentCollaborationPanel', () => {
  it('reports a failed attachments or comments read', async () => {
    setActivePinia(createPinia())
    h.attachments.mockRejectedValueOnce(new Error('Attachments are unavailable.'))
    h.comments.mockRejectedValueOnce(new Error('Comments are unavailable.'))

    mount(DocumentCollaborationPanel, {
      props: { docType: 'invoice', docId: 'i1' },
      global: { stubs: { TAttachmentPanel: true, TCommentThread: true } },
    })
    await flushPromises()

    expect(h.error).toHaveBeenCalledWith('Attachments are unavailable.')
    expect(h.error).toHaveBeenCalledWith('Comments are unavailable.')
  })

  // Attachments and comments each have their own read code: seeing a document
  // does not mean seeing its internal discussion. Without the code the read is
  // a certain 403, so it is not sent and its panel is not shown.
  it('★ reads and shows only the halves the viewer may read', async () => {
    setActivePinia(createPinia())
    useAdminAuthStore().setUserInfo({
      id: 'clerk', username: 'clerk', roles: [], permissions: ['finance.document.view', 'finance.attachment.view'],
    } as never)
    h.attachments.mockReset().mockResolvedValue([])
    h.comments.mockReset().mockResolvedValue([])
    h.error.mockReset()

    const wrapper = mount(DocumentCollaborationPanel, {
      props: { docType: 'invoice', docId: 'i1' },
      global: { stubs: { TAttachmentPanel: true, TCommentThread: true } },
    })
    await flushPromises()

    expect(h.attachments).toHaveBeenCalledWith('invoice', 'i1')
    expect(h.comments).not.toHaveBeenCalled()
    expect(h.error).not.toHaveBeenCalled()
    expect(wrapper.findComponent({ name: 'TAttachmentPanel' }).exists()).toBe(true)
    expect(wrapper.findComponent({ name: 'TCommentThread' }).exists()).toBe(false)
  })
})

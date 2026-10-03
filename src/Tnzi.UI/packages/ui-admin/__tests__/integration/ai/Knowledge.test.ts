import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Knowledge integration test - production-grade card grid (TCardPage) with a
 * create/edit modal, a per-card Reindex action, and a document-management
 * drawer (Documents + Search test tabs).
 *
 * The drawer's "open manage" flow calls bridge.knowledge.getDocuments, and the
 * card Reindex action calls bridge.knowledge.reindex - both sub-contracts are
 * mocked. KB rows mirror @tnzi/core KnowledgeBaseDto.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn(), upload: vi.fn() }),
}))

const knowledgeFetch = vi.fn(async () => ({
  items: [
    {
      id: 'kb1',
      name: 'Product Docs',
      description: 'Customer-facing product documentation',
      embeddingProvider: 'openai',
      embeddingModel: 'text-embedding-3-small',
      chunkSize: 1000,
      chunkOverlap: 200,
      documentCount: 87,
      chunkCount: 1024,
      isEnabled: true,
      isUserQueryable: true,
      creationTime: '2026-04-01T00:00:00Z',
    },
    {
      id: 'kb2',
      name: 'Internal Wiki',
      description: 'Engineering knowledge base',
      embeddingProvider: 'openai',
      embeddingModel: 'text-embedding-3-large',
      chunkSize: 1500,
      chunkOverlap: 300,
      documentCount: 312,
      chunkCount: 4096,
      isEnabled: false,
      isUserQueryable: false,
      creationTime: '2026-04-02T00:00:00Z',
    },
  ],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 20,
}))

const getDocuments = vi.fn(async () => ({
  items: [
    {
      id: 'doc1',
      knowledgeBaseId: 'kb1',
      fileName: 'guide.pdf',
      contentType: 'application/pdf',
      fileSize: 524288,
      chunkCount: 42,
      status: 1,
      version: 1,
      creationTime: '2026-04-01T00:00:00Z',
    },
  ],
  totalCount: 1,
  pageIndex: 1,
  pageSize: 20,
}))

const reindexMock = vi.fn(async (_id: string) => ({
  knowledgeBaseId: 'kb1',
  chunkCount: 1024,
  documentCount: 87,
  durationMs: 1500,
}))

vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    knowledge: {
      fetch: knowledgeFetch,
      create: vi.fn(async (data: unknown) => ({ id: 'kb3', ...(data as object) })),
      update: vi.fn(async (id: string, data: unknown) => ({ id, ...(data as object) })),
      delete: vi.fn(async () => undefined),
      reindex: reindexMock,
      getDocuments,
      getDocumentStatus: vi.fn(async (_kbId: string, _docId: string) => ({ id: 'doc1', status: 1 })),
      uploadDocument: vi.fn(async () => ({
        documentId: 'doc2',
        fileName: 'new.txt',
        status: 0,
        chunkCount: 0,
        isDuplicate: false,
      })),
      deleteDocument: vi.fn(async () => undefined),
      searchTest: vi.fn(async () => [
        { content: 'matched chunk', sourceName: 'guide.pdf', score: 0.92, chunkIndex: 0 },
      ]),
    },
  }),
}))

// Reverse lookup "which agents use this knowledge base" (admin/agents/grants/reverse/knowledge).
const usedBy = vi.fn(async (): Promise<unknown[]> => [])
vi.mock('../../../src/services/bridges/agent-grant-bridge', () => ({
  createAgentGrantBridge: () => ({ usedBy, listForAgent: vi.fn(), setEnabled: vi.fn(), remove: vi.fn() }),
}))

import Knowledge from '../../../src/pages/ai/knowledge/Knowledge.vue'

const stubs = {
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  Pagination: { name: 'Pagination', template: '<div class="n-pagination-stub" />' },
  Input: {
    name: 'Input',
    props: ['value'],
    emits: ['update:value'],
    template:
      '<input class="n-input-stub" :value="value" @input="$emit(\'update:value\', $event.target.value)" />',
  },
  InputNumber: {
    name: 'InputNumber',
    props: ['value'],
    emits: ['update:value'],
    template: '<input type="number" class="n-input-number-stub" :value="value" />',
  },
  Switch: { name: 'Switch', props: ['value'], emits: ['update:value'], template: '<button class="n-switch-stub" />' },
  Select: { name: 'Select', props: ['value', 'options'], emits: ['update:value'], template: '<select class="n-select-stub" />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: {
    name: 'Modal',
    props: ['show'],
    emits: ['update:show'],
    template: '<div v-if="show" class="n-modal-stub"><slot /><slot name="footer" /></div>',
  },
  Popover: { name: 'Popover', template: '<div><slot name="trigger" /><slot /></div>' },
  Popconfirm: { name: 'Popconfirm', template: '<div><slot name="trigger" /><slot /></div>' },
  Drawer: { name: 'Drawer', props: ['show'], emits: ['update:show'], template: '<div v-if="show" class="n-drawer-stub"><slot /></div>' },
  DrawerContent: { name: 'DrawerContent', template: '<div class="n-drawer-content-stub"><slot /></div>' },
  Tabs: { name: 'Tabs', props: ['value'], emits: ['update:value'], template: '<div class="n-tabs-stub"><slot /></div>' },
  TabPane: { name: 'TabPane', props: ['name', 'tab'], template: '<div class="n-tab-pane-stub"><slot /></div>' },
  Progress: { name: 'Progress', props: ['percentage'], template: '<div class="n-progress-stub" />' },
  Tag: { name: 'Tag', template: '<span class="n-tag-stub"><slot /></span>' },
  Checkbox: { name: 'Checkbox', template: '<input type="checkbox" />' },
  Form: { name: 'Form', template: '<form><slot /></form>' },
  FormItem: { name: 'FormItem', template: '<div class="form-item"><slot /></div>' },
}

describe('Knowledge page (TCardPage card grid + document drawer)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    knowledgeFetch.mockClear()
    getDocuments.mockClear()
    reindexMock.mockClear()
  })

  it('mounts and fetches knowledge bases on mount', async () => {
    mount(Knowledge, { global: { stubs } })
    await flushPromises()
    expect(knowledgeFetch).toHaveBeenCalledTimes(1)
  })

  it('renders one card per knowledge base', async () => {
    const wrapper = mount(Knowledge, { global: { stubs } })
    await flushPromises()
    expect(wrapper.findAll('.t-entity-card')).toHaveLength(2)
  })

  it('flags the knowledge base that is open to direct user queries', async () => {
    const wrapper = mount(Knowledge, { global: { stubs } })
    await flushPromises()
    const cards = wrapper.findAll('.t-entity-card')
    expect(cards[0]?.text()).toContain('User queryable')
    expect(cards[1]?.text()).not.toContain('User queryable')
  })

  it('cards show knowledge base names and the page title', async () => {
    const wrapper = mount(Knowledge, { global: { stubs } })
    await flushPromises()
    expect(wrapper.text()).toContain('Product Docs')
    expect(wrapper.text()).toContain('Internal Wiki')
    expect(wrapper.text()).toContain('Knowledge')
  })

  it('openManage opens the drawer and loads the knowledge base documents', async () => {
    const wrapper = mount(Knowledge, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      openManage: (row: { id: string; name: string }) => Promise<void>
      manageVisible: boolean
      managed: { id: string } | null
    }
    await vm.openManage({ id: 'kb1', name: 'Product Docs' })
    await flushPromises()
    expect(vm.manageVisible).toBe(true)
    expect(vm.managed?.id).toBe('kb1')
    expect(getDocuments).toHaveBeenCalledTimes(1)
    expect(getDocuments).toHaveBeenCalledWith('kb1')
  })

  it('reindex action calls bridge.knowledge.reindex with the row id', async () => {
    const wrapper = mount(Knowledge, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      onReindex: (row: { id: string }) => Promise<void>
    }
    await vm.onReindex({ id: 'kb1' })
    await flushPromises()
    expect(reindexMock).toHaveBeenCalledWith('kb1')
  })
})

describe('Knowledge page - agents that depend on a knowledge base', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    usedBy.mockReset()
    usedBy.mockResolvedValue([])
  })

  type ManageVm = {
    openManage: (row: { id: string; name: string }) => Promise<void>
    manageTab: string
  }

  it('the Agents tab of the manage drawer loads the reverse lookup when opened', async () => {
    usedBy.mockResolvedValue([{ agentId: 'a1', agentName: 'Support Bot', agentIsEnabled: true }])
    const wrapper = mount(Knowledge, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as ManageVm
    await vm.openManage({ id: 'kb1', name: 'Product Docs' })
    await flushPromises()
    // Documents is the landing tab; the lookup waits until Agents is opened.
    expect(usedBy).not.toHaveBeenCalled()

    vm.manageTab = 'agents'
    await flushPromises()
    expect(usedBy).toHaveBeenCalledWith('knowledge', 'kb1')
    expect(wrapper.text()).toContain('Agents with an active grant: 1')
    expect(wrapper.text()).toContain('Support Bot')
  })

  it('a refused lookup in the Agents tab reads as a failure, not as zero agents', async () => {
    usedBy.mockRejectedValue(new Error('Permission denied'))
    const wrapper = mount(Knowledge, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as ManageVm
    await vm.openManage({ id: 'kb1', name: 'Product Docs' })
    vm.manageTab = 'agents'
    await flushPromises()

    expect(wrapper.text()).toContain('Could not check which agents use this: Permission denied')
    expect(wrapper.text()).not.toContain('No agent has an active grant')
  })

  it('opening the delete confirmation names the dependent agents', async () => {
    usedBy.mockResolvedValue([{ agentId: 'a1', agentName: 'Support Bot', agentIsEnabled: true }])
    const wrapper = mount(Knowledge, { global: { stubs } })
    await flushPromises()
    const deleteConfirms = wrapper
      .findAllComponents({ name: 'Popconfirm' })
      .filter((c) => c.find('.t-grant-usage').exists())
    expect(deleteConfirms).toHaveLength(2)

    deleteConfirms[1]!.vm.$emit('update:show', true)
    await flushPromises()
    expect(usedBy).toHaveBeenCalledWith('knowledge', 'kb2')
    expect(deleteConfirms[1]!.text()).toContain('1 agent(s) have an active grant for this')
    expect(deleteConfirms[1]!.text()).toContain('Support Bot')
  })
})

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import Files from '../../../src/pages/storage/Files.vue'

vi.mock('vue-router', () => ({
  useRoute: () => ({ meta: {}, query: {}, params: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
}))
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

const getTree = vi.fn(async () => [
  { id: 'd1', name: 'Documents', parentId: null, path: '/Documents', sortOrder: 0, fileCount: 1, children: [] },
])
const fetchFiles = vi.fn(async () => ({
  items: [
    { id: 'f1', fileName: 'photo.jpg', originalName: 'photo.jpg', size: 1024, contentType: 'image/jpeg', url: '/files/f1', referenceCount: 0, extension: '.jpg', provider: 'local', folderId: null },
    { id: 'f2', fileName: 'doc.pdf', originalName: 'doc.pdf', size: 2048, contentType: 'application/pdf', url: '/files/f2', referenceCount: 0, extension: '.pdf', provider: 'local', folderId: null },
  ],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 20,
}))

vi.mock('../../../src/services/bridges/storage-bridge', () => ({
  createStorageBridge: () => ({
    files: {
      fetch: fetchFiles,
      delete: vi.fn(async () => undefined),
      downloadUrl: vi.fn(() => '/api/files/f1/download'),
      previewUrl: vi.fn(() => '/api/files/f1/preview'),
      upload: vi.fn(async () => ({ id: 'f-new', url: '/files/f-new' })),
      moveTo: vi.fn(async () => undefined),
      initUpload: vi.fn(async () => ({ uploadId: 'sess-1' })),
      uploadChunk: vi.fn(async () => undefined),
      completeUpload: vi.fn(async () => ({ url: '/files/f-new' })),
    },
    folders: {
      getTree, getById: vi.fn(), create: vi.fn(), update: vi.fn(),
      delete: vi.fn(async () => undefined), move: vi.fn(async () => undefined),
    },
    preview: { canPreview: vi.fn(async () => true), url: vi.fn(async () => '/x') },
    tags: { set: vi.fn(async () => ({})), byTag: vi.fn() },
    metadata: { get: vi.fn(async () => ({})), set: vi.fn(async () => ({})) },
    references: { byFile: vi.fn(async () => []), byEntity: vi.fn(async () => []) },
  }),
}))

// Pass-through TContentPage (renders #actions + default slots) and lightweight
// stubs for the heavy children so we can assert on the explorer / list table.
const TContentPage = { template: '<div class="content-page"><slot name="actions" /><slot /></div>' }
const TFileExplorer = {
  props: ['folders', 'files', 'selectedFileIds', 'loading', 'translate'],
  template: '<div class="explorer" :data-files="files.length" :data-folders="folders.length" />',
}
const TFilePreviewModal = { props: ['show', 'file'], template: '<div class="preview-modal" />' }
const TResponsiveTable = {
  props: ['data'],
  template:
    '<div class="dt" :data-rows="data.length" :data-folders="data.filter(r => r.kind === \'folder\').length" :data-folder-name="(data.find(r => r.kind === \'folder\') || {}).id" />',
}

const stubs = {
  TContentPage,
  TFileExplorer,
  TFilePreviewModal,
  TResponsiveTable,
  TSvgIcon: { template: '<i />' },
  Spin: { template: '<div><slot /></div>' },
  Tree: { template: '<div class="tree" />' },
  Dropdown: { template: '<div />' },
  Modal: { props: ['show'], template: '<div v-if="show"><slot /></div>' },
  Pagination: { template: '<div />' },
  Input: { template: '<input />' },
  Select: { template: '<select />' },
  Button: { template: '<button @click="$emit(\'click\')"><slot /></button>' },
  ButtonGroup: { template: '<div><slot /></div>' },
  Form: { template: '<form><slot /></form>' },
  FormItem: { template: '<div><slot /></div>' },
  InputNumber: { template: '<input type="number" />' },
  Popconfirm: { template: '<div><slot name="trigger" /></div>' },
  DynamicTags: { template: '<div />' },
  Empty: { template: '<div />' },
  Space: { template: '<div><slot /></div>' },
}

async function flush(): Promise<void> {
  await nextTick()
  await new Promise((r) => setTimeout(r, 20))
}

describe('Files page — Finder file manager', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    getTree.mockClear()
    fetchFiles.mockClear()
    try {
      localStorage.removeItem('tnzi-admin:storage-view')
    } catch {
      /* ignore */
    }
  })

  it('loads folders + files on mount and shows the grid explorer (folders as tiles, no left tree)', async () => {
    const wrapper = mount(Files, { global: { stubs } })
    await flush()
    expect(getTree).toHaveBeenCalled()
    expect(fetchFiles).toHaveBeenCalled()
    const explorer = wrapper.find('.explorer')
    expect(explorer.exists()).toBe(true)
    expect(explorer.attributes('data-files')).toBe('2')
    // root view shows the top-level folder as a sub-folder tile (folders inline)
    expect(explorer.attributes('data-folders')).toBe('1')
    // the left folder-tree sidebar is gone
    expect(wrapper.find('.t-storage-file-page__tree').exists()).toBe(false)
  })

  it('renders a breadcrumb with the root crumb', async () => {
    const wrapper = mount(Files, { global: { stubs } })
    await flush()
    expect(wrapper.find('.t-storage-file-page__breadcrumb').exists()).toBe(true)
    expect(wrapper.find('.t-storage-file-page__crumb').exists()).toBe(true)
  })

  it('list view unifies folders + files as table rows (folders first)', async () => {
    localStorage.setItem('tnzi-admin:storage-view', 'list')
    const wrapper = mount(Files, { global: { stubs } })
    await flush()
    const table = wrapper.find('.dt')
    expect(table.exists()).toBe(true)
    // 1 sub-folder row + 2 file rows = 3 rows in one table
    expect(table.attributes('data-rows')).toBe('3')
    expect(table.attributes('data-folders')).toBe('1')
    expect(table.attributes('data-folder-name')).toBe('d1')
    // grid explorer is not rendered in list mode
    expect(wrapper.find('.explorer').exists()).toBe(false)
  })
})

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import Files from '../../../src/pages/storage/Files.vue'
import { useAdminRouteStore } from '../../../src/stores/useAdminRouteStore'

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
    // Backend serializes tags as a single comma-separated string (not an array).
    { id: 'f1', fileName: 'photo.jpg', originalName: 'photo.jpg', size: 1024, contentType: 'image/jpeg', url: '/files/f1', referenceCount: 0, extension: '.jpg', provider: 'local', folderId: null, tags: 'holiday, 2026' },
    { id: 'f2', fileName: 'doc.pdf', originalName: 'doc.pdf', size: 2048, contentType: 'application/pdf', url: '/files/f2', referenceCount: 0, extension: '.pdf', provider: 'local', folderId: null, tags: null },
  ],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 20,
}))
// One record by id: what the drawer re-reads after a version changed the file's content.
const getFile = vi.fn(async (id: string) => ({ id, originalName: `${id}.bin`, size: 4096 }))
// The metadata modal reads the whole map and writes it back whole (PUT replaces).
const getMetadata = vi.fn(async (_id: string): Promise<Record<string, string>> => ({}))
const setMetadata = vi.fn(async (_id: string, _map: Record<string, string>) => ({}))
// Upload paths: a small file goes up whole, a large one in chunks; both then move into the folder.
const uploadFile = vi.fn(async (_file: File, _options?: { isTemporary?: boolean }) => ({ id: 'f-new', url: '/files/f-new' }))
const moveTo = vi.fn(async (_ids: string[], _folderId: string | null) => undefined)
const uploadChunk = vi.fn(async () => undefined)
const completeUpload = vi.fn(async (_uploadId: string) => ({ id: 'f-big', url: '/files/f-big' }))

vi.mock('../../../src/services/bridges/storage-bridge', () => ({
  createStorageBridge: () => ({
    files: {
      fetch: fetchFiles,
      get: getFile,
      delete: vi.fn(async () => undefined),
      downloadUrl: vi.fn(() => '/api/files/f1/download'),
      previewUrl: vi.fn(() => '/api/files/f1/preview'),
      upload: uploadFile,
      moveTo,
      initUpload: vi.fn(async () => ({ uploadId: 'sess-1' })),
      uploadChunk,
      completeUpload,
    },
    folders: {
      getTree, getById: vi.fn(), create: vi.fn(), update: vi.fn(),
      delete: vi.fn(async () => undefined), move: vi.fn(async () => undefined),
    },
    preview: { canPreview: vi.fn(async () => true), url: vi.fn(async () => '/x') },
    tags: { set: vi.fn(async () => ({})), byTag: vi.fn() },
    metadata: { get: getMetadata, set: setMetadata },
    references: { byFile: vi.fn(async () => []), byEntity: vi.fn(async () => []) },
    integrity: { verifyOne: vi.fn(async () => ({ status: 'Healthy' })), batchVerify: vi.fn() },
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

describe('Files page - Finder file manager', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    getTree.mockClear()
    fetchFiles.mockClear()
    getFile.mockClear()
    getMetadata.mockReset()
    getMetadata.mockImplementation(async () => ({}))
    setMetadata.mockClear()
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

  it('uploads library files as permanent records, not temporary uploads', async () => {
    // Nothing references a library file, so a temporary upload would be removed by
    // background cleanup once the retention period passes (chunked uploads were
    // already permanent: a file's lifetime must not depend on its size).
    const wrapper = mount(Files, { global: { stubs } })
    await flush()
    uploadFile.mockClear()

    const file = new File(['x'], 'contract.pdf')
    const input = wrapper.find('input[type="file"]')
    Object.defineProperty(input.element, 'files', { value: [file], configurable: true })
    await input.trigger('change')
    await flush()

    expect(uploadFile).toHaveBeenCalledWith(file, { isTemporary: false })
  })

  it('renders a breadcrumb with the root crumb', async () => {
    const wrapper = mount(Files, { global: { stubs } })
    await flush()
    expect(wrapper.find('.t-storage-file-page__breadcrumb').exists()).toBe(true)
    expect(wrapper.find('.t-storage-file-page__crumb').exists()).toBe(true)
  })

  describe('version history in the detail drawer', () => {
    // The drawer host renders its body while open; the history component is a stub
    // that records the file it was given and can emit `changed` like the real one.
    const TDetailHost = {
      props: ['state', 'title'],
      template: '<div v-if="state.visible.value" class="detail-host"><slot /></div>',
    }
    const TFileVersionHistory = {
      props: ['file', 'translate'],
      emits: ['changed'],
      template: '<div class="version-history" :data-file="file.id" @click="$emit(\'changed\', file.id)" />',
    }
    const drawerStubs = { ...stubs, TDetailHost, TFileVersionHistory }

    async function openDrawer(row?: Record<string, unknown>) {
      const wrapper = mount(Files, { global: { stubs: drawerStubs } })
      await flush()
      const vm = wrapper.vm as unknown as { openDetail: (row: unknown) => void }
      const [first] = (await fetchFiles.mock.results[0].value).items
      vm.openDetail(row ?? first)
      await flush()
      return wrapper
    }

    function drawerFile(wrapper: Awaited<ReturnType<typeof openDrawer>>) {
      return wrapper.findComponent(TFileVersionHistory).props('file') as { id: string; size: number }
    }

    it('shows the opened file\'s version history when the workspace module is loaded', async () => {
      useAdminRouteStore().setAvailableModules(new Set(['storage-workspace']))
      const wrapper = await openDrawer()
      expect(wrapper.find('.version-history').attributes('data-file')).toBe('f1')
    })

    it('leaves the section out when the host did not load Tnzi.Storage.Workspace', async () => {
      useAdminRouteStore().setAvailableModules(new Set(['storage']))
      const wrapper = await openDrawer()
      expect(wrapper.find('.detail-host').exists()).toBe(true)
      expect(wrapper.find('.version-history').exists()).toBe(false)
    })

    it('waits for the module probe before mounting it (it sends a request on mount)', async () => {
      useAdminRouteStore().setModuleSignalPending(true)
      const wrapper = await openDrawer()
      expect(wrapper.find('.version-history').exists()).toBe(false)

      useAdminRouteStore().setModuleSignalPending(false)
      await flush()
      expect(wrapper.find('.version-history').exists()).toBe(true)
    })

    it('re-reads the listing and the record when a version upload or restore changed the current content', async () => {
      const wrapper = await openDrawer()
      const before = fetchFiles.mock.calls.length

      await wrapper.find('.version-history').trigger('click')
      await flush()

      expect(fetchFiles.mock.calls.length).toBe(before + 1)
      expect(getFile).toHaveBeenCalledWith('f1')
      // The drawer stays open on the same file, rebound to the fresh record (the new size).
      expect(drawerFile(wrapper).id).toBe('f1')
      expect(drawerFile(wrapper).size).toBe(4096)
    })

    it('refreshes a deep-linked drawer whose file is not on the current page', async () => {
      // `?detail=view:<id>` can open a file the listing never loaded; looking it up in the
      // page would find nothing and leave the old size on screen.
      const wrapper = await openDrawer({ id: 'f9', originalName: 'elsewhere.pdf', size: 10, referenceCount: 1 })

      await wrapper.find('.version-history').trigger('click')
      await flush()

      expect(getFile).toHaveBeenCalledWith('f9')
      expect(drawerFile(wrapper).id).toBe('f9')
      expect(drawerFile(wrapper).size).toBe(4096)
    })

    it('does not pull the drawer back when the user moved to another file meanwhile', async () => {
      let release: (record: unknown) => void = () => {}
      getFile.mockImplementationOnce((() => new Promise((r) => { release = r })) as never)
      const wrapper = await openDrawer()

      await wrapper.find('.version-history').trigger('click')
      // While the record re-read is in flight, the user opens f2.
      const vm = wrapper.vm as unknown as { openDetail: (row: unknown) => void }
      const second = (await fetchFiles.mock.results[0].value).items[1]
      vm.openDetail(second)
      await flush()
      release({ id: 'f1', originalName: 'photo.jpg', size: 4096 })
      await flush()

      expect(drawerFile(wrapper).id).toBe('f2')
    })
  })

  describe('metadata modal (Save replaces the whole map)', () => {
    // Renders the open overlay's body and footer; only one overlay is open at a time.
    const TDetailHost = {
      props: ['state', 'title'],
      template:
        '<div v-if="state.visible.value" class="detail-host"><slot /><div class="detail-footer"><slot name="footer" :close="state.close" /></div></div>',
    }
    // A disabled native button swallows the click, as NButton does.
    const Button = {
      props: ['disabled', 'loading'],
      emits: ['click'],
      template: '<button :disabled="disabled" @click="$emit(\'click\')"><slot /></button>',
    }
    const metaStubs = { ...stubs, TDetailHost, Button }

    type MetaVm = {
      openMetadataModal: (row: unknown) => void
      saveMetadata: () => Promise<void>
      metadataDetail: { close: () => void }
      metadataRows: Array<{ key: string; value: string }>
    }

    function deferred<T>() {
      let resolve: (value: T) => void = () => {}
      let reject: (reason: unknown) => void = () => {}
      const promise = new Promise<T>((res, rej) => {
        resolve = res
        reject = rej
      })
      return { promise, resolve, reject }
    }

    async function mountPage() {
      const wrapper = mount(Files, { global: { stubs: metaStubs } })
      await flush()
      const items = (await fetchFiles.mock.results[0].value).items
      return { wrapper, vm: wrapper.vm as unknown as MetaVm, first: items[0], second: items[1] }
    }

    function saveButton(wrapper: Awaited<ReturnType<typeof mountPage>>['wrapper']) {
      const buttons = wrapper.findAll('.detail-footer button')
      return buttons[buttons.length - 1]!
    }

    it('saves the map it read, with the user\'s edit, once the read has landed', async () => {
      getMetadata.mockImplementation(async () => ({ author: 'Ann', dept: 'Legal' }))
      const { wrapper, vm, first } = await mountPage()

      vm.openMetadataModal(first)
      await flush()
      expect(saveButton(wrapper).attributes('disabled')).toBeUndefined()
      vm.metadataRows[1]!.value = 'Finance'
      await saveButton(wrapper).trigger('click')
      await flush()

      expect(setMetadata).toHaveBeenCalledWith('f1', { author: 'Ann', dept: 'Finance' })
    })

    it('offers no Save while the read is still in flight', async () => {
      const pending = deferred<Record<string, string>>()
      getMetadata.mockImplementation(() => pending.promise)
      const { wrapper, vm, first } = await mountPage()

      vm.openMetadataModal(first)
      await flush()
      expect(saveButton(wrapper).attributes('disabled')).toBeDefined()
      await saveButton(wrapper).trigger('click')
      await vm.saveMetadata()
      expect(setMetadata).not.toHaveBeenCalled()

      pending.resolve({ author: 'Ann' })
      await flush()
      expect(saveButton(wrapper).attributes('disabled')).toBeUndefined()
    })

    it('offers no Save after a failed read, says so instead of "no metadata", and can retry', async () => {
      getMetadata.mockImplementationOnce(async () => {
        throw new Error('Metadata service unavailable')
      })
      const { wrapper, vm, first } = await mountPage()

      vm.openMetadataModal(first)
      await flush()
      expect(wrapper.find('.detail-host').text()).toContain('Metadata service unavailable')
      expect(saveButton(wrapper).attributes('disabled')).toBeDefined()
      // Adding a key and saving would otherwise PUT just that key and erase the rest.
      await vm.saveMetadata()
      expect(setMetadata).not.toHaveBeenCalled()

      getMetadata.mockImplementation(async () => ({ author: 'Ann' }))
      await wrapper.find('.metadata-load-error button').trigger('click')
      await flush()
      expect(wrapper.find('.metadata-load-error').exists()).toBe(false)
      await saveButton(wrapper).trigger('click')
      await flush()
      expect(setMetadata).toHaveBeenCalledWith('f1', { author: 'Ann' })
    })

    it('never shows (or saves) a late answer for the file opened before', async () => {
      const forFirst = deferred<Record<string, string>>()
      const forSecond = deferred<Record<string, string>>()
      getMetadata.mockImplementation((id: string) => (id === 'f1' ? forFirst.promise : forSecond.promise))
      const { wrapper, vm, first, second } = await mountPage()

      vm.openMetadataModal(first)
      await flush()
      vm.metadataDetail.close()
      await flush()
      vm.openMetadataModal(second)
      await flush()

      // The first file's answer lands while the second file's read is still out.
      forFirst.resolve({ owner: 'from-f1' })
      await flush()
      expect(vm.metadataRows).toEqual([])
      expect(saveButton(wrapper).attributes('disabled')).toBeDefined()

      forSecond.resolve({ owner: 'from-f2' })
      await flush()
      await saveButton(wrapper).trigger('click')
      await flush()
      expect(setMetadata).toHaveBeenCalledTimes(1)
      expect(setMetadata).toHaveBeenCalledWith('f2', { owner: 'from-f2' })
    })

    it('keeps the second file\'s rows when the first file\'s answer arrives after them', async () => {
      const forFirst = deferred<Record<string, string>>()
      getMetadata.mockImplementation((id: string) => (id === 'f1' ? forFirst.promise : Promise.resolve({ owner: 'from-f2' })))
      const { wrapper, vm, first, second } = await mountPage()

      vm.openMetadataModal(first)
      await flush()
      vm.metadataDetail.close()
      await flush()
      vm.openMetadataModal(second)
      await flush()
      forFirst.resolve({ owner: 'from-f1' })
      await flush()

      expect(vm.metadataRows).toEqual([{ key: 'owner', value: 'from-f2' }])
      await saveButton(wrapper).trigger('click')
      await flush()
      expect(setMetadata).toHaveBeenCalledWith('f2', { owner: 'from-f2' })
    })
  })

  // Clicking folder A then folder B: A's listing arriving last must not be shown
  // under B's breadcrumb, where delete and move would act on files that are
  // somewhere else.
  it('★ shows the files of the folder opened last when an earlier listing answers late', async () => {
    const wrapper = mount(Files, { global: { stubs } })
    await flush()
    const answers = new Map<string, (value: unknown) => void>()
    const original = fetchFiles.getMockImplementation()!
    fetchFiles.mockImplementation(((query: { filters?: { folderId?: string } }) =>
      new Promise((resolve) => { answers.set(String(query.filters?.folderId), resolve) })) as never)
    try {
      const vm = wrapper.vm as unknown as { openFolder: (id: string) => void; files: Array<{ id: string }> }
      vm.openFolder('A')
      vm.openFolder('B')
      await flush()

      answers.get('B')!({ items: [{ id: 'in-B' }], totalCount: 1, pageIndex: 1, pageSize: 20 })
      await flush()
      answers.get('A')!({ items: [{ id: 'in-A' }], totalCount: 1, pageIndex: 1, pageSize: 20 })
      await flush()

      expect(vm.files.map((f) => f.id)).toEqual(['in-B'])
    } finally {
      fetchFiles.mockImplementation(original)
    }
  })

  describe('uploading into a folder', () => {
    // A stand-in for a large file: only `size` and `slice` are read on the chunked path.
    const largeFile = (name: string): File =>
      ({ name, size: 120 * 1024 * 1024, slice: () => new Blob(['x']) }) as unknown as File

    beforeEach(() => {
      uploadFile.mockClear()
      moveTo.mockClear()
      uploadChunk.mockClear()
      completeUpload.mockClear()
      useAdminRouteStore().setAvailableModules(new Set(['storage', 'storage-workspace']))
    })

    it('files a large (chunked) upload into the folder it was dropped on, like a small one', async () => {
      const wrapper = mount(Files, { global: { stubs } })
      await flush()
      const vm = wrapper.vm as unknown as { uploadFiles: (list: File[], target: string | null) => Promise<void> }

      await vm.uploadFiles([largeFile('contract.pdf')], 'd1')

      expect(uploadChunk).toHaveBeenCalled()
      expect(completeUpload).toHaveBeenCalledWith('sess-1')
      expect(moveTo).toHaveBeenCalledWith(['f-big'], 'd1')
    })

    it('leaves a large upload at the root when it was made at the root', async () => {
      const wrapper = mount(Files, { global: { stubs } })
      await flush()
      const vm = wrapper.vm as unknown as { uploadFiles: (list: File[], target: string | null) => Promise<void> }

      await vm.uploadFiles([largeFile('contract.pdf')], null)

      expect(completeUpload).toHaveBeenCalledWith('sess-1')
      expect(moveTo).not.toHaveBeenCalled()
    })
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

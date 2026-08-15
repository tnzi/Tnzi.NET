/**
 * `useChildCollection` - the detail-page analogue of `useCrudPage` for a nested
 * child collection (matter parties / key-dates / documents / notes …).
 *
 * A multi-section detail page otherwise hand-wires, per child collection, the
 * identical shape: a `list` ref, an add/edit overlay open-state + editing item,
 * `openCreate`/`openEdit`/`save`/`remove`, a per-action busy flag, and an
 * explicit reload-after-write. This composable bundles all of it - fetch into
 * the section, add/edit overlay, delete, optimistic reload, per-action busy,
 * and `.create/.update/.delete` permission gating - so a detail page declares
 * the endpoints once instead of ~30 lines per collection.
 *
 *   const parties = useChildCollection({
 *     fetch: () => bridge.parties.byParent(matterId),
 *     create: (d) => bridge.parties.create({ ...d, matterId }),
 *     update: (id, d) => bridge.parties.update(id, d),
 *     remove: (id) => bridge.parties.delete(id),
 *     permission: 'crm.matter.party',
 *   })
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { canAction, normalizeCrudPermission, type CrudActionPermissions } from './permission-gates'

export interface UseChildCollectionOptions<TItem, TCreate, TUpdate, TId> {
  /** Fetch the children (e.g. `() => bridge.byParent(parentId)`). Re-run after each write. */
  fetch: () => Promise<TItem[]>
  create?: (data: TCreate) => Promise<unknown>
  update?: (id: TId, data: TUpdate) => Promise<unknown>
  remove?: (id: TId) => Promise<unknown>
  /** Extract the id from an item. Default: `item.id`. */
  getId?: (item: TItem) => TId
  /**
   * Load on creation (default `true`). Pass `false` when the parent id isn't
   * known yet - then call `load()` once it is.
   */
  autoLoad?: boolean
  /**
   * Operation-permission base → derives `canCreate`/`canUpdate`/`canDelete`
   * (string `'x'` → `x.create`/`x.update`/`x.delete`, or the object form).
   * Same fail-open semantics as `useCrudPage`.
   */
  permission?: string | CrudActionPermissions
  /** Called when `load()` fails; the error is also exposed on `error`. */
  onError?: (error: unknown) => void
}

export interface UseChildCollectionReturn<TItem, TCreate, TUpdate, TId> {
  /** The loaded children (a section renders these). */
  items: Ref<TItem[]>
  loading: Ref<boolean>
  saving: Ref<boolean>
  removing: Ref<boolean>
  /** Last load error (null when the most recent load succeeded). */
  error: Ref<unknown>
  /** Add/edit overlay open state. */
  formOpen: Ref<boolean>
  mode: Ref<'create' | 'edit'>
  editingItem: Ref<TItem | null>
  editingId: ComputedRef<TId | null>
  /** Write-affordance visibility = data callback exists && action permission held. */
  canCreate: ComputedRef<boolean>
  canUpdate: ComputedRef<boolean>
  canDelete: ComputedRef<boolean>
  load: () => Promise<void>
  /** Alias for `load` (symmetry with `useCrudPage.refresh`). */
  refresh: () => Promise<void>
  openCreate: () => void
  openEdit: (item: TItem) => void
  close: () => void
  /** Create (in `create` mode) or update the editing item (in `edit` mode), then reload + close. */
  save: (data: TCreate | TUpdate) => Promise<void>
  /** Delete by id or by item, then reload. */
  remove: (idOrItem: TId | TItem) => Promise<void>
}

export function useChildCollection<TItem, TCreate = Partial<TItem>, TUpdate = Partial<TItem>, TId = string>(
  options: UseChildCollectionOptions<TItem, TCreate, TUpdate, TId>,
): UseChildCollectionReturn<TItem, TCreate, TUpdate, TId> {
  const getId = options.getId ?? ((it: TItem) => (it as { id: TId }).id)
  const perms = normalizeCrudPermission(options.permission)

  const items = ref([]) as Ref<TItem[]>
  const loading = ref(false)
  const saving = ref(false)
  const removing = ref(false)
  const error = ref<unknown>(null)
  const formOpen = ref(false)
  const mode = ref<'create' | 'edit'>('create')
  const editingItem = ref<TItem | null>(null) as Ref<TItem | null>
  const editingId = computed<TId | null>(() => (editingItem.value ? getId(editingItem.value) : null))

  const canCreate = computed(() => !!options.create && canAction(perms.create))
  const canUpdate = computed(() => !!options.update && canAction(perms.update))
  const canDelete = computed(() => !!options.remove && canAction(perms.delete))

  async function load(): Promise<void> {
    loading.value = true
    error.value = null
    try {
      items.value = await options.fetch()
    } catch (e) {
      // Surface via `error` + `onError` instead of throwing: the autoLoad
      // fire-and-forget below must not become an unhandled rejection, and a
      // failed reload after a write must not mask the (successful) write.
      error.value = e
      options.onError?.(e)
    } finally {
      loading.value = false
    }
  }

  function openCreate(): void {
    mode.value = 'create'
    editingItem.value = null
    formOpen.value = true
  }
  function openEdit(item: TItem): void {
    mode.value = 'edit'
    editingItem.value = item
    formOpen.value = true
  }
  function close(): void {
    formOpen.value = false
  }

  async function save(data: TCreate | TUpdate): Promise<void> {
    saving.value = true
    try {
      if (mode.value === 'edit') {
        // Never fall through to create from an edit form.
        if (!options.update || !editingItem.value) return
        await options.update(getId(editingItem.value), data as TUpdate)
      } else {
        if (!options.create) return
        await options.create(data as TCreate)
      }
      formOpen.value = false
      await load()
    } finally {
      saving.value = false
    }
  }

  async function remove(idOrItem: TId | TItem): Promise<void> {
    if (!options.remove) return
    const id =
      typeof idOrItem === 'object' && idOrItem !== null ? getId(idOrItem as TItem) : (idOrItem as TId)
    removing.value = true
    try {
      await options.remove(id)
      await load()
    } finally {
      removing.value = false
    }
  }

  if (options.autoLoad !== false) void load()

  return {
    items,
    loading,
    saving,
    removing,
    error,
    formOpen,
    mode,
    editingItem,
    editingId,
    canCreate,
    canUpdate,
    canDelete,
    load,
    refresh: load,
    openCreate,
    openEdit,
    close,
    save,
    remove,
  }
}

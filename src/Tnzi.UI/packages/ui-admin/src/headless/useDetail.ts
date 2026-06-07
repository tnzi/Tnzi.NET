import { ref, computed, type Ref } from 'vue'
import { useRoute, useRouter, type RouteLocationNormalizedLoaded, type Router } from 'vue-router'
import { useFormModal, type FormModalMode } from './useFormModal'

export type DetailMode = 'modal' | 'drawer' | 'page'
export type DetailAction = FormModalMode // 'create' | 'edit' | 'view'
export type DetailLayout = 'plain' | 'tabs' | 'side'

export interface DetailSection {
  key: string
  label: string
  icon?: string
  group?: string
  disabled?: boolean
}

export interface UseDetailOptions<T> {
  /** Presentation mode. Default 'modal'. */
  mode?: DetailMode
  sections?: DetailSection[]
  defaultSection?: string
  /** Load a record when opened with an id (view/edit, and page-mode deep links). */
  loadData?: (id: string | number) => Promise<T>
  /** Persist on submit. */
  submitData?: (action: DetailAction, data: T) => Promise<void>
  /** Page-mode routing target. Required for `mode: 'page'` open()/close(). */
  pageRoute?: { name: string; idParam?: string }
  /** Page mode: keep the active section in a route query param (`?section=`). */
  syncSectionToUrl?: boolean
  /**
   * Derive the route id from an object payload in page mode. Required if you
   * call `open(action, fullRecord)` (not a bare id) while `mode: 'page'`,
   * since the id must go into the route params. Ignored in modal/drawer mode.
   */
  getId?: (data: T) => string | number
}

export interface UseDetailReturn<T> {
  mode: Ref<DetailMode>
  action: Ref<DetailAction | null>
  visible: Ref<boolean>
  data: Ref<T | null>
  loading: Ref<boolean>
  error: Ref<Error | null>
  activeSection: Ref<string | null>
  open: (action: DetailAction, payload?: T | string | number | null) => Promise<void>
  close: () => void
  submit: () => Promise<void>
  setSection: (key: string) => void
}

function isId(v: unknown): v is string | number {
  return typeof v === 'string' || typeof v === 'number'
}

function tryGetRoute(): RouteLocationNormalizedLoaded | undefined {
  try {
    return useRoute() as RouteLocationNormalizedLoaded
  } catch {
    return undefined
  }
}

function tryGetRouter(): Router | undefined {
  try {
    return useRouter() as Router
  } catch {
    return undefined
  }
}

export function useDetail<T = unknown>(options: UseDetailOptions<T> = {}): UseDetailReturn<T> {
  const mode = ref<DetailMode>(options.mode ?? 'modal')
  const form = useFormModal<T>()
  const loading = ref(false)
  const error = ref<Error | null>(null)

  // Section nav — initialise from the URL in page mode when syncing, else default.
  const route = tryGetRoute()
  const router = tryGetRouter()
  const initialSection =
    (options.syncSectionToUrl ? (route?.query?.section as string | undefined) : undefined) ??
    options.defaultSection ??
    options.sections?.[0]?.key ??
    null
  const activeSection = ref<string | null>(initialSection)

  async function loadIfNeeded(payload?: T | string | number | null): Promise<T | null> {
    if (payload == null) return null
    if (isId(payload)) {
      if (!options.loadData) return null
      loading.value = true
      error.value = null
      try {
        return await options.loadData(payload)
      } catch (e) {
        error.value = e instanceof Error ? e : new Error(String(e))
        return null
      } finally {
        loading.value = false
      }
    }
    return payload
  }

  async function open(action: DetailAction, payload?: T | string | number | null): Promise<void> {
    if (mode.value === 'page' && options.pageRoute && router) {
      // Page mode: navigate to the detail route; the route component builds its
      // own useDetail and hydrates from params/query on mount.
      const idParam = options.pageRoute.idParam ?? 'id'
      // Resolve the route id: a bare id payload is used directly; an object
      // payload is run through getId. 'create' legitimately has no id.
      const id = isId(payload)
        ? payload
        : payload != null && options.getId
          ? options.getId(payload)
          : undefined
      const params = id != null ? { [idParam]: String(id) } : {}
      await router.push({ name: options.pageRoute.name, params, query: { action } })
      return
    }
    const loaded = await loadIfNeeded(payload)
    form.open(action, loaded)
  }

  function close(): void {
    if (mode.value === 'page' && router) {
      router.back()
      return
    }
    form.close()
  }

  async function submit(): Promise<void> {
    if (!form.formData.value || !form.mode.value) return
    if (options.submitData) {
      await options.submitData(form.mode.value, form.formData.value)
    } else if (form.mode.value !== 'view') {
      // No persistence wired for an editable detail — do not fake success by
      // closing. Surfaces a misconfiguration instead of silently discarding.
      return
    }
    form.close()
  }

  function setSection(key: string): void {
    activeSection.value = key
    if (options.syncSectionToUrl && mode.value === 'page' && router && route) {
      void router.replace({ query: { ...route.query, section: key } })
    }
  }

  return {
    mode,
    action: computed(() => form.mode.value) as Ref<DetailAction | null>,
    visible: form.visible,
    data: form.formData,
    loading,
    error,
    activeSection,
    open,
    close,
    submit,
    setSection,
  }
}

import { ref, toRaw, type Ref } from 'vue'

export type FormModalMode = 'create' | 'edit' | 'view'

export interface UseFormModalReturn<T> {
  visible: Ref<boolean>
  mode: Ref<FormModalMode | null>
  formData: Ref<T | null>
  open: (mode: FormModalMode, initial?: T | null) => void
  close: () => void
  confirm: () => Promise<T | null>
}

// `structuredClone` cannot serialise Vue reactive Proxy (DataCloneError, by spec).
// Strip Proxy via `toRaw` first, then prefer structuredClone (preserves Date/Map/Set);
// fall back to JSON round-trip for anything structuredClone still rejects (functions, etc.).
function cloneInitial<T>(initial: T | null | undefined): T | null {
  if (initial == null) return null
  const raw = toRaw(initial) as T
  if (typeof structuredClone === 'function') {
    try {
      return structuredClone(raw)
    } catch {
      // fall through to JSON
    }
  }
  return JSON.parse(JSON.stringify(raw)) as T
}

export function useFormModal<T = unknown>(): UseFormModalReturn<T> {
  const visible = ref(false)
  const mode = ref<FormModalMode | null>(null)
  const formData = ref<T | null>(null) as Ref<T | null>

  function open(nextMode: FormModalMode, initial: T | null = null): void {
    mode.value = nextMode
    formData.value = cloneInitial(initial)
    visible.value = true
  }

  function close(): void {
    visible.value = false
    mode.value = null
    formData.value = null
  }

  async function confirm(): Promise<T | null> {
    if (!visible.value) return null
    return formData.value
  }

  return {
    visible,
    mode,
    formData,
    open,
    close,
    confirm,
  }
}

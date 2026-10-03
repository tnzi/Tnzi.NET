import { ref, type Ref } from 'vue'

/**
 * The full record behind a list page's read-only `#detail` drawer.
 *
 * A finance list row is the summary the list endpoint returns; the drawer needs
 * the whole document (its lines, extraction fields, file). The crud engine owns
 * WHICH record is open (`crud.openView` / `?detail=view:<id>`) and calls the
 * page's `onView`; this holds WHAT that record looks like in full.
 *
 * Loads are last-wins: opening B while A is still in flight must not let A's
 * late response paint B's drawer.
 */
export interface ViewedRecord<T> {
  /** The loaded record, or null while loading / after a failed load. */
  readonly record: Ref<T | null>
  /** Load the record with this id, replacing whatever was shown. */
  load: (id: string) => Promise<void>
}

export function useViewedRecord<T>(
  getById: (id: string) => Promise<T | null>,
  onError: (message: string) => void,
): ViewedRecord<T> {
  const record = ref<T | null>(null) as Ref<T | null>
  let seq = 0

  async function load(id: string): Promise<void> {
    const mine = ++seq
    record.value = null
    if (!id) return
    try {
      const loaded = await getById(id)
      if (mine === seq) record.value = loaded
    } catch (error) {
      if (mine === seq) onError(error instanceof Error ? error.message : String(error))
    }
  }

  return { record, load }
}

/**
 * The few words the controls say themselves (a busy state's screen reader
 * text, a retry button, a close button), in the app's language.
 *
 * Taken from the catalogue an app provides (`createAiI18n`) when there is one,
 * otherwise English. Read through the injection key alone, so the controls
 * entry does not carry a catalogue (`i18n/key.ts`).
 */
import { computed, inject, type ComputedRef } from 'vue'
import { AI_I18N_KEY } from '../../i18n/key'

export interface ControlsText {
  loading: string
  retry: string
  close: string
  cancel: string
  confirm: string
  /** A column's resize handle; `{name}` is the column. */
  resizeColumn: string
  /** The button of `TInfoTip`. */
  moreInfo: string
  /** Something going on, with no words of its own (`TSegmented`'s `running`). */
  running: string
  /** `TSearchSelect`: its search box, an empty result, its clear button, a failed load. */
  search: string
  noMatches: string
  clear: string
  loadFailed: string
}

const ENGLISH: ControlsText = {
  loading: 'Loading...',
  retry: 'Retry',
  close: 'Close',
  cancel: 'Cancel',
  confirm: 'Confirm',
  resizeColumn: 'Resize column {name}',
  moreInfo: 'More information',
  running: 'Working',
  search: 'Search',
  noMatches: 'No matches',
  clear: 'Clear',
  loadFailed: 'Could not load',
}

export function useControlsText(): ComputedRef<ControlsText> {
  const messages = inject(AI_I18N_KEY, null)
  return computed(() => {
    const common = messages?.value.common
    if (!common) return ENGLISH
    return {
      loading: common.loading,
      retry: common.retry,
      close: common.close,
      cancel: common.cancel,
      confirm: common.confirm,
      resizeColumn: common.resizeColumn ?? ENGLISH.resizeColumn,
      moreInfo: common.moreInfo ?? ENGLISH.moreInfo,
      running: common.running ?? ENGLISH.running,
      search: common.search ?? ENGLISH.search,
      noMatches: common.noMatches ?? ENGLISH.noMatches,
      clear: common.clear ?? ENGLISH.clear,
      loadFailed: common.loadFailed ?? ENGLISH.loadFailed,
    }
  })
}

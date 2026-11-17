/**
 * The words `TImageViewer` says (its buttons, its count, a picture that failed), in the app's
 * language.
 *
 * Taken from the catalogue an app provides (`createAiI18n`, `createAiI18nPlugin`) when there is
 * one, otherwise English. Read through the injection key alone, as the controls read theirs
 * (`../controls/controls-text.ts`), so `@tnzi/ui-ai/dialogs`, which exports the viewer, does not
 * carry the whole English catalogue. A catalogue written before a word existed falls back to the
 * English of that word.
 */
import { computed, inject, type ComputedRef } from 'vue'
import { AI_I18N_KEY } from '../../i18n/key'

export interface ImageViewerText {
  /** The viewer's accessible name when the picture has none. */
  label: string
  previous: string
  next: string
  /** `{index}` / `{count}`. */
  counter: string
  failed: string
  zoomIn: string
  zoomOut: string
  /** The zoom level button, which fits the picture to the screen again. */
  fit: string
  /** `{percent}`. */
  zoomLevel: string
  close: string
}

const ENGLISH: ImageViewerText = {
  label: 'Image',
  previous: 'Previous image',
  next: 'Next image',
  counter: '{index} / {count}',
  failed: "This image couldn't be loaded",
  zoomIn: 'Zoom in',
  zoomOut: 'Zoom out',
  fit: 'Fit to screen',
  zoomLevel: '{percent}%',
  close: 'Close',
}

export function useImageViewerText(): ComputedRef<ImageViewerText> {
  const messages = inject(AI_I18N_KEY, null)
  return computed(() => {
    const catalogue = messages?.value
    if (!catalogue) return ENGLISH
    const viewer: Partial<ImageViewerText> = catalogue.imageViewer ?? {}
    return {
      label: viewer.label ?? ENGLISH.label,
      previous: viewer.previous ?? ENGLISH.previous,
      next: viewer.next ?? ENGLISH.next,
      counter: viewer.counter ?? ENGLISH.counter,
      failed: viewer.failed ?? ENGLISH.failed,
      zoomIn: viewer.zoomIn ?? ENGLISH.zoomIn,
      zoomOut: viewer.zoomOut ?? ENGLISH.zoomOut,
      fit: viewer.fit ?? ENGLISH.fit,
      zoomLevel: viewer.zoomLevel ?? ENGLISH.zoomLevel,
      close: catalogue.common?.close ?? ENGLISH.close,
    }
  })
}

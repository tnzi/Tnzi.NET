/**
 * The page of `imageViewer.browser.test.ts`: a page of controls that shows `TImageViewer` from the
 * built `@tnzi/ui-ai/dialogs` and nothing else of the package. Its HTML links `controls.css`
 * alone. The test drives it through `window.viewer`.
 */
import { createApp, defineComponent, h, ref } from 'vue'
import { disableRemoteIcons } from '../../../dist/controls.js'
import { TImageViewer } from '../../../dist/dialogs.js'

disableRemoteIcons()

const picture = (fill: string) =>
  `data:image/svg+xml,${encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" width="800" height="600"><rect width="800" height="600" fill="${fill}"/><circle cx="400" cy="300" r="120" fill="white"/></svg>`)}`

const images = [
  { src: picture('#2f6fd6'), alt: 'Row 1' },
  { src: picture('#d6762f'), alt: 'Row 2' },
]
const index = ref<number | null>(null)

createApp(
  defineComponent({
    setup: () => () =>
      h(TImageViewer, { images, index: index.value, 'onUpdate:index': (next: number | null) => (index.value = next) }),
  }),
).mount('#app')

;(window as unknown as { viewer: unknown }).viewer = {
  open: (at: number) => (index.value = at),
  index: () => index.value,
}

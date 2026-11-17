/**
 * The package's layering scale in code: which z-index a surface takes.
 *
 * The values are tokens in `styles/index.css` (`--tnzi-ai-z-sticky` / `-drawer`
 * / `-dialog` / `-popover` / `-toast` / `-tooltip`); this module only adds what
 * a token cannot say, the place of a dialog in the stack of open dialogs.
 */
import { getCurrentScope, onScopeDispose, ref, watch, type Ref } from 'vue'

export type Layer = 'sticky' | 'drawer' | 'dialog' | 'popover' | 'toast' | 'tooltip'

const FALLBACK: Record<Layer, number> = {
  sticky: 10,
  drawer: 50,
  dialog: 1000,
  popover: 2000,
  toast: 3000,
  tooltip: 3100,
}

/** The z-index of a layer, as a CSS value that follows its token. */
export function layerZIndex(layer: Layer, offset = 0): string {
  const base = `var(--tnzi-ai-z-${layer}, ${FALLBACK[layer]})`
  return offset === 0 ? base : `calc(${base} + ${offset})`
}

/* The depths held by open dialogs. A dialog opening takes one more than the
   deepest held, so the one opened last is on top whatever its place in the
   document (a confirmation whose host is mounted with the app, before the
   dialog that asked for it, must not open under that dialog). Released depths
   are free again, so the values stay as small as the number of dialogs open
   at once, well under the popover layer. */
const held = new Set<number>()

/** Take the depth for a dialog that is opening now. */
export function acquireDialogDepth(): number {
  const depth = held.size === 0 ? 1 : Math.max(...held) + 1
  held.add(depth)
  return depth
}

/** Give a depth back when its dialog closes. */
export function releaseDialogDepth(depth: number): void {
  held.delete(depth)
}

/**
 * The z-index of a modal surface while `active()` is true: the dialog layer
 * plus its depth in the stack of open dialogs. The value is kept after closing
 * (a leave transition still draws the surface) and the depth is released.
 */
export function useDialogLayer(active: () => boolean): Readonly<Ref<string>> {
  const zIndex = ref(layerZIndex('dialog', 1))
  let depth: number | null = null

  function release(): void {
    if (depth !== null) releaseDialogDepth(depth)
    depth = null
  }

  watch(
    active,
    (open) => {
      if (open && depth === null) {
        depth = acquireDialogDepth()
        zIndex.value = layerZIndex('dialog', depth)
      } else if (!open) {
        release()
      }
    },
    { immediate: true },
  )

  if (getCurrentScope()) onScopeDispose(release)
  return zIndex
}

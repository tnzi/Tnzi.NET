/**
 * A page of the product's that fills `TChatApp`'s view area and scrolls inside itself (an app in
 * an iframe at `height: 100%`, a mail client with panes of its own) says so by calling
 * `useChatViewFill()` in its setup. While it is on screen the view area neither scrolls nor keeps
 * room for a scrollbar: an ordinary page keeps that room whether or not it overflows (so moving
 * between a long page and a short one does not slide it sideways), which beside a page that never
 * lets the area scroll is only an empty strip along its right edge.
 *
 * The claim lasts while the component is mounted and active: a page kept by `<KeepAlive>` lets go
 * when it is deactivated and claims again when it comes back. Outside a `TChatApp` it does
 * nothing.
 */
import {
  getCurrentInstance,
  inject,
  onActivated,
  onBeforeUnmount,
  onDeactivated,
  onMounted,
  provide,
  toValue,
  watch,
  type InjectionKey,
  type MaybeRefOrGetter,
} from 'vue'

/** What `TChatApp` hands its pages: a claim on its view area, and the claim's release. */
export interface ChatViewFillControl {
  /** The calling page fills the view area until the returned release is called. */
  claim: () => () => void
}

export const CHAT_VIEW_FILL_KEY: InjectionKey<ChatViewFillControl> = Symbol('tnzi-chat-view-fill')

export function provideChatViewFill(control: ChatViewFillControl): void {
  provide(CHAT_VIEW_FILL_KEY, control)
}

/**
 * Call in the setup of a page that fills the view area and scrolls itself. `fills` (default
 * `true`) may be a ref or getter, for a page that fills it only in some of its states.
 */
export function useChatViewFill(fills: MaybeRefOrGetter<boolean> = true): void {
  const control = inject(CHAT_VIEW_FILL_KEY, null)
  if (!control || !getCurrentInstance()) return

  let release: (() => void) | null = null
  let active = false
  const sync = (): void => {
    const wanted = active && toValue(fills)
    if (wanted && !release) release = control.claim()
    else if (!wanted && release) {
      release()
      release = null
    }
  }
  const setActive = (value: boolean): void => {
    active = value
    sync()
  }

  watch(() => toValue(fills), sync)
  onMounted(() => setActive(true))
  onActivated(() => setActive(true))
  onDeactivated(() => setActive(false))
  onBeforeUnmount(() => setActive(false))
}

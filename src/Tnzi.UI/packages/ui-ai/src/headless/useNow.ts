/**
 * `useNow` - the current time, ticking once a second while `active` says so (a running task's clock), still
 * otherwise. The timer is released when `active` turns false and when the calling scope ends.
 */
import { getCurrentScope, onScopeDispose, ref, watch, type Ref } from 'vue'

export function useNow(active: () => boolean, intervalMs = 1000): Readonly<Ref<number>> {
  const now = ref(Date.now())
  let timer: ReturnType<typeof setInterval> | undefined

  const stop = (): void => {
    if (timer) clearInterval(timer)
    timer = undefined
  }

  watch(
    active,
    (isActive) => {
      stop()
      now.value = Date.now()
      if (isActive) timer = setInterval(() => (now.value = Date.now()), intervalMs)
    },
    { immediate: true },
  )

  if (getCurrentScope()) onScopeDispose(stop)
  return now
}

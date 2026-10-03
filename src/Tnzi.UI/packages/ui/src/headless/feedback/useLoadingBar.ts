import { ref, onBeforeUnmount, getCurrentInstance } from 'vue'

export function useLoadingBar() {
  const visible = ref(false)
  const progress = ref(0)
  let timer: ReturnType<typeof setInterval> | null = null
  // The hide scheduled by `finish()`. Held so a `start()` inside its 250ms
  // window cancels it: otherwise a quick second navigation shows the bar and
  // the first one's timeout hides it mid-way.
  let finishTimer: ReturnType<typeof setTimeout> | null = null

  function clear() {
    if (timer) {
      clearInterval(timer)
      timer = null
    }
    if (finishTimer) {
      clearTimeout(finishTimer)
      finishTimer = null
    }
  }

  function start(): void {
    clear()
    visible.value = true
    progress.value = 0
    timer = setInterval(() => {
      if (progress.value < 90) progress.value += Math.random() * 8
    }, 200)
  }

  function finish(): void {
    clear()
    progress.value = 100
    finishTimer = setTimeout(() => {
      finishTimer = null
      visible.value = false
      progress.value = 0
    }, 250)
  }

  function error(): void {
    finish()
  }

  if (getCurrentInstance()) {
    onBeforeUnmount(() => clear())
  }

  return { visible, progress, start, finish, error }
}

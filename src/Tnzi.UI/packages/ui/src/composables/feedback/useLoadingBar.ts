import { ref, onBeforeUnmount, getCurrentInstance } from 'vue'

export function useLoadingBar() {
  const visible = ref(false)
  const progress = ref(0)
  let timer: ReturnType<typeof setInterval> | null = null

  function clear() {
    if (timer) {
      clearInterval(timer)
      timer = null
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
    setTimeout(() => {
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

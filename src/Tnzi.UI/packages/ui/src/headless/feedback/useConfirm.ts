import { ref } from 'vue'

interface ConfirmOptions {
  title: string
  content?: string
  okText?: string
  cancelText?: string
}

interface ConfirmState extends ConfirmOptions {
  show: boolean
  resolve?: (value: boolean) => void
}

export function useConfirm() {
  const state = ref<ConfirmState>({
    title: '',
    content: '',
    show: false,
    resolve: undefined,
  })

  function confirm(options: ConfirmOptions): Promise<boolean> {
    return new Promise<boolean>((resolve) => {
      state.value = {
        ...options,
        show: true,
        resolve: (value: boolean) => {
          state.value.show = false
          state.value.resolve = undefined
          resolve(value)
        },
      }
    })
  }

  function close(value: boolean): void {
    state.value.resolve?.(value)
  }

  return { state, confirm, close }
}

import { watch, nextTick, onMounted, type Ref } from 'vue'

/**
 * useAutoGrowTextarea — grow a textarea with its content up to a max height,
 * then scroll. JS-driven (works in every browser, unlike `field-sizing`).
 */
export function useAutoGrowTextarea(
  elRef: Ref<HTMLTextAreaElement | null>,
  text: Ref<string>,
  maxHeight = 200,
): void {
  function resize(): void {
    const el = elRef.value
    if (!el) return
    el.style.height = 'auto'
    const next = Math.min(el.scrollHeight, maxHeight)
    el.style.height = `${next}px`
    el.style.overflowY = el.scrollHeight > maxHeight ? 'auto' : 'hidden'
  }

  watch(text, () => void nextTick(resize), { flush: 'post' })
  onMounted(resize)
}

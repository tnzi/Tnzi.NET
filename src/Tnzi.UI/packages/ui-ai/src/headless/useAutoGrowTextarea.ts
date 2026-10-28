import { watch, nextTick, onMounted, type Ref } from 'vue'

/**
 * useAutoGrowTextarea - grow a textarea with its content up to a max height,
 * then scroll. JS-driven (works in every browser, unlike `field-sizing`).
 *
 * Returns `resize`, for a caller that changes what the height is measured
 * from without changing the text (its `rows`, its font size).
 */
export function useAutoGrowTextarea(
  elRef: Ref<HTMLTextAreaElement | null>,
  text: Ref<string>,
  maxHeight = 200,
): () => void {
  function resize(): void {
    const el = elRef.value
    if (!el) return
    el.style.height = 'auto'
    // `scrollHeight` is the padding box. A bordered `border-box` field needs
    // its borders on top (or it scrolls by 2px); a `content-box` one needs its
    // padding taken off (or it grows by the padding on every resize).
    const next = Math.min(el.scrollHeight + boxAdjustment(el), maxHeight)
    el.style.height = `${next}px`
    el.style.overflowY = el.scrollHeight + boxAdjustment(el) > maxHeight ? 'auto' : 'hidden'
  }

  watch(text, () => void nextTick(resize), { flush: 'post' })
  watch(elRef, () => void nextTick(resize), { flush: 'post' })
  onMounted(resize)
  return resize
}

function boxAdjustment(el: HTMLElement): number {
  const style = getComputedStyle(el)
  const px = (value: string): number => Number.parseFloat(value) || 0
  if (style.boxSizing === 'border-box') return px(style.borderTopWidth) + px(style.borderBottomWidth)
  return -(px(style.paddingTop) + px(style.paddingBottom))
}

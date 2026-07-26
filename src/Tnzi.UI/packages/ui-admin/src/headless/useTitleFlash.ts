/**
 * Browser tab title flash - the classic web-IM "you have a message" cue used by
 * Slack / WhatsApp Web / Facebook / LinkedIn when the tab is not focused.
 *
 * `flash(text)` alternates `document.title` between the page's real title and
 * `text` (e.g. "(3) New messages") on a ~1s interval. It auto-restores when the
 * tab regains focus / visibility, and `stop()` restores it explicitly (e.g. when
 * the unread count drops to zero or the window is opened).
 *
 * SSR/test-safe: no-ops when `document`/`window` are unavailable.
 */
const FLASH_INTERVAL_MS = 1000

export function useTitleFlash() {
  let interval: ReturnType<typeof setInterval> | null = null
  let original: string | null = null
  let altText = ''
  let showingAlt = false

  function tick(): void {
    if (typeof document === 'undefined') return
    showingAlt = !showingAlt
    document.title = showingAlt ? altText : original ?? document.title
  }

  function onVisibility(): void {
    if (typeof document !== 'undefined' && !document.hidden) stop()
  }

  function flash(text: string): void {
    if (typeof document === 'undefined' || typeof window === 'undefined') return
    altText = text
    if (interval !== null) return // already flashing - just updated the text
    original = document.title
    showingAlt = false
    tick() // show the alt text immediately, don't wait a full interval
    interval = setInterval(tick, FLASH_INTERVAL_MS)
    window.addEventListener('focus', stop)
    document.addEventListener('visibilitychange', onVisibility)
  }

  function stop(): void {
    if (interval !== null) {
      clearInterval(interval)
      interval = null
    }
    if (typeof document !== 'undefined' && original !== null) document.title = original
    original = null
    showingAlt = false
    if (typeof window !== 'undefined') window.removeEventListener('focus', stop)
    if (typeof document !== 'undefined') document.removeEventListener('visibilitychange', onVisibility)
  }

  return { flash, stop }
}

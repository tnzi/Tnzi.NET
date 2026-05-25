/**
 * `useBreakpoint` — responsive breakpoint helper.
 *
 * **Sunk to `@tnzi/ui` in 0.2.71+** — this module now re-exports the
 * implementation from `@tnzi/ui/composables/theme` so site/chat/mobile can
 * use the same helper without depending on ui-admin.
 *
 * The shape returned matches the original ui-admin `useBreakpoint()` exactly,
 * so existing callers don't need any changes. Prefer the richer
 * `useBreakpoints()` (plural) from `@tnzi/ui` in new code — it adds the
 * coarse triad (isMobile/isTablet/isDesktop) and `…AndUp` helpers.
 */
export {
  useBreakpoint,
  __resetTouchProbeForTests,
  type UseBreakpointReturn,
} from '@tnzi/ui'

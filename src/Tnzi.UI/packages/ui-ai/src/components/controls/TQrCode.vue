<script setup lang="ts">
/**
 * TQrCode - a value as a QR code a phone can scan: an authenticator's setup
 * link, a one-time sign-in link, a pairing code.
 *
 * Drawn as one SVG: a white square with the dark modules as a single path, so
 * it is sharp at any size and needs no canvas. It is black on white in both
 * themes on purpose: scanners expect dark modules on a light field, and an
 * inverted code on a dark page is one many of them cannot read. The quiet zone
 * the QR spec asks for (`margin`, four modules by default) is part of the
 * drawing rather than CSS padding, so the code always has its white border
 * whatever the page puts around it. Error correction level M (`ecc`) survives
 * a smudged or glare-hit screen without making the grid too dense for a small
 * box.
 *
 * The encoder (`uqr`) is loaded with `import()` the first time a value is
 * drawn, so a page that may show a code pays for it only when it does. Until
 * it has loaded (and for an empty value) a white box of the same size stands
 * in, so the layout does not move.
 *
 * `label` is the accessible name (`role="img"`); the value itself is not
 * announced, since it is usually a secret or a long link. Show the value in
 * words beside the code when the user may need to type it.
 */
import { shallowRef, watch } from 'vue'

type QrEcc = 'L' | 'M' | 'Q' | 'H'

const props = withDefaults(
  defineProps<{
    /** What the code encodes. Empty draws the blank box. */
    value: string
    /** Accessible name of the image ("QR code for your authenticator app"). */
    label: string
    /** Side in px (default 152). */
    size?: number
    /** Error correction level (default `M`). */
    ecc?: QrEcc
    /** Quiet zone in modules (default 4, as the QR spec asks). */
    margin?: number
  }>(),
  { size: 152, ecc: 'M', margin: 4 },
)

const code = shallowRef<{ modules: number; path: string } | null>(null)
let seq = 0

watch(
  () => [props.value, props.ecc, props.margin] as const,
  async ([value, ecc, margin]) => {
    const mine = ++seq
    if (!value) {
      code.value = null
      return
    }
    const { encode } = await import('uqr')
    if (mine !== seq) return
    const { size, data } = encode(value, { ecc, border: Math.max(0, margin) })
    const path = data
      .flatMap((row, y) => row.map((dark, x) => (dark ? `M${x} ${y}h1v1h-1z` : '')))
      .join('')
    code.value = { modules: size, path }
  },
  { immediate: true },
)
</script>

<template>
  <svg
    class="t-qr-code"
    :width="size"
    :height="size"
    :viewBox="code ? `0 0 ${code.modules} ${code.modules}` : '0 0 1 1'"
    role="img"
    :aria-label="label"
    :aria-busy="value && !code ? 'true' : undefined"
    shape-rendering="crispEdges"
    data-qr-code
  >
    <!-- Literal colours, not tokens: the code must stay black on white in
         the dark theme too, or many scanners cannot read it. -->
    <rect width="100%" height="100%" fill="#fff" />
    <path v-if="code" :d="code.path" fill="#000" />
  </svg>
</template>

<style scoped>
.t-qr-code {
  display: block;
  flex-shrink: 0;
  max-width: 100%;
  height: auto;
  aspect-ratio: 1;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: var(--tnzi-ai-radius-md, 8px);
}
</style>

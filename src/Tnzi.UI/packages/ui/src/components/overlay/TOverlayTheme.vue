<template>
  <!-- Renderless (`abstract`): THE provider every overlay renders under.
       Two things ride on it, and TModalShell / TDrawerShell go through this
       same component so there is exactly one place they are decided:
         - theme: reset to the GLOBAL light/dark mode, so a modal / drawer
           opened from a page whose "Card / List" surface is dark does not
           inherit the content area's inner dark theme through the Teleport
           (see headless/useOverlayTheme).
         - density: every button and form control inside defaults to `small`
           unless it, an enclosing NForm / NFormItem, or the app's root
           componentOptions says otherwise (see useOverlayComponentOptions).
       Wrap a hand-rolled `NModal` / `NDrawer` in this; a gate in ui-admin
       (`overlay-chrome.test.ts`) refuses one that is not. -->
  <NConfigProvider
    abstract
    :theme="overlayTheme"
    :theme-overrides="overlayOverrides"
    :component-options="dense ? overlayComponentOptions : undefined"
  >
    <slot />
  </NConfigProvider>
</template>

<script setup lang="ts">
import { NConfigProvider } from 'naive-ui'
import {
  useOverlayComponentOptions,
  useOverlayTheme,
  useOverlayThemeOverrides,
} from '../../headless/theme/useOverlayTheme'

withDefaults(
  defineProps<{
    /**
     * Apply the overlay control density (`small` buttons and form controls).
     * Default true - this component is for overlays, and an overlay is dense.
     *
     * `false` is for the one thing that needs the theme reset WITHOUT being an
     * overlay: a host that mounts a whole PAGE on a floating surface (the
     * desktop layout's window host). A page keeps the page-level defaults;
     * shrinking every control on it would be the exact opposite of "pages
     * outside overlays are unaffected". Anything else passing `false` is
     * quietly re-creating the per-dialog inconsistency this exists to end,
     * which is why the same gate lists who may.
     */
    dense?: boolean
  }>(),
  { dense: true },
)

const overlayTheme = useOverlayTheme()
const overlayOverrides = useOverlayThemeOverrides()
const overlayComponentOptions = useOverlayComponentOptions()
</script>

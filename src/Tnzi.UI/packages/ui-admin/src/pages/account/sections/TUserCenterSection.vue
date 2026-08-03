<template>
  <!--
    Layout primitive shared by every User Center section: a fixed header bar
    (title + optional actions) over a body that claims the residual height.
    `fill` lets a table section own the scroll; `contained` wraps a form body in
    the width-capped `.t-detail-content` column.
  -->
  <section class="t-uc-section">
    <header class="t-uc-section__bar">
      <h3 class="t-uc-section__title">
        <TSvgIcon v-if="resolvedIcon" :icon="resolvedIcon" :size="18" class="t-uc-section__title-icon" />
        <span>{{ title }}</span>
      </h3>
      <div v-if="$slots.actions" class="t-uc-section__actions"><slot name="actions" /></div>
    </header>
    <div class="t-uc-section__body" :class="{ 't-uc-section__body--fill': fill }">
      <div v-if="contained" class="t-detail-content"><slot /></div>
      <slot v-else />
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed, inject } from 'vue'
import { TSvgIcon } from '@tnzi/ui'
import { DETAIL_ACTIVE_SECTION_ICON } from '../../../components/detail/active-section-icon'

const props = withDefaults(
  defineProps<{
    title: string
    /** Explicit icon before the title; defaults to the active nav item's icon
     *  (provided by the shell) so the panel title mirrors the left-menu icon. */
    icon?: string
    /** Table sections: the body clips and the flex-height table owns the scroll. */
    fill?: boolean
    /** Wrap the body in the width-capped `.t-detail-content` column (forms). */
    contained?: boolean
  }>(),
  { icon: undefined, fill: false, contained: true },
)

// Fall back to the shell-provided active section icon (side layout: menu icon
// ⇒ panel title icon). Undefined outside a UserCenter subtree - harmless.
const injectedIcon = inject(DETAIL_ACTIVE_SECTION_ICON, undefined)
const resolvedIcon = computed(() => props.icon ?? injectedIcon?.value)

defineSlots<{
  default?: () => unknown
  actions?: () => unknown
}>()
</script>

<style src="./user-center-sections.css"></style>

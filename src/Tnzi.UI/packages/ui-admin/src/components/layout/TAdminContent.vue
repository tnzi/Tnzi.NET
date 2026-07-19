<template>
  <section
    class="t-admin-content"
    :data-full-content="appStore.fullContent ? 'true' : undefined"
  >
    <Transition :name="currentTransition" mode="out-in">
      <div v-if="appStore.reloadFlag" :key="routeKey" class="t-admin-content__page">
        <slot />
      </div>
    </Transition>
  </section>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useAdminAppStore } from '../../stores/useAdminAppStore'
import type { PageTransition } from '../../stores/useAdminThemeStore'

interface Props {
  /**
   * Page transition name. Maps to one of the keyframe families in
   * `styles/transition.css` (all prefixed with `tnzi-`). Pass `'none'` to disable.
   */
  transitionName?: PageTransition
  /**
   * Optional key to force transition on route changes. Defaults to a static
   * 'default' so the component works outside vue-router; wire it to
   * `$route.fullPath` in real usage.
   */
  routeKey?: string
}

const props = withDefaults(defineProps<Props>(), {
  transitionName: 'fade-slide',
  routeKey: 'default',
})

const appStore = useAdminAppStore()

const currentTransition = computed(() => {
  if (props.transitionName === 'none') return ''
  return `tnzi-${props.transitionName}`
})

defineExpose({ currentTransition })
</script>

<style scoped>
/* soybean parity: the content frame itself does NOT scroll. Each page
   is responsible for placing its own scroll boundary — CRUD pages let
   the NDataTable scroll internally (so the pagination footer stays
   pinned), long-form pages opt into outer scroll by wrapping their
   body in `.t-page-scroll` (utility class in styles/polish.css). The
   page wrapper gets `height: 100% + flex column` so flex children
   (TCrudPage's list-card) can claim a real `flex: 1` of vertical
   space. */
.t-admin-content {
  position: relative;
  flex: 1 1 auto;
  min-height: 0;
  overflow: hidden;
  background-color: var(--tnzi-admin-content-bg, var(--tnzi-layout-bg));
  padding: var(--tnzi-admin-content-padding, 16px);
}
.t-admin-content[data-full-content='true'] {
  padding: 0;
}
/* Phone gutter is driven entirely by the `--tnzi-admin-content-padding` token
   (variables.css steps it 12 → 10 <768px → 8 <480px). No hardcoded phone media
   query here — that used to pin 10px and defeat the 8px step on small phones. */
.t-admin-content__page {
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
</style>

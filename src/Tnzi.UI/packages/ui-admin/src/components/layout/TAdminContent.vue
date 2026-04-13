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

interface Props {
  /** Page transition name. Pass 'none' to disable animation. */
  transitionName?: 'fade' | 'slide-left' | 'slide-right' | 'zoom' | 'none'
  /**
   * Optional key to force transition on route changes. Defaults to a static
   * 'default' so the component works outside vue-router; wire it to
   * `$route.fullPath` in real usage.
   */
  routeKey?: string
}

const props = withDefaults(defineProps<Props>(), {
  transitionName: 'fade',
  routeKey: 'default',
})

const appStore = useAdminAppStore()

const currentTransition = computed(() => (props.transitionName === 'none' ? '' : props.transitionName))

defineExpose({ currentTransition })
</script>

<style scoped>
.t-admin-content {
  position: relative;
  flex: 1 1 auto;
  min-height: 0;
  overflow: auto;
  background-color: var(--tnzi-content-bg, var(--tnzi-layout-bg));
  padding: var(--tnzi-content-padding, 16px);
}
.t-admin-content[data-full-content='true'] {
  padding: 0;
}
.t-admin-content__page {
  min-height: 100%;
}

.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.2s ease;
}
.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}

.slide-left-enter-active,
.slide-left-leave-active {
  transition: all 0.25s ease;
}
.slide-left-enter-from {
  transform: translateX(24px);
  opacity: 0;
}
.slide-left-leave-to {
  transform: translateX(-24px);
  opacity: 0;
}

.slide-right-enter-active,
.slide-right-leave-active {
  transition: all 0.25s ease;
}
.slide-right-enter-from {
  transform: translateX(-24px);
  opacity: 0;
}
.slide-right-leave-to {
  transform: translateX(24px);
  opacity: 0;
}

.zoom-enter-active,
.zoom-leave-active {
  transition: all 0.2s ease;
}
.zoom-enter-from,
.zoom-leave-to {
  transform: scale(0.96);
  opacity: 0;
}
</style>

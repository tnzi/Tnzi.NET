<template>
  <Teleport to="body">
    <Transition name="loading-bar-fade">
      <div v-if="state.loading" class="loading-bar-container">
        <div
          class="loading-bar"
          :class="[`loading-bar--${state.status}`]"
          :style="{ maxWidth: `${state.maxWidth}%` }"
        />
      </div>
    </Transition>
  </Teleport>
</template>

<script setup lang="ts">
import { loadingBarState as state } from './loading-bar-store';
</script>

<style scoped>
.loading-bar-container {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  height: 2px;
  z-index: 5999;
  pointer-events: none;
}

.loading-bar {
  width: 100%;
  height: 100%;
  background: hsl(var(--primary));
}

.loading-bar--starting {
  transition: max-width 4s linear;
}

.loading-bar--finishing {
  transition: max-width 0.2s linear;
}

.loading-bar--error {
  transition: max-width 0.2s linear;
  background: hsl(var(--destructive));
}

.loading-bar-fade-enter-active {
  transition: opacity 0.3s;
}

.loading-bar-fade-leave-active {
  transition: opacity 0.8s;
}

.loading-bar-fade-enter-from,
.loading-bar-fade-leave-to {
  opacity: 0;
}
</style>

<template>
  <div class="t-app-shell">
    <div v-if="$slots.header" class="t-app-shell__header">
      <slot name="header" />
    </div>

    <main class="t-app-shell__main" :style="{ flex: 1 }">
      <slot />
    </main>

    <div v-if="$slots.footer" class="t-app-shell__footer">
      <slot name="footer" />
    </div>

    <!-- Mobile drawer overlay -->
    <teleport v-if="$slots['mobile-drawer']" to="body">
      <transition name="t-drawer-fade">
        <div
          v-if="drawerOpen"
          class="t-app-shell__backdrop"
          @click="emit('update:drawerOpen', false)"
        />
      </transition>
      <transition name="t-drawer-slide">
        <aside
          v-if="drawerOpen"
          class="t-app-shell__drawer t-app-shell__drawer--open"
        >
          <slot name="mobile-drawer" />
        </aside>
      </transition>
    </teleport>
  </div>
</template>

<script setup lang="ts">
interface Props {
  /** Whether the mobile drawer is open. Use with v-model:drawerOpen. */
  drawerOpen?: boolean
}

withDefaults(defineProps<Props>(), {
  drawerOpen: false,
})

const emit = defineEmits<{
  'update:drawerOpen': [value: boolean]
}>()
</script>

<style scoped>
.t-app-shell {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
  background-color: var(--tnzi-layout-bg);
}
.t-app-shell__header {
  flex-shrink: 0;
}
.t-app-shell__main {
  flex: 1 1 auto;
  min-height: 0;
}
.t-app-shell__footer {
  flex-shrink: 0;
}
.t-app-shell__backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.45);
  z-index: 999;
}
.t-app-shell__drawer {
  position: fixed;
  top: 0;
  left: 0;
  bottom: 0;
  width: min(80vw, 320px);
  background-color: var(--tnzi-container-bg);
  z-index: 1000;
  box-shadow: var(--tnzi-shadow-sider);
  overflow-y: auto;
}
.t-drawer-fade-enter-active,
.t-drawer-fade-leave-active {
  transition: opacity 0.2s ease;
}
.t-drawer-fade-enter-from,
.t-drawer-fade-leave-to {
  opacity: 0;
}
.t-drawer-slide-enter-active,
.t-drawer-slide-leave-active {
  transition: transform 0.25s ease;
}
.t-drawer-slide-enter-from,
.t-drawer-slide-leave-to {
  transform: translateX(-100%);
}
</style>

<template>
  <div class="t-auth-layout" :class="layoutClass">
    <aside v-if="$slots.aside" class="t-auth-layout__aside">
      <slot name="aside" />
    </aside>
    <div class="t-auth-layout__main">
      <div v-if="$slots.brand" class="t-auth-layout__brand">
        <slot name="brand" />
      </div>
      <div class="t-auth-layout__content">
        <slot />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, useSlots } from 'vue'

const slots = useSlots()

const layoutClass = computed(() =>
  slots.aside ? 't-auth-layout--split' : 't-auth-layout--centered',
)
</script>

<style scoped>
.t-auth-layout {
  min-height: 100vh;
  display: flex;
  background-color: var(--tnzi-layout-bg);
}
.t-auth-layout--centered {
  align-items: center;
  justify-content: center;
  padding: 24px;
}
.t-auth-layout--split {
  flex-direction: row;
}
.t-auth-layout__aside {
  flex: 1 1 50%;
  background: linear-gradient(135deg, var(--tnzi-primary-500), var(--tnzi-primary-700));
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 48px;
}
@media (max-width: 768px) {
  .t-auth-layout__aside {
    display: none;
  }
}
.t-auth-layout__main {
  flex: 1 1 50%;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 48px 24px;
  background-color: var(--tnzi-container-bg);
}
.t-auth-layout--centered .t-auth-layout__main {
  flex: 0 0 auto;
  background-color: var(--tnzi-container-bg);
  border-radius: 12px;
  padding: 48px;
  max-width: 440px;
  width: 100%;
  box-shadow: var(--tnzi-shadow-card);
}
.t-auth-layout__brand {
  margin-bottom: 32px;
  display: flex;
  justify-content: center;
}
.t-auth-layout__content {
  width: 100%;
  max-width: 360px;
}
</style>

<template>
  <div class="t-error-page">
    <div class="t-error-page__illustration">
      <slot name="illustration">
        <div class="t-error-page__code">{{ code }}</div>
      </slot>
    </div>
    <div class="t-error-page__title">{{ title }}</div>
    <div v-if="description" class="t-error-page__description">{{ description }}</div>
    <div v-if="$slots.action" class="t-error-page__action">
      <slot name="action" />
    </div>
  </div>
</template>

<script setup lang="ts">
interface Props {
  code: string
  title: string
  description?: string
}

defineProps<Props>()
</script>

<style scoped>
.t-error-page {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 64px 24px;
  text-align: center;
  min-height: 60vh;
}
.t-error-page__illustration {
  margin-bottom: 24px;
}
.t-error-page__code {
  font-size: 96px;
  font-weight: 700;
  color: var(--tnzi-primary-500);
  line-height: 1;
}
.t-error-page__title {
  font-size: 28px;
  font-weight: 600;
  color: var(--tnzi-base-text);
  margin-bottom: 12px;
}
.t-error-page__description {
  font-size: 16px;
  color: var(--tnzi-base-text-muted);
  max-width: 520px;
  line-height: 1.6;
  margin-bottom: 24px;
}
.t-error-page__action {
  display: flex;
  gap: 12px;
  justify-content: center;
}
</style>

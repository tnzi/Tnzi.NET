<template>
  <div class="t-result" :class="`t-result--${status}`">
    <div class="t-result__icon">
      <slot name="icon">
        <svg v-if="status === 'success'" width="72" height="72" viewBox="0 0 72 72" fill="none" aria-hidden="true">
          <circle cx="36" cy="36" r="34" fill="var(--tnzi-success-50)" stroke="var(--tnzi-success-500)" stroke-width="2" />
          <path d="M22 38 L32 48 L52 26" stroke="var(--tnzi-success-500)" stroke-width="4" fill="none" stroke-linecap="round" stroke-linejoin="round" />
        </svg>
        <svg v-else-if="status === 'error'" width="72" height="72" viewBox="0 0 72 72" fill="none" aria-hidden="true">
          <circle cx="36" cy="36" r="34" fill="var(--tnzi-error-50)" stroke="var(--tnzi-error-500)" stroke-width="2" />
          <path d="M24 24 L48 48 M48 24 L24 48" stroke="var(--tnzi-error-500)" stroke-width="4" stroke-linecap="round" />
        </svg>
        <svg v-else-if="status === 'warning'" width="72" height="72" viewBox="0 0 72 72" fill="none" aria-hidden="true">
          <circle cx="36" cy="36" r="34" fill="var(--tnzi-warning-50)" stroke="var(--tnzi-warning-500)" stroke-width="2" />
          <path d="M36 20 L36 42" stroke="var(--tnzi-warning-500)" stroke-width="4" stroke-linecap="round" />
          <circle cx="36" cy="52" r="2.5" fill="var(--tnzi-warning-500)" />
        </svg>
        <svg v-else width="72" height="72" viewBox="0 0 72 72" fill="none" aria-hidden="true">
          <circle cx="36" cy="36" r="34" fill="var(--tnzi-info-50)" stroke="var(--tnzi-info-500)" stroke-width="2" />
          <path d="M36 30 L36 52" stroke="var(--tnzi-info-500)" stroke-width="4" stroke-linecap="round" />
          <circle cx="36" cy="22" r="2.5" fill="var(--tnzi-info-500)" />
        </svg>
      </slot>
    </div>
    <div class="t-result__title">{{ title }}</div>
    <div v-if="$slots.description" class="t-result__description">
      <slot name="description" />
    </div>
    <div v-if="$slots.action" class="t-result__action">
      <slot name="action" />
    </div>
  </div>
</template>

<script setup lang="ts">
type ResultStatus = 'success' | 'error' | 'warning' | 'info'

interface Props {
  status: ResultStatus
  title: string
}

defineProps<Props>()
</script>

<style scoped>
.t-result {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 48px 24px;
  text-align: center;
}
.t-result__icon {
  margin-bottom: 24px;
}
.t-result__title {
  font-size: 24px;
  font-weight: 600;
  color: var(--tnzi-base-text);
  margin-bottom: 12px;
}
.t-result__description {
  color: var(--tnzi-base-text-muted);
  max-width: 480px;
  line-height: 1.5;
  margin-bottom: 24px;
}
.t-result__action {
  display: flex;
  gap: 12px;
  justify-content: center;
}
</style>

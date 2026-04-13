<template>
  <div class="t-empty">
    <div class="t-empty__icon">
      <slot name="icon">
        <svg width="80" height="80" viewBox="0 0 80 80" fill="none" aria-hidden="true">
          <circle cx="40" cy="40" r="36" stroke="var(--tnzi-border)" stroke-width="2" fill="var(--tnzi-primary-50)" />
          <path d="M24 44 L36 56 L56 32" stroke="var(--tnzi-base-text-muted)" stroke-width="3" fill="none" stroke-linecap="round" />
        </svg>
      </slot>
    </div>
    <div class="t-empty__description">
      <slot name="description">{{ description }}</slot>
    </div>
    <div v-if="$slots.action" class="t-empty__action">
      <slot name="action" />
    </div>
  </div>
</template>

<script setup lang="ts">
interface Props {
  description?: string
}

withDefaults(defineProps<Props>(), {
  description: 'No data',
})
</script>

<style scoped>
.t-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 40px 24px;
  color: var(--tnzi-base-text-muted);
}
.t-empty__icon {
  margin-bottom: 16px;
}
.t-empty__description {
  font-size: 14px;
  line-height: 1.5;
  text-align: center;
}
.t-empty__action {
  margin-top: 16px;
}
</style>

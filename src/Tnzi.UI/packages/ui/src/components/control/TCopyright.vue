<template>
  <div class="t-copyright">
    <span class="t-copyright__text">© {{ yearText }} {{ company }}{{ suffix }}</span>
    <span v-if="$slots.beian" class="t-copyright__beian">
      <slot name="beian" />
    </span>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

interface Props {
  company: string
  startYear?: number
  suffix?: string
}

const props = withDefaults(defineProps<Props>(), {
  startYear: undefined,
  suffix: '. All rights reserved.',
})

const yearText = computed(() => {
  const current = new Date().getFullYear()
  if (props.startYear && props.startYear < current) {
    return `${props.startYear}-${current}`
  }
  return `${current}`
})
</script>

<style scoped>
.t-copyright {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  color: var(--tnzi-base-text-muted, #6b7280);
}
.t-copyright__beian :deep(a) {
  color: var(--tnzi-base-text-muted, #6b7280);
  text-decoration: none;
}
.t-copyright__beian :deep(a:hover) {
  color: var(--tnzi-primary-500, #3b82f6);
}
</style>

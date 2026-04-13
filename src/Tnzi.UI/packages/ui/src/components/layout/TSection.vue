<template>
  <section :style="sectionStyle">
    <header v-if="hasHeader" class="t-section__header">
      <div class="t-section__heading">
        <h2 v-if="$slots.title || title" class="t-section__title">
          <slot name="title">{{ title }}</slot>
        </h2>
        <p v-if="$slots.subtitle" class="t-section__subtitle">
          <slot name="subtitle" />
        </p>
      </div>
      <div v-if="$slots.actions" class="t-section__actions">
        <slot name="actions" />
      </div>
    </header>
    <div class="t-section__body">
      <slot />
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed, useSlots, type CSSProperties } from 'vue'

interface Props {
  /** Section title. Overridden by the `title` slot if provided. */
  title?: string
  /** Vertical padding (applies to top and bottom). Defaults to 64px. */
  paddingY?: string
}

const props = withDefaults(defineProps<Props>(), {
  title: '',
  paddingY: '64px',
})

const slots = useSlots()

const hasHeader = computed(() => !!props.title || !!slots.title || !!slots.subtitle || !!slots.actions)

const sectionStyle = computed<CSSProperties>(() => ({
  paddingTop: props.paddingY,
  paddingBottom: props.paddingY,
}))
</script>

<style scoped>
.t-section__header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 24px;
  margin-bottom: 32px;
}

.t-section__heading {
  flex: 1 1 auto;
  min-width: 0;
}

.t-section__title {
  margin: 0;
  font-size: 28px;
  font-weight: 600;
  color: var(--tnzi-base-text);
  line-height: 1.2;
}

.t-section__subtitle {
  margin: 8px 0 0;
  color: var(--tnzi-base-text-muted);
  font-size: 16px;
  line-height: 1.5;
}

.t-section__actions {
  flex-shrink: 0;
}
</style>

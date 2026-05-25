<script setup lang="ts">
/**
 * `TWidgetTips` — short prose card.
 *
 * Use for "Tips" / "What's new" / static welcome copy. Both the title
 * and body accept i18n keys (resolved against the bundled locale) or
 * raw text so consumers can mix and match without setting up their
 * own i18n stack.
 */
import { computed } from 'vue'
import { maybeTranslate } from '../../pages/_shared/translate'

interface Props {
  title?: string
  body?: string
}

const props = withDefaults(defineProps<Props>(), {
  title: undefined,
  body: undefined,
})

const resolvedTitle = computed(() => maybeTranslate(props.title))
const resolvedBody = computed(() => maybeTranslate(props.body))
</script>

<template>
  <div class="t-widget-tips">
    <h3 v-if="resolvedTitle" class="t-widget-tips__title">{{ resolvedTitle }}</h3>
    <p v-if="resolvedBody" class="t-widget-tips__body">{{ resolvedBody }}</p>
    <slot />
  </div>
</template>

<style scoped>
.t-widget-tips {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.t-widget-tips__title {
  margin: 0;
  font-size: 15px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.t-widget-tips__body {
  margin: 0;
  font-size: 13px;
  line-height: 1.6;
  color: var(--tnzi-base-text-muted, #888);
}
</style>

<script setup lang="ts">
/**
 * TArtifact - Artifact panel container
 *
 * A framed panel: a header with the title (or `#header`), the actions
 * (`#actions`, Download by default) and Close, then the scrolling body.
 */

import { useAiI18n } from '../../i18n/index';
import TIconButton from '../controls/TIconButton.vue';
const t = useAiI18n();

defineProps<{
  title: string;
  description?: string;
}>();

defineEmits<{
  close: [];
  download: [];
}>();
</script>

<template>
  <div class="t-artifact">
    <div class="t-artifact__head">
      <div class="t-artifact__heading">
        <slot name="header">
          <div class="t-artifact__title">{{ title }}</div>
          <p v-if="description" class="t-artifact__desc">{{ description }}</p>
        </slot>
      </div>
      <div class="t-artifact__actions">
        <slot name="actions">
          <TIconButton icon="lucide:download" :label="t.artifact.download" @click="$emit('download')" />
        </slot>
        <TIconButton icon="lucide:x" :label="t.artifact.close" @click="$emit('close')" />
      </div>
    </div>
    <div class="t-ai-scroll relative flex-1 min-h-0 overflow-auto p-4">
      <slot />
    </div>
  </div>
</template>

<style scoped>
.t-artifact {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 14px;
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  box-shadow: var(--tnzi-ai-popover-shadow);
}
.t-artifact__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 6px 8px 6px 16px;
  border-bottom: 1px solid var(--tnzi-ai-border);
}
.t-artifact__heading { min-width: 0; }
.t-artifact__title {
  font-size: 14px;
  font-weight: 500;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-artifact__desc {
  margin: 0;
  font-size: 12px;
  color: var(--tnzi-ai-text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-artifact__actions {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  gap: 2px;
}
</style>

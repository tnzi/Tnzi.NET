<template>
  <TEmpty :text="text">
    <template v-if="hint" #description>
      <span class="t-list-empty__text">{{ text || 'No data' }}</span>
      <span class="t-list-empty__hint">{{ hint }}</span>
    </template>
    <NButton
      v-if="showCreate"
      class="t-crud-empty-cta"
      size="small"
      tertiary
      type="primary"
      @click="emit('create')"
    >
      {{ createLabel }}
    </NButton>
  </TEmpty>
</template>

<script setup lang="ts">
/**
 * TListEmpty - the empty state of the three CRUD renderers: TEmpty's line, the
 * page's optional hint under it, and the first-load Create call-to-action.
 * One component so the table, the card list, the tile grid and the row list
 * cannot drift apart (they each drew their own copy before).
 */
import { NButton } from 'naive-ui'
import { TEmpty } from '@tnzi/ui'

defineProps<{
  text?: string
  hint?: string
  showCreate?: boolean
  createLabel?: string
}>()

const emit = defineEmits<{ create: [] }>()
</script>

<style scoped>
/* 与列表工具栏的「新建」同一处理：tertiary 按钮的字色不用主色原值（浅色主色在浅灰底上
   看不见），用保证可读的「主色作文字」令牌。 */
.t-crud-empty-cta :deep(.n-button__content) { color: var(--tnzi-primary-text, var(--tnzi-primary)); }
.t-list-empty__text {
  display: block;
  text-align: center;
}
.t-list-empty__hint {
  display: block;
  max-width: 420px;
  margin: 4px auto 0;
  font-size: 12.5px;
  line-height: 1.55;
  text-align: center;
  opacity: 0.85;
}
</style>

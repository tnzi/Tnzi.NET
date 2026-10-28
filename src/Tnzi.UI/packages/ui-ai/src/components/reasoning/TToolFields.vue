<script setup lang="ts">
/**
 * @experimental
 * TToolFields - a tool's arguments (or a result's named values) as labelled lines,
 * inside a tool step: "File path  /var/log/app.log". A web address is a link that
 * opens in a new tab; long or multi-line text gets a block of its own, folded when
 * long. Build the lines with `toolFields(value)`.
 */
import type { ToolField } from './tool-views'
import TToolOutput from './TToolOutput.vue'

withDefaults(
  defineProps<{
    fields: readonly ToolField[]
    /** A heading above the lines ("Input"). */
    label?: string
  }>(),
  { label: undefined },
)
</script>

<template>
  <section class="t-tool-fields">
    <div v-if="label" class="t-tool-fields__label">{{ label }}</div>
    <dl class="t-tool-fields__list">
      <div
        v-for="(field, i) in fields"
        :key="`${field.label}:${i}`"
        class="t-tool-fields__row"
        :class="{ 'is-block': field.block, 'is-bare': !field.label }"
      >
        <dt v-if="field.label" class="t-tool-fields__name">{{ field.label }}</dt>
        <dd class="t-tool-fields__value">
          <a
            v-if="field.href"
            :href="field.href"
            target="_blank"
            rel="noopener noreferrer"
            class="t-tool-fields__link"
          >{{ field.text }}</a>
          <TToolOutput v-else-if="field.block" :text="field.text" :fold-lines="8" />
          <template v-else>{{ field.text }}</template>
        </dd>
      </div>
    </dl>
  </section>
</template>

<style scoped>
.t-tool-fields {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}
.t-tool-fields__label {
  font-size: 12px;
  font-weight: 500;
  color: var(--tnzi-ai-text-tertiary);
}
/* One grid for every line, so the values start at the same x. A row is
   `display: contents`; a block value (and its name) takes the whole width. */
.t-tool-fields__list {
  display: grid;
  grid-template-columns: max-content minmax(0, 1fr);
  gap: 4px 12px;
  margin: 0;
  font-size: 13px;
  line-height: 1.5;
}
.t-tool-fields__row { display: contents; }
.t-tool-fields__row.is-block > .t-tool-fields__name,
.t-tool-fields__row.is-block > .t-tool-fields__value,
.t-tool-fields__row.is-bare > .t-tool-fields__value { grid-column: 1 / -1; }
.t-tool-fields__name {
  max-width: 180px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--tnzi-ai-text-secondary);
}
.t-tool-fields__value {
  min-width: 0;
  margin: 0;
  color: var(--tnzi-ai-text);
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
.t-tool-fields__link {
  color: var(--tnzi-ai-accent-text);
  text-decoration: none;
}
.t-tool-fields__link:hover { text-decoration: underline; }
</style>

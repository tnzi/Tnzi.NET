<script setup lang="ts">
/**
 * TModelSelector - Model selection dialog with search filtering
 *
 * A `TDialog` with a search field over the models, grouped by `group` (else
 * provider). The search field takes focus when the dialog opens and is
 * cleared once it has closed. Each model is a button; the current one carries
 * a check and `aria-current`.
 *
 * Provider logos are opt-in through `providerLogo`. The picker used to load
 * them from a third-party host for every user who opened it, which told that
 * host each user's address and every provider name the deployment configures
 * (internal ones included), and broke under a strict CSP. Without a resolver
 * each row shows a generic glyph and nothing leaves the page.
 */

import { computed, ref } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '../../i18n/index';
import TButton from '../controls/TButton.vue';
import TDialog from '../overlay/TDialog.vue';
const t = useAiI18n();

export interface ModelOption {
  id: string;
  name: string;
  provider: string;
  group?: string;
}

const props = withDefaults(defineProps<{
  models: ModelOption[];
  modelValue?: string;
  open?: boolean;
  /**
   * URL of a provider's logo, or null/undefined for the generic glyph. The
   * resolver owns the URL, so it also owns encoding the provider name into it.
   * Serve logos from your own origin where you can; a remote host sees every
   * user who opens the picker.
   */
  providerLogo?: (provider: string) => string | null | undefined;
}>(), {
  modelValue: undefined,
  open: false,
  providerLogo: undefined,
});

const emit = defineEmits<{
  'update:modelValue': [modelId: string];
  'update:open': [open: boolean];
}>();

const searchQuery = ref('');

const filtered = computed(() => {
  const query = searchQuery.value.toLowerCase().trim();
  if (!query) return props.models;
  return props.models.filter(
    (m) =>
      m.name.toLowerCase().includes(query) ||
      m.provider.toLowerCase().includes(query) ||
      m.id.toLowerCase().includes(query),
  );
});

const grouped = computed(() => {
  const groups = new Map<string, ModelOption[]>();
  for (const model of filtered.value) {
    const key = model.group ?? model.provider;
    const existing = groups.get(key) ?? [];
    groups.set(key, [...existing, model]);
  }
  return groups;
});

const showModal = computed({
  get: () => props.open,
  set: (val) => emit('update:open', val),
});

function selectModel(modelId: string): void {
  emit('update:modelValue', modelId);
  emit('update:open', false);
}

function logoUrl(provider: string): string | null {
  return props.providerLogo?.(provider) || null;
}
</script>

<template>
  <div class="t-model-selector__trigger" @click="showModal = true">
    <slot name="trigger">
      <TButton icon="lucide:cpu">{{ t.model.select }}</TButton>
    </slot>
  </div>

  <TDialog
    v-model="showModal"
    :title="t.model.select"
    size="sm"
    class="t-model-selector"
    @closed="searchQuery = ''"
  >
    <div class="t-model-selector__search">
      <Icon icon="lucide:search" class="t-model-selector__search-icon" aria-hidden="true" />
      <input
        v-model="searchQuery"
        type="search"
        class="t-input t-model-selector__search-input"
        :placeholder="t.model.search"
        :aria-label="t.model.search"
      />
    </div>

    <div class="t-model-selector__list t-ai-scroll">
      <div v-if="filtered.length === 0" class="t-model-selector__empty">
        {{ t.model.noResults }}
      </div>
      <template v-else>
        <div v-for="[group, groupModels] in grouped" :key="group" class="t-model-selector__group" role="group" :aria-label="group">
          <div class="t-model-selector__group-label" aria-hidden="true">{{ group }}</div>
          <button
            v-for="model in groupModels"
            :key="model.id"
            type="button"
            class="t-model-selector__row"
            :class="{ 'is-current': model.id === modelValue }"
            :aria-current="model.id === modelValue ? 'true' : undefined"
            :data-model-id="model.id"
            @click="selectModel(model.id)"
          >
            <img
              v-if="logoUrl(model.provider)"
              :src="logoUrl(model.provider) ?? undefined"
              :alt="model.provider"
              class="t-model-selector__logo t-model-logo"
              loading="lazy"
            />
            <Icon
              v-else
              icon="lucide:cpu"
              class="t-model-selector__glyph"
              aria-hidden="true"
            />
            <span class="t-model-selector__name">{{ model.name }}</span>
            <Icon
              v-if="model.id === modelValue"
              icon="lucide:check"
              class="t-model-selector__check"
              aria-hidden="true"
            />
          </button>
        </div>
      </template>
    </div>
  </TDialog>
</template>

<style scoped>
.t-model-selector__trigger {
  display: inline-flex;
}
.t-model-selector__search {
  position: relative;
  margin-bottom: 8px;
}
.t-model-selector__search-icon {
  position: absolute;
  top: 50%;
  left: 10px;
  transform: translateY(-50%);
  font-size: 14px;
  color: var(--tnzi-ai-text-tertiary);
  pointer-events: none;
}
.t-model-selector__search-input {
  padding-left: 30px;
}
.t-model-selector__list {
  position: relative;
  max-height: 300px;
  overflow-y: auto;
}
.t-model-selector__empty {
  padding: 24px 0;
  text-align: center;
  font-size: 13px;
  color: var(--tnzi-ai-text-secondary);
}
.t-model-selector__group + .t-model-selector__group {
  margin-top: 4px;
  padding-top: 4px;
  border-top: 1px solid var(--tnzi-ai-border);
}
.t-model-selector__group-label {
  padding: 6px 8px;
  font-size: 12px;
  font-weight: 500;
  color: var(--tnzi-ai-text-secondary);
}
.t-model-selector__row {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  min-height: 32px;
  padding: 6px 8px;
  border: none;
  border-radius: 8px;
  background: transparent;
  color: var(--tnzi-ai-text);
  font-family: inherit;
  font-size: 13px;
  text-align: left;
  cursor: pointer;
}
.t-model-selector__row:hover {
  background: var(--tnzi-ai-hover);
}
.t-model-selector__row:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: -2px;
}
.t-model-selector__logo,
.t-model-selector__glyph {
  flex-shrink: 0;
  width: 16px;
  height: 16px;
}
.t-model-selector__glyph {
  color: var(--tnzi-ai-text-secondary);
}
.t-model-selector__name {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-model-selector__check {
  flex-shrink: 0;
  font-size: 14px;
  color: var(--tnzi-ai-accent-text);
}
/* Provider logos that need dark-mode inversion */
.dark .t-model-logo,
[data-theme='dark'] .t-model-logo {
  filter: invert(1);
}
</style>

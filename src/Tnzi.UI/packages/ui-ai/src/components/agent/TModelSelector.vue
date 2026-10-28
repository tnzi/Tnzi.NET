<script setup lang="ts">
/**
 * TModelSelector - Model selection dialog with search filtering
 *
 * Provider logos are opt-in through `providerLogo`. The picker used to load
 * them from a third-party host for every user who opened it, which told that
 * host each user's address and every provider name the deployment configures
 * (internal ones included), and broke under a strict CSP. Without a resolver
 * each row shows a generic glyph and nothing leaves the page.
 */

import { NModal, NInput, NButton } from 'naive-ui';
import { computed, ref } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '../../i18n/index';
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

function handleAfterEnter(): void {
  searchQuery.value = '';
}

function logoUrl(provider: string): string | null {
  return props.providerLogo?.(provider) || null;
}
</script>

<template>
  <div @click="showModal = true">
    <slot name="trigger">
      <NButton secondary size="small">
        <template #icon><Icon icon="lucide:cpu" /></template>
        {{ t.model.select }}
      </NButton>
    </slot>
  </div>

  <NModal
    v-model:show="showModal"
    preset="card"
    :title="t.model.select"
    class="!max-w-md"
    :auto-focus="false"
    @after-enter="handleAfterEnter"
  >
    <div class="flex flex-col gap-2">
      <NInput
        v-model:value="searchQuery"
        :placeholder="t.model.search"
        clearable
        size="large"
      >
        <template #prefix>
          <Icon icon="lucide:search" class="size-4 text-tnzi-muted" />
        </template>
      </NInput>

      <div class="t-ai-scroll relative max-h-[300px] overflow-y-auto">
        <template v-if="filtered.length === 0">
          <div class="py-6 text-center text-sm text-tnzi-muted">
            {{ t.model.noResults }}
          </div>
        </template>
        <template v-else>
          <template v-for="[group, groupModels] in grouped" :key="group">
            <div class="px-2 py-1.5 text-xs font-medium text-tnzi-muted">
              {{ group }}
            </div>
            <div
              v-for="model in groupModels"
              :key="model.id"
              class="flex cursor-pointer items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-tnzi-layout"
              @click="selectModel(model.id)"
            >
              <img
                v-if="logoUrl(model.provider)"
                :src="logoUrl(model.provider) ?? undefined"
                :alt="model.provider"
                class="size-4 t-model-logo"
                loading="lazy"
              />
              <Icon
                v-else
                icon="lucide:cpu"
                class="size-4 shrink-0 text-tnzi-muted"
                aria-hidden="true"
              />
              <span class="flex-1 truncate text-xs">{{ model.name }}</span>
              <Icon
                v-if="model.id === modelValue"
                icon="lucide:check"
                class="size-3.5 text-ai-accent-text"
              />
            </div>
            <div class="my-1 h-px bg-tnzi-border last:hidden" />
          </template>
        </template>
      </div>
    </div>
  </NModal>
</template>

<style scoped>
/* Provider logos that need dark-mode inversion */
.dark .t-model-logo,
[data-theme='dark'] .t-model-logo {
  filter: invert(1);
}
</style>

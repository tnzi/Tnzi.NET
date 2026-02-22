<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';

interface Props {
  modelValue?: Record<string, any>;
  placeholder?: string;
  loading?: boolean;
  disabled?: boolean;
  clearable?: boolean;
  showReset?: boolean;
  searchButtonText?: string;
  size?: 'small' | 'medium' | 'large';
}

const props = withDefaults(defineProps<Props>(), {
  modelValue: () => ({ keyword: '' }),
  placeholder: '',
  loading: false,
  disabled: false,
  clearable: true,
  showReset: true,
  searchButtonText: '',
  size: 'medium',
});

const emit = defineEmits<{
  'update:modelValue': [value: Record<string, any>];
  search: [value: Record<string, any>];
  reset: [];
}>();

const { t } = useI18n();

const keyword = computed({
  get: () => props.modelValue.keyword ?? '',
  set: (value: string) => emit('update:modelValue', { ...props.modelValue, keyword: value }),
});

const inputSizeClass = computed(() => {
  const map = {
    small: 'h-8 text-xs',
    medium: 'h-10 text-sm',
    large: 'h-11 text-base',
  } as const;

  return map[props.size];
});

const handleSearch = () => emit('search', props.modelValue);
const handleReset = () => {
  emit('update:modelValue', { ...props.modelValue, keyword: '' });
  emit('reset');
};
</script>

<template>
  <form class="flex w-full items-center gap-2" @submit.prevent="handleSearch">
    <input
      v-model="keyword"
      type="text"
      class="min-w-0 flex-1 rounded-md border bg-background px-3 outline-none ring-offset-background transition focus-visible:ring-2 focus-visible:ring-ring"
      :class="inputSizeClass"
      :disabled="props.disabled"
      :placeholder="props.placeholder || t('common.search')"
    />

    <button
      v-if="props.showReset"
      type="button"
      class="rounded-md border bg-background px-3 py-2 text-sm transition hover:bg-accent disabled:opacity-50"
      :disabled="props.disabled"
      @click="handleReset"
    >
      {{ t('common.reset') }}
    </button>

    <button
      type="submit"
      class="rounded-md bg-primary px-4 py-2 text-sm font-semibold text-primary-foreground transition hover:opacity-90 disabled:opacity-50"
      :disabled="props.disabled || props.loading"
    >
      <span v-if="props.loading">{{ t('common.loading') }}</span>
      <span v-else>{{ props.searchButtonText || t('common.search') }}</span>
    </button>
  </form>
</template>


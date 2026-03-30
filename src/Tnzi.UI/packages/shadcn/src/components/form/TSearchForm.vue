<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
import { Input } from '../primitive/ui/input';
import { Button } from '../primitive/ui/button';

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
  set: (value: string | number) => emit('update:modelValue', { ...props.modelValue, keyword: String(value) }),
});

const buttonSize = computed(() => {
  const map = { small: 'sm', medium: 'default', large: 'lg' } as const;
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
    <Input
      v-model="keyword"
      type="text"
      class="min-w-0 flex-1"
      :disabled="props.disabled"
      :placeholder="props.placeholder || t('common.search')"
    />

    <Button
      v-if="props.showReset"
      type="button"
      variant="outline"
      :size="buttonSize"
      :disabled="props.disabled"
      @click="handleReset"
    >
      {{ t('common.reset') }}
    </Button>

    <Button
      type="submit"
      :size="buttonSize"
      :disabled="props.disabled || props.loading"
    >
      <span v-if="props.loading">{{ t('common.loading') }}</span>
      <span v-else>{{ props.searchButtonText || t('common.search') }}</span>
    </Button>
  </form>
</template>

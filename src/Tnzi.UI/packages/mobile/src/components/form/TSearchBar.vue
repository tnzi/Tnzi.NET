<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';

interface Props {
  modelValue?: Record<string, unknown>;
  placeholder?: string;
  loading?: boolean;
  disabled?: boolean;
  showReset?: boolean;
  searchButtonText?: string;
}

const props = withDefaults(defineProps<Props>(), {
  modelValue: () => ({ keyword: '' }),
  placeholder: '',
  loading: false,
  disabled: false,
  showReset: true,
  searchButtonText: '',
});

const emit = defineEmits<{
  'update:modelValue': [value: Record<string, unknown>];
  search: [value: Record<string, unknown>];
  reset: [];
}>();

const { t } = useI18n();

const keyword = computed<string>({
  get: () => {
    const value = props.modelValue.keyword;
    if (typeof value === 'string' || typeof value === 'number') {
      return String(value);
    }
    return '';
  },
  set: (value: string) => emit('update:modelValue', { ...props.modelValue, keyword: value }),
});

const handleSearch = () => emit('search', props.modelValue);
const handleReset = () => {
  emit('update:modelValue', { ...props.modelValue, keyword: '' });
  emit('reset');
};
</script>

<template>
  <van-search
    v-model="keyword"
    :placeholder="props.placeholder || t('common.search')"
    :show-action="true"
    :disabled="props.disabled"
    @search="handleSearch"
  >
    <template #action>
      <div class="flex items-center gap-2">
        <van-button v-if="props.showReset" size="mini" plain @click="handleReset">{{ t('common.reset') }}</van-button>
        <van-button size="mini" type="primary" :loading="props.loading" @click="handleSearch">
          {{ props.searchButtonText || t('common.search') }}
        </van-button>
      </div>
    </template>
  </van-search>
</template>

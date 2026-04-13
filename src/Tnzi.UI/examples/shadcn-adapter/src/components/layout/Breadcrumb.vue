<script setup lang="ts">
import { useI18n } from '@tnzi/core/adapters/i18n';
import type { IBreadcrumbItem } from '@tnzi/core/types/shared-ui';

interface IBreadcrumbProps {
  items: IBreadcrumbItem[];
  separator?: string;
  class?: string | string[];
  style?: string | Record<string, string | number>;
}

interface IBreadcrumbEmits {
  itemClick: [item: IBreadcrumbItem, index: number];
}

const { t } = useI18n();

const props = defineProps<IBreadcrumbProps>();
const emit = defineEmits<IBreadcrumbEmits>();

const handleItemClick = (item: IBreadcrumbItem, index: number) => {
  if (item.clickable !== false) {
    emit('itemClick', item, index);
  }
};
</script>

<template>
  <nav
    :class="[
      'border-b border-border/60 bg-background px-4 py-3 text-sm',
      props.class,
    ]"
    :style="props.style"
    aria-label="Breadcrumb"
  >
    <ol class="flex flex-wrap items-center gap-1 text-muted-foreground">
      <li
        v-for="(item, index) in props.items"
        :key="item.key"
        class="inline-flex items-center gap-1"
        @click="() => handleItemClick(item, index)"
      >
        <a
          v-if="item.path && item.clickable !== false"
          :href="item.path"
          class="transition-colors hover:text-foreground"
        >
          {{ item.label }}
        </a>
        <span
          v-else
          :class="index === (props.items?.length ?? 0) - 1 ? 'font-medium text-foreground' : ''"
        >
          {{ item.label }}
        </span>
        <span
          v-if="index < (props.items?.length ?? 0) - 1"
          class="px-1 text-muted-foreground/70"
          aria-hidden="true"
        >/</span>
      </li>
      <li v-if="!props.items?.length" class="text-muted-foreground/70">{{ t('common.home') }}</li>
    </ol>
  </nav>
</template>


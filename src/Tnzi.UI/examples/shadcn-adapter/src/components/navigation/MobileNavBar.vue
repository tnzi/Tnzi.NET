<script setup lang="ts">
import { useI18n } from '@tnzi/core/adapters/i18n';
import { Button } from '../primitive/ui';

interface INavBarProps {
  title?: string;
  showBack?: boolean;
  showClose?: boolean;
  leftContent?: string | object;
  rightContent?: string | object;
  fixed?: boolean;
  backgroundColor?: string;
  textColor?: string;
  safeAreaInsetTop?: boolean;
  class?: string | string[];
  style?: string | Record<string, string | number>;
}

interface INavBarEmits {
  back: [];
  close: [];
  leftClick: [];
  rightClick: [];
}

const { t } = useI18n();

const props = withDefaults(defineProps<INavBarProps>(), {
  title: '',
  showBack: false,
  showClose: false,
  fixed: false,
  safeAreaInsetTop: false,
});

const emit = defineEmits<INavBarEmits>();
</script>

<template>
  <header
    class="flex min-h-12 items-center justify-between gap-2 border-b bg-background px-3"
    :class="[props.class, props.fixed ? 'sticky top-0 z-40' : '', props.safeAreaInsetTop ? 'pt-2' : '']"
    :style="props.style"
  >
    <div class="flex items-center gap-2">
      <Button v-if="props.showBack" size="sm" variant="ghost" @click="emit('back')">
        {{ t('common.back') }}
      </Button>
      <div v-if="$slots.left">
        <button type="button" class="rounded px-2 py-1 text-sm hover:bg-accent" @click="emit('leftClick')">
          <slot name="left" />
        </button>
      </div>
    </div>

    <div class="min-w-0 flex-1 text-center text-sm font-semibold">
      <slot name="title">{{ props.title }}</slot>
    </div>

    <div class="flex items-center gap-2">
      <div v-if="$slots.right">
        <button type="button" class="rounded px-2 py-1 text-sm hover:bg-accent" @click="emit('rightClick')">
          <slot name="right" />
        </button>
      </div>
      <Button v-if="props.showClose" size="sm" variant="ghost" @click="emit('close')">
        {{ t('common.close') }}
      </Button>
    </div>
  </header>
</template>

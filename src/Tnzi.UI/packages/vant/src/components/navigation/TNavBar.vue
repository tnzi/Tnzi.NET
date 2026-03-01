<script setup lang="ts">
import type { INavBarEmits, INavBarProps } from '@tnzi/core/components';
import { useI18n } from '@tnzi/core/adapters/i18n';

const { t } = useI18n();

const props = withDefaults(defineProps<INavBarProps>(), {
  title: '',
  showBack: true,
  showClose: false,
  fixed: false,
  safeAreaInsetTop: true,
});

const emit = defineEmits<INavBarEmits>();

const onClickLeft = () => {
  emit('leftClick');
  if (props.showBack) emit('back');
};

const onClickRight = () => {
  emit('rightClick');
  if (props.showClose) emit('close');
};
</script>

<template>
  <van-nav-bar
    :title="props.title"
    :fixed="props.fixed"
    :safe-area-inset-top="props.safeAreaInsetTop"
    :left-text="props.showBack ? t('common.back') : ''"
    :right-text="props.showClose ? t('common.close') : ''"
    :class="props.class"
    :style="props.style"
    @click-left="onClickLeft"
    @click-right="onClickRight"
  >
    <template v-if="$slots.left" #left>
      <slot name="left" />
    </template>
    <template v-if="$slots.right" #right>
      <slot name="right" />
    </template>
    <template v-if="$slots.title" #title>
      <slot name="title" />
    </template>
  </van-nav-bar>
</template>

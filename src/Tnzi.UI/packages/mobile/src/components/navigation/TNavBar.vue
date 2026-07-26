<script setup lang="ts">
import { computed } from 'vue';
import type { CSSProperties } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';

interface INavBarProps {
  title?: string;
  showBack?: boolean;
  showClose?: boolean;
  fixed?: boolean;
  /** Bar background. Any CSS color. */
  backgroundColor?: string;
  /** Title, action text and arrow color. Any CSS color. */
  textColor?: string;
  safeAreaInsetTop?: boolean;
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
  showBack: true,
  showClose: false,
  fixed: false,
  safeAreaInsetTop: true,
});

const emit = defineEmits<INavBarEmits>();

// Vant styles NavBar through custom properties, so overriding the colors is a
// matter of redeclaring them on the element rather than fighting its selectors.
const colorVars = computed<CSSProperties>(() => {
  const vars: Record<string, string> = {};
  if (props.backgroundColor) vars['--van-nav-bar-background'] = props.backgroundColor;
  if (props.textColor) {
    vars['--van-nav-bar-title-text-color'] = props.textColor;
    vars['--van-nav-bar-text-color'] = props.textColor;
    vars['--van-nav-bar-icon-color'] = props.textColor;
  }
  return vars as CSSProperties;
});

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
    :style="colorVars"
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

<script setup lang="ts">
import { computed } from 'vue'
import { NButton } from 'naive-ui'
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

const props = withDefaults(defineProps<INavBarProps>(), {
  showBack: false,
  showClose: false,
  fixed: false,
})

const emit = defineEmits<{
  back: []
  close: []
  leftClick: []
  rightClick: []
}>()

const navStyle = computed(() => {
  const style: Record<string, string> = {}
  if (props.backgroundColor) {
    style.backgroundColor = props.backgroundColor
  }
  if (props.textColor) {
    style.color = props.textColor
  }
  return style
})
</script>

<template>
  <div
    class="flex items-center h-12 px-3 border-b border-naive bg-naive-card box-border"
    :class="{ 'fixed top-0 left-0 right-0 z-100': fixed }"
    :style="navStyle"
  >
    <div class="shrink-0 flex items-center cursor-pointer" @click="emit('leftClick')">
      <slot name="left">
        <NButton v-if="showBack" text @click.stop="emit('back')">
          &larr; Back
        </NButton>
        <NButton v-if="showClose" text @click.stop="emit('close')">
          &times;
        </NButton>
      </slot>
    </div>
    <div class="flex-1 text-center text-4 font-600 truncate">
      <slot name="title">
        {{ title }}
      </slot>
    </div>
    <div class="shrink-0 flex items-center cursor-pointer" @click="emit('rightClick')">
      <slot name="right" />
    </div>
  </div>
</template>

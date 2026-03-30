<script setup lang="ts">
import { computed } from 'vue'
import { NButton } from 'naive-ui'
import type { INavBarProps } from '@tnzi/core'

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
  <div class="t-navbar" :class="{ 'is-fixed': fixed }" :style="navStyle">
    <div class="t-navbar__left" @click="emit('leftClick')">
      <slot name="left">
        <NButton v-if="showBack" text @click.stop="emit('back')">
          &larr; Back
        </NButton>
        <NButton v-if="showClose" text @click.stop="emit('close')">
          &times;
        </NButton>
      </slot>
    </div>
    <div class="t-navbar__title">
      <slot name="title">
        {{ title }}
      </slot>
    </div>
    <div class="t-navbar__right" @click="emit('rightClick')">
      <slot name="right" />
    </div>
  </div>
</template>

<style scoped>
.t-navbar {
  display: flex;
  align-items: center;
  height: 48px;
  padding: 0 12px;
  border-bottom: 1px solid var(--n-border-color, #e0e0e6);
  background-color: var(--n-color, #fff);
  box-sizing: border-box;
}

.t-navbar.is-fixed {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  z-index: 100;
}

.t-navbar__left {
  flex: 0 0 auto;
  display: flex;
  align-items: center;
  cursor: pointer;
}

.t-navbar__title {
  flex: 1;
  text-align: center;
  font-size: 16px;
  font-weight: 600;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.t-navbar__right {
  flex: 0 0 auto;
  display: flex;
  align-items: center;
  cursor: pointer;
}
</style>

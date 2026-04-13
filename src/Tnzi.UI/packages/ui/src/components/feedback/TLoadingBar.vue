<template>
  <div class="t-loading-bar" :style="{ display: visible ? 'block' : 'none' }">
    <div class="t-loading-bar__progress" :style="progressStyle" />
  </div>
</template>

<script setup lang="ts">
import { computed, ref, onBeforeUnmount } from 'vue'

interface Props {
  color?: string
  height?: string
}

const props = withDefaults(defineProps<Props>(), {
  color: '',
  height: '2px',
})

const visible = ref(false)
const percent = ref(0)
let timer: ReturnType<typeof setInterval> | null = null
let finishTimer: ReturnType<typeof setTimeout> | null = null

function clearTimers() {
  if (timer) {
    clearInterval(timer)
    timer = null
  }
  if (finishTimer) {
    clearTimeout(finishTimer)
    finishTimer = null
  }
}

function start() {
  clearTimers()
  visible.value = true
  percent.value = 0
  timer = setInterval(() => {
    if (percent.value < 90) {
      percent.value += Math.random() * 10
      if (percent.value > 90) percent.value = 90
    }
  }, 200)
}

function finish() {
  clearTimers()
  percent.value = 100
  finishTimer = setTimeout(() => {
    visible.value = false
    percent.value = 0
  }, 300)
}

function error() {
  clearTimers()
  percent.value = 100
  finishTimer = setTimeout(() => {
    visible.value = false
    percent.value = 0
  }, 300)
}

onBeforeUnmount(() => {
  clearTimers()
})

const progressStyle = computed(() => ({
  width: `${percent.value}%`,
  height: props.height,
  backgroundColor: props.color || 'var(--tnzi-primary-500)',
}))

defineExpose({ start, finish, error })
</script>

<style scoped>
.t-loading-bar {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  z-index: 3000;
  pointer-events: none;
}
.t-loading-bar__progress {
  transition: width 0.2s ease;
}
</style>

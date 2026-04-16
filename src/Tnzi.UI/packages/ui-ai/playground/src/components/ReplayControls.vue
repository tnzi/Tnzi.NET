<script setup lang="ts">
import { Icon } from '@iconify/vue'
import type { PlaybackState } from '../mock/types'

defineProps<{
  playbackState: PlaybackState
  speed: number
}>()

const emit = defineEmits<{
  play: []
  pause: []
  skipToEnd: []
  reset: []
  'update:speed': [value: number]
}>()

const speeds = [0.5, 1, 2, 4] as const
</script>

<template>
  <div class="replay-controls">
    <button
      v-if="playbackState !== 'playing'"
      type="button"
      class="replay-controls__btn"
      aria-label="Play"
      @click="emit('play')"
    >
      <Icon icon="lucide:play" />
    </button>
    <button
      v-else
      type="button"
      class="replay-controls__btn"
      aria-label="Pause"
      @click="emit('pause')"
    >
      <Icon icon="lucide:pause" />
    </button>
    <button type="button" class="replay-controls__btn" aria-label="Skip to end" @click="emit('skipToEnd')">
      <Icon icon="lucide:fast-forward" />
    </button>
    <button type="button" class="replay-controls__btn" aria-label="Reset" @click="emit('reset')">
      <Icon icon="lucide:rotate-ccw" />
    </button>
    <select
      class="replay-controls__speed"
      :value="speed"
      @change="emit('update:speed', Number(($event.target as HTMLSelectElement).value))"
    >
      <option v-for="s in speeds" :key="s" :value="s">{{ s }}×</option>
    </select>
    <span class="replay-controls__state">{{ playbackState }}</span>
  </div>
</template>

<style scoped>
.replay-controls {
  display: flex; align-items: center; gap: 6px;
  padding: 6px 10px;
  border: 1px solid var(--tnzi-ai-border, #e5e7eb);
  border-radius: 8px;
  background: var(--tnzi-ai-surface, #fff);
}
.replay-controls__btn {
  width: 28px; height: 28px;
  display: flex; align-items: center; justify-content: center;
  background: transparent; border: none; border-radius: 4px;
  cursor: pointer; color: inherit;
}
.replay-controls__btn:hover { background: var(--tnzi-ai-border, #f3f4f6); }
.replay-controls__speed {
  height: 28px; padding: 0 6px;
  background: transparent; border: 1px solid var(--tnzi-ai-border, #e5e7eb);
  border-radius: 4px; font-size: 12px; color: inherit;
}
.replay-controls__state { font-size: 11px; opacity: .6; text-transform: uppercase; }
</style>

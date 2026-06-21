<template>
  <span class="t-presence-dot" :class="`t-presence-dot--${kind}`" :style="{ width: px, height: px }" />
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { UserPresenceStatus } from '@tnzi/core/services/chat'

const props = withDefaults(defineProps<{ status?: UserPresenceStatus | null; size?: number }>(), { size: 10 })
const px = computed(() => `${props.size}px`)
const kind = computed(() => {
  switch (props.status) {
    case UserPresenceStatus.Online: return 'online'
    case UserPresenceStatus.Away: return 'away'
    case UserPresenceStatus.Busy: return 'busy'
    case UserPresenceStatus.Invisible: return 'invisible'
    default: return 'offline'
  }
})
</script>

<style scoped>
.t-presence-dot { display: inline-block; border-radius: 50%; box-sizing: border-box; border: 2px solid var(--chat-surface, #fff); }
.t-presence-dot--online { background: #1aad19; }
.t-presence-dot--away { background: #f5a623; }
.t-presence-dot--busy { background: #e64340; }
.t-presence-dot--offline { background: #bcbcbc; }
.t-presence-dot--invisible { background: transparent; border-color: #bcbcbc; }
</style>

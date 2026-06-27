<template>
  <div class="t-chat-avatar-wrap" :style="{ width: px, height: px }">
    <div class="t-chat-avatar" :style="{ width: px, height: px, borderRadius: radius, background: avatarBg }">
      <img v-if="showImg" :src="url ?? ''" :alt="name ?? ''" class="t-chat-avatar__img" @error="imgOk = false" />
      <Icon v-else-if="system" icon="mdi:bullhorn-variant" class="t-chat-avatar__sys" :width="iconSize" :height="iconSize" />
      <span v-else class="t-chat-avatar__initial" :style="{ fontSize: initialSize }">{{ initial }}</span>
    </div>
    <TPresenceDot v-if="status != null" class="t-chat-avatar__dot" :status="status" :size="Math.max(8, Math.round(size * 0.28))" />
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { Icon } from '@iconify/vue'
import { UserPresenceStatus } from '@tnzi/core/services/chat'
import { resolveChatAvatarUrl, avatarColor, avatarInitial } from './avatar'
import TPresenceDot from './TPresenceDot.vue'

const props = withDefaults(
  defineProps<{
    name?: string | null
    fileId?: string | null
    /** Seed for the deterministic colour — defaults to fileId || name. */
    seed?: string | null
    size?: number
    status?: UserPresenceStatus | null
    /** Render a distinct system/announcement icon avatar (theme-colour solid +
     *  megaphone) so a System conversation is recognisable at a glance instead
     *  of a deterministic-colour letter avatar. */
    system?: boolean
  }>(),
  { size: 40 },
)

const imgOk = ref(true)
// Reset the error flag if the source changes (keyed lists reuse instances).
watch(
  () => props.fileId,
  () => {
    imgOk.value = true
  },
)

const url = computed(() => resolveChatAvatarUrl(props.fileId))
const showImg = computed(() => !!url.value && imgOk.value)
const color = computed(() => avatarColor(props.seed ?? props.name))
const initial = computed(() => avatarInitial(props.name))
const px = computed(() => `${props.size}px`)
const radius = computed(() => `${Math.max(4, Math.round(props.size * 0.18))}px`)
const initialSize = computed(() => `${Math.round(props.size * 0.42)}px`)
const iconSize = computed(() => Math.round(props.size * 0.56))
// System avatar uses a fixed theme-primary solid so it never collides with the
// deterministic palette of personal avatars.
const SYSTEM_BG = 'var(--chat-send, var(--tnzi-primary-600, #158278))'
const avatarBg = computed(() => (showImg.value ? '#fff' : props.system ? SYSTEM_BG : color.value))
</script>

<style scoped>
.t-chat-avatar-wrap { position: relative; flex-shrink: 0; }
.t-chat-avatar__dot { position: absolute; right: -1px; bottom: -1px; }

.t-chat-avatar {
  overflow: hidden;
  display: flex;
  align-items: center;
  justify-content: center;
  user-select: none;
}

.t-chat-avatar__img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}

.t-chat-avatar__initial {
  font-weight: 600;
  color: #fff;
  line-height: 1;
  letter-spacing: 0.01em;
}

.t-chat-avatar__sys {
  color: #fff;
}
</style>

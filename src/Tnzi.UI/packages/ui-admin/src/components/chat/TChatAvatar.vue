<template>
  <TAvatar
    :src="url"
    :name="system ? null : name"
    :seed="seed"
    :size="size"
    shape="rounded"
    :icon="system ? 'mdi:bullhorn-variant' : ''"
    :prefer-icon="system"
    :color="system ? SYSTEM_BG : undefined"
  >
    <template v-if="status != null" #badge>
      <TPresenceDot :status="status" :size="Math.max(8, Math.round(size * 0.28))" />
    </template>
  </TAvatar>
</template>

<script setup lang="ts">
/**
 * `TChatAvatar` — chat-flavoured wrapper around the shared `@tnzi/ui` `TAvatar`.
 *
 * Adds the two things chat needs on top of the generic primitive: a storage
 * file-id → preview-URL resolution, and the presence-dot / system-announcement
 * (megaphone) variants. Everything else (image → initial fallback, deterministic
 * colour, sizing) comes from `TAvatar`.
 */
import { computed } from 'vue'
import { TAvatar } from '@tnzi/ui'
import { UserPresenceStatus } from '@tnzi/core/services/chat'
import { resolveChatAvatarUrl } from './avatar'
import TPresenceDot from './TPresenceDot.vue'

const props = withDefaults(
  defineProps<{
    name?: string | null
    fileId?: string | null
    /** Seed for the deterministic colour — defaults to name (via TAvatar). */
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

const url = computed(() => resolveChatAvatarUrl(props.fileId))
// System avatar uses a fixed theme-primary solid so it never collides with the
// deterministic palette of personal avatars.
const SYSTEM_BG = 'var(--chat-send, var(--tnzi-primary-600, #158278))'
</script>

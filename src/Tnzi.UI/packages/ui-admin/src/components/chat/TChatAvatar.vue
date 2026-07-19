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
    <!-- A peer who lost `chat.use` gets a distinct "unavailable" marker (grey
         block) instead of the normal presence dot — the conversation stays, but
         they can no longer take part. The disabled marker takes precedence. -->
    <template v-if="disabled || status != null" #badge>
      <span
        v-if="disabled"
        class="t-chat-avatar__disabled"
        :style="{ width: badgeSize, height: badgeSize }"
        :title="disabledTitle"
      >
        <Icon icon="mdi:cancel" :width="10" />
      </span>
      <TPresenceDot v-else :status="status" :size="Math.max(8, Math.round(size * 0.28))" />
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
import { Icon } from '@iconify/vue'
import { TAvatar } from '@tnzi/ui'
import { UserPresenceStatus } from '@tnzi/core/services/chat'
import { translatePageKey } from '../../pages/_shared/translate'
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
    /** Peer has lost `chat.use` — show a distinct "unavailable" marker instead
     *  of the presence dot. */
    disabled?: boolean
  }>(),
  { size: 40 },
)

const url = computed(() => resolveChatAvatarUrl(props.fileId))
// System avatar uses a fixed theme-primary solid so it never collides with the
// deterministic palette of personal avatars.
const SYSTEM_BG = 'var(--chat-send, var(--tnzi-primary-600, #158278))'
const badgeSize = computed(() => `${Math.max(12, Math.round(props.size * 0.36))}px`)
const disabledTitle = computed(() => translatePageKey('chat', 'window.peerDisabled'))
</script>

<style scoped>
.t-chat-avatar__disabled {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  background: var(--chat-text-3, #b0b0b0);
  color: #fff;
  border: 2px solid var(--chat-bg, #fff);
  box-sizing: border-box;
}
</style>

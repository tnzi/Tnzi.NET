<template>
  <NPopover
    trigger="click"
    :show="popoverVisible"
    :z-index="POPOVER_Z"
    placement="left-start"
    :flip="true"
    @update:show="onUpdateShow"
  >
    <template #trigger>
      <slot>
        <TChatAvatar :name="name" :file-id="avatarFileId" :seed="userId" :size="36" />
      </slot>
    </template>

    <div class="t-member-popover">
      <div v-if="loading" class="t-member-popover__loading">
        <NSpin size="small" />
      </div>
      <template v-else-if="profile">
        <!-- Avatar + name row -->
        <div class="t-member-popover__header">
          <TChatAvatar
            :name="profile.name"
            :file-id="profile.avatarFileId"
            :seed="userId"
            :size="48"
            :status="profile.status"
          />
          <div class="t-member-popover__identity">
            <div class="t-member-popover__name">
              {{ profile.name }}<span v-if="alias" class="t-member-popover__alias"> ({{ alias }})</span>
            </div>
            <div class="t-member-popover__status-text" :class="`t-member-popover__status-text--${statusKind(profile.status)}`">
              {{ statusLabel(profile.status) }}
            </div>
          </div>
        </div>

        <!-- Contact info (shown only when the field carries a value) -->
        <div v-if="profile.bio || profile.email || profile.phone" class="t-member-popover__info">
          <div v-if="profile.bio" class="t-member-popover__info-row">
            <Icon icon="mdi:card-text-outline" :width="14" class="t-member-popover__info-icon" />
            <span class="t-member-popover__info-text">{{ profile.bio }}</span>
          </div>
          <div v-if="profile.email" class="t-member-popover__info-row">
            <Icon icon="mdi:email-outline" :width="14" class="t-member-popover__info-icon" />
            <span class="t-member-popover__info-text">{{ profile.email }}</span>
          </div>
          <div v-if="profile.phone" class="t-member-popover__info-row">
            <Icon icon="mdi:phone-outline" :width="14" class="t-member-popover__info-icon" />
            <span class="t-member-popover__info-text">{{ profile.phone }}</span>
          </div>
        </div>

        <!-- Last seen (offline only) -->
        <div v-if="isOffline(profile.status) && profile.lastSeenAt" class="t-member-popover__last-seen">
          {{ t('window.lastSeen') }}: {{ formatDateTime(profile.lastSeenAt) }}
        </div>

        <!-- Actions -->
        <div class="t-member-popover__actions">
          <NButton size="small" type="primary" block @click="onSendMessage">
            {{ t('window.sendMessage') }}
          </NButton>
        </div>
      </template>
      <div v-else class="t-member-popover__error">—</div>
    </div>
  </NPopover>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { NPopover, NButton, NSpin } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { UserPresenceStatus } from '@tnzi/core/services/chat'
import type { ChatContactProfileDto } from '@tnzi/core/services/chat'
import { formatDateTime } from '@tnzi/core'
import { useChatStore } from '../../stores/useChatStore'
import { translatePageKey } from '../../pages/_shared/translate'
import TChatAvatar from './TChatAvatar.vue'

// Popover must clear the chat NModal — give it a z-index above it. It also opens
// to the LEFT (into the message area, like WeChat) rather than the default "top":
// the member grid sits in the narrow right-side info panel, so a top-row avatar's
// "top" popover would extend above the window and get clipped by the viewport.
const POPOVER_Z = 3000

const props = defineProps<{
  userId: string
  name: string
  avatarFileId?: string | null
  alias?: string | null
}>()

const emit = defineEmits<{
  message: [userId: string]
}>()

const t = (k: string) => translatePageKey('chat', k)
const store = useChatStore()

const popoverVisible = ref(false)
const loading = ref(false)
const profile = ref<ChatContactProfileDto | null>(null)
let fetched = false

watch(() => props.userId, () => { fetched = false; profile.value = null })

function onUpdateShow(show: boolean) {
  popoverVisible.value = show
  if (show && !fetched) {
    fetched = true
    loading.value = true
    store
      .getContactProfile(props.userId)
      .then((p) => { profile.value = p })
      .catch(() => { /* leave profile null; popover still shows */ })
      .finally(() => { loading.value = false })
  }
}

function onSendMessage() {
  popoverVisible.value = false
  emit('message', props.userId)
}

function statusKind(status: UserPresenceStatus): string {
  switch (status) {
    case UserPresenceStatus.Online: return 'online'
    case UserPresenceStatus.Away: return 'away'
    case UserPresenceStatus.Busy: return 'busy'
    default: return 'offline'
  }
}

function statusLabel(status: UserPresenceStatus): string {
  switch (status) {
    case UserPresenceStatus.Online: return t('presence.online')
    case UserPresenceStatus.Away: return t('presence.away')
    case UserPresenceStatus.Busy: return t('presence.busy')
    case UserPresenceStatus.Invisible: return t('presence.invisible')
    default: return t('presence.offline')
  }
}

function isOffline(status: UserPresenceStatus): boolean {
  return status === UserPresenceStatus.Offline || status === UserPresenceStatus.Invisible
}
</script>

<style scoped>
.t-member-popover {
  width: 220px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 2px 0;
}

.t-member-popover__loading {
  display: flex;
  justify-content: center;
  align-items: center;
  height: 64px;
}

.t-member-popover__header {
  display: flex;
  align-items: center;
  gap: 10px;
}

.t-member-popover__identity {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.t-member-popover__name {
  font-size: 14px;
  font-weight: 600;
  color: var(--chat-text, var(--tnzi-base-text, #1a1a1a));
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.t-member-popover__alias {
  font-weight: 400;
  color: var(--chat-text-muted, var(--tnzi-base-text-muted, #888));
}

.t-member-popover__status-text {
  font-size: 12px;
}

.t-member-popover__status-text--online  { color: var(--chat-presence-online, #1aad19); }
.t-member-popover__status-text--away    { color: var(--chat-presence-away, #f5a623); }
.t-member-popover__status-text--busy    { color: var(--chat-presence-busy, #e64340); }
.t-member-popover__status-text--offline { color: var(--chat-presence-offline, #bcbcbc); }

.t-member-popover__last-seen {
  font-size: 11px;
  color: var(--chat-text-muted, var(--tnzi-base-text-muted, #888));
}

.t-member-popover__info {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding-top: 2px;
  border-top: 1px solid var(--chat-border, rgb(51 54 57 / 0.08));
}

.t-member-popover__info-row {
  display: flex;
  align-items: center;
  gap: 7px;
  font-size: 12px;
  color: var(--chat-text-2, var(--tnzi-base-text-muted, #6f6f6f));
}

.t-member-popover__info-icon {
  flex-shrink: 0;
  color: var(--chat-text-3, #a8a8a8);
}

.t-member-popover__info-text {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.t-member-popover__actions {
  padding-top: 2px;
}

.t-member-popover__error {
  text-align: center;
  color: var(--chat-text-muted, var(--tnzi-base-text-muted, #aaa));
  font-size: 12px;
}
</style>

<template>
  <NPopover
    trigger="click"
    :show="open"
    :show-arrow="true"
    :z-index="POPOVER_Z"
    placement="bottom-start"
    :width="160"
    raw
    @update:show="open = $event"
  >
    <template #trigger>
      <button class="t-presence-picker" type="button" :title="t('presence.myStatus')">
        <TChatAvatar :name="name" :file-id="avatarFileId" :status="status" :size="30" />
      </button>
    </template>

    <div class="t-presence-menu">
      <div class="t-presence-menu__head">{{ t('presence.myStatus') }}</div>
      <button
        v-for="opt in visibleOptions"
        :key="opt.key"
        type="button"
        class="t-presence-menu__opt"
        :class="{ 't-presence-menu__opt--active': opt.key === status }"
        @click="select(opt.key)"
      >
        <TPresenceDot :status="opt.key" :size="9" />
        <span class="t-presence-menu__label">{{ t(opt.labelKey) }}</span>
        <Icon v-if="opt.key === status" icon="mdi:check" :width="15" class="t-presence-menu__check" />
      </button>
    </div>
  </NPopover>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { NPopover } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { UserPresenceStatus } from '@tnzi/core/services/chat'
import { translatePageKey } from '../../i18n/translate'
import TChatAvatar from './TChatAvatar.vue'
import TPresenceDot from './TPresenceDot.vue'

const props = withDefaults(
  defineProps<{
    status: UserPresenceStatus
    name?: string
    avatarFileId?: string
    /** Deployment toggle - false drops "Invisible" from the status menu. */
    allowInvisible?: boolean
  }>(),
  { allowInvisible: true },
)

const emit = defineEmits<{ change: [status: UserPresenceStatus] }>()

const t = (k: string) => translatePageKey('chat', k)

// Popover must clear the chat NModal.
const POPOVER_Z = 3000
const open = ref(false)

const STATUS_OPTIONS: { key: UserPresenceStatus; labelKey: string }[] = [
  { key: UserPresenceStatus.Online, labelKey: 'presence.online' },
  { key: UserPresenceStatus.Away, labelKey: 'presence.away' },
  { key: UserPresenceStatus.Busy, labelKey: 'presence.busy' },
  { key: UserPresenceStatus.Invisible, labelKey: 'presence.invisible' },
]

const visibleOptions = computed(() =>
  props.allowInvisible
    ? STATUS_OPTIONS
    : STATUS_OPTIONS.filter((o) => o.key !== UserPresenceStatus.Invisible),
)

function select(key: UserPresenceStatus) {
  open.value = false
  emit('change', key)
}
</script>

<style scoped>
.t-presence-picker {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  border: none;
  background: transparent;
  padding: 0;
  cursor: pointer;
  border-radius: 50%;
}
</style>

<style>
/* Unscoped - the menu renders inside the teleported popover (`raw`). */
.t-presence-menu {
  background: var(--chat-surface, #fff);
  border: 1px solid var(--chat-border, #e6e6e6);
  border-radius: var(--tnzi-admin-radius-md, 8px);
  box-shadow: var(--tnzi-shadow-popover, 0 6px 24px rgba(0, 0, 0, 0.16));
  padding: 5px;
  overflow: hidden;
}

.t-presence-menu__head {
  font-size: 11px;
  color: var(--chat-text-3, #a8a8a8);
  padding: 4px 8px 6px;
}

.t-presence-menu__opt {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  border: none;
  background: transparent;
  cursor: pointer;
  padding: 7px 8px;
  border-radius: 6px;
  font-size: 13px;
  color: var(--chat-text, #1f1f1f);
  text-align: left;
}

.t-presence-menu__opt:hover {
  background: var(--chat-hover, rgb(51 54 57 / 0.06));
}

.t-presence-menu__label {
  flex: 1;
  min-width: 0;
}

.t-presence-menu__check {
  flex-shrink: 0;
  color: var(--chat-send, #158278);
}
</style>

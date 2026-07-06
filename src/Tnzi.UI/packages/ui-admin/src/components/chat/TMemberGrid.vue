<template>
  <div class="t-member-grid">
    <div class="t-member-grid__cells" :style="gridStyle">
      <!-- Member cells (collapsed or all) -->
      <template v-for="m in visibleMembers" :key="m.userId">
        <TMemberPopover
          :user-id="m.userId"
          :name="m.name"
          :avatar-file-id="m.avatarFileId"
          :alias="m.alias"
          :removable="canRemove && m.userId !== myId"
          @message="(uid) => emit('message', uid)"
          @remove="(uid) => emit('remove', uid)"
        >
          <div class="t-member-grid__cell">
            <TChatAvatar
              :name="m.name"
              :file-id="m.avatarFileId"
              :seed="m.userId"
              :size="AVATAR_SIZE"
              :status="chatStore.config.enablePresence ? m.status : null"
            />
            <span class="t-member-grid__name">{{ m.alias || m.name }}</span>
          </div>
        </TMemberPopover>
      </template>

      <!-- Add button — always the last cell, same row (never wraps onto its own
           line): in collapsed mode it sits as the final cell of the last row. -->
      <div v-if="canAdd" class="t-member-grid__cell t-member-grid__add-cell" @click="emit('add')">
        <div class="t-member-grid__add">
          <Icon icon="mdi:plus" :width="AVATAR_SIZE * 0.5" :height="AVATAR_SIZE * 0.5" class="t-member-grid__add-icon" />
        </div>
        <span class="t-member-grid__name">{{ t('window.add') }}</span>
      </div>
    </div>

    <!-- Show All / Show Less toggle (only when members overflow the collapsed rows) -->
    <div
      v-if="hasMore"
      class="t-member-grid__show-all"
      @click="expanded = !expanded"
    >
      <template v-if="!expanded">{{ t('window.showAll') }} ({{ members.length }})</template>
      <template v-else>{{ t('window.showLess') }}</template>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { Icon } from '@iconify/vue'
import type { ConversationMemberDto } from '@tnzi/core/services/chat'
import { translatePageKey } from '../../pages/_shared/translate'
import { useChatStore } from '../../stores/useChatStore'
import TChatAvatar from './TChatAvatar.vue'
import TMemberPopover from './TMemberPopover.vue'

const chatStore = useChatStore()

const AVATAR_SIZE = 36

const props = withDefaults(
  defineProps<{
    members: ConversationMemberDto[]
    canAdd: boolean
    /** Owner-only: expose "remove from group" on every member except myId. */
    canRemove?: boolean
    /** Current user id — the owner can never remove themselves. */
    myId?: string
    /** Grid columns — narrower avatars + more columns fill the 250px panel width
     *  (avoids the right-side whitespace of the old 3-per-row layout). */
    columns?: number
    /** Max rows shown while collapsed (the Add cell counts toward the last row). */
    maxRows?: number
  }>(),
  { columns: 5, maxRows: 3 },
)

const emit = defineEmits<{
  add: []
  message: [userId: string]
  remove: [userId: string]
}>()

const t = (k: string) => translatePageKey('chat', k)

const expanded = ref(false)

const gridStyle = computed(() => ({ gridTemplateColumns: `repeat(${props.columns}, 1fr)` }))

// Cells available while collapsed; the Add button claims one of them when present.
const collapsedMemberCount = computed(() => props.columns * props.maxRows - (props.canAdd ? 1 : 0))
const hasMore = computed(() => props.members.length > collapsedMemberCount.value)

const visibleMembers = computed(() =>
  expanded.value || !hasMore.value
    ? props.members
    : props.members.slice(0, collapsedMemberCount.value),
)
</script>

<style scoped>
.t-member-grid {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.t-member-grid__cells {
  display: grid;
  gap: 10px 4px;
}

.t-member-grid__cell {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
  cursor: pointer;
  min-width: 0;
}

.t-member-grid__name {
  font-size: 11px;
  color: var(--chat-text-muted, var(--tnzi-base-text-muted, #888));
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 100%;
  text-align: center;
  line-height: 1.2;
}

/* Add cell */
.t-member-grid__add-cell {
  cursor: pointer;
}

/* Inner dashed box: avatar-sized, centered within the cell */
.t-member-grid__add {
  width: 36px;
  height: 36px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: 1px dashed var(--chat-border, var(--tnzi-border, #d9d9d9));
  border-radius: 6px;
  color: var(--chat-text-muted, var(--tnzi-base-text-muted, #999));
  transition: background 0.15s, color 0.15s;
}

.t-member-grid__add-cell:hover .t-member-grid__add {
  background: rgb(var(--tnzi-base-text-rgb, 51 54 57) / 0.06);
  color: var(--chat-text, var(--tnzi-base-text, #333));
}

.t-member-grid__add-icon {
  flex-shrink: 0;
}

.t-member-grid__show-all {
  font-size: 12px;
  color: var(--chat-send, var(--tnzi-primary, #18a058));
  cursor: pointer;
  padding: 2px 0;
  user-select: none;
  align-self: flex-start;
}

.t-member-grid__show-all:hover {
  text-decoration: underline;
}
</style>

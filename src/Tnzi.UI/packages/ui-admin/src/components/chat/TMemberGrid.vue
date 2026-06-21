<template>
  <div class="t-member-grid">
    <div class="t-member-grid__cells">
      <!-- Member cells (collapsed or all) -->
      <template v-for="m in visibleMembers" :key="m.userId">
        <TMemberPopover
          :user-id="m.userId"
          :name="m.name"
          :avatar-file-id="m.avatarFileId"
          :alias="m.alias"
          @message="(uid) => emit('message', uid)"
        >
          <div class="t-member-grid__cell">
            <TChatAvatar
              :name="m.name"
              :file-id="m.avatarFileId"
              :seed="m.userId"
              :size="AVATAR_SIZE"
              :status="m.status"
            />
            <span class="t-member-grid__name">{{ m.name }}</span>
          </div>
        </TMemberPopover>
      </template>

      <!-- Add button — same outer cell footprint as a member cell (56px wide) -->
      <div v-if="canAdd" class="t-member-grid__cell t-member-grid__add-cell" @click="emit('add')">
        <div class="t-member-grid__add">
          <Icon icon="mdi:plus" :width="AVATAR_SIZE * 0.55" :height="AVATAR_SIZE * 0.55" class="t-member-grid__add-icon" />
        </div>
      </div>
    </div>

    <!-- Show All / Show Less toggle -->
    <div
      v-if="members.length > collapsedCount"
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
import TChatAvatar from './TChatAvatar.vue'
import TMemberPopover from './TMemberPopover.vue'

const AVATAR_SIZE = 40

const props = withDefaults(
  defineProps<{
    members: ConversationMemberDto[]
    canAdd: boolean
    collapsedCount?: number
  }>(),
  { collapsedCount: 10 },
)

const emit = defineEmits<{
  add: []
  message: [userId: string]
}>()

const t = (k: string) => translatePageKey('chat', k)

const expanded = ref(false)

const visibleMembers = computed(() =>
  expanded.value || props.members.length <= props.collapsedCount
    ? props.members
    : props.members.slice(0, props.collapsedCount),
)
</script>

<style scoped>
.t-member-grid {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.t-member-grid__cells {
  display: flex;
  flex-wrap: wrap;
  gap: 11px 10px;
}

.t-member-grid__cell {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
  cursor: pointer;
  width: 48px;
}

.t-member-grid__name {
  font-size: 11px;
  color: var(--chat-text-muted, var(--tnzi-base-text-muted, #888));
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 48px;
  text-align: center;
  line-height: 1.2;
}

/* Add cell: same 48px column footprint as a member cell */
.t-member-grid__add-cell {
  cursor: pointer;
}

/* Inner dashed box: avatar-sized (40×40) centered within the cell */
.t-member-grid__add {
  width: 40px;
  height: 40px;
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
}

.t-member-grid__show-all:hover {
  text-decoration: underline;
}
</style>

<template>
  <div class="t-group-avatar" :style="boxStyle">
    <!-- Positioning is inline: TAvatar's own scoped root style sets
         `position: relative` at equal specificity, so a scoped class rule
         here loses depending on stylesheet order - inline always wins. -->
    <TAvatar
      v-for="(tile, i) in tiles"
      :key="tile.member.userId || i"
      class="t-group-avatar__cell"
      :style="{ position: 'absolute', left: `${tile.left}px`, top: `${tile.top}px` }"
      :src="resolveChatAvatarUrl(tile.member.avatarFileId)"
      :name="tile.member.name || null"
      :seed="tile.member.userId"
      :size="tile.px"
      shape="rounded"
      :radius="2"
    />
  </div>
</template>

<script setup lang="ts">
/**
 * `TGroupAvatar` - WeChat-style composite group avatar.
 *
 * Tiles the first (up to 9) member avatars inside one square using the
 * `groupAvatarLayout` grid (2 columns for 1-4 members, 3 columns for 5-9).
 * Each cell is a full `TAvatar`, so members without a picture degrade to
 * their name initial on the deterministic palette. The member selection
 * (earliest N joined) comes from the backend via
 * `ConversationListItemDto.memberAvatars`.
 */
import { computed } from 'vue'
import { TAvatar } from '@tnzi/ui'
import type { ChatContactDto } from '@tnzi/core/services/chat'
import { resolveChatAvatarUrl } from './avatar'
import { groupAvatarLayout } from './groupAvatar'

const props = withDefaults(
  defineProps<{
    members: ChatContactDto[]
    /** Pixel size (width = height). Default 40 - matches TChatAvatar. */
    size?: number
  }>(),
  { size: 40 },
)

const shown = computed(() => props.members.slice(0, 9))

// Member + pixel-resolved cell, zipped so the template never indexes two
// parallel arrays (layout length always equals the shown length). The gap
// fraction is 1/size so cells sit exactly 1px apart; positions stay
// fractional (no rounding) so the gap doesn't drift per cell.
const tiles = computed(() => {
  const layout = groupAvatarLayout(shown.value.length, 1 / props.size)
  return shown.value.map((member, i) => {
    const c = layout[i] ?? { left: 0, top: 0, size: 1 }
    return {
      member,
      left: +(c.left * props.size).toFixed(2),
      top: +(c.top * props.size).toFixed(2),
      px: Math.max(8, +(c.size * props.size).toFixed(2)),
    }
  })
})

// Same rounded-corner formula as TAvatar's `rounded` shape so the composite
// sits visually flush with the single-avatar rows in the conversation list.
const boxStyle = computed(() => ({
  width: `${props.size}px`,
  height: `${props.size}px`,
  borderRadius: `${Math.max(4, Math.round(props.size * 0.18))}px`,
}))
</script>

<style scoped>
.t-group-avatar {
  position: relative;
  flex-shrink: 0;
  overflow: hidden;
  background: var(--chat-search-bg, rgb(51 54 57 / 0.08));
}
</style>

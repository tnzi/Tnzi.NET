<template>
  <n-card
    class="t-user-card"
    :class="[
      `t-user-card--${size}`,
      { 't-user-card--clickable': clickable },
    ]"
    @click="onCardClick"
  >
    <template #header>
      <div class="t-user-card-header">
        <n-avatar
          v-if="user.avatar"
          :src="user.avatar"
          :size="avatarSize"
          round
        />
        <n-avatar
          v-else
          :size="avatarSize"
          round
        >
          {{ userInitial }}
        </n-avatar>
        <span class="t-user-card-name">{{ user.name }}</span>
      </div>
    </template>

    <template #header-extra v-if="showStatus && user.status">
      <n-tag
        :type="statusTagType"
        size="small"
        round
      >
        {{ statusLabel }}
      </n-tag>
    </template>

    <div class="t-user-card-body">
      <div v-if="showEmail && user.email" class="t-user-card-field">
        <n-text depth="3" class="t-user-card-label">Email</n-text>
        <n-text>{{ user.email }}</n-text>
      </div>
      <div v-if="showRole && user.role" class="t-user-card-field">
        <n-text depth="3" class="t-user-card-label">Role</n-text>
        <n-text>{{ user.role }}</n-text>
      </div>
      <div v-if="user.description" class="t-user-card-field">
        <n-text depth="3" class="t-user-card-label">Description</n-text>
        <n-text depth="2">{{ user.description }}</n-text>
      </div>
    </div>

    <template #action v-if="showActions && resolvedActions.length > 0">
      <n-space justify="end" :size="8">
        <n-button
          v-for="action in resolvedActions"
          :key="action"
          size="small"
          :type="actionButtonType(action)"
          :quaternary="action !== 'delete'"
          :ghost="action === 'delete'"
          @click.stop="onAction(action)"
        >
          {{ actionLabel(action) }}
        </n-button>
      </n-space>
    </template>
  </n-card>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { NCard, NAvatar, NTag, NText, NButton, NSpace } from 'naive-ui'

interface UserInfo {
  id: string | number
  name: string
  avatar?: string
  email?: string
  role?: string
  status?: 'active' | 'inactive' | 'pending'
  description?: string
}

type ActionType = 'edit' | 'delete' | 'view'

interface Props {
  user: UserInfo
  showActions?: boolean
  actions?: ActionType[]
  clickable?: boolean
  size?: 'small' | 'medium' | 'large'
  showStatus?: boolean
  showEmail?: boolean
  showRole?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  showActions: true,
  actions: () => ['view', 'edit'],
  clickable: false,
  size: 'medium',
  showStatus: true,
  showEmail: true,
  showRole: true,
})

const emit = defineEmits<{
  click: [user: UserInfo]
  edit: [user: UserInfo]
  delete: [user: UserInfo]
  view: [user: UserInfo]
}>()

const userInitial = computed(() => {
  return props.user.name.charAt(0).toUpperCase()
})

const avatarSize = computed(() => {
  const sizeMap: Record<string, number> = {
    small: 32,
    medium: 40,
    large: 48,
  }
  return sizeMap[props.size] ?? 40
})

const statusTagType = computed<'success' | 'warning' | 'default'>(() => {
  const typeMap: Record<string, 'success' | 'warning' | 'default'> = {
    active: 'success',
    inactive: 'default',
    pending: 'warning',
  }
  return typeMap[props.user.status ?? ''] ?? 'default'
})

const statusLabel = computed(() => {
  const labelMap: Record<string, string> = {
    active: 'Active',
    inactive: 'Inactive',
    pending: 'Pending',
  }
  return labelMap[props.user.status ?? ''] ?? props.user.status ?? ''
})

const resolvedActions = computed(() => props.actions ?? ['view', 'edit'])

function actionButtonType(action: ActionType): 'primary' | 'error' | 'info' | 'default' {
  const typeMap: Record<ActionType, 'primary' | 'error' | 'info' | 'default'> = {
    view: 'info',
    edit: 'primary',
    delete: 'error',
  }
  return typeMap[action] ?? 'default'
}

function actionLabel(action: ActionType): string {
  const labelMap: Record<ActionType, string> = {
    view: 'View',
    edit: 'Edit',
    delete: 'Delete',
  }
  return labelMap[action] ?? action
}

function onCardClick() {
  if (props.clickable) {
    emit('click', props.user)
  }
}

function onAction(action: ActionType) {
  if (action === 'edit') emit('edit', props.user)
  else if (action === 'delete') emit('delete', props.user)
  else if (action === 'view') emit('view', props.user)
}
</script>

<style scoped>
.t-user-card--clickable {
  cursor: pointer;
  transition: box-shadow 0.2s ease, transform 0.15s ease;
}

.t-user-card--clickable:hover {
  box-shadow: 0 2px 12px rgba(0, 0, 0, 0.1);
  transform: translateY(-1px);
}

.t-user-card-header {
  display: flex;
  align-items: center;
  gap: 12px;
}

.t-user-card-name {
  font-weight: 600;
  font-size: 15px;
}

.t-user-card--small .t-user-card-name {
  font-size: 13px;
}

.t-user-card--large .t-user-card-name {
  font-size: 17px;
}

.t-user-card-body {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.t-user-card-field {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.t-user-card-label {
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}
</style>

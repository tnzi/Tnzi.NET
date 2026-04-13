<template>
  <n-card
    :class="[
      { 'cursor-pointer transition-all hover:shadow-md hover:-translate-y-0.25': clickable },
    ]"
    @click="onCardClick"
  >
    <template #header>
      <div class="flex items-center gap-3">
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
        <span :class="nameClass">{{ user.name }}</span>
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

    <div class="flex flex-col gap-2">
      <div v-if="showEmail && user.email" class="flex flex-col gap-0.5">
        <n-text depth="3" class="text-3 uppercase tracking-[0.025em]">Email</n-text>
        <n-text>{{ user.email }}</n-text>
      </div>
      <div v-if="showRole && user.role" class="flex flex-col gap-0.5">
        <n-text depth="3" class="text-3 uppercase tracking-[0.025em]">Role</n-text>
        <n-text>{{ user.role }}</n-text>
      </div>
      <div v-if="user.description" class="flex flex-col gap-0.5">
        <n-text depth="3" class="text-3 uppercase tracking-[0.025em]">Description</n-text>
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

const nameClass = computed(() => {
  const sizeMap: Record<string, string> = {
    small: 'font-600 text-[13px]',
    medium: 'font-600 text-[15px]',
    large: 'font-600 text-[17px]',
  }
  return sizeMap[props.size] ?? sizeMap.medium
})

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


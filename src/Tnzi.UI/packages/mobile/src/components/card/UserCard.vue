<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
interface IUserCardProps {
  user: {
    id: string | number;
    name: string;
    avatar?: string;
    email?: string;
    role?: string;
    status?: 'active' | 'inactive' | 'pending';
    description?: string;
  };
  showActions?: boolean;
  actions?: ('edit' | 'delete' | 'view')[];
  clickable?: boolean;
  size?: 'small' | 'medium' | 'large';
  showStatus?: boolean;
  showEmail?: boolean;
  showRole?: boolean;
  avatarFallback?: string;
  class?: string | string[];
  style?: string | Record<string, string | number>;
}

interface IUserCardEmits {
  click: [user: IUserCardProps['user']];
  edit: [user: IUserCardProps['user']];
  delete: [user: IUserCardProps['user']];
  view: [user: IUserCardProps['user']];
}

const props = withDefaults(defineProps<IUserCardProps>(), {
  showActions: false,
  actions: () => ['edit', 'delete', 'view'],
  clickable: true,
  size: 'medium',
  showStatus: true,
  showEmail: true,
  showRole: true,
  avatarFallback: 'U',
});

const emit = defineEmits<IUserCardEmits>();
const { t } = useI18n();

const statusType = computed(() => {
  if (props.user.status === 'active') return 'success';
  if (props.user.status === 'inactive') return 'default';
  return 'warning';
});

const handleAction = (action: 'edit' | 'delete' | 'view') => {
  if (action === 'edit') emit('edit', props.user);
  else if (action === 'delete') emit('delete', props.user);
  else emit('view', props.user);
};
</script>

<template>
  <van-card
    :title="props.user.name"
    :desc="props.user.description || ''"
    :thumb="props.user.avatar || ''"
    @click="props.clickable ? emit('click', props.user) : undefined"
  >
    <template #tags>
      <van-tag v-if="props.showRole && props.user.role" plain type="primary" class="mr-1">{{ props.user.role }}</van-tag>
      <van-tag v-if="props.showStatus && props.user.status" :type="statusType">{{ props.user.status }}</van-tag>
    </template>

    <template #footer>
      <div class="flex items-center justify-between gap-2">
        <span v-if="props.showEmail && props.user.email" class="text-xs text-slate-500">{{ props.user.email }}</span>
        <div v-if="props.showActions" class="flex gap-2">
          <van-button
            v-for="action in props.actions"
            :key="action"
            size="mini"
            plain
            type="primary"
            @click.stop="handleAction(action)"
          >
            {{ t(`common.${action}`) }}
          </van-button>
        </div>
      </div>
    </template>
  </van-card>
</template>


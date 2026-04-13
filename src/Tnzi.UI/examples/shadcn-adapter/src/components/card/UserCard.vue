<script setup lang="ts">
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

const sizeClassMap = {
  small: 'p-4',
  medium: 'p-5',
  large: 'p-6',
} as const;

const statusClassMap = {
  active: 'bg-emerald-100 text-emerald-700',
  inactive: 'bg-slate-100 text-slate-600',
  pending: 'bg-amber-100 text-amber-700',
} as const;

const avatarSizeMap = {
  small: 'h-9 w-9 text-sm',
  medium: 'h-12 w-12 text-base',
  large: 'h-14 w-14 text-lg',
} as const;

const getInitials = () => {
  if (!props.user.name) return props.avatarFallback;
  return props.user.name
    .split(' ')
    .map((word) => word[0])
    .slice(0, 2)
    .join('')
    .toUpperCase();
};

const handleCardClick = () => {
  if (props.clickable) {
    emit('click', props.user);
  }
};

const handleActionClick = (action: 'edit' | 'delete' | 'view') => {
  if (action === 'edit') {
    emit('edit', props.user);
    return;
  }
  if (action === 'delete') {
    emit('delete', props.user);
    return;
  }
  emit('view', props.user);
};
</script>

<template>
  <article
    class="rounded-xl border bg-card text-card-foreground shadow-sm transition"
    :class="[sizeClassMap[props.size], props.clickable ? 'cursor-pointer hover:shadow-md' : '']"
    @click="handleCardClick"
  >
    <div class="flex items-start gap-3">
      <img
        v-if="props.user.avatar"
        :src="props.user.avatar"
        :alt="props.user.name"
        class="rounded-full object-cover"
        :class="avatarSizeMap[props.size]"
      />
      <div
        v-else
        class="grid place-items-center rounded-full bg-primary font-semibold text-primary-foreground"
        :class="avatarSizeMap[props.size]"
      >
        {{ getInitials() }}
      </div>

      <div class="min-w-0 flex-1">
        <h3 class="truncate text-base font-semibold">{{ props.user.name }}</h3>
        <p v-if="props.showRole && props.user.role" class="mt-1 text-sm text-muted-foreground">
          {{ props.user.role }}
        </p>
        <p v-if="props.showEmail && props.user.email" class="truncate text-sm text-muted-foreground">
          {{ props.user.email }}
        </p>
        <span
          v-if="props.showStatus && props.user.status"
          class="mt-2 inline-flex rounded-full px-2 py-0.5 text-xs font-medium"
          :class="statusClassMap[props.user.status]"
        >
          {{ props.user.status }}
        </span>
      </div>
    </div>

    <p v-if="props.user.description" class="mt-3 text-sm text-muted-foreground">
      {{ props.user.description }}
    </p>

    <div v-if="props.showActions" class="mt-4 flex justify-end gap-2" @click.stop>
      <button
        v-for="action in props.actions"
        :key="action"
        type="button"
        class="rounded-md border px-3 py-1.5 text-xs font-medium capitalize transition hover:bg-accent"
        @click="handleActionClick(action)"
      >
        {{ action }}
      </button>
    </div>
  </article>
</template>

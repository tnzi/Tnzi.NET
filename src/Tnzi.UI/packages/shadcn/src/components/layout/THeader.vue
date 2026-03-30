<script setup lang="ts">
import type { IHeaderEmits, IHeaderProps } from '@tnzi/core/components';
import { Button } from '../primitive/ui';
import { useShadcnTheme } from '../../composables/useShadcnTheme';

const props = defineProps<IHeaderProps>();
const emit = defineEmits<IHeaderEmits>();

const { isDark, toggleTheme } = useShadcnTheme();

const handleThemeToggle = () => {
  toggleTheme();
  emit('themeToggle', isDark.value);
};
</script>

<template>
  <header class="flex min-h-14 items-center justify-between gap-4" :class="props.class" :style="props.style">
    <div class="flex min-w-0 items-center gap-3">
      <img v-if="props.logo" :src="props.logo" :alt="props.logoText || 'logo'" class="h-8 w-8 rounded" />
      <span v-else-if="props.logoText" class="text-base font-semibold text-primary">{{ props.logoText }}</span>
      <h1 v-if="props.title" class="truncate text-base font-semibold">{{ props.title }}</h1>
    </div>

    <nav v-if="props.showBreadcrumbs && props.breadcrumbs?.length" class="hidden items-center gap-2 text-sm text-muted-foreground md:flex">
      <template v-for="(item, index) in props.breadcrumbs" :key="item.key || index">
        <a v-if="item.path && index < props.breadcrumbs.length - 1" :href="item.path" class="transition hover:text-foreground">
          {{ item.label }}
        </a>
        <span v-else class="text-foreground">{{ item.label }}</span>
        <span v-if="index < props.breadcrumbs.length - 1">/</span>
      </template>
    </nav>

    <div class="flex items-center gap-2">
      <Button
        v-if="props.showThemeToggle"
        variant="outline"
        size="sm"
        @click="handleThemeToggle"
      >
        {{ isDark ? 'Light' : 'Dark' }}
      </Button>

      <button
        v-if="props.user && props.showUserMenu"
        type="button"
        class="inline-flex items-center gap-2 rounded-md px-2 py-1 text-sm transition hover:bg-accent"
        @click="emit('userMenuClick', 'profile')"
      >
        <img v-if="props.user.avatar" :src="props.user.avatar" :alt="props.user.name || 'user'" class="h-7 w-7 rounded-full" />
        <span v-else class="grid h-7 w-7 place-items-center rounded-full bg-primary text-xs font-semibold text-primary-foreground">
          {{ (props.user.name || 'U').slice(0, 1).toUpperCase() }}
        </span>
        <span class="hidden sm:inline">{{ props.user.name }}</span>
      </button>
    </div>
  </header>
</template>

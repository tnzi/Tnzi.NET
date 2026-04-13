<script setup lang="ts">
import { SwipeCell as VanSwipeCell, Button as VanButton } from 'vant';

export interface SwipeAction {
  text: string;
  type?: 'primary' | 'success' | 'warning' | 'danger';
  onClick?: () => void;
}

const props = withDefaults(defineProps<{
  leftActions?: SwipeAction[];
  rightActions?: SwipeAction[];
  disabled?: boolean;
}>(), {
  leftActions: () => [],
  rightActions: () => [],
  disabled: false,
});
</script>

<template>
  <VanSwipeCell :disabled="props.disabled">
    <template v-if="props.leftActions.length" #left>
      <VanButton
        v-for="(action, i) in props.leftActions"
        :key="i"
        :type="action.type ?? 'primary'"
        square
        @click="action.onClick?.()"
      >
        {{ action.text }}
      </VanButton>
    </template>

    <slot />

    <template v-if="props.rightActions.length" #right>
      <VanButton
        v-for="(action, i) in props.rightActions"
        :key="i"
        :type="action.type ?? 'danger'"
        square
        @click="action.onClick?.()"
      >
        {{ action.text }}
      </VanButton>
    </template>
  </VanSwipeCell>
</template>

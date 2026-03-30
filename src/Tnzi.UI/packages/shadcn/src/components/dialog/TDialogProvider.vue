<script setup lang="ts">
/**
 * TDialogProvider - Programmatic dialog renderer.
 *
 * Mount this component once at your app root (like vue-sonner's <Toaster>).
 * It renders styled AlertDialog based on the reactive singleton state
 * managed by dialog-state.ts.
 */
import { computed } from 'vue';
import { getDialogState } from '../../composables/dialog-state';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '../primitive/ui/alert-dialog';
import { Input } from '../primitive/ui/input';

const state = getDialogState();

const typeColor = computed(() => {
  const colors: Record<string, string> = {
    info: 'text-blue-500',
    success: 'text-green-500',
    warning: 'text-amber-500',
    error: 'text-red-500',
  };
  return colors[state.type] ?? colors.info;
});

const showCancelButton = computed(() => state.mode === 'confirm' || state.mode === 'prompt');

function handleConfirm() {
  if (state.resolve) {
    if (state.mode === 'alert') {
      state.resolve(undefined);
    } else if (state.mode === 'confirm') {
      state.resolve(true);
    } else if (state.mode === 'prompt') {
      state.resolve(state.inputValue);
    }
  }
  state.open = false;
}

function handleCancel() {
  if (state.resolve) {
    if (state.mode === 'confirm') {
      state.resolve(false);
    } else if (state.mode === 'prompt') {
      state.resolve(null);
    }
  }
  state.open = false;
}
</script>

<template>
  <AlertDialog :open="state.open" @update:open="(v: boolean) => { if (!v) handleCancel() }">
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle class="flex items-center gap-2">
          <!-- Inline SVG icons by type -->
          <svg v-if="state.type === 'info'" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="size-5 shrink-0" :class="typeColor">
            <circle cx="12" cy="12" r="10" /><path d="M12 16v-4" /><path d="M12 8h.01" />
          </svg>
          <svg v-else-if="state.type === 'success'" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="size-5 shrink-0" :class="typeColor">
            <circle cx="12" cy="12" r="10" /><path d="m9 12 2 2 4-4" />
          </svg>
          <svg v-else-if="state.type === 'warning'" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="size-5 shrink-0" :class="typeColor">
            <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3" /><path d="M12 9v4" /><path d="M12 17h.01" />
          </svg>
          <svg v-else-if="state.type === 'error'" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="size-5 shrink-0" :class="typeColor">
            <circle cx="12" cy="12" r="10" /><path d="m15 9-6 6" /><path d="m9 9 6 6" />
          </svg>
          <span>{{ state.title }}</span>
        </AlertDialogTitle>
        <AlertDialogDescription>
          {{ state.content }}
        </AlertDialogDescription>
      </AlertDialogHeader>

      <!-- Prompt mode: input field -->
      <Input
        v-if="state.mode === 'prompt'"
        v-model="state.inputValue"
        class="mt-2"
        @keydown.enter="handleConfirm"
      />

      <AlertDialogFooter>
        <AlertDialogCancel v-if="showCancelButton" @click="handleCancel">
          {{ state.cancelText }}
        </AlertDialogCancel>
        <AlertDialogAction @click="handleConfirm">
          {{ state.confirmText }}
        </AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>

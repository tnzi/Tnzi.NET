<template>
  <div class="space-y-4">
    <!-- Message Adapter -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">Message (vue-sonner)</CardTitle>
        <CardDescription>Manages toast notifications via vue-sonner.</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="flex flex-wrap gap-2">
          <Button size="sm" variant="outline" @click="testMessage('info')">Info</Button>
          <Button size="sm" variant="outline" @click="testMessage('success')">Success</Button>
          <Button size="sm" variant="outline" @click="testMessage('warning')">Warning</Button>
          <Button size="sm" variant="outline" @click="testMessage('error')">Error</Button>
          <Button size="sm" variant="outline" @click="testLoading">Loading</Button>
        </div>
      </CardContent>
    </Card>

    <!-- Dialog Adapter -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">Dialog (alert / confirm)</CardTitle>
        <CardDescription>Dialog adapter wrapping browser alert/confirm.</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="flex flex-wrap gap-2">
          <Button size="sm" @click="testDialog('info')">Info Dialog</Button>
          <Button size="sm" @click="testDialog('success')">Success Dialog</Button>
          <Button size="sm" @click="testDialog('warning')">Warning Dialog</Button>
          <Button size="sm" @click="testDialog('error')">Error Dialog</Button>
          <Button size="sm" variant="destructive" @click="testConfirmDialog">Confirm Dialog</Button>
        </div>
        <p v-if="lastDialogResult !== null" class="mt-2 text-sm text-muted-foreground">
          Last result: {{ lastDialogResult }}
        </p>
      </CardContent>
    </Card>

    <!-- Theme Adapter -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">Theme</CardTitle>
        <CardDescription>Theme adapter for dark/light mode management.</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="flex items-center gap-3">
          <div class="rounded-md border px-3 py-1.5 text-sm">
            Resolved theme: <span class="font-semibold">{{ isDark ? 'Dark' : 'Light' }}</span>
          </div>
          <Button size="sm" @click="theme.setTheme(false)">Apply Light</Button>
          <Button size="sm" @click="theme.setTheme(true)">Apply Dark</Button>
        </div>
      </CardContent>
    </Card>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue';
import { Button, Card, CardHeader, CardTitle, CardDescription, CardContent } from '@tnzi/shadcn';
import { useShadcnMessage, useShadcnDialog, useShadcnTheme } from '@tnzi/shadcn';

const message = useShadcnMessage();
const dialog = useShadcnDialog();
const theme = useShadcnTheme();

const isDark = computed(() => theme.isDark.value);
const lastDialogResult = ref<string | null>(null);

const testMessage = (type: 'info' | 'success' | 'warning' | 'error') => {
  const messages: Record<string, string> = {
    info: 'This is an info message',
    success: 'Operation successful!',
    warning: 'This is a warning',
    error: 'An error occurred',
  };
  message.show(messages[type], type);
};

const testLoading = () => {
  message.show('Loading...', 'info');
  // Auto-dismiss is handled by vue-sonner
};

const testDialog = (type: 'info' | 'success' | 'warning' | 'error') => {
  const titles: Record<string, string> = {
    info: 'Information', success: 'Success', warning: 'Warning', error: 'Error',
  };
  const contents: Record<string, string> = {
    info: 'This is an informational dialog',
    success: 'Operation completed successfully',
    warning: 'Please be careful with this action',
    error: 'Something went wrong',
  };
  dialog.show({ title: titles[type], content: contents[type], type });
};

const testConfirmDialog = async () => {
  const confirmed = await dialog.confirm({
    title: 'Confirm Action',
    content: 'Are you sure you want to proceed?',
    positiveText: 'Yes',
    negativeText: 'No',
  });
  if (confirmed) {
    lastDialogResult.value = 'Confirmed';
    message.show('Confirmed!', 'success');
  } else {
    lastDialogResult.value = 'Cancelled';
    message.show('Cancelled', 'info');
  }
};
</script>

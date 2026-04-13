<template>
  <div class="space-y-4">
    <!-- Message -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">Message</CardTitle>
        <CardDescription>顶部消息条 — 轻量级反馈提示</CardDescription>
      </CardHeader>
      <CardContent class="space-y-3">
        <div class="flex flex-wrap gap-2">
          <Button size="sm" @click="message.info('This is an info message')">Info</Button>
          <Button size="sm" variant="secondary" @click="message.success('Operation succeeded!')">Success</Button>
          <Button size="sm" variant="outline" @click="message.warning('Please check your input')">Warning</Button>
          <Button size="sm" variant="destructive" @click="message.error('Something went wrong')">Error</Button>
          <Button size="sm" variant="ghost" @click="handleLoading">Loading (2s)</Button>
        </div>
      </CardContent>
    </Card>

    <!-- Notification -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">Notification</CardTitle>
        <CardDescription>右侧通知卡片 — 富内容通知，支持标题、描述、操作</CardDescription>
      </CardHeader>
      <CardContent class="space-y-3">
        <div class="flex flex-wrap gap-2">
          <Button size="sm" @click="notificationApi.info('Your profile has been updated.', { title: 'Info' })">Info</Button>
          <Button size="sm" variant="secondary" @click="notificationApi.success('File uploaded successfully.', { title: 'Success', meta: 'Just now' })">Success + Meta</Button>
          <Button size="sm" variant="outline" @click="notificationApi.warning('Your storage is almost full.', { title: 'Warning', duration: 5000 })">Warning (5s auto-close)</Button>
          <Button size="sm" variant="destructive" @click="notificationApi.error('Failed to connect to server.', { title: 'Error' })">Error</Button>
          <Button size="sm" variant="ghost" @click="notificationApi.destroyAll()">Destroy All</Button>
        </div>
      </CardContent>
    </Card>

    <!-- Dialog -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">Dialog</CardTitle>
        <CardDescription>模态对话框 — naive-ui 风格动画 (scale 0.5→1)</CardDescription>
      </CardHeader>
      <CardContent class="space-y-3">
        <div class="flex flex-wrap gap-2">
          <Button size="sm" @click="handleAlert">Alert</Button>
          <Button size="sm" variant="secondary" @click="handleConfirm">Confirm</Button>
          <Button size="sm" variant="outline" @click="handleDialogInfo">Info Dialog</Button>
          <Button size="sm" variant="destructive" @click="handleDialogError">Error Dialog</Button>
        </div>
        <p v-if="lastDialogResult !== null" class="text-sm text-muted-foreground">
          Last result: <code class="bg-muted px-1 rounded">{{ lastDialogResult }}</code>
        </p>
      </CardContent>
    </Card>

    <!-- Loading Bar -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">Loading Bar</CardTitle>
        <CardDescription>顶部加载进度条 — 路由切换/异步操作</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="flex flex-wrap gap-2">
          <Button size="sm" @click="loadingBarApi.start()">Start</Button>
          <Button size="sm" variant="secondary" @click="loadingBarApi.finish()">Finish</Button>
          <Button size="sm" variant="destructive" @click="loadingBarApi.error()">Error</Button>
          <Button size="sm" variant="outline" @click="simulateAsync">Simulate Async (2s)</Button>
        </div>
      </CardContent>
    </Card>

    <!-- Popconfirm -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">Popconfirm</CardTitle>
        <CardDescription>气泡确认框 — 轻量级操作确认</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="flex flex-wrap gap-2">
          <Popconfirm
            content="Are you sure you want to delete this item?"
            :on-positive-click="() => { message.success('Deleted!') }"
          >
            <Button size="sm" variant="destructive">Delete</Button>
          </Popconfirm>

          <Popconfirm
            type="info"
            content="This will reset all settings to default."
            positive-text="Reset"
            negative-text="Keep"
            :on-positive-click="() => { message.info('Settings reset') }"
          >
            <Button size="sm" variant="outline">Reset Settings</Button>
          </Popconfirm>

          <Popconfirm
            type="error"
            content="This action cannot be undone!"
            positive-text="Yes, delete all"
            :on-positive-click="handleAsyncDelete"
          >
            <Button size="sm" variant="destructive">Async Delete (1s)</Button>
          </Popconfirm>

          <Popconfirm content="Can't touch this" :disabled="true">
            <Button size="sm" variant="ghost" disabled>Disabled</Button>
          </Popconfirm>
        </div>
      </CardContent>
    </Card>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import {
  Button, Card, CardHeader, CardTitle, CardDescription, CardContent,
  Popconfirm,
  useMessage, useDialog,
} from '@tnzi/ui';
import { notificationApi } from '@tnzi/ui';
import { loadingBarApi } from '@tnzi/ui';

const message = useMessage();
const dialog = useDialog();
const lastDialogResult = ref<string | null>(null);

// Message loading demo
function handleLoading() {
  const msg = message.loading('Processing...');
  setTimeout(() => {
    msg.destroy();
    message.success('Done!');
  }, 2000);
}

// Dialog demos
async function handleAlert() {
  await dialog.alert({ content: 'This is a simple alert.' });
  lastDialogResult.value = 'alert closed';
}

async function handleConfirm() {
  const ok = await dialog.confirm({ content: 'Do you want to proceed?' });
  lastDialogResult.value = `confirm: ${ok}`;
}

function handleDialogInfo() {
  dialog.info({
    title: 'Information',
    content: 'This is an informational dialog with a custom title.',
    positiveText: 'Got it',
  });
}

function handleDialogError() {
  dialog.error({
    title: 'Connection Failed',
    content: 'Unable to reach the server. Please check your network connection.',
    positiveText: 'Retry',
    negativeText: 'Cancel',
  });
}

// Loading bar demo
async function simulateAsync() {
  loadingBarApi.start();
  await new Promise(resolve => setTimeout(resolve, 2000));
  loadingBarApi.finish();
}

// Popconfirm async demo
function handleAsyncDelete(): Promise<void> {
  return new Promise(resolve => {
    setTimeout(() => {
      message.success('All data deleted!');
      resolve();
    }, 1000);
  });
}
</script>

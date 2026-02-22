<template>
  <div class="section-page">
    <div class="section-header">
      <h1 class="section-title">Adapters</h1>
      <p class="section-desc">Message, dialog, and theme adapters that wrap Naive UI's native APIs.</p>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Message Adapter</h2>
      <p class="demo-desc">createNaiveMessageAdapter wraps Naive UI's useMessage API.</p>
      <PreviewBox :no-badge :full-width>
        <n-space>
          <n-button @click="() => msgAdapter.info('Information message')">Info</n-button>
          <n-button type="primary" @click="() => msgAdapter.success('Operation succeeded')">
            Success
          </n-button>
          <n-button type="warning" @click="() => msgAdapter.warning('Warning message')">
            Warning
          </n-button>
          <n-button type="error" @click="() => msgAdapter.error('Error occurred')">Error</n-button>
          <n-button @click="handleLoading">Loading</n-button>
        </n-space>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Dialog Adapter</h2>
      <p class="demo-desc">createNaiveDialogAdapter wraps Naive UI's useDialog API.</p>
      <PreviewBox :no-badge :full-width>
        <n-space vertical :size="8">
          <n-space>
            <n-button @click="handleAlert">Alert</n-button>
            <n-button type="primary" @click="handleConfirm">Confirm</n-button>
            <n-button @click="handlePrompt">Prompt</n-button>
          </n-space>
          <n-text v-if="lastDialogResult !== null" depth="3">
            Last result: {{ lastDialogResult }}
          </n-text>
        </n-space>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Theme Adapter</h2>
      <p class="demo-desc">createNaiveThemeAdapter manages dark/light mode on the document element.</p>
      <PreviewBox :no-badge :full-width>
        <n-space align="center">
          <n-text>Resolved theme: {{ themeAdapter.getResolvedTheme() }}</n-text>
          <n-button size="small" @click="handleApplyLight">Apply Light</n-button>
          <n-button size="small" @click="handleApplyDark">Apply Dark</n-button>
        </n-space>
      </PreviewBox>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { useMessage, useDialog } from 'naive-ui';
import {
  createNaiveMessageAdapter,
  createNaiveDialogAdapter,
  createNaiveThemeAdapter,
} from '@tnzi/naive-ui';
import PreviewBox from '../components/PreviewBox.vue';

const message = useMessage();
const dialog = useDialog();

const msgAdapter = createNaiveMessageAdapter(message);
const dlgAdapter = createNaiveDialogAdapter(dialog);
const themeAdapter = createNaiveThemeAdapter();

const lastDialogResult = ref<string | null>(null);

function handleLoading() {
  const close = msgAdapter.loading('Loading...');
  setTimeout(() => close(), 2000);
}

async function handleAlert() {
  await dlgAdapter.alert('This is an alert dialog.', { title: 'Alert' });
  lastDialogResult.value = 'Alert closed';
}

async function handleConfirm() {
  const confirmed = await dlgAdapter.confirm('Do you want to proceed?', { title: 'Confirm' });
  lastDialogResult.value = confirmed ? 'Confirmed' : 'Cancelled';
}

async function handlePrompt() {
  const value = await dlgAdapter.prompt('Enter a value:');
  lastDialogResult.value = value ? `Input: ${value}` : 'Cancelled';
}

function handleApplyLight() {
  themeAdapter.applyTheme('light');
}

function handleApplyDark() {
  themeAdapter.applyTheme('dark');
}
</script>

<style scoped>
.section-page {
  max-width: 960px;
}
.section-header {
  margin-bottom: 32px;
}
.section-title {
  font-size: 32px;
  font-weight: 800;
  color: var(--pg-text, #0f172a);
  margin: 0 0 8px 0;
}
.section-desc {
  font-size: 15px;
  color: var(--pg-text-muted, #64748b);
  margin: 0;
  line-height: 1.6;
}
.demo-block {
  margin-bottom: 32px;
}
.demo-label {
  font-size: 16px;
  font-weight: 600;
  color: var(--pg-text, #0f172a);
  margin: 0 0 12px 0;
}
.demo-desc {
  font-size: 13px;
  color: var(--pg-text-muted, #64748b);
  margin: 0 0 8px 0;
  line-height: 1.5;
}
</style>

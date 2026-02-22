<script setup lang="ts">
import { ref } from 'vue';
import { showToast, showSuccessToast, showFailToast, showLoadingToast, showConfirmDialog, showDialog } from 'vant';
import { createVantMessageAdapter, createVantDialogAdapter } from '@tnzi/vant';

// 直接使用 adapter 实例
const messageAdapter = createVantMessageAdapter();
const dialogAdapter = createVantDialogAdapter();

const loadingClose = ref<(() => void) | null>(null);

// Message Adapter 测试
const testMessageSuccess = () => messageAdapter.success('Operation successful!');
const testMessageError = () => messageAdapter.error('Something went wrong');
const testMessageWarning = () => messageAdapter.warning('Please be careful');
const testMessageInfo = () => messageAdapter.info('Here is some info');
const testMessageLoading = () => {
  const close = messageAdapter.loading('Loading data...');
  loadingClose.value = close ?? null;
  setTimeout(() => {
    if (close) close();
    loadingClose.value = null;
    showToast('Loading finished');
  }, 2000);
};

// Dialog Adapter 测试
const testDialogAlert = async () => {
  await dialogAdapter.alert('This is an alert message.', { title: 'Alert' });
  showToast('Alert closed');
};

const testDialogConfirm = async () => {
  const result = await dialogAdapter.confirm('Are you sure you want to proceed?', {
    title: 'Confirm',
    confirmText: 'Yes',
    cancelText: 'No',
  });
  showToast(result ? 'Confirmed!' : 'Cancelled');
};

const testDialogPrompt = async () => {
  const value = await dialogAdapter.prompt('Enter your name:', { title: 'Prompt' });
  if (value) {
    showToast(`Hello, ${value}!`);
  }
};

// 原生 Vant 方法
const testNativeToast = () => showToast('Native Vant Toast');
const testNativeDialog = () => {
  showDialog({ title: 'Native Dialog', message: 'Vant native showDialog' });
};
</script>

<template>
  <div class="adapters-section">
    <!-- Message Adapter -->
    <van-cell-group inset title="MessageAdapter (Toast)">
      <van-cell title="createVantMessageAdapter()" label="Wraps Vant Toast functions" />
      <div class="btn-grid">
        <van-button type="primary" size="small" @click="testMessageSuccess">Success</van-button>
        <van-button type="danger" size="small" @click="testMessageError">Error</van-button>
        <van-button type="warning" size="small" @click="testMessageWarning">Warning</van-button>
        <van-button size="small" @click="testMessageInfo">Info</van-button>
        <van-button type="primary" size="small" plain @click="testMessageLoading">Loading</van-button>
      </div>
    </van-cell-group>

    <!-- Dialog Adapter -->
    <van-cell-group inset title="DialogAdapter">
      <van-cell title="createVantDialogAdapter()" label="Wraps Vant Dialog functions" />
      <div class="btn-grid">
        <van-button type="primary" size="small" @click="testDialogAlert">Alert</van-button>
        <van-button type="primary" size="small" @click="testDialogConfirm">Confirm</van-button>
        <van-button type="primary" size="small" @click="testDialogPrompt">Prompt</van-button>
      </div>
    </van-cell-group>

    <!-- Vant Native -->
    <van-cell-group inset title="Native Vant APIs">
      <van-cell title="showToast / showDialog" label="Direct Vant function calls" />
      <div class="btn-grid">
        <van-button size="small" @click="testNativeToast">showToast</van-button>
        <van-button size="small" @click="testNativeDialog">showDialog</van-button>
      </div>
    </van-cell-group>
  </div>
</template>

<style scoped>
.adapters-section {
  padding-bottom: 8px;
}

.btn-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  padding: 8px 12px;
}
</style>

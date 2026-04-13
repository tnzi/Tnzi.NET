<template>
  <div class="desktop-wrapper">
    <div class="device-area">
      <DeviceFrame>
        <slot />
      </DeviceFrame>
    </div>
    <div class="info-panel">
      <h1 class="info-title">@tnzi/mobile</h1>
      <p class="info-desc">Tnzi UI Mobile Components (Vant)</p>
      <div class="qr-section">
        <p class="qr-label">Scan to preview on mobile</p>
        <img :src="qrUrl" alt="QR Code" class="qr-code" />
      </div>
      <div class="info-links">
        <p class="info-hint">Built with Vue 3 + Vant 4</p>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import DeviceFrame from './DeviceFrame.vue';

const qrUrl = computed(() => {
  const url = typeof window !== 'undefined' ? window.location.href : '';
  return `https://api.qrserver.com/v1/create-qr-code/?size=200x200&data=${encodeURIComponent(url)}`;
});
</script>

<style scoped>
.desktop-wrapper {
  display: flex;
  align-items: flex-start;
  justify-content: center;
  gap: 48px;
  min-height: 100vh;
  padding: 32px;
  background: #f0f2f5;
}

.device-area {
  flex-shrink: 0;
}

.info-panel {
  padding-top: 40px;
  max-width: 280px;
}

.info-title {
  font-size: 24px;
  font-weight: 700;
  color: #1a1a1a;
  margin: 0 0 8px 0;
}

.info-desc {
  font-size: 14px;
  color: #666;
  margin: 0 0 32px 0;
}

.qr-section {
  margin-bottom: 24px;
}

.qr-label {
  font-size: 13px;
  color: #999;
  margin: 0 0 12px 0;
}

.qr-code {
  width: 200px;
  height: 200px;
  border-radius: 8px;
  border: 1px solid #e0e0e0;
}

.info-links {
  margin-top: 16px;
}

.info-hint {
  font-size: 12px;
  color: #bbb;
  margin: 0;
}
</style>

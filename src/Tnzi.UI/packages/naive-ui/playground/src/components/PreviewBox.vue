<script setup lang="ts">
defineProps<{
  /** Use full width layout (no centering) */
  fullWidth?: boolean;
  /** Hide the LIVE PREVIEW badge */
  noBadge?: boolean;
  /** Custom min-height */
  minHeight?: string;
}>();
</script>

<template>
  <div class="pg-preview-card" :class="{ 'pg-preview-card--full': fullWidth }">
    <div v-if="!noBadge" class="pg-preview-badge">
      <span class="pg-preview-dot" />
      <span>LIVE PREVIEW</span>
    </div>
    <div
      class="pg-preview-content"
      :style="minHeight ? { minHeight } : undefined"
    >
      <slot />
    </div>
  </div>
</template>

<style scoped>
.pg-preview-card {
  position: relative;
  background-color: var(--pg-preview-bg, #ffffff);
  border: 1px solid var(--pg-border, #e2e8f0);
  border-radius: 12px;
  overflow: hidden;
}

.pg-preview-badge {
  position: absolute;
  top: 12px;
  left: 14px;
  display: flex;
  align-items: center;
  gap: 6px;
  z-index: 1;
}

.pg-preview-dot {
  width: 7px;
  height: 7px;
  background-color: #10b981;
  border-radius: 50%;
  animation: pulse-dot 2s ease-in-out infinite;
}

@keyframes pulse-dot {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.4; }
}

.pg-preview-badge span:last-child {
  font-size: 10px;
  font-weight: 700;
  letter-spacing: 0.05em;
  color: var(--pg-text-muted, #94a3b8);
}

.pg-preview-content {
  padding: 40px 24px 24px;
  min-height: 200px;
  display: flex;
  align-items: center;
  justify-content: center;
  position: relative;
  background-color: var(--pg-preview-content-bg, #f8fafc);
  background-image: radial-gradient(var(--pg-preview-grid, #e2e8f0) 1px, transparent 1px);
  background-size: 20px 20px;
}

.pg-preview-card--full .pg-preview-content {
  justify-content: flex-start;
  align-items: flex-start;
}
</style>

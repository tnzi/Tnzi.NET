<template>
  <div class="docs-page">
    <iframe
      src="https://www.naiveui.com/en-US/os-theme/components/button"
      class="docs-iframe"
      sandbox="allow-scripts allow-same-origin allow-popups"
    />
  </div>
</template>

<style scoped>
.docs-page {
  display: flex;
  flex-direction: column;
  height: calc(100vh - 56px - 64px);
  margin: -32px;
}
.docs-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--pg-text-muted, #64748b);
}
.docs-iframe {
  flex: 1;
  width: 100%;
  border: none;
  background: white;
}
</style>

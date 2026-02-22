<template>
  <div class="docs-page">
    <iframe
      src="https://www.shadcn-vue.com/docs/components/button"
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
.docs-iframe {
  flex: 1;
  width: 100%;
  border: none;
  background: white;
}
</style>

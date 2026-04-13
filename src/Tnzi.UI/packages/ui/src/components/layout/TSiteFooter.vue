<template>
  <footer class="t-site-footer">
    <div v-if="$slots.columns" class="t-site-footer__columns">
      <slot name="columns" />
    </div>
    <div v-if="$slots.default" class="t-site-footer__body">
      <slot />
    </div>
    <div class="t-site-footer__bottom">
      <div class="t-site-footer__copyright">
        <slot name="copyright">
          <span v-if="copyright">{{ copyright }}</span>
        </slot>
      </div>
      <div v-if="$slots.beian" class="t-site-footer__beian">
        <slot name="beian" />
      </div>
    </div>
  </footer>
</template>

<script setup lang="ts">
interface Props {
  /** Fallback copyright text if no slot provided. */
  copyright?: string
}

defineProps<Props>()
</script>

<style scoped>
.t-site-footer {
  padding: 48px clamp(16px, 4vw, 32px) 24px;
  background-color: var(--tnzi-container-bg);
  color: var(--tnzi-base-text-muted);
  border-top: 1px solid var(--tnzi-border);
}
.t-site-footer__columns {
  max-width: 1280px;
  margin: 0 auto 32px;
}
.t-site-footer__body {
  max-width: 1280px;
  margin: 0 auto 24px;
}
.t-site-footer__bottom {
  max-width: 1280px;
  margin: 0 auto;
  padding-top: 24px;
  border-top: 1px solid var(--tnzi-border);
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: 16px;
  font-size: 14px;
}
</style>

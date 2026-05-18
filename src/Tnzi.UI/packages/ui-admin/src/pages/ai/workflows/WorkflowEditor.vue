<template>
  <!--
    WorkflowEditor — Phase 5 Task 5.5 lazy-load shell.

    This page is intentionally minimal: it defers @tnzi/ui-ai's WorkflowCanvas
    via defineAsyncComponent so the heavy vue-flow dependency stays out of the
    ui-admin main bundle. Route :id is forwarded to the canvas as a prop —
    the canvas consumes the bridge internally.

    Lessons applied:
      - 7: inline LoadErrorBanner / LoadingSpinner instead of useMessage
      - 12: do NOT bypass the canvas to call workflow APIs from this shell —
            the canvas owns the workflow data lifecycle
  -->
  <div class="t-workflow-editor t-page-scroll">
    <Suspense>
      <WorkflowCanvas v-if="workflowId" :workflow-id="workflowId" />
      <div v-else class="t-workflow-editor__empty">{{ t('editor.missingId') }}</div>
    </Suspense>
  </div>
</template>

<script setup lang="ts">
import { defineAsyncComponent, computed, h } from 'vue'
import { useRoute } from 'vue-router'
import { translatePageKey } from '../../_shared/translate'

const t = (key: string) => translatePageKey('ai.workflows', key)

const LoadingSpinner = {
  name: 'LoadingSpinner',
  render: () =>
    h('div', { class: 't-workflow-editor__loading' }, 'Loading workflow editor…'),
}

const LoadErrorBanner = {
  name: 'LoadErrorBanner',
  render: () =>
    h(
      'div',
      { class: 't-workflow-editor__error', role: 'alert' },
      'Failed to load workflow editor — check console',
    ),
}

const WorkflowCanvas = defineAsyncComponent({
  loader: () => import('@tnzi/ui-ai').then((m) => m.WorkflowCanvas),
  loadingComponent: LoadingSpinner,
  errorComponent: LoadErrorBanner,
  delay: 200,
  timeout: 30000,
})

const route = useRoute()
const workflowId = computed<string | undefined>(() => {
  const raw = route.params?.id
  if (Array.isArray(raw)) return raw[0]
  return typeof raw === 'string' && raw.length > 0 ? raw : undefined
})
</script>

<style scoped>
.t-workflow-editor {
  width: 100%;
  height: 100%;
  min-height: 600px;
}
.t-workflow-editor__empty,
.t-workflow-editor__loading,
.t-workflow-editor__error {
  padding: 2rem;
  text-align: center;
  color: var(--t-muted, #666);
}
.t-workflow-editor__error {
  color: var(--t-danger, #c33);
}
</style>

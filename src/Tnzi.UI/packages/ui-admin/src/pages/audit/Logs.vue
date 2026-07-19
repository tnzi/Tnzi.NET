<template>
  <TContentPage :title="t('title')" :translate="t" card scroll="fill">
    <TAuditTimeline page-id="audit.logs" :fetch="bridge.logs.fetch" :fetch-detail="bridge.logs.detail" :translate="t" />
  </TContentPage>
</template>

<script setup lang="ts">
import TAuditTimeline from './components/TAuditTimeline.vue'
import TContentPage from '../../components/layout/TContentPage.vue'
import { createAuditBridge } from '../../services/bridges/audit-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'

const bridge = createAuditBridge({ client: useAdminClient() })
const t = makePageTranslator('audit.logs')
</script>

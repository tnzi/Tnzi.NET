<template>
  <TContentPage :title="t('title')" :translate="t" card scroll="fill">
    <TAuditTimeline page-id="audit.operations" :fetch="bridge.operations.fetch" :translate="t" />
  </TContentPage>
</template>

<script setup lang="ts">
import TAuditTimeline from './TAuditTimeline.vue'
import TContentPage from '../../components/layout/TContentPage.vue'
import { createAuditBridge } from '../../services/bridges/audit-bridge'
import { useAdminClient } from '../../plugin/client'
import { translatePageKey } from '../_shared/translate'

const bridge = createAuditBridge({ client: useAdminClient() })
const t = (key: string) => translatePageKey('audit.operations', key)
</script>

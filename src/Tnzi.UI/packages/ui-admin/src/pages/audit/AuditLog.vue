<template>
  <TAuditTimeline page-id="audit.logs" :fetch="bridge.logs.fetch" :translate="t" />
</template>

<script setup lang="ts">
import TAuditTimeline from './TAuditTimeline.vue'
import { createAuditBridge } from '../../services/bridges/audit-bridge'
import { useAdminClient } from '../../plugin/client'
import { translatePageKey } from '../_shared/translate'

const bridge = createAuditBridge({ client: useAdminClient() })
const t = (key: string) => translatePageKey('audit.logs', key)
</script>

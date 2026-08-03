<template>
  <TUserCenterSection :title="t('history.title')" fill :contained="false">
    <template #actions>
      <NButton size="small" tertiary :loading="loading" @click="load">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
        {{ t('refresh') }}
      </NButton>
    </template>

    <TResponsiveTable
      class="t-uc-table"
      :data="history"
      :columns="columns"
      :row-key="(r: LoginLogDto) => r.id"
      size="small"
      :bordered="false"
      :loading="loading"
      :flex-height="true"
      :pagination="{ pageSize: 15 }"
    />
  </TUserCenterSection>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../../utils/placeholders'
import { computed, h, onMounted, ref, watch } from 'vue'
import { NButton, NTag } from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateTime } from '@tnzi/core'
import type { LoginLogDto } from '@tnzi/core/services/identity'
import TUserCenterSection from './TUserCenterSection.vue'
import TResponsiveTable from '../../../components/data/TResponsiveTable.vue'
import { createGuardedLoader } from '../guarded-loader'
import { useUserCenterContext } from '../user-center-context'

const ctx = useUserCenterContext()
const t = ctx.t

const history = ref<LoginLogDto[]>([])
const loading = ref(false)

const load = createGuardedLoader<LoginLogDto[]>({
  flag: loading,
  fetch: () => ctx.bridge.me.getLoginHistory(),
  apply: (rows) => {
    history.value = rows ?? []
  },
  onError: (e) => ctx.message.error(e instanceof Error ? e.message : String(e)),
  timeoutMessage: t('loadTimeout'),
})

const columns = computed<DataTableColumns<LoginLogDto>>(() => [
  {
    key: 'loginTime',
    title: t('history.cols.time'),
    render: (row) => formatDateTime(row.loginTime, { fallback: EMPTY_DASH }),
  },
  { key: 'ipAddress', title: t('history.cols.ip') },
  { key: 'deviceInfo', title: t('history.cols.device') },
  {
    key: 'isSuccess',
    title: t('history.cols.result'),
    render: (row) =>
      h(
        NTag,
        { size: 'small', bordered: false, type: row.isSuccess ? 'success' : 'error' },
        { default: () => (row.isSuccess ? t('history.success') : t('history.failed')) },
      ),
  },
  { key: 'failureReason', title: t('history.cols.reason') },
])

onMounted(() => void load())
watch(() => ctx.reloadKey.value, () => void load())
</script>

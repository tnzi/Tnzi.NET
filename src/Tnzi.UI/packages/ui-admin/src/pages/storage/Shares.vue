<template>
  <!--
    Shares - active share-link management. Read + revoke only: shares are
    created from the file detail / user API, never here. The list is paged
    via `shares.fetch` (filters: fileId / creatorId / includeExpired /
    includeDisabled). Per-row Revoke + batch Revoke both call
    `shares.batchRevoke`. No create/edit affordances (createData/updateData
    omitted → hidden automatically); deleteData IS the revoke wiring so the
    batch toolbar surfaces "Revoke".
  -->
  <TCrudPage
    :state="crud"
    :all-columns="shareColumns"
    :title="t('title')"
    :title-help="t('banner.body')"
    :title-help-title="t('banner.title')"
    :search-fields="shareSearchFields"
    :translate="t"
    :row-actions="rowActions"
  >
    <template #batchActions="{ selectedIds }">
      <NPopconfirm v-if="can('storage.file.delete')" @positive-click="() => batchRevoke(selectedIds)">
        <template #trigger>
          <NButton size="small" type="error" ghost :disabled="!selectedIds.length">
            {{ t('actions.revoke') }}
          </NButton>
        </template>
        {{ t('actions.confirmRevoke') }}
      </NPopconfirm>
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { NButton, NPopconfirm } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { type RowAction } from '../../headless/row-actions'
import { createStorageBridge } from '../../services/bridges/storage-bridge'
import { useRouter } from 'vue-router'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safe-message'
import { buildShareColumns, shareSearchFields } from './shares-config'
import type { FileShareSummaryDto } from '@tnzi/core/services/storage'

const t = makePageTranslator('storage.shares')
const message = useSafeMessage()
const router = useRouter()
const { can } = usePermissionGuard()
const bridge = createStorageBridge({ client: useAdminClient() })

const shareColumns = buildShareColumns(t)

const crud = useCrudPage<FileShareSummaryDto, string>({
  pageId: 'storage.shares',
  permission: 'storage.file',
  columns: shareColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.shares.fetch(query),
  // Revoke wired through deleteData so the batch toolbar surfaces it; the
  // per-row Revoke action below reuses the same path.
  deleteData: async (ids) => {
    await bridge.shares.batchRevoke(ids)
  },
})

const rowActions: RowAction<FileShareSummaryDto>[] = [
  {
    // 创建分享的人真正想要的是那条 URL，不是那串令牌。没有这个动作，
    // 他就得自己去拼 `/share/<token>` —— 而拼错了才发现是在收件人那边。
    key: 'copy-link',
    label: 'actions.copyLink',
    show: (row) => row.isEnabled === true && !row.isExpired && !row.isExhausted,
    onClick: (row) => void copyLink(row),
  },
  {
    key: 'revoke',
    label: 'actions.revoke',
    type: 'error',
    confirm: 'actions.confirmRevoke',
    show: (row) => can('storage.file.delete') && row.isEnabled === true,
    onClick: (row) => void revokeOne(row),
  },
]

/**
 * 复制收件人链接。用的是路由解析而不是拼字符串，所以子路径部署
 * （`basePath` / IIS 虚拟目录）下拿到的也是能用的绝对地址。
 */
async function copyLink(row: FileShareSummaryDto): Promise<void> {
  const path = router.resolve({ name: 'share-link', params: { token: row.shareToken } }).href
  const url = new URL(path, window.location.origin).toString()
  try {
    await navigator.clipboard.writeText(url)
    message.success(t('actions.copyLinkSuccess'))
  } catch {
    // 剪贴板要安全上下文 + 用户手势，拿不到时把地址显示出来让人自己复制，
    // 比默默失败强。
    message.info(url)
  }
}

async function revokeOne(row: FileShareSummaryDto): Promise<void> {
  try {
    await bridge.shares.batchRevoke([row.id])
    message.success(t('actions.revokeSuccess'))
    await crud.refresh()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

async function batchRevoke(ids: string[]): Promise<void> {
  if (!ids.length) return
  try {
    const count = await bridge.shares.batchRevoke(ids)
    message.success(t('actions.batchRevokeSuccess', { n: count }))
    crud.batchActions.clear()
    await crud.refresh()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}
</script>

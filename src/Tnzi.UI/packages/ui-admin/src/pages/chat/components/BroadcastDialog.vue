<template>
  <!--
    BroadcastDialog - system-notification producer in a modal (opened from the
    Conversations toolbar "Broadcast" button). Two tabs: Compose (form) and
    History (paged BroadcastLog list, each row's full content shown via a
    click popover). Target = All / By role / By user; roles and users are picked
    with multi-select autocompletes (roles loaded in full + local filter; users
    remote-searched by keyword) instead of pasting comma-separated IDs.
  -->
  <TOverlayTheme>
  <NModal
    :show="show"
    preset="card"
    :title="t('title')"
    size="small"
    :bordered="false"
    style="width: 640px; max-width: 92vw"
    @update:show="(v: boolean) => emit('update:show', v)"
  >
    <NTabs type="line" size="small" animated default-value="compose" pane-style="padding-top: 14px">
      <NTabPane name="compose" :tab="t('tabCompose')">
        <NForm ref="formRef" :model="form" :rules="rules" label-placement="top" size="small">
          <NFormItem :label="t('content')" path="content">
            <NInput
              v-model:value="form.content"
              type="textarea"
              :rows="4"
              :placeholder="t('contentPlaceholder')"
            />
          </NFormItem>

          <NFormItem :label="t('target')" path="targetMode">
            <NRadioGroup v-model:value="form.targetMode" size="small">
              <NRadioButton value="all">{{ t('targetAll') }}</NRadioButton>
              <NRadioButton value="roles">{{ t('targetRoles') }}</NRadioButton>
              <NRadioButton value="users">{{ t('targetUsers') }}</NRadioButton>
            </NRadioGroup>
          </NFormItem>

          <NFormItem v-if="form.targetMode === 'roles'" :label="t('roleIds')" path="roleIds">
            <TRoleSelector
              v-model:value="form.roleIds"
              :fetcher="roleFetcher"
              multiple
              size="small"
              :placeholder="t('selectRoles')"
            />
          </NFormItem>

          <NFormItem v-if="form.targetMode === 'users'" :label="t('userIds')" path="userIds">
            <TUserSelector
              v-model:value="form.userIds"
              :fetcher="userFetcher"
              multiple
              size="small"
              :placeholder="t('selectUsers')"
            />
          </NFormItem>

          <NButton type="primary" size="small" block :loading="sending" @click="handleSend">
            <template #icon><TSvgIcon icon="mdi:bullhorn-outline" :size="16" /></template>
            {{ t('send') }}
          </NButton>
        </NForm>
      </NTabPane>

      <NTabPane name="history" :tab="t('tabHistory')">
        <div class="t-bc-history__head">
          <NButton text size="tiny" :loading="loadingHistory" @click="loadHistory">
            <template #icon><TSvgIcon icon="mdi:refresh" :size="15" /></template>
            {{ t('history.refresh') }}
          </NButton>
        </div>

        <TResponsiveTable
          :columns="columns"
          :data="history"
          :loading="loadingHistory"
          :row-key="(r: BroadcastLogDto) => r.id"
          :pagination="false"
          size="small"
          :empty-text="t('history.empty')"
        />

        <div v-if="total > pageSize" class="t-bc-history__pager">
          <NPagination
            v-model:page="page"
            :page-size="pageSize"
            :item-count="total"
            size="small"
            @update:page="loadHistory"
          />
        </div>
      </NTabPane>
    </NTabs>
  </NModal>
  </TOverlayTheme>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import {
  NModal, NTabs, NTabPane, NButton, NForm, NFormItem, NInput, NPagination, NRadioButton, NRadioGroup,
} from 'naive-ui'
import type { FormInst, FormRules } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TResponsiveTable from '../../../components/data/TResponsiveTable.vue'
import TRoleSelector from '../../../components/forms/TRoleSelector.vue'
import TUserSelector from '../../../components/forms/TUserSelector.vue'
import { TOverlayTheme } from '../../../components/overlay'
import type { SelectorOption } from '../../../components/forms/_selector-factory'
import { createChatBridge } from '../../../services/bridges/chat-bridge'
import { createIdentityBridge } from '../../../services/bridges/identity-bridge'
import { useAdminClient } from '../../../plugin/client'
import { makePageTranslator } from '../../_shared/translate'
import { useSafeMessage } from '../../_shared/safe-message'
import { buildBroadcastColumns } from '../broadcast-config'
import type { BroadcastDto, BroadcastLogDto } from '@tnzi/core/services/chat'

const props = defineProps<{ show: boolean }>()
const emit = defineEmits<{ 'update:show': [value: boolean] }>()

const t = makePageTranslator('chat.broadcast')
const message = useSafeMessage()
const client = useAdminClient()
const bridge = createChatBridge({ client })
const identity = createIdentityBridge({ client })

const formRef = ref<FormInst | null>(null)
const sending = ref(false)
const form = ref({
  content: '',
  targetMode: 'all' as 'all' | 'roles' | 'users',
  roleIds: [] as string[],
  userIds: [] as string[],
})
const rules: FormRules = {
  content: [{ required: true, message: 'Content is required', trigger: 'blur' }],
}

// Role / user pickers reuse the shared TRoleSelector / TUserSelector (the
// selector factory handles debounced remote search + retaining the labels of
// already-picked tags). Each just supplies a keyword→options fetcher.
const roleFetcher = async (keyword: string): Promise<SelectorOption[]> => {
  try {
    const roles = await identity.roles.getAll()
    const kw = keyword.trim().toLowerCase()
    return roles
      .filter((r) => !kw || r.name.toLowerCase().includes(kw))
      .map((r) => ({ label: r.name, value: r.id }))
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    return []
  }
}

const userFetcher = async (keyword: string): Promise<SelectorOption[]> => {
  try {
    const res = await identity.users.fetch({
      pageIndex: 1,
      pageSize: 20,
      searchText: keyword.trim(),
      sortField: undefined,
      sortOrder: null,
      filters: {},
    })
    return res.items.map((u) => ({
      label: u.email ? `${u.userName} (${u.email})` : u.userName,
      value: u.id,
    }))
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    return []
  }
}

// --- History ---
const columns = buildBroadcastColumns(t)
const history = ref<BroadcastLogDto[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = 6
const loadingHistory = ref(false)

async function loadHistory(): Promise<void> {
  loadingHistory.value = true
  try {
    const res = await bridge.broadcasts({ pageIndex: page.value, pageSize })
    history.value = res.items
    total.value = res.totalCount
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    loadingHistory.value = false
  }
}

async function handleSend(): Promise<void> {
  await formRef.value?.validate()

  // Target selection is validated manually (the picker is conditional, so a
  // form rule on roleIds/userIds would fire for the hidden field too).
  if (form.value.targetMode === 'roles' && form.value.roleIds.length === 0) {
    message.error(t('rolesRequired'))
    return
  }
  if (form.value.targetMode === 'users' && form.value.userIds.length === 0) {
    message.error(t('usersRequired'))
    return
  }

  const dto: BroadcastDto = { content: form.value.content }
  if (form.value.targetMode === 'roles') {
    dto.roleIds = [...form.value.roleIds]
  } else if (form.value.targetMode === 'users') {
    dto.userIds = [...form.value.userIds]
  } else {
    dto.all = true
  }

  sending.value = true
  try {
    await bridge.broadcast(dto)
    message.success(t('success'))
    form.value = { content: '', targetMode: 'all', roleIds: [], userIds: [] }
    page.value = 1
    await loadHistory()
  } catch (e) {
    message.error(e instanceof Error ? e.message : t('failed'))
  } finally {
    sending.value = false
  }
}

// Reset the form and (re)load history each time the dialog opens.
watch(
  () => props.show,
  (visible) => {
    if (!visible) return
    form.value = { content: '', targetMode: 'all', roleIds: [], userIds: [] }
    page.value = 1
    void loadHistory()
  },
)
</script>

<style scoped>
.t-bc-history__head {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  margin-bottom: 8px;
}
.t-bc-history__pager {
  display: flex;
  justify-content: flex-end;
  margin-top: 12px;
}
</style>

<template>
  <TContentPage :title="t('title')" :translate="t" card scroll="fill">
    <template #actions>
      <NButton size="small" @click="loadTree">{{ t('refresh') }}</NButton>
      <NButton v-if="can('organization.create')" size="small" type="primary" @click="openCreate(null)">
        {{ t('addRoot') }}
      </NButton>
    </template>

    <TMasterDetailLayout :master-width="320">
      <template #master>
          <NInput
            v-model:value="filter"
            :placeholder="t('searchPlaceholder')"
            clearable
            size="small"
          >
            <template #prefix>
              <span class="opacity-60">🔍</span>
            </template>
          </NInput>
          <NSpin :show="loading" class="mt-8px">
            <NTree
              v-if="tree.length"
              :data="treeData"
              :pattern="filter"
              :selected-keys="selectedKey ? [selectedKey] : []"
              :default-expand-all="true"
              :show-irrelevant-nodes="false"
              block-line
              class="t-org-page__naive-tree"
              @update:selected-keys="onSelect"
            />
            <div v-else-if="!loading" class="t-org-page__empty">
              {{ t('emptyTip') }}
            </div>
          </NSpin>
      </template>
      <template #detail>
        <div class="t-org-page__detail">
          <div v-if="!selectedNode" class="t-org-page__placeholder">
            {{ t('selectPrompt') }}
          </div>
          <div v-else>
            <header class="t-org-page__detail-header">
              <h3>{{ selectedNode.name }}</h3>
              <NSpace>
                <NButton v-if="can('organization.create')" size="small" @click="openCreate(selectedNode.id)">
                  {{ t('addChild') }}
                </NButton>
                <NPopconfirm v-if="can('organization.delete')" @positive-click="handleDelete">
                  <template #trigger>
                    <NButton size="small" type="error" ghost>
                      {{ t('delete') }}
                    </NButton>
                  </template>
                  {{ t('confirmDelete') }}
                </NPopconfirm>
              </NSpace>
            </header>
            <NForm label-placement="left" label-width="100px">
              <NFormItem :label="t('fields.name')" required>
                <NInput v-model:value="form.name" />
              </NFormItem>
              <NFormItem :label="t('fields.code')">
                <NInput v-model:value="form.code" :placeholder="t('fields.codePlaceholder')" />
              </NFormItem>
              <NFormItem :label="t('fields.remark')">
                <NInput
                  v-model:value="form.remark"
                  type="textarea"
                  :rows="2"
                  :placeholder="t('fields.remarkPlaceholder')"
                />
              </NFormItem>
              <NFormItem :label="t('fields.sortOrder')">
                <NInputNumber v-model:value="form.sortOrder" :min="0" />
              </NFormItem>
              <NFormItem :label="t('fields.enabled')">
                <NSwitch v-model:value="form.isEnabled" />
              </NFormItem>
            </NForm>
            <div class="t-org-page__actions">
              <NButton v-if="can('organization.update')" type="primary" :loading="saving" @click="saveUpdate">
                {{ t('save') }}
              </NButton>
              <NButton @click="resetForm">{{ t('reset') }}</NButton>
            </div>

            <!--
              Organization members panel - paged user list scoped to the
              selected org. The `includeChildren` toggle expands the
              backend query to every descendant unit. The "Remove" row
              action calls `users.removeFromOrganization(userId)` which
              detaches the user without deleting them - the user just
              ends up orphaned and an admin can reassign via the user
              page or by adding them to a different org.
            -->
            <div class="t-org-page__members">
              <header class="t-org-page__section-header">
                <h4 class="t-org-page__section-title">{{ t('members.title') }}</h4>
                <NSpace align="center">
                  <NCheckbox v-model:checked="members.includeChildren" @update:checked="reloadMembers">
                    {{ t('members.includeChildren') }}
                  </NCheckbox>
                  <NButton size="small" :loading="members.loading" @click="reloadMembers">
                    {{ t('refresh') }}
                  </NButton>
                </NSpace>
              </header>
              <p class="t-org-page__hint">{{ t('members.hint') }}</p>
              <TResponsiveTable
                :data="members.items"
                :columns="memberColumns"
                :loading="members.loading"
                :pagination="{
                  page: members.pageIndex,
                  pageSize: members.pageSize,
                  itemCount: members.totalCount,
                  showSizePicker: true,
                  pageSizes: [10, 20, 50],
                  onUpdatePage: (p: number) => { members.pageIndex = p; reloadMembers() },
                  onUpdatePageSize: (s: number) => { members.pageSize = s; members.pageIndex = 1; reloadMembers() },
                }"
                :bordered="false"
                size="small"
                remote
                :row-key="(r: OrgMemberRow) => r.id"
                :row-actions="memberRowActions"
                :row-actions-title="t('members.actions')"
                :translate="t"
              />
            </div>
          </div>
        </div>
      </template>
    </TMasterDetailLayout>

    <!-- Create overlay - works for both root and child create. useDetail owns
         the open-state + `?create=new` deep link; TDetailHost renders the modal
         chrome (auto-fullscreen on narrow viewports via TModalShell). -->
    <TDetailHost
      :state="createDetail"
      :title="t(createParentId ? 'addChild' : 'addRoot')"
      :width="500"
      :translate="t"
    >
      <template #default>
        <NForm label-placement="left" label-width="100px">
          <NFormItem :label="t('fields.name')" required>
            <NInput v-model:value="createForm.name" />
          </NFormItem>
          <NFormItem :label="t('fields.code')">
            <NInput v-model:value="createForm.code" />
          </NFormItem>
          <NFormItem :label="t('fields.remark')">
            <NInput
              v-model:value="createForm.remark"
              type="textarea"
              :rows="2"
              :placeholder="t('fields.remarkPlaceholder')"
            />
          </NFormItem>
          <NFormItem :label="t('fields.sortOrder')">
            <NInputNumber v-model:value="createForm.sortOrder" :min="0" />
          </NFormItem>
        </NForm>
      </template>
      <template #footer="{ close }">
        <div class="flex justify-end gap-8px">
          <NButton @click="close">{{ t('cancel') }}</NButton>
          <NButton type="primary" :disabled="!createForm.name" :loading="saving" @click="submitCreate">
            {{ t('create') }}
          </NButton>
        </div>
      </template>
    </TDetailHost>
  </TContentPage>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../utils/placeholders'
import { computed, reactive, ref, h, onMounted, watch } from 'vue'
import type { DataTableColumns, TreeOption } from 'naive-ui'
import {
  NTree, NSpace, NButton, NCheckbox, NInput, NInputNumber,
  NSwitch, NSpin, NForm, NFormItem, NPopconfirm,
} from 'naive-ui'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { type RowAction } from '../../headless/rowActions'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { useSafeMessage } from '../_shared/safeMessage'
import { createIdentityBridge, type OrganizationTreeNodeDto, type OrganizationDto } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import TContentPage from '../../components/layout/TContentPage.vue'
import TMasterDetailLayout from '../../components/layout/TMasterDetailLayout.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import { useDetail } from '../../headless/useDetail'
import type { UserListItemDto } from '@tnzi/core/services/identity'

const bridge = createIdentityBridge({ client: useAdminClient() })
const t = makePageTranslator('identity.organizations')
const { can } = usePermissionGuard()

type OrgMemberRow = UserListItemDto

const message = useSafeMessage()

const tree = ref<OrganizationTreeNodeDto[]>([])
const loading = ref(false)
const saving = ref(false)
const filter = ref('')
const selectedKey = ref<string | null>(null)

const form = reactive({
  name: '',
  code: '' as string | undefined,
  remark: '' as string | undefined,
  sortOrder: 0,
  isEnabled: true,
})

// Create overlay: useDetail owns the open-state + `?create=new` deep link;
// the form fields bind to a page-local reactive object (parentId is set by
// which "Add" button was clicked, so it lives outside the form payload).
interface OrgCreateForm {
  name: string
  code: string | undefined
  remark: string | undefined
  sortOrder: number
}
const createParentId = ref<string | null>(null)
const createForm = reactive<OrgCreateForm>({
  name: '',
  code: undefined,
  remark: undefined,
  sortOrder: 0,
})
const createDetail = useDetail({ mode: 'modal', url: 'create' })

// Flat lookup by id (built whenever tree changes).
const flatById = computed(() => {
  const map = new Map<string, OrganizationTreeNodeDto>()
  const walk = (nodes: OrganizationTreeNodeDto[]) => {
    for (const n of nodes) {
      map.set(n.id, n)
      if (n.children?.length) walk(n.children)
    }
  }
  walk(tree.value)
  return map
})

const selectedNode = computed(() =>
  selectedKey.value ? flatById.value.get(selectedKey.value) ?? null : null,
)

// Adapt OrganizationTreeNodeDto[] → naive-ui TreeOption[] (key + label + children).
const treeData = computed<TreeOption[]>(() => {
  const adapt = (nodes: OrganizationTreeNodeDto[]): TreeOption[] =>
    nodes.map((n) => ({
      key: n.id,
      label: n.name,
      children: n.children?.length ? adapt(n.children) : undefined,
      prefix: n.code
        ? () =>
            h(
              'span',
              { class: 'text-muted text-12px mr-4px' },
              n.code ?? '',
            )
        : undefined,
    }))
  return adapt(tree.value)
})

async function loadTree(): Promise<void> {
  loading.value = true
  try {
    tree.value = await bridge.organizations.getTree()
    // Re-sync form with the currently selected node if it still exists.
    if (selectedKey.value) onSelect([selectedKey.value])
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    loading.value = false
  }
}

function onSelect(keys: Array<string | number>): void {
  const key = keys[0] as string | undefined
  selectedKey.value = key ?? null
  if (!key) return
  const node = flatById.value.get(key)
  if (!node) return
  form.name = node.name
  form.code = node.code ?? undefined
  form.remark = node.remark ?? undefined
  form.sortOrder = node.sortOrder
  form.isEnabled = node.isEnabled
}

function resetForm(): void {
  if (selectedNode.value) onSelect([selectedNode.value.id])
}

async function saveUpdate(): Promise<void> {
  if (!selectedNode.value) return
  saving.value = true
  try {
    await bridge.organizations.update(selectedNode.value.id, {
      name: form.name,
      code: form.code ?? null,
      remark: form.remark ?? null,
      sortOrder: form.sortOrder,
      isEnabled: form.isEnabled,
    })
    message.success(t('updateSuccess'))
    await loadTree()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    saving.value = false
  }
}

async function handleDelete(): Promise<void> {
  if (!selectedNode.value) return
  try {
    await bridge.organizations.delete(selectedNode.value.id)
    message.success(t('deleteSuccess'))
    selectedKey.value = null
    await loadTree()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

function openCreate(parentId: string | null): void {
  createParentId.value = parentId
  createForm.name = ''
  createForm.code = undefined
  createForm.remark = undefined
  createForm.sortOrder = 0
  void createDetail.open('create', {})
}

async function submitCreate(): Promise<void> {
  saving.value = true
  try {
    const created = await bridge.organizations.create({
      name: createForm.name,
      code: createForm.code ?? null,
      remark: createForm.remark ?? null,
      parentId: createParentId.value ?? null,
      sortOrder: createForm.sortOrder,
    })
    message.success(t('createSuccess'))
    createDetail.close()
    await loadTree()
    selectedKey.value = (created as OrganizationDto).id
    onSelect([(created as OrganizationDto).id])
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    saving.value = false
  }
}

// ─── Members panel ─────────────────────────────────────────────────────────
// Paged list of users that belong to the currently selected org node.
// Re-fetched whenever the selection or `includeChildren` toggle changes;
// pagination changes reuse the same loader. We hold the result in a
// `reactive` bag rather than a Pinia store because state is fully local
// to this page.
const members = reactive({
  items: [] as OrgMemberRow[],
  totalCount: 0,
  pageIndex: 1,
  pageSize: 10,
  includeChildren: false,
  loading: false,
})

async function loadMembers(orgId: string): Promise<void> {
  members.loading = true
  try {
    const result = await bridge.organizations.getUsers(orgId, {
      pageIndex: members.pageIndex,
      pageSize: members.pageSize,
      includeChildren: members.includeChildren,
    })
    members.items = result.items as OrgMemberRow[]
    members.totalCount = result.totalCount
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    members.items = []
    members.totalCount = 0
  } finally {
    members.loading = false
  }
}

function reloadMembers(): void {
  if (selectedKey.value) void loadMembers(selectedKey.value)
}

async function removeMember(user: OrgMemberRow): Promise<void> {
  try {
    await bridge.users.removeFromOrganization(user.id)
    message.success(t('members.removeSuccess'))
    reloadMembers()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

// Reset paging + reload whenever the selected org changes. Skip the
// effect when nothing's selected (the empty-state placeholder shows).
watch(selectedKey, (key) => {
  members.pageIndex = 1
  members.items = []
  members.totalCount = 0
  if (key) void loadMembers(key)
})

const memberColumns = computed<DataTableColumns<OrgMemberRow>>(() => [
  { key: 'userName', title: t('members.userName'), minWidth: 140 },
  { key: 'email', title: t('members.email'), minWidth: 180, ellipsis: { tooltip: true } },
  { key: 'phoneNumber', title: t('members.phoneNumber'), width: 140 },
  // When includeChildren=true the user's actual org may differ from the
  // selected node - surface it so admins can tell who lives in which
  // sub-unit without exporting.
  {
    key: 'organizationName',
    title: t('members.organization'),
    minWidth: 160,
    ellipsis: { tooltip: true },
    render: (row) => row.organizationName ?? EMPTY_DASH,
  },
])

// Declarative operation column for the members sub-table - "Remove" detaches
// the user from the org (confirm gated) via the existing removeMember handler.
const memberRowActions: RowAction<OrgMemberRow>[] = [
  {
    key: 'remove',
    label: 'members.remove',
    type: 'error',
    show: () => can('user.update'),
    confirm: (row) => t('members.confirmRemove', { user: row.userName }),
    onClick: (row) => void removeMember(row),
  },
]

onMounted(() => {
  void loadTree()
})
</script>

<style scoped>
/* Master/detail grid, responsive stacking and pane scroll come from
   <TMasterDetailLayout>. Only page-specific content styling stays here. */
/* The tree (not the whole master pane) owns the scroll so the search input
   above it stays pinned while a long org tree scrolls. The master pane is a
   flex column from TMasterDetailLayout, so flex:1 + overflow engages here. */
.t-org-page__naive-tree {
  flex: 1;
  min-height: 0;
  margin-top: 8px;
  overflow: auto;
}
.t-org-page__detail {
  padding: 0 8px;
}
.t-org-page__placeholder {
  color: var(--tnzi-base-text-muted);
  text-align: center;
  padding: 60px 16px;
}
.t-org-page__detail-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 16px;
}
.t-org-page__detail-header h3 {
  margin: 0;
  font-size: 18px;
}
.t-org-page__actions {
  display: flex;
  gap: 8px;
  margin-top: 8px;
}
.t-org-page__empty {
  color: var(--tnzi-base-text-muted);
  text-align: center;
  padding: 24px 8px;
  font-size: 13px;
}
/* Members panel - visually separated from the edit form by a top border
   so the two zones don't run together. The panel itself doesn't claim
   flex-grow; TContentPage body handles overflow. */
.t-org-page__members {
  margin-top: 24px;
  padding-top: 16px;
  border-top: 1px dashed var(--tnzi-border);
}
.t-org-page__section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 8px;
}
.t-org-page__section-title {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.t-org-page__hint {
  color: var(--tnzi-base-text-muted);
  font-size: 12px;
  line-height: 1.5;
  margin: 0 0 12px;
}
</style>

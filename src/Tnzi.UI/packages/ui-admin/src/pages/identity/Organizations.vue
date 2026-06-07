<template>
  <TContentPage :title="t('title')" :translate="t" card scroll="fill">
    <template #actions>
      <NButton size="small" @click="loadTree">{{ t('refresh') }}</NButton>
      <NButton size="small" type="primary" @click="openCreate(null)">
        {{ t('addRoot') }}
      </NButton>
    </template>

    <div class="t-org-page__layout">
        <!-- Left: tree -->
        <div class="t-org-page__tree">
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
        </div>

        <!-- Right: detail / edit form -->
        <div class="t-org-page__detail">
          <div v-if="!selectedNode" class="t-org-page__placeholder">
            {{ t('selectPrompt') }}
          </div>
          <div v-else>
            <header class="t-org-page__detail-header">
              <h3>{{ selectedNode.name }}</h3>
              <NSpace>
                <NButton size="small" @click="openCreate(selectedNode.id)">
                  {{ t('addChild') }}
                </NButton>
                <NPopconfirm @positive-click="handleDelete">
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
              <NButton type="primary" :loading="saving" @click="saveUpdate">
                {{ t('save') }}
              </NButton>
              <NButton @click="resetForm">{{ t('reset') }}</NButton>
            </div>

            <!--
              Organization members panel — paged user list scoped to the
              selected org. The `includeChildren` toggle expands the
              backend query to every descendant unit. The "Remove" row
              action calls `users.removeFromOrganization(userId)` which
              detaches the user without deleting them — the user just
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
              />
            </div>
          </div>
        </div>
    </div>

    <!-- Create modal — works for both root and child create -->
    <NModal
      v-model:show="createModal.show"
      :title="t(createModal.parentId ? 'addChild' : 'addRoot')"
      preset="card"
      :style="createModalStyle"
    >
      <NForm label-placement="left" label-width="100px">
        <NFormItem :label="t('fields.name')" required>
          <NInput v-model:value="createModal.form.name" />
        </NFormItem>
        <NFormItem :label="t('fields.code')">
          <NInput v-model:value="createModal.form.code" />
        </NFormItem>
        <NFormItem :label="t('fields.remark')">
          <NInput
            v-model:value="createModal.form.remark"
            type="textarea"
            :rows="2"
            :placeholder="t('fields.remarkPlaceholder')"
          />
        </NFormItem>
        <NFormItem :label="t('fields.sortOrder')">
          <NInputNumber v-model:value="createModal.form.sortOrder" :min="0" />
        </NFormItem>
      </NForm>
      <template #footer>
        <div class="flex justify-end gap-8px">
          <NButton @click="createModal.show = false">{{ t('cancel') }}</NButton>
          <NButton type="primary" :disabled="!createModal.form.name" :loading="saving" @click="submitCreate">
            {{ t('create') }}
          </NButton>
        </div>
      </template>
    </NModal>
  </TContentPage>
</template>

<script setup lang="ts">
import { computed, reactive, ref, h, onMounted, watch } from 'vue'
import type { DataTableColumns, TreeOption } from 'naive-ui'
import {
  NTree, NSpace, NButton, NCheckbox, NInput, NInputNumber,
  NSwitch, NSpin, NForm, NFormItem, NPopconfirm, NModal,
} from 'naive-ui'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { useSafeMessage } from '../_shared/safeMessage'
import { useBreakpoint } from '../../headless/useBreakpoint'
import { createIdentityBridge, type OrganizationTreeNodeDto, type OrganizationDto } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import { interpolate, translatePageKey } from '../_shared/translate'
import TContentPage from '../../components/layout/TContentPage.vue'
import type { UserListItemDto } from '@tnzi/core/services/identity'

const bridge = createIdentityBridge({ client: useAdminClient() })
const t = (key: string, params?: Record<string, unknown>) =>
  interpolate(translatePageKey('identity.organizations', key), params)

type OrgMemberRow = UserListItemDto

const message = useSafeMessage()
const bp = useBreakpoint()

// Phase 1 responsive parity: cap modal width at viewport - margins so
// the dialog never escapes a phone screen. Mirrors TFormModal's
// behaviour without the full state-machine refactor (reactive
// metadata + form is convoluted to migrate to useFormModal).
const createModalStyle = computed(() =>
  bp.isSm.value
    ? { width: '100vw', maxWidth: '100vw' }
    : { width: 'min(500px, 95vw)' },
)

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

const createModal = reactive({
  show: false,
  parentId: null as string | null,
  form: { name: '', code: '' as string | undefined, remark: '' as string | undefined, sortOrder: 0 },
})

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
  createModal.parentId = parentId
  createModal.form = { name: '', code: undefined, remark: undefined, sortOrder: 0 }
  createModal.show = true
}

async function submitCreate(): Promise<void> {
  saving.value = true
  try {
    const created = await bridge.organizations.create({
      name: createModal.form.name,
      code: createModal.form.code ?? null,
      remark: createModal.form.remark ?? null,
      parentId: createModal.parentId ?? null,
      sortOrder: createModal.form.sortOrder,
    })
    message.success(t('createSuccess'))
    createModal.show = false
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
  // selected node — surface it so admins can tell who lives in which
  // sub-unit without exporting.
  {
    key: 'organizationName',
    title: t('members.organization'),
    minWidth: 160,
    ellipsis: { tooltip: true },
    render: (row) => row.organizationName ?? '—',
  },
  {
    key: 'actions',
    title: t('members.actions'),
    width: 100,
    align: 'right',
    render: (row) =>
      h(
        NPopconfirm,
        { onPositiveClick: () => removeMember(row) },
        {
          trigger: () =>
            h(
              NButton,
              { size: 'tiny', type: 'error', ghost: true },
              { default: () => t('members.remove') },
            ),
          default: () => t('members.confirmRemove', { user: row.userName }),
        },
      ),
  },
])

onMounted(() => {
  void loadTree()
})
</script>

<style scoped>
.t-org-page__layout {
  flex: 1;
  display: grid;
  grid-template-columns: 320px 1fr;
  gap: 16px;
  min-height: 0;
  height: 100%;
}
.t-org-page__tree {
  border-right: 1px solid var(--tnzi-border);
  padding-right: 16px;
  display: flex;
  flex-direction: column;
  min-height: 0;
}
/* Phone: stack the tree above the detail; the tree gets a capped height and
   a bottom divider instead of the right border. */
@media (max-width: 767px) {
  .t-org-page__layout {
    grid-template-columns: 1fr;
    height: auto;
  }
  .t-org-page__tree {
    border-right: none;
    padding-right: 0;
    border-bottom: 1px solid var(--tnzi-border);
    padding-bottom: 12px;
    max-height: 40vh;
  }
  .t-org-page__detail {
    padding: 0;
  }
}
.t-org-page__naive-tree {
  flex: 1;
  margin-top: 8px;
  min-height: 0;
  overflow: auto;
}
.t-org-page__detail {
  padding: 0 8px;
  overflow: auto;
  min-height: 0;
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
/* Members panel — visually separated from the edit form by a top border
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

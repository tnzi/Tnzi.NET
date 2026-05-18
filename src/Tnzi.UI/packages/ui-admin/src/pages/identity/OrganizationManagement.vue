<template>
  <div class="t-org-page t-page-scroll">
    <NCard :title="t('title')" :bordered="false" class="t-org-page__card">
      <template #header-extra>
        <NSpace>
          <NButton size="small" @click="loadTree">{{ t('refresh') }}</NButton>
          <NButton size="small" type="primary" @click="openCreate(null)">
            {{ t('addRoot') }}
          </NButton>
        </NSpace>
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
              <span style="opacity: 0.6">🔍</span>
            </template>
          </NInput>
          <NSpin :show="loading" style="margin-top: 8px">
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
                <NInput v-model:value="form.remark" type="textarea" :rows="2" />
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
          </div>
        </div>
      </div>
    </NCard>

    <!-- Create modal — works for both root and child create -->
    <NModal v-model:show="createModal.show" :title="t(createModal.parentId ? 'addChild' : 'addRoot')" preset="card" style="width: 500px">
      <NForm label-placement="left" label-width="100px">
        <NFormItem :label="t('fields.name')" required>
          <NInput v-model:value="createModal.form.name" />
        </NFormItem>
        <NFormItem :label="t('fields.code')">
          <NInput v-model:value="createModal.form.code" />
        </NFormItem>
        <NFormItem :label="t('fields.remark')">
          <NInput v-model:value="createModal.form.remark" type="textarea" :rows="2" />
        </NFormItem>
        <NFormItem :label="t('fields.sortOrder')">
          <NInputNumber v-model:value="createModal.form.sortOrder" :min="0" />
        </NFormItem>
      </NForm>
      <template #footer>
        <div style="display: flex; justify-content: flex-end; gap: 8px">
          <NButton @click="createModal.show = false">{{ t('cancel') }}</NButton>
          <NButton type="primary" :disabled="!createModal.form.name" :loading="saving" @click="submitCreate">
            {{ t('create') }}
          </NButton>
        </div>
      </template>
    </NModal>
  </div>
</template>

<script setup lang="ts">
import { computed, reactive, ref, h, onMounted } from 'vue'
import type { TreeOption } from 'naive-ui'
import {
  NCard, NTree, NSpace, NButton, NInput, NInputNumber, NSwitch, NSpin,
  NForm, NFormItem, NPopconfirm, NModal, useMessage,
} from 'naive-ui'
import { createIdentityBridge, type OrganizationTreeNodeDto, type OrganizationDto } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import { translatePageKey } from '../_shared/translate'

const bridge = createIdentityBridge({ client: useAdminClient() })
const t = (key: string) => translatePageKey('identity.organizations', key)

let message: { success(s: string): void; error(s: string): void }
try {
  message = useMessage()
} catch {
  message = { success: () => {}, error: () => {} }
}

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
              { style: 'color: var(--tnzi-base-text-muted); font-size: 12px; margin-right: 4px' },
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

onMounted(() => {
  void loadTree()
})
</script>

<style scoped>
.t-org-page {
  padding: 16px;
}
.t-org-page__layout {
  display: grid;
  grid-template-columns: 320px 1fr;
  gap: 16px;
  min-height: 480px;
}
.t-org-page__tree {
  border-right: 1px solid var(--tnzi-base-border, #efeff5);
  padding-right: 16px;
}
.t-org-page__naive-tree {
  margin-top: 8px;
  max-height: 60vh;
  overflow: auto;
}
.t-org-page__detail {
  padding: 0 8px;
}
.t-org-page__placeholder {
  color: var(--tnzi-base-text-muted, #888);
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
  color: var(--tnzi-base-text-muted, #888);
  text-align: center;
  padding: 24px 8px;
  font-size: 13px;
}
</style>

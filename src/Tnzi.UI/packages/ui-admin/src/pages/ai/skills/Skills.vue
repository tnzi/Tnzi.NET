<template>
  <!--
    Skills — production-grade card page.

    Layout: TCardPage (mode=page) with the usage-stats KPI strip rendered
    between the page header and the card grid via the #kpis slot (content-page
    standard: header → KPI row → list). The card grid shows the
    full skill catalog (DB + file-source rows merged); file-source rows are
    read-only (isReadOnly → edit/delete disabled). Toolbar has a category-tree
    filter (#toolbarLeft) + Export/Import + Popular buttons (#toolbarRight); the
    keyword search lives in the header bar (searchFields). A view drawer lazy-
    loads the full SKILL.md content via getBySlug; a popular drawer shows the
    most-activated skills ranking.
  -->
  <div class="ai-skills-page">
    <TCardPage
      :state="crud"
      mode="page"
      :title="t('title')"
      :title-help="t('banner')"
      :cols="{ xs: 1, sm: 2, md: 3, lg: 4 }"
      :search-fields="searchFields"
      :search-placeholder="t('search.placeholder')"
      :form-modal-width="760"
      :translate="t"
    >
      <!-- KPI strip — getUsageStats; between the page header and the grid. -->
      <template #kpis>
        <TKpiRow class="ai-skills-kpis">
          <TStatCard
            v-for="kpi in kpiCards"
            :key="kpi.key"
            class="ai-skills-kpi"
            :label="t(kpi.labelKey)"
            :value="kpi.value"
          />
        </TKpiRow>
      </template>

      <template #toolbarLeft>
          <NSelect
            :value="categoryId"
            size="small"
            clearable
            class="ai-skills-category-select w-200px max-w-full"
            :placeholder="t('category.all')"
            :options="categoryOptions"
            label-field="label"
            value-field="value"
            @update:value="onCategoryChange"
          />
        </template>

        <template #toolbarRight>
          <NButton size="small" tertiary @click="openPopular">
            <template #icon><TSvgIcon icon="mdi:trophy-outline" :size="16" /></template>
            {{ t('popular.button') }}
          </NButton>
          <NButton size="small" tertiary :loading="exporting" @click="doExport">
            <template #icon><TSvgIcon icon="mdi:tray-arrow-up" :size="16" /></template>
            {{ t('importExport.export') }}
          </NButton>
          <NButton size="small" tertiary :loading="importing" @click="triggerImport">
            <template #icon><TSvgIcon icon="mdi:tray-arrow-down" :size="16" /></template>
            {{ t('importExport.import') }}
          </NButton>
        </template>

        <template #card="{ item }">
          <TEntityCard clickable @click="openDetail(item)">
            <div class="flex items-center gap-8px mb-6px">
              <span class="ai-skills-card__icon">
                <TSvgIcon icon="mdi:puzzle-outline" :size="18" />
              </span>
              <span class="ai-skills-card__name flex-1">{{ item.name }}</span>
              <NTag
                v-if="item.isReadOnly"
                size="small"
                type="default"
                :bordered="false"
              >
                {{ t('badge.file') }}
              </NTag>
            </div>

            <div class="ai-skills-card__slug font-mono">{{ item.slug }}</div>

            <div class="flex flex-wrap gap-4px mb-6px">
              <NTag size="small" :type="scopeType(item.scope)" :bordered="false">
                {{ t(scopeKey(item.scope)) }}
              </NTag>
              <NTag
                size="small"
                :type="item.enabled ? 'success' : 'warning'"
                :bordered="false"
              >
                {{ item.enabled ? t('badge.enabled') : t('badge.disabled') }}
              </NTag>
            </div>

            <div
              v-if="(item.tags?.length ?? 0) > 0"
              class="flex flex-wrap gap-4px mb-6px"
            >
              <NTag
                v-for="tag in item.tags"
                :key="tag"
                size="small"
                type="info"
                :bordered="false"
              >
                {{ tag }}
              </NTag>
            </div>

            <div class="ai-skills-card__desc">
              {{ describePreview(item.description) || t('noDescription') }}
            </div>

            <template #actions>
              <NButton
                v-if="item.enabled"
                size="small"
                ghost
                @click="toggleEnabled(item)"
              >
                {{ t('actions.deactivate') }}
              </NButton>
              <NButton
                v-else
                size="small"
                type="success"
                ghost
                @click="toggleEnabled(item)"
              >
                {{ t('actions.activate') }}
              </NButton>
              <NButton
                size="small"
                ghost
                :disabled="item.isReadOnly"
                @click="crud.openEdit(item)"
              >
                {{ t('actions.edit') }}
              </NButton>
              <NPopconfirm v-if="!item.isReadOnly" @positive-click="removeOne(item)">
                <template #trigger>
                  <NButton size="small" type="error" ghost>{{ t('actions.delete') }}</NButton>
                </template>
                {{ t('deleteConfirm') }}
              </NPopconfirm>
              <NButton v-else size="small" type="error" ghost disabled>
                {{ t('actions.delete') }}
              </NButton>
            </template>
          </TEntityCard>
        </template>

        <template #form="{ formData, mode }">
          <TFormSchemaRenderer
            :schema="skillFormSchema"
            :model="(formData ?? {}) as Record<string, unknown>"
            :readonly="mode === 'view'"
            :translate="t"
            :columns="2"
          />
        </template>
    </TCardPage>

    <!-- Hidden file input for Import (.json). -->
    <input
      ref="fileInputRef"
      type="file"
      accept="application/json,.json"
      class="hidden"
      @change="onFileSelected"
    />

    <!-- Detail drawer — full SKILL.md content (lazy-loaded by slug). -->
    <NDrawer v-model:show="detailVisible" :width="680" placement="right">
      <NDrawerContent v-if="detailRow" :title="detailRow.name" closable>
        <NSpin :show="detailLoading" size="small">
          <div class="ai-skills-detail__meta">
            <div>
              <strong>{{ t('detail.slug') }}:</strong> <code>{{ detailRow.slug }}</code>
            </div>
            <div>
              <strong>{{ t('detail.scope') }}:</strong>
              <NTag size="small" :type="scopeType(detailRow.scope)" :bordered="false">
                {{ t(scopeKey(detailRow.scope)) }}
              </NTag>
            </div>
            <div>
              <strong>{{ t('detail.status') }}:</strong>
              <NTag
                size="small"
                :type="detailRow.enabled ? 'success' : 'warning'"
                :bordered="false"
              >
                {{ detailRow.enabled ? t('badge.enabled') : t('badge.disabled') }}
              </NTag>
            </div>
            <div v-if="detailRow.isReadOnly && detailRow.filePath">
              <strong>{{ t('detail.filePath') }}:</strong> <code>{{ detailRow.filePath }}</code>
            </div>
            <div v-if="(detailRow.tags?.length ?? 0) > 0">
              <strong>{{ t('detail.tags') }}:</strong>
              <NTag
                v-for="tag in detailRow.tags"
                :key="tag"
                size="small"
                type="info"
                :bordered="false"
                class="ml-4px"
              >{{ tag }}</NTag>
            </div>
            <div v-if="detailRow.description">
              <strong>{{ t('detail.description') }}:</strong> {{ detailRow.description }}
            </div>
          </div>

          <div v-if="detailContent.whenToUse" class="ai-skills-detail__section">
            <h3>{{ t('detail.whenToUse') }}</h3>
            <pre class="ai-skills-detail__body">{{ detailContent.whenToUse }}</pre>
          </div>

          <div class="ai-skills-detail__section">
            <h3>{{ t('detail.content') }}</h3>
            <pre class="ai-skills-detail__body">{{ detailContent.content || t('detail.noContent') }}</pre>
          </div>
        </NSpin>

        <template #footer>
          <NButton
            v-if="detailRow && !detailRow.isReadOnly"
            size="small"
            type="primary"
            @click="editFromDrawer"
          >
            {{ t('actions.edit') }}
          </NButton>
        </template>
      </NDrawerContent>
    </NDrawer>

    <!-- Popular drawer — getPopular ranking (name + activation count). -->
    <NDrawer v-model:show="popularVisible" :width="420" placement="right">
      <NDrawerContent :title="t('popular.title')" closable>
        <NSpin :show="popularLoading" size="small">
          <ol v-if="popularSkills.length" class="ai-skills-popular">
            <li
              v-for="(p, i) in popularSkills"
              :key="p.slug"
              class="ai-skills-popular__item"
            >
              <span class="ai-skills-popular__rank">{{ i + 1 }}</span>
              <span class="ai-skills-popular__name">{{ p.name }}</span>
              <span class="ai-skills-popular__count">
                {{ t('popular.activations', { count: p.activationCount }) }}
              </span>
            </li>
          </ol>
          <div v-else class="ai-skills-popular__empty">{{ t('popular.empty') }}</div>
        </NSpin>
      </NDrawerContent>
    </NDrawer>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import {
  NButton,
  NDrawer,
  NDrawerContent,
  NPopconfirm,
  NSelect,
  NSpin,
  NTag,
  useMessage,
} from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TCardPage from '../../../components/crud/TCardPage.vue'
import TEntityCard from '../../../components/data/TEntityCard.vue'
import TKpiRow from '../../../components/data/TKpiRow.vue'
import TStatCard from '../../../components/data/TStatCard.vue'
import { useCrudPage } from '../../../headless/useCrudPage'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import TFormSchemaRenderer from '../../_shared/form-schema'
import { translatePageKey, interpolate } from '../../_shared/translate'
import {
  skillColumns,
  skillFormSchema,
  skillSearchFields as searchFields,
  describePreview,
  scopeLabelKey,
  scopeBadgeType,
  flattenCategoryTree,
  type CategoryOption,
} from './skill-config'
import type {
  SkillSummaryDto,
  SkillDetailDto,
  CreateSkillDto,
  UpdateSkillDto,
  SkillUsageStatsDto,
  PopularSkillDto,
} from '@tnzi/core/services/ai'

const t = (key: string, params?: Record<string, unknown>) =>
  interpolate(translatePageKey('ai.skills', key), params)

const message = (() => {
  try {
    return useMessage()
  } catch {
    return null
  }
})()

const bridge = createAiBridge({ client: useAdminClient() })

// --- card-source scope badge helpers (config-driven, both ordinal + name) ---
const scopeKey = (scope: unknown) => scopeLabelKey(scope)
const scopeType = (scope: unknown) => scopeBadgeType(scope)

// --- category filter -------------------------------------------------------
// Strategy: bridge.skills.fetch has no categoryId filter, so when a category is
// selected we swap the data source to bridge.skillCategories.getSkills(id) which
// returns the (non-paged) skill list for that category. Selecting "all" reverts
// to the standard paged catalog fetch. Both feed the same useCrudPage state.
const categoryId = ref<string | null>(null)
const categoryOptions = ref<CategoryOption[]>([])

const crud = useCrudPage<SkillSummaryDto>({
  pageId: 'ai.skills',
  columns: skillColumns,
  // File-source skills have no DB row → backend returns Guid.Empty for `id`;
  // fall back to slug so card keys + selection stay unique within scope.
  rowKey: (s) => {
    const id = String(s.id ?? '')
    return id && id !== '00000000-0000-0000-0000-000000000000' ? id : `slug:${s.slug}`
  },
  fetchData: async (query) => {
    const cat = query.filters.categoryId as string | undefined
    if (cat) {
      // Category-scoped: getSkills returns a plain array → wrap into a PagedList.
      const items = await bridge.skillCategories.getSkills(cat)
      return {
        items,
        totalCount: items.length,
        pageIndex: 1,
        pageSize: items.length || 1,
        totalPages: 1,
        hasPreviousPage: false,
        hasNextPage: false,
      }
    }
    return bridge.skills.fetch(query)
  },
  createData: (data) => bridge.skills.create(data as CreateSkillDto),
  updateData: (id, data) => bridge.skills.update(String(id), data as UpdateSkillDto),
  deleteData: (ids) => bridge.skills.delete(ids.map(String)),
})

void loadCategories()
crud.refresh().catch(() => undefined)

function onCategoryChange(value: string | null): void {
  categoryId.value = value
  crud.setFilters({ ...crud.query.value.filters, categoryId: value ?? undefined })
  crud.refresh().catch(() => undefined)
}

async function loadCategories(): Promise<void> {
  try {
    const tree = await bridge.skillCategories.getTree()
    categoryOptions.value = flattenCategoryTree(tree)
  } catch {
    categoryOptions.value = []
  }
}

// --- KPI strip -------------------------------------------------------------
const stats = ref<SkillUsageStatsDto | null>(null)
const kpiCards = computed(() => [
  { key: 'total', labelKey: 'kpi.total', value: stats.value?.totalSkills ?? 0 },
  { key: 'enabled', labelKey: 'kpi.enabled', value: stats.value?.enabledSkills ?? 0 },
  { key: 'disabled', labelKey: 'kpi.disabled', value: stats.value?.disabledSkills ?? 0 },
  { key: 'activations', labelKey: 'kpi.activations', value: stats.value?.totalActivations ?? 0 },
])

async function loadStats(): Promise<void> {
  try {
    stats.value = await bridge.skills.getUsageStats()
  } catch {
    stats.value = null
  }
}

// Kick off the stats load now that `stats` + `loadStats` are initialized
// (must come after the `const stats` declaration to avoid a TDZ error).
void loadStats()

// --- enable / disable ------------------------------------------------------
async function toggleEnabled(item: SkillSummaryDto): Promise<void> {
  try {
    if (item.enabled) {
      await bridge.skills.deactivate(String(item.id))
      message?.success(t('actions.deactivated'))
    } else {
      await bridge.skills.activate(String(item.id))
      message?.success(t('actions.activated'))
    }
    await crud.refresh()
    await loadStats()
  } catch {
    message?.error(t('actions.toggleFailed'))
  }
}

async function removeOne(item: SkillSummaryDto): Promise<void> {
  await crud.handleDelete([crud.rowKey(item)])
  await loadStats()
}

// --- detail drawer (lazy-load full content by slug) ------------------------
const detailRow = ref<SkillSummaryDto | null>(null)
const detailVisible = ref(false)
const detailLoading = ref(false)
const detailContent = ref<{ content: string; whenToUse: string }>({ content: '', whenToUse: '' })

async function openDetail(row: SkillSummaryDto): Promise<void> {
  detailRow.value = row
  detailVisible.value = true
  detailContent.value = { content: '', whenToUse: row.whenToUse ?? '' }
  detailLoading.value = true
  try {
    const full: SkillDetailDto = await bridge.skills.getBySlug(row.slug)
    detailContent.value = {
      content: full.content ?? '',
      whenToUse: full.whenToUse ?? row.whenToUse ?? '',
    }
  } catch {
    detailContent.value = { content: '', whenToUse: row.whenToUse ?? '' }
  } finally {
    detailLoading.value = false
  }
}

function editFromDrawer(): void {
  if (!detailRow.value) return
  detailVisible.value = false
  crud.openEdit(detailRow.value)
}

// --- popular drawer --------------------------------------------------------
const popularVisible = ref(false)
const popularLoading = ref(false)
const popularSkills = ref<PopularSkillDto[]>([])

async function openPopular(): Promise<void> {
  popularVisible.value = true
  popularLoading.value = true
  try {
    popularSkills.value = await bridge.skills.getPopular(10)
  } catch {
    popularSkills.value = []
  } finally {
    popularLoading.value = false
  }
}

// --- export ----------------------------------------------------------------
const exporting = ref(false)
async function doExport(): Promise<void> {
  exporting.value = true
  try {
    const skills = await bridge.skills.exportSkills()
    const blob = new Blob([JSON.stringify(skills, null, 2)], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `skills-export-${new Date().toISOString().slice(0, 10)}.json`
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    URL.revokeObjectURL(url)
    message?.success(t('importExport.exportSuccess', { count: skills.length }))
  } catch {
    message?.error(t('importExport.exportFailed'))
  } finally {
    exporting.value = false
  }
}

// --- import ----------------------------------------------------------------
const fileInputRef = ref<HTMLInputElement | null>(null)
const importing = ref(false)

function triggerImport(): void {
  fileInputRef.value?.click()
}

async function onFileSelected(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  // Reset the input so selecting the same file again re-triggers change.
  input.value = ''
  if (!file) return
  importing.value = true
  try {
    const text = await file.text()
    const parsed = JSON.parse(text)
    // Accept either a bare array (exportSkills output) or a full request body.
    const skills = Array.isArray(parsed) ? parsed : parsed?.skills
    if (!Array.isArray(skills)) {
      message?.error(t('importExport.invalidFile'))
      return
    }
    const result = await bridge.skills.importSkills({ skills })
    message?.success(
      t('importExport.importResult', {
        imported: result.created,
        updated: result.updated,
        failed: result.errors?.length ?? 0,
      }),
    )
    await crud.refresh()
    await loadStats()
  } catch {
    message?.error(t('importExport.importFailed'))
  } finally {
    importing.value = false
  }
}
</script>

<style scoped>
.ai-skills-page {
  width: 100%;
  height: 100%;
  min-height: 0;
}
.ai-skills-card__icon {
  display: inline-flex;
  color: var(--tnzi-primary, #6d5ce7);
}
.ai-skills-card__name {
  font-weight: 500;
  font-size: 14px;
  color: var(--tnzi-base-text);
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.ai-skills-card__slug {
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #888);
  margin-bottom: 6px;
}
.ai-skills-card__desc {
  font-size: 13px;
  color: var(--tnzi-base-text-muted, #888);
  line-height: 1.45;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
  min-height: 36px;
}
.ai-skills-detail__meta {
  display: flex;
  flex-direction: column;
  gap: 6px;
  font-size: 13px;
  padding-bottom: 12px;
  border-bottom: 1px solid var(--tnzi-border, #e5e7eb);
}
.ai-skills-detail__section {
  margin-top: 16px;
}
.ai-skills-detail__section h3 {
  margin: 0 0 8px;
  font-size: 14px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.ai-skills-detail__body {
  margin: 0;
  padding: 12px;
  background: var(--tnzi-bg-deep, #f6f8fa);
  border-radius: 6px;
  font-family: var(--tnzi-font-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
  font-size: 12px;
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-word;
  max-height: 50vh;
  overflow: auto;
}
.ai-skills-popular {
  margin: 0;
  padding: 0;
  list-style: none;
}
.ai-skills-popular__item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 0;
  border-bottom: 1px solid var(--tnzi-border, #f0f0f0);
  font-size: 13px;
}
.ai-skills-popular__rank {
  flex-shrink: 0;
  width: 22px;
  height: 22px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  background: var(--tnzi-bg-deep, #f0f0f3);
  color: var(--tnzi-base-text-muted, #888);
  font-size: 12px;
  font-weight: 600;
}
.ai-skills-popular__name {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--tnzi-base-text);
}
.ai-skills-popular__count {
  flex-shrink: 0;
  color: var(--tnzi-base-text-muted, #888);
  font-size: 12px;
}
.ai-skills-popular__empty {
  color: var(--tnzi-base-text-muted, #888);
  font-size: 13px;
}
</style>

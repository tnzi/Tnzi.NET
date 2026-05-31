<template>
  <!--
    LogViewer — admin read-only viewer for the on-disk log files written
    by `Tnzi.Logging` (Serilog file sinks). Two-pane layout:
      - Left:  level navigator + per-level rolling-day file list
      - Right: tail viewer for the selected file (last N lines) + search

    All endpoints under `admin/logs/*` are read-only — there is no delete /
    edit UX because log retention is owned server-side by Serilog's
    `RetainedFileCountLimit` per level.
  -->
  <div class="t-log-viewer-page">
    <div class="t-log-viewer-page__sidebar">
      <div class="t-log-viewer-page__sidebar-header">
        <span class="t-log-viewer-page__title">{{ t('levels') }}</span>
        <NButton
          quaternary
          circle
          size="small"
          :title="t('actions.refresh')"
          @click="refreshLevels"
        >
          <template #icon>
            <TSvgIcon icon="mdi:refresh" :size="16" />
          </template>
        </NButton>
      </div>
      <NSpin :show="levelsLoading">
        <div class="t-log-viewer-page__levels">
          <div
            v-for="lv in levels"
            :key="lv.level"
            class="t-log-viewer-page__level"
            :class="{
              'is-active': selectedLevel === lv.level,
              'is-disabled': !lv.isEnabled,
            }"
            @click="selectLevel(lv.level)"
          >
            <div class="t-log-viewer-page__level-row">
              <TSvgIcon :icon="levelIcon(lv.level)" :size="14" />
              <span class="t-log-viewer-page__level-name">{{ lv.level }}</span>
              <NTag size="tiny" :type="lv.isEnabled ? 'success' : 'default'">
                {{ lv.fileCount }}
              </NTag>
            </div>
            <div v-if="lv.lastModifiedUtc" class="t-log-viewer-page__level-meta">
              {{ formatBytes(lv.totalSize) }} · {{ formatDate(lv.lastModifiedUtc) }}
            </div>
            <div v-else class="t-log-viewer-page__level-meta">{{ t('empty') }}</div>
          </div>
        </div>
      </NSpin>

      <div v-if="selectedLevel" class="t-log-viewer-page__files-section">
        <div class="t-log-viewer-page__sidebar-header">
          <span class="t-log-viewer-page__title">{{ t('files') }}</span>
          <NButton
            quaternary
            circle
            size="small"
            :title="t('actions.refresh')"
            @click="refreshFiles"
          >
            <template #icon>
              <TSvgIcon icon="mdi:refresh" :size="14" />
            </template>
          </NButton>
        </div>
        <NSpin :show="filesLoading">
          <div class="t-log-viewer-page__files">
            <div
              v-for="file in files"
              :key="file.fileName"
              class="t-log-viewer-page__file"
              :class="{ 'is-active': selectedFile === file.fileName }"
              @click="selectFile(file.fileName)"
            >
              <div class="t-log-viewer-page__file-name">{{ file.fileName }}</div>
              <div class="t-log-viewer-page__file-meta">
                {{ formatBytes(file.size) }} · {{ formatDate(file.lastModifiedUtc) }}
              </div>
            </div>
            <div v-if="!filesLoading && files.length === 0" class="t-log-viewer-page__empty">
              {{ t('noFiles') }}
            </div>
          </div>
        </NSpin>
      </div>
    </div>

    <div class="t-log-viewer-page__main">
      <div class="t-log-viewer-page__toolbar">
        <div class="t-log-viewer-page__file-title">
          <template v-if="selectedFile">
            <TSvgIcon :icon="levelIcon(selectedLevel)" :size="14" />
            <span>{{ selectedLevel }} / {{ selectedFile }}</span>
            <NTag v-if="tail" size="tiny" type="info">
              {{ formatBytes(tail.totalSize) }}
            </NTag>
            <NTag v-if="tail?.truncated" size="tiny" type="warning">
              {{ t('truncated') }}
            </NTag>
          </template>
          <template v-else>
            <span class="t-log-viewer-page__placeholder">{{ t('pickFile') }}</span>
          </template>
        </div>
        <div class="t-log-viewer-page__toolbar-right">
          <NInput
            v-model:value="clientFilter"
            size="small"
            :placeholder="t('filterPlaceholder')"
            clearable
            style="width: 200px"
          >
            <template #prefix>
              <TSvgIcon icon="mdi:filter-outline" :size="14" />
            </template>
          </NInput>
          <NSelect
            v-model:value="lineCount"
            :options="lineCountOptions"
            size="small"
            style="width: 110px"
            @update:value="reloadTail"
          />
          <NButton size="small" :loading="tailLoading" @click="reloadTail">
            <template #icon>
              <TSvgIcon icon="mdi:refresh" :size="14" />
            </template>
            {{ t('actions.reload') }}
          </NButton>
          <NPopover trigger="click" placement="bottom-end" :width="380">
            <template #trigger>
              <NButton size="small" :type="searchActive ? 'primary' : 'default'">
                <template #icon>
                  <TSvgIcon icon="mdi:magnify" :size="14" />
                </template>
                {{ t('actions.search') }}
              </NButton>
            </template>
            <div class="t-log-viewer-page__search-panel">
              <NInput
                v-model:value="searchKeyword"
                :placeholder="t('searchKeywordPlaceholder')"
                clearable
                @keydown.enter="runSearch"
              />
              <div style="display: flex; gap: 8px;">
                <NSelect
                  v-model:value="searchLevel"
                  :options="searchLevelOptions"
                  :placeholder="t('searchAnyLevel')"
                  clearable
                  size="small"
                  style="flex: 1"
                />
                <NSelect
                  v-model:value="searchMaxResults"
                  :options="searchMaxResultsOptions"
                  size="small"
                  style="width: 110px"
                />
              </div>
              <NButton type="primary" :loading="searchLoading" @click="runSearch">
                {{ t('actions.runSearch') }}
              </NButton>
              <div v-if="searchResult" class="t-log-viewer-page__search-results">
                <div class="t-log-viewer-page__search-summary">
                  {{ t('searchMatches', { count: searchResult.hits.length }) }}
                  <span v-if="searchResult.truncated">· {{ t('truncated') }}</span>
                  <span class="t-log-viewer-page__search-elapsed">{{ searchResult.elapsedMs }}ms</span>
                </div>
                <ul class="t-log-viewer-page__search-list">
                  <li
                    v-for="hit in searchResult.hits"
                    :key="`${hit.level}-${hit.fileName}-${hit.lineNumber}`"
                    @click="jumpToHit(hit)"
                  >
                    <span class="t-log-viewer-page__hit-meta">
                      {{ hit.level }} / {{ hit.fileName }}:{{ hit.lineNumber }}
                    </span>
                    <div class="t-log-viewer-page__hit-line">{{ hit.line }}</div>
                  </li>
                </ul>
              </div>
            </div>
          </NPopover>
        </div>
      </div>
      <!-- The NSpin wrapper used to short-circuit the height: 100% chain
           (its `inline-block` default broke the parent flex sizing). We
           keep the loading indicator inline in the toolbar instead and
           render lines straight into the scrollable container so the
           internal `overflow: auto` actually fires on a sized parent. -->
      <div class="t-log-viewer-page__tail">
        <div v-if="!tail" class="t-log-viewer-page__placeholder-pane">
          {{ t('pickFile') }}
        </div>
        <div v-else class="t-log-viewer-page__lines">
          <div
            v-for="(line, idx) in visibleLines"
            :key="idx"
            class="t-log-viewer-page__line"
            :class="lineClass(line)"
          >
            <span class="t-log-viewer-page__lineno">{{ idx + 1 }}</span>
            <span class="t-log-viewer-page__linecontent">{{ line }}</span>
          </div>
          <div v-if="visibleLines.length === 0" class="t-log-viewer-page__empty">
            {{ clientFilter ? t('noMatch') : t('emptyFile') }}
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, onMounted } from 'vue'
import { NButton, NInput, NPopover, NSelect, NSpin, NTag } from 'naive-ui'
// NSpin is still used for the sidebar (level/file list) — kept in the import.
import { TSvgIcon } from '@tnzi/ui'
import { formatDateTime as formatDate } from '@tnzi/core'
import { useAdminClient } from '../../plugin/client'
import { createLoggingBridge } from '../../services/bridges/logging-bridge'
import type {
  LogLevelInfoDto,
  LogFileInfoDto,
  LogTailResultDto,
  LogSearchHitDto,
  LogSearchResultDto,
} from '@tnzi/core/services/logging'
import { translatePageKey, interpolate } from '../_shared/translate'

const bridge = createLoggingBridge({ client: useAdminClient() })

const t = (key: string, params?: Record<string, unknown>) =>
  interpolate(translatePageKey('system.logFiles', key), params)

const levels = ref<LogLevelInfoDto[]>([])
const levelsLoading = ref(false)
const selectedLevel = ref<string>('')

const files = ref<LogFileInfoDto[]>([])
const filesLoading = ref(false)
const selectedFile = ref<string>('')

const tail = ref<LogTailResultDto | null>(null)
const tailLoading = ref(false)
const lineCount = ref<number>(500)
const lineCountOptions = [
  { value: 100, label: 'Last 100' },
  { value: 500, label: 'Last 500' },
  { value: 1000, label: 'Last 1000' },
  { value: 2000, label: 'Last 2000' },
  { value: 5000, label: 'Last 5000' },
]

const clientFilter = ref('')

const searchKeyword = ref('')
const searchLevel = ref<string | null>(null)
const searchMaxResults = ref<number>(200)
const searchResult = ref<LogSearchResultDto | null>(null)
const searchLoading = ref(false)
const searchLevelOptions = computed(() =>
  levels.value.map((l) => ({ value: l.level, label: l.level })),
)
const searchMaxResultsOptions = [
  { value: 50, label: '50' },
  { value: 200, label: '200' },
  { value: 500, label: '500' },
  { value: 1000, label: '1000' },
]
const searchActive = computed(() => searchResult.value !== null)

const visibleLines = computed<string[]>(() => {
  if (!tail.value) return []
  const q = clientFilter.value.trim().toLowerCase()
  if (!q) return tail.value.lines
  return tail.value.lines.filter((l) => l.toLowerCase().includes(q))
})

function levelIcon(level: string): string {
  switch ((level ?? '').toLowerCase()) {
    case 'error': return 'mdi:alert-circle-outline'
    case 'fatal': return 'mdi:fire'
    case 'warning': return 'mdi:alert-outline'
    case 'information': return 'mdi:information-outline'
    case 'debug': return 'mdi:bug-outline'
    default: return 'mdi:file-document-outline'
  }
}

function lineClass(line: string): string {
  if (/\[(ERR|FTL)\]/i.test(line)) return 't-log-viewer-page__line--error'
  if (/\[WRN\]/i.test(line)) return 't-log-viewer-page__line--warn'
  if (/\[DBG\]/i.test(line)) return 't-log-viewer-page__line--debug'
  return ''
}

function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`
  if (n < 1024 * 1024 * 1024) return `${(n / 1024 / 1024).toFixed(2)} MB`
  return `${(n / 1024 / 1024 / 1024).toFixed(2)} GB`
}

async function refreshLevels(): Promise<void> {
  levelsLoading.value = true
  try {
    levels.value = await bridge.logFiles.getLevels()
    // Auto-select first enabled level that has files
    if (!selectedLevel.value) {
      const candidate = levels.value.find((l) => l.isEnabled && l.fileCount > 0)
        ?? levels.value.find((l) => l.isEnabled)
      if (candidate) selectLevel(candidate.level)
    }
  } catch {
    levels.value = []
  } finally {
    levelsLoading.value = false
  }
}

async function refreshFiles(): Promise<void> {
  if (!selectedLevel.value) return
  filesLoading.value = true
  try {
    files.value = await bridge.logFiles.getFiles(selectedLevel.value)
    // Auto-select newest file
    const newest = files.value[0]
    if (newest && !files.value.find((f) => f.fileName === selectedFile.value)) {
      selectFile(newest.fileName)
    }
  } catch {
    files.value = []
  } finally {
    filesLoading.value = false
  }
}

function selectLevel(level: string): void {
  selectedLevel.value = level
  selectedFile.value = ''
  files.value = []
  tail.value = null
  void refreshFiles()
}

function selectFile(fileName: string): void {
  selectedFile.value = fileName
  void reloadTail()
}

async function reloadTail(): Promise<void> {
  if (!selectedLevel.value || !selectedFile.value) {
    tail.value = null
    return
  }
  tailLoading.value = true
  try {
    tail.value = await bridge.logFiles.tail({
      level: selectedLevel.value,
      file: selectedFile.value,
      lines: lineCount.value,
    })
  } catch {
    tail.value = null
  } finally {
    tailLoading.value = false
  }
}

async function runSearch(): Promise<void> {
  const kw = searchKeyword.value.trim()
  if (!kw) return
  searchLoading.value = true
  try {
    searchResult.value = await bridge.logFiles.search({
      keyword: kw,
      level: searchLevel.value || undefined,
      maxResults: searchMaxResults.value,
    })
  } catch {
    searchResult.value = null
  } finally {
    searchLoading.value = false
  }
}

function jumpToHit(hit: LogSearchHitDto): void {
  if (hit.level !== selectedLevel.value) {
    selectedLevel.value = hit.level
    void refreshFiles().then(() => {
      selectedFile.value = hit.fileName
      void reloadTail()
    })
  } else if (hit.fileName !== selectedFile.value) {
    selectFile(hit.fileName)
  }
}

onMounted(() => {
  void refreshLevels()
})
</script>

<style scoped>
.t-log-viewer-page {
  display: flex;
  gap: 12px;
  height: 100%;
  min-height: 0;
}
.t-log-viewer-page__sidebar {
  width: 280px;
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
  background: var(--tnzi-container-bg);
  border: 1px solid var(--tnzi-border);
  border-radius: 8px;
  padding: 12px;
  min-height: 0;
  overflow: auto;
}
.t-log-viewer-page__sidebar-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.t-log-viewer-page__title {
  font-weight: 600;
  font-size: 13px;
  color: var(--tnzi-base-text-muted, #888);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}
.t-log-viewer-page__levels,
.t-log-viewer-page__files {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.t-log-viewer-page__level,
.t-log-viewer-page__file {
  padding: 8px 10px;
  border-radius: 6px;
  cursor: pointer;
  transition: background 0.15s ease;
}
.t-log-viewer-page__level:hover,
.t-log-viewer-page__file:hover {
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.08);
}
.t-log-viewer-page__level.is-active,
.t-log-viewer-page__file.is-active {
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.12);
  color: var(--tnzi-primary);
}
.t-log-viewer-page__level.is-disabled {
  opacity: 0.55;
}
.t-log-viewer-page__level-row {
  display: flex;
  align-items: center;
  gap: 6px;
}
.t-log-viewer-page__level-name {
  flex: 1;
  font-weight: 500;
}
.t-log-viewer-page__level-meta,
.t-log-viewer-page__file-meta {
  margin-top: 2px;
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-log-viewer-page__file-name {
  font-family: var(--tnzi-font-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
  font-size: 12px;
}
.t-log-viewer-page__empty,
.t-log-viewer-page__placeholder-pane,
.t-log-viewer-page__placeholder {
  color: var(--tnzi-base-text-muted, #888);
  text-align: center;
  padding: 16px;
  font-size: 13px;
}
.t-log-viewer-page__placeholder-pane {
  padding: 48px 24px;
}
.t-log-viewer-page__main {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
  min-height: 0;
}
.t-log-viewer-page__toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  background: var(--tnzi-container-bg);
  border: 1px solid var(--tnzi-border);
  border-radius: 8px;
  padding: 8px 12px;
}
.t-log-viewer-page__file-title {
  display: flex;
  align-items: center;
  gap: 8px;
  font-weight: 500;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-log-viewer-page__toolbar-right {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-log-viewer-page__tail {
  flex: 1 1 auto;
  display: flex;
  flex-direction: column;
  /* Terminal output — intentionally GitHub-dark in both light and dark
     mode, mirroring how IDEs render log files. The inner line-hover
     and lineno colours below (rgb(255 255 255 / x)) only read correctly
     against this constant dark background. */
  background: #0d1117;
  color: #e6edf3;
  border-radius: 8px;
  overflow: hidden;
  min-height: 0;
}
.t-log-viewer-page__lines {
  flex: 1 1 auto;
  min-height: 0;
  overflow: auto;
  padding: 8px 0;
  font-family: var(--tnzi-font-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
  font-size: 12px;
  line-height: 1.5;
}
.t-log-viewer-page__line {
  display: flex;
  gap: 12px;
  padding: 0 12px;
}
.t-log-viewer-page__line:hover {
  background: rgb(255 255 255 / 0.05);
}
.t-log-viewer-page__lineno {
  flex-shrink: 0;
  width: 48px;
  text-align: right;
  color: rgb(255 255 255 / 0.35);
  user-select: none;
}
.t-log-viewer-page__linecontent {
  flex: 1;
  white-space: pre-wrap;
  word-break: break-all;
}
.t-log-viewer-page__line--error {
  color: #f85149;
}
.t-log-viewer-page__line--warn {
  color: #e3b341;
}
.t-log-viewer-page__line--debug {
  color: #8b949e;
}
.t-log-viewer-page__search-panel {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.t-log-viewer-page__search-results {
  max-height: 320px;
  overflow: auto;
}
.t-log-viewer-page__search-summary {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
  margin-bottom: 4px;
}
.t-log-viewer-page__search-elapsed {
  margin-left: 8px;
}
.t-log-viewer-page__search-list {
  margin: 0;
  padding: 0;
  list-style: none;
}
.t-log-viewer-page__search-list > li {
  padding: 6px 8px;
  border-radius: 4px;
  cursor: pointer;
}
.t-log-viewer-page__search-list > li:hover {
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.08);
}
.t-log-viewer-page__hit-meta {
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-log-viewer-page__hit-line {
  font-family: var(--tnzi-font-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
  font-size: 12px;
  word-break: break-all;
  margin-top: 2px;
}
</style>

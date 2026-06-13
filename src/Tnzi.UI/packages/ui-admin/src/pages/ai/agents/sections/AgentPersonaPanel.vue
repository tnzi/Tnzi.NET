<template>
  <!--
    AgentPersonaPanel — the Persona section as a standalone rich page (mirrors Fabrikam
    BotDetail's PersonaTab quality). Framework personas are a SHARED catalog
    (AgentPersona, referenced by Agent.personaId), so this page:
      • left  — browse the catalog, preview, create/edit, "apply to this agent"
      • right — version history reused from AgentVersion (each snapshot records the
                PersonaId + Instructions applied at that time): pills + preview +
                rollback. No new backend table (per the approved design).
  -->
  <TDetailSection :title="t('detail.panels.persona')" :hint="t('detail.persona.hint')" max-width="none">
    <template #actions>
      <NButton size="small" type="primary" @click="openCreate">
        <template #icon><TSvgIcon icon="mdi:plus" :size="14" /></template>
        {{ t('detail.persona.new') }}
      </NButton>
    </template>

    <div class="t-persona__grid">
      <!-- Left: shared persona library -->
      <aside class="t-persona__lib">
        <div class="t-persona__lib-head">{{ t('detail.persona.library') }}</div>
        <div v-if="personas.length === 0" class="t-persona__empty">{{ t('detail.persona.libraryEmpty') }}</div>
        <ul v-else class="t-persona__lib-list">
          <li
            v-for="p in personas"
            :key="p.id"
            class="t-persona__lib-item"
            :class="{ 'is-selected': mode === 'persona' && selectedPersonaId === p.id }"
            role="button"
            tabindex="0"
            @click="selectPersona(p.id)"
            @keydown.enter="selectPersona(p.id)"
          >
            <span class="t-persona__lib-dot" :class="{ 'is-applied': p.id === currentPersonaId }" />
            <span class="t-persona__lib-name">{{ p.name }}</span>
            <NTag v-if="p.scope === ResourceScope.System" size="tiny" :bordered="false">{{ t('detail.system') }}</NTag>
            <NTag v-if="p.id === currentPersonaId" size="tiny" type="success" :bordered="false">
              {{ t('detail.persona.applied') }}
            </NTag>
          </li>
        </ul>
      </aside>

      <!-- Right: detail + version history -->
      <div class="t-persona__detail">
        <!-- Version strip (from AgentVersion) -->
        <div class="t-persona__versions">
          <span class="t-persona__versions-lbl">{{ t('detail.persona.versions') }}</span>
          <NSpin v-if="versionsLoading" :size="14" />
          <template v-else-if="sortedVersions.length">
            <div class="t-persona__pills">
              <button
                v-for="v in pillVersions"
                :key="v.id"
                type="button"
                class="t-persona__pill"
                :class="{ 'is-active': mode === 'version' && selectedVersion === v.version, 'is-current': v.version === currentVersion }"
                @click="selectVersion(v.version)"
              >
                <span v-if="v.version === currentVersion" class="t-persona__pill-dot" />
                v{{ v.version }}
              </button>
              <NPopover v-if="olderVersions.length" trigger="click" placement="bottom-start">
                <template #trigger>
                  <button type="button" class="t-persona__pill t-persona__pill--more">+{{ olderVersions.length }} {{ t('detail.persona.older') }}</button>
                </template>
                <div class="t-persona__older">
                  <button
                    v-for="v in olderVersions"
                    :key="v.id"
                    type="button"
                    class="t-persona__older-row"
                    :class="{ 'is-active': mode === 'version' && selectedVersion === v.version }"
                    @click="selectVersion(v.version)"
                  >
                    <span>v{{ v.version }}</span>
                    <span class="t-persona__older-note">{{ v.changeNote || formatTime(v.creationTime) }}</span>
                  </button>
                </div>
              </NPopover>
            </div>
          </template>
          <span v-else class="t-persona__versions-empty">{{ t('detail.persona.versionsEmpty') }}</span>
        </div>

        <!-- Version preview banner (read-only) -->
        <div v-if="mode === 'version' && selectedSnapshot" class="t-persona__verbanner">
          <div class="t-persona__verbanner-head">
            <span>{{ t('detail.persona.versionPreview') }} v{{ selectedVersion }}</span>
            <span class="t-persona__verbanner-persona">{{ snapshotPersonaName }}</span>
            <span class="t-persona__grow" />
            <NPopconfirm v-if="selectedVersion !== currentVersion" @positive-click="rollback">
              <template #trigger>
                <NButton size="tiny" type="warning">{{ t('detail.persona.rollback') }}</NButton>
              </template>
              {{ t('detail.persona.rollbackConfirm') }}
            </NPopconfirm>
            <NButton size="tiny" quaternary @click="exitVersionPreview">{{ t('detail.persona.exitPreview') }}</NButton>
          </div>
        </div>

        <!-- Selected persona meta -->
        <div v-if="mode === 'persona' && selectedPersona" class="t-persona__meta">
          <span class="t-persona__meta-name">{{ selectedPersona.name }}</span>
          <code class="t-persona__meta-slug">{{ selectedPersona.slug }}</code>
          <span class="t-persona__grow" />
          <NButton v-if="!isPersonaLocked(selectedPersona)" size="tiny" quaternary @click="openEdit">
            {{ t('detail.persona.editDetails') }}
          </NButton>
        </div>

        <!-- Content editor / preview -->
        <NInput
          v-model:value="editorContent"
          type="textarea"
          class="t-persona__editor font-mono"
          :readonly="contentReadonly"
          :rows="16"
          :placeholder="t('detail.persona.contentPlaceholder')"
        />

        <!-- Instructions preview (version mode only) -->
        <div v-if="mode === 'version' && selectedSnapshot?.instructions" class="t-persona__instr">
          <div class="t-persona__instr-lbl">{{ t('detail.persona.snapshotInstructions') }}</div>
          <pre class="t-persona__instr-body">{{ selectedSnapshot.instructions }}</pre>
        </div>

        <!-- Action bar -->
        <div v-if="mode === 'persona'" class="t-persona__actions">
          <NButton
            v-if="selectedPersona && !isPersonaLocked(selectedPersona)"
            size="small"
            :disabled="!contentDirty"
            :loading="savingContent"
            @click="saveContent"
          >
            {{ t('detail.persona.saveContent') }}
          </NButton>
          <NButton
            v-if="selectedPersonaId && selectedPersonaId !== currentPersonaId"
            size="small"
            type="primary"
            @click="apply"
          >
            {{ t('detail.persona.apply') }}
          </NButton>
          <NButton
            v-if="currentPersonaId"
            size="small"
            quaternary
            :disabled="!currentPersonaId"
            @click="emit('apply', null)"
          >
            {{ t('detail.persona.clear') }}
          </NButton>
        </div>
      </div>
    </div>

    <!-- Create / edit catalog persona modal -->
    <NModal
      v-model:show="modal.show"
      preset="card"
      :title="modal.id ? t('detail.persona.editTitle') : t('detail.persona.newTitle')"
      class="w-560px max-w-96vw"
    >
      <NForm label-placement="top" :show-feedback="false">
        <NFormItem :label="t('detail.persona.fieldName')" required>
          <NInput v-model:value="modal.name" :placeholder="t('detail.persona.fieldNamePlaceholder')" />
        </NFormItem>
        <NFormItem :label="t('detail.persona.fieldSlug')" required>
          <NInput v-model:value="modal.slug" :placeholder="t('detail.persona.fieldSlugPlaceholder')" :disabled="!!modal.id" />
        </NFormItem>
        <NFormItem :label="t('detail.persona.fieldDescription')">
          <NInput v-model:value="modal.description" type="textarea" :rows="2" />
        </NFormItem>
        <NFormItem :label="t('detail.persona.fieldContent')" required>
          <NInput v-model:value="modal.content" type="textarea" :rows="8" class="font-mono" />
        </NFormItem>
      </NForm>
      <template #footer>
        <div class="flex justify-end gap-8px">
          <NButton size="small" @click="modal.show = false">{{ t('detail.persona.cancel') }}</NButton>
          <NButton
            size="small"
            type="primary"
            :loading="savingModal"
            :disabled="!modal.name.trim() || !modal.slug.trim() || !modal.content.trim()"
            @click="saveModal"
          >
            {{ t('detail.persona.save') }}
          </NButton>
        </div>
      </template>
    </NModal>
  </TDetailSection>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { NButton, NForm, NFormItem, NInput, NModal, NPopconfirm, NPopover, NSpin, NTag, useMessage } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TDetailSection from '../../../../components/detail/TDetailSection.vue'
import { translatePageKey } from '../../../_shared/translate'
import { createAiBridge } from '../../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../../plugin/client'
import { useAdminAuthStore } from '../../../../stores/useAdminAuthStore'
import { ResourceScope } from '@tnzi/core/services/ai'
import type { AgentPersonaDto, AgentVersionDto } from '@tnzi/core/services/ai'

interface Props {
  currentPersonaId: string | null
  personas: AgentPersonaDto[]
  versions: AgentVersionDto[]
  versionsLoading?: boolean
}
const props = withDefaults(defineProps<Props>(), { versionsLoading: false })

const emit = defineEmits<{
  /** Set this agent's persona (null clears it). Parent persists + syncs agent state. */
  apply: [personaId: string | null]
  /** Roll the agent back to a version. Parent calls rollbackToVersion. */
  rollback: [version: number]
  /** A catalog persona was created/edited — parent reloads the catalog. */
  personasChanged: []
}>()

const t = (key: string) => translatePageKey('ai.agents', key)
const bridge = createAiBridge({ client: useAdminClient() })
const authStore = useAdminAuthStore()

// Mirrors the backend write guard (AgentPersonaService Update/Delete): system personas are
// locked only for tenant-scoped sessions; without tenant context (host admin / MT off) they
// remain editable.
function isPersonaLocked(p: AgentPersonaDto | null | undefined): boolean {
  return !!p && p.scope === ResourceScope.System && !!authStore.currentTenantId
}

const message = (() => { try { return useMessage() } catch { return null } })()

// ---- View mode: browsing the catalog vs previewing a version snapshot --------
const mode = ref<'persona' | 'version'>('persona')
const selectedPersonaId = ref<string | null>(props.currentPersonaId)
const selectedVersion = ref<number | null>(null)

// Keep selection in sync when the applied persona changes (apply / rollback).
watch(() => props.currentPersonaId, (id) => {
  if (mode.value === 'persona') selectedPersonaId.value = id
})

const selectedPersona = computed(() =>
  selectedPersonaId.value ? props.personas.find((p) => p.id === selectedPersonaId.value) ?? null : null,
)

// ---- Version pills (reuse AgentVersion) -------------------------------------
const sortedVersions = computed(() => [...props.versions].sort((a, b) => b.version - a.version))
const currentVersion = computed(() => sortedVersions.value[0]?.version ?? null)
const PILL_LIMIT = 4
const pillVersions = computed(() => sortedVersions.value.slice(0, PILL_LIMIT))
const olderVersions = computed(() => sortedVersions.value.slice(PILL_LIMIT))

interface VersionSnapshot { personaId: string | null; instructions: string | null; name?: string }
function parseSnapshot(json: string): VersionSnapshot {
  try {
    const o = JSON.parse(json) as Record<string, unknown>
    const pick = (a: string, b: string) => (o[a] ?? o[b]) as string | null | undefined
    return {
      personaId: (pick('PersonaId', 'personaId') ?? null) as string | null,
      instructions: (pick('Instructions', 'instructions') ?? null) as string | null,
      name: pick('Name', 'name') as string | undefined,
    }
  } catch {
    return { personaId: null, instructions: null }
  }
}
const selectedSnapshot = computed<VersionSnapshot | null>(() => {
  if (mode.value !== 'version' || selectedVersion.value == null) return null
  const v = sortedVersions.value.find((x) => x.version === selectedVersion.value)
  return v ? parseSnapshot(v.configSnapshot) : null
})
const snapshotPersonaName = computed(() => {
  const pid = selectedSnapshot.value?.personaId
  if (!pid) return t('detail.persona.noPersona')
  return props.personas.find((p) => p.id === pid)?.name ?? pid
})

// ---- Content editor ---------------------------------------------------------
const editorContent = ref('')
const baselineContent = ref('')
const savingContent = ref(false)

const contentReadonly = computed(() => mode.value === 'version' || !selectedPersona.value || isPersonaLocked(selectedPersona.value))
const contentDirty = computed(() => mode.value === 'persona' && editorContent.value !== baselineContent.value)

// Drive the editor from whatever is selected.
watch([mode, selectedPersona, selectedSnapshot], () => {
  if (mode.value === 'version') {
    const pid = selectedSnapshot.value?.personaId
    const p = pid ? props.personas.find((x) => x.id === pid) : null
    editorContent.value = p?.content ?? selectedSnapshot.value?.instructions ?? ''
    baselineContent.value = editorContent.value
  } else {
    editorContent.value = selectedPersona.value?.content ?? ''
    baselineContent.value = editorContent.value
  }
}, { immediate: true })

function selectPersona(id: string): void {
  mode.value = 'persona'
  selectedPersonaId.value = id
}
function selectVersion(version: number): void {
  mode.value = 'version'
  selectedVersion.value = version
}
function exitVersionPreview(): void {
  mode.value = 'persona'
  selectedVersion.value = null
  selectedPersonaId.value = props.currentPersonaId
}

function toast(kind: 'ok' | 'err', text: string): void {
  if (!message) return
  if (kind === 'ok') message.success(text)
  else message.error(text)
}

function apply(): void {
  if (selectedPersonaId.value) emit('apply', selectedPersonaId.value)
}
function rollback(): void {
  if (selectedVersion.value != null) emit('rollback', selectedVersion.value)
}

async function saveContent(): Promise<void> {
  const p = selectedPersona.value
  if (!p || isPersonaLocked(p) || !contentDirty.value) return
  savingContent.value = true
  try {
    await bridge.personas.update(p.id, { content: editorContent.value })
    baselineContent.value = editorContent.value
    toast('ok', t('detail.persona.contentSaved'))
    emit('personasChanged')
  } catch (e) {
    toast('err', (e as Error).message ?? t('detail.persona.saveError'))
  } finally {
    savingContent.value = false
  }
}

// ---- Create / edit catalog persona modal ------------------------------------
interface ModalState { show: boolean; id: string | null; name: string; slug: string; description: string; content: string }
const modal = reactive<ModalState>({ show: false, id: null, name: '', slug: '', description: '', content: '' })
const savingModal = ref(false)

function openCreate(): void {
  Object.assign(modal, { show: true, id: null, name: '', slug: '', description: '', content: '' })
}
function openEdit(): void {
  const p = selectedPersona.value
  if (!p || isPersonaLocked(p)) return
  Object.assign(modal, {
    show: true, id: p.id, name: p.name, slug: p.slug, description: p.description ?? '', content: editorContent.value,
  })
}

async function saveModal(): Promise<void> {
  if (!modal.name.trim() || !modal.slug.trim() || !modal.content.trim()) return
  savingModal.value = true
  try {
    if (modal.id) {
      await bridge.personas.update(modal.id, {
        name: modal.name.trim(), content: modal.content, description: modal.description.trim() || null,
      })
    } else {
      const created = await bridge.personas.create({
        name: modal.name.trim(), slug: modal.slug.trim(), content: modal.content,
        description: modal.description.trim() || undefined,
      })
      selectedPersonaId.value = created.id
      mode.value = 'persona'
    }
    modal.show = false
    toast('ok', t('detail.persona.saved'))
    emit('personasChanged')
  } catch (e) {
    toast('err', (e as Error).message ?? t('detail.persona.saveError'))
  } finally {
    savingModal.value = false
  }
}

function formatTime(v?: string | Date | null): string {
  if (!v) return ''
  try { return new Date(v).toLocaleDateString() } catch { return '' }
}
</script>

<style scoped>
.t-persona__grid {
  display: grid;
  grid-template-columns: 260px 1fr;
  gap: 20px;
  align-items: start;
}
@media (max-width: 900px) {
  .t-persona__grid { grid-template-columns: 1fr; }
}

/* Library */
.t-persona__lib {
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-md, 8px);
  overflow: hidden;
}
.t-persona__lib-head {
  padding: 10px 14px;
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.6px;
  color: var(--tnzi-base-text-muted, #888);
  background: var(--tnzi-layout-bg);
  border-bottom: 1px solid var(--tnzi-border);
}
.t-persona__lib-list { list-style: none; margin: 0; padding: 6px; display: flex; flex-direction: column; gap: 2px; }
.t-persona__lib-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 10px;
  border-radius: var(--tnzi-admin-radius-sm, 6px);
  cursor: pointer;
  font-size: 13px;
  color: var(--tnzi-base-text);
}
.t-persona__lib-item:hover { background: rgb(var(--tnzi-primary-rgb, 109 92 231) / 0.06); }
.t-persona__lib-item.is-selected {
  background: rgb(var(--tnzi-primary-rgb, 109 92 231) / 0.1);
  color: var(--tnzi-primary);
  font-weight: 600;
}
.t-persona__lib-dot { width: 6px; height: 6px; border-radius: 50%; background: transparent; flex-shrink: 0; }
.t-persona__lib-dot.is-applied { background: var(--tnzi-success, #18a058); }
.t-persona__lib-name { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.t-persona__empty { padding: 24px 12px; text-align: center; font-size: 12.5px; color: var(--tnzi-base-text-muted, #9ca3af); }

/* Detail column */
.t-persona__detail { display: flex; flex-direction: column; gap: 12px; min-width: 0; }
.t-persona__versions { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
.t-persona__versions-lbl {
  font-size: 11px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.6px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-persona__versions-empty { font-size: 12.5px; color: var(--tnzi-base-text-muted, #888); }
.t-persona__pills { display: flex; gap: 4px; align-items: center; flex-wrap: wrap; }
.t-persona__pill {
  display: inline-flex; align-items: center; gap: 4px;
  padding: 3px 10px; font-size: 12.5px;
  background: var(--tnzi-layout-bg); border: 1px solid transparent; border-radius: 12px;
  cursor: pointer; color: var(--tnzi-base-text-muted, #888); transition: all 0.15s ease;
}
.t-persona__pill:hover { background: rgb(var(--tnzi-primary-rgb, 109 92 231) / 0.06); }
.t-persona__pill.is-active {
  background: var(--tnzi-container-bg);
  border-color: rgb(var(--tnzi-primary-rgb, 109 92 231) / 0.3);
  color: var(--tnzi-primary); font-weight: 600;
}
.t-persona__pill-dot { width: 6px; height: 6px; border-radius: 50%; background: var(--tnzi-success, #18a058); }
.t-persona__pill--more { color: var(--tnzi-base-text-muted, #888); font-size: 11.5px; }
.t-persona__older { display: flex; flex-direction: column; min-width: 220px; }
.t-persona__older-row {
  display: flex; align-items: center; gap: 8px; padding: 6px 8px;
  background: none; border: none; cursor: pointer; text-align: left;
  border-radius: var(--tnzi-admin-radius-sm, 4px); font-size: 12.5px;
}
.t-persona__older-row:hover { background: rgb(var(--tnzi-primary-rgb, 109 92 231) / 0.06); }
.t-persona__older-row.is-active { background: rgb(var(--tnzi-primary-rgb, 109 92 231) / 0.1); color: var(--tnzi-primary); font-weight: 600; }
.t-persona__older-note { color: var(--tnzi-base-text-muted, #888); font-size: 11.5px; }

.t-persona__verbanner {
  border: 1px solid rgb(var(--tnzi-warning-rgb, 240 160 32) / 0.4);
  background: rgb(var(--tnzi-warning-rgb, 240 160 32) / 0.06);
  border-radius: var(--tnzi-admin-radius-md, 8px);
  padding: 8px 12px;
}
.t-persona__verbanner-head { display: flex; align-items: center; gap: 10px; font-size: 12.5px; }
.t-persona__verbanner-persona { font-weight: 600; color: var(--tnzi-base-text); }

.t-persona__meta { display: flex; align-items: center; gap: 10px; }
.t-persona__meta-name { font-size: 14px; font-weight: 600; color: var(--tnzi-base-text); }
.t-persona__meta-slug { font-family: var(--tnzi-font-mono, ui-monospace, monospace); font-size: 11.5px; color: var(--tnzi-base-text-muted, #888); }
.t-persona__grow { flex: 1; }

.t-persona__editor :deep(textarea) { font-size: 13px; line-height: 1.6; }

.t-persona__instr {
  border: 1px solid var(--tnzi-border); border-radius: var(--tnzi-admin-radius-md, 8px);
  background: var(--tnzi-layout-bg); padding: 10px 12px;
}
.t-persona__instr-lbl {
  font-size: 11px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;
  color: var(--tnzi-base-text-muted, #888); margin-bottom: 6px;
}
.t-persona__instr-body {
  margin: 0; font-size: 11.5px; line-height: 1.5; white-space: pre-wrap; word-break: break-word;
  max-height: 200px; overflow: auto; color: var(--tnzi-base-text-muted, #555);
}
.t-persona__actions { display: flex; justify-content: flex-end; gap: 8px; }
</style>

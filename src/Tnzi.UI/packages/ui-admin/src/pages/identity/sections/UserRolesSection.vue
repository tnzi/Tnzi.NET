<template>
  <TDetailSection :title="t('detail.sections.roles')" :hint="t('actions.manageRolesHint')" max-width="none">
    <NSpin :show="loading">
      <TEmpty v-if="!loading && !roles.length" :text="t('actions.noRolesAvailable')" size="small" />
      <div v-else class="ur-grid">
        <label
          v-for="role in roles"
          :key="role.id"
          class="ur-role"
          :class="{ 'ur-role--on': selected.includes(role.id) }"
        >
          <NCheckbox
            :checked="selected.includes(role.id)"
            :disabled="!canEdit"
            @update:checked="(v: boolean) => toggle(role.id, v)"
          />
          <span class="ur-role__body">
            <span class="ur-role__name">{{ role.name }}</span>
            <span v-if="role.description" class="ur-role__desc">{{ role.description }}</span>
          </span>
          <NTag v-if="role.isSystem" size="small" round :bordered="false" type="info">
            {{ t('admin.shared.status.system') }}
          </NTag>
        </label>
      </div>
    </NSpin>

    <template v-if="canEdit" #savebar>
      <span v-if="dirty" class="ur-dirty">{{ t('detail.roles.dirty', { added: addedCount, removed: removedCount }) }}</span>
      <NButton size="small" :disabled="!dirty" @click="reset">{{ t('admin.common.reset') }}</NButton>
      <NButton size="small" type="primary" :loading="saving" :disabled="!dirty" @click="save">
        {{ t('admin.common.save') }}
      </NButton>
    </template>
  </TDetailSection>
</template>

<script setup lang="ts">
/**
 * Role membership for ONE user, as a pickable list rather than a modal of bare
 * checkboxes: each role shows its description and whether it is a system role,
 * so the person granting access can see what they are granting.
 */
import { computed, ref, watch } from 'vue'
import { NButton, NCheckbox, NSpin, NTag } from 'naive-ui'
import TDetailSection from '../../../components/detail/TDetailSection.vue'
import TEmpty from '../../../components/data/TEmpty.vue'
import { createIdentityBridge } from '../../../services/bridges/identity-bridge'
import { useAdminClient } from '../../../plugin/client'
import { useSafeMessage } from '../../_shared/safeMessage'
import type { RoleDto } from '@tnzi/core/services/identity'

const props = defineProps<{
  userId: string
  /** Role NAMES from the user record (the DTO denormalises names, not ids). */
  userRoleNames: string[]
  canEdit: boolean
  t: (key: string, named?: Record<string, unknown>) => string
}>()

const emit = defineEmits<{ saved: [] }>()

const bridge = createIdentityBridge({ client: useAdminClient() })
const message = useSafeMessage()

const roles = ref<RoleDto[]>([])
const loading = ref(true)
const saving = ref(false)
const selected = ref<string[]>([])
const original = ref<string[]>([])

/** Resolve the user's role NAMES against the catalogue to get ids. */
function syncSelection(): void {
  const names = new Set(props.userRoleNames)
  const matched = roles.value.filter((r) => names.has(r.name)).map((r) => r.id)
  selected.value = matched
  original.value = [...matched]
}

async function load(): Promise<void> {
  loading.value = true
  try {
    roles.value = await bridge.roles.getAll()
    syncSelection()
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    loading.value = false
  }
}
void load()

// A save elsewhere (or navigating between users) re-seeds the ticks.
watch(() => props.userRoleNames, syncSelection)

function toggle(id: string, checked: boolean): void {
  selected.value = checked ? [...selected.value, id] : selected.value.filter((x) => x !== id)
}

const addedCount = computed(() => selected.value.filter((id) => !original.value.includes(id)).length)
const removedCount = computed(() => original.value.filter((id) => !selected.value.includes(id)).length)
const dirty = computed(() => addedCount.value > 0 || removedCount.value > 0)

function reset(): void {
  selected.value = [...original.value]
}

async function save(): Promise<void> {
  saving.value = true
  try {
    await bridge.users.setRoles(props.userId, selected.value, original.value)
    original.value = [...selected.value]
    message.success(props.t('actions.manageRolesSuccess'))
    emit('saved')
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    saving.value = false
  }
}
</script>

<style scoped>
.ur-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
  gap: 10px;
}
.ur-role {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  padding: 10px 12px;
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-md, 8px);
  background: var(--tnzi-admin-card-bg, var(--tnzi-container-bg));
  cursor: pointer;
  transition: border-color 0.15s, background 0.15s;
}
.ur-role:hover {
  border-color: var(--tnzi-primary);
}
.ur-role--on {
  border-color: var(--tnzi-primary);
  background: rgb(var(--tnzi-primary-rgb) / 0.05);
}
.ur-role__body {
  flex: 1 1 auto;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.ur-role__name {
  font-size: 13.5px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.ur-role__desc {
  font-size: 12px;
  line-height: 1.45;
  color: var(--tnzi-base-text-muted);
}
.ur-dirty {
  margin-right: auto;
  font-size: 12.5px;
  color: var(--tnzi-warning);
}
</style>

<template>
  <div class="fv-tab">
    <!--
      Scope bar. The picker is built from GET /admin/feature-values/providers,
      not from a hard-coded list: the backend only accepts writes for scopes a
      registered provider reads back, and an inactive scope (Tenant with
      multi-tenancy off) is shown disabled with the server's reason rather than
      offered as a place values silently go to die.
    -->
    <div class="fv-scope">
      <span class="fv-scope__label">{{ t('values.scope') }}</span>
      <NSelect
        :value="providerName"
        :options="providerOptions"
        :loading="providersLoading"
        size="small"
        class="fv-scope__provider"
        @update:value="onProviderChange"
      />
      <template v-if="selectedProvider?.requiresKey">
        <NInput
          v-model:value="providerKey"
          size="small"
          clearable
          class="fv-scope__key"
          :placeholder="t('values.keyPlaceholder', { provider: selectedProvider.name })"
          @keydown.enter="applyScope"
          @clear="applyScope"
        />
        <NButton size="small" type="primary" :disabled="!providerKey.trim()" @click="applyScope">
          {{ t('values.apply') }}
        </NButton>
      </template>
      <NText v-if="selectedProvider && !selectedProvider.isActive" depth="3" class="fv-scope__inactive">
        <TSvgIcon icon="mdi:alert-circle-outline" :size="14" />
        {{ t('values.inactiveHint', { reason: selectedProvider.inactiveReason ?? '' }) }}
      </NText>
      <NText v-else-if="selectedProvider?.requiresKey && !appliedScope" depth="3" class="fv-scope__hint">
        {{ t('values.keyHint') }}
      </NText>
    </div>

    <TCrudPage
      :state="crud"
      :all-columns="featureValueColumns"
      :show-header="false"
      :row-actions="rowActions"
      :translate="t"
      :form-modal-width="560"
    >
      <template #form="{ formData }">
        <div v-if="formData" class="fv-form-context">
          <TDescriptions :items="formContext(formData as FeatureValueRow)" :max-columns="2" />
        </div>
        <TFormSchemaRenderer
          :schema="featureValueFormSchema"
          :model="(formData ?? {}) as Record<string, unknown>"
          :translate="t"
        />
      </template>
    </TCrudPage>
  </div>
</template>

<script setup lang="ts">
/**
 * Feature values - what every flag resolves to for ONE scope.
 *
 * The list is the runtime's answer, not the stored rows: each line shows the
 * effective value and where it comes from (this scope's own value, inherited
 * from the deployment-wide Global scope, or the definition default), so what an
 * operator reads here is what `IFeatureChecker` will return.
 *
 * Two row actions and nothing else:
 *   set   - opens the typed editor (switch / number / text by value type) and
 *           posts the scope's value. Hidden for code-defined definitions, which
 *           carry no database id and cannot hold a value.
 *   clear - deletes the scope's own row so the flag falls back to inherited /
 *           default. Only offered when the scope actually holds a row.
 *
 * ★ A refusal from the backend (unknown / inactive provider, wrong key shape,
 *   code-defined definition) is the feature, not noise: the bridge throws with
 *   the server's message and `useCrudPage` surfaces it as-is.
 */
import { computed, onMounted, ref } from 'vue'
import { NButton, NInput, NSelect, NText } from 'naive-ui'
import { TDescriptions, TSvgIcon } from '@tnzi/ui'
import TCrudPage from '../../../components/crud/TCrudPage.vue'
import TFormSchemaRenderer from '../../_shared/form-schema'
import { useCrudPage, type CrudPageQuery } from '../../../headless/useCrudPage'
import { usePermissionGuard } from '../../../headless/usePermissionGuard'
import { editAction, type RowAction } from '../../../headless/row-actions'
import {
  createFeatureBridge,
  type FeatureValueProviderDto,
  type FeatureValueScope,
} from '../../../services/bridges/feature-bridge'
import { useAdminClient } from '../../../plugin/client'
import { EMPTY_DASH } from '../../../utils/placeholders'
import { makePageTranslator } from '../../_shared/translate'
import { useSafeMessage } from '../../_shared/safe-message'
import { pagedResult } from '../../../services/_mappers'
import {
  featureValueColumns,
  featureValueFormSchema,
  toFormValue,
  toWireValue,
  type FeatureValueRow,
} from './feature-values-config'

const bridge = createFeatureBridge({ client: useAdminClient() })
const t = makePageTranslator('system.features')
const message = useSafeMessage()
const { can } = usePermissionGuard()

// ---- Scope -----------------------------------------------------------------
const providers = ref<FeatureValueProviderDto[]>([])
const providersLoading = ref(false)
const providerName = ref<string | null>(null)
const providerKey = ref('')
/** The scope the list is currently showing; null until a keyed scope has a key. */
const appliedScope = ref<FeatureValueScope | null>(null)

const selectedProvider = computed(() => providers.value.find((p) => p.name === providerName.value) ?? null)

const providerOptions = computed(() =>
  providers.value.map((p) => ({
    label: p.isActive ? p.name : t('values.providerInactive', { name: p.name }),
    value: p.name,
    // Disabled, not hidden: an operator must be able to see that the scope
    // exists and read why it cannot be written to.
    disabled: !p.isActive,
  })),
)

function resolveScope(): FeatureValueScope | null {
  const p = selectedProvider.value
  if (!p) return null
  if (!p.requiresKey) return { providerName: p.name, providerKey: null }
  const key = providerKey.value.trim()
  return key ? { providerName: p.name, providerKey: key } : null
}

async function applyScope() {
  appliedScope.value = resolveScope()
  await crud.refresh()
}

async function onProviderChange(name: string) {
  providerName.value = name
  providerKey.value = ''
  await applyScope()
}

async function loadProviders() {
  providersLoading.value = true
  try {
    providers.value = await bridge.values.providers()
    // Land on the first scope that can actually be written to; the keyless one
    // (Global) comes first among those because it needs no key to show a list.
    const active = providers.value.filter((p) => p.isActive)
    const first = active.find((p) => !p.requiresKey) ?? active[0] ?? providers.value[0]
    providerName.value = first?.name ?? null
    await applyScope()
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    providersLoading.value = false
  }
}

// ---- List ------------------------------------------------------------------
const emptyPage = (q: CrudPageQuery) =>
  pagedResult<FeatureValueRow>({ items: [], totalCount: 0, pageIndex: q.pageIndex, pageSize: q.pageSize })

const crud = useCrudPage<FeatureValueRow, string>({
  pageId: 'system.featureValues',
  permission: 'feature',
  columns: featureValueColumns,
  // featureName is unique across the merged catalogue; ids are not (every
  // code-defined row and every unset row carries the all-zero id).
  rowKey: (r) => r.featureName,
  // Own query key for the open editor. All three tabs stay mounted on one route,
  // and two list shells sharing the default `detail` key fight over it: this
  // tab pushes `?detail=edit:<flag>`, the definitions tab cannot resolve that
  // record, drops the key, and this editor closes the instant it opens.
  detailUrl: 'value',
  autoLoad: false,
  fetchData: async (q) => {
    const scope = appliedScope.value
    if (!scope) return emptyPage(q)
    const page = await bridge.values.all(scope, q)
    return { ...page, items: page.items.map((r) => ({ ...r, value: toFormValue(r) })) }
  },
  // "update" = set the scope's value. The form model is the row plus the typed
  // `value`; convert it back to the wire string by the definition's type.
  updateData: async (_id, data) => {
    const scope = appliedScope.value
    if (!scope) throw new Error(t('values.noScope'))
    const row = data as FeatureValueRow
    await bridge.values.set({
      featureDefinitionId: row.featureDefinitionId,
      providerName: scope.providerName,
      providerKey: scope.providerKey ?? null,
      value: toWireValue(row.value, row.valueType),
    })
    return row
  },
})

async function clearOverride(row: FeatureValueRow) {
  try {
    await bridge.values.clear(row.id)
    message.success(t('values.cleared'))
    await crud.refresh()
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  }
}

const rowActions: RowAction<FeatureValueRow>[] = [
  editAction(crud, {
    label: 'values.actions.set',
    // Code-defined definitions have no database id - the backend answers 400
    // FEATURE_DEFINITION_NOT_OVERRIDABLE, so do not offer the button.
    show: (row) => row.canOverride,
  }),
  {
    key: 'clear',
    label: 'values.actions.clear',
    type: 'warning',
    confirm: 'values.actions.clearConfirm',
    show: (row) => row.isExplicitlySet && can('feature.delete'),
    onClick: (row) => void clearOverride(row),
  },
]

function formContext(row: FeatureValueRow) {
  return [
    { label: t('values.columns.feature'), value: row.displayName || row.featureName },
    { label: t('values.columns.valueType'), value: row.valueType },
    { label: t('values.columns.defaultValue'), value: row.defaultValue || EMPTY_DASH },
    {
      label: t('values.columns.effectiveValue'),
      value: row.effectiveProvider
        ? `${row.effectiveValue || EMPTY_DASH} (${row.effectiveProvider})`
        : row.effectiveValue || EMPTY_DASH,
    },
  ]
}

onMounted(() => {
  void loadProviders()
})

// The list state is the only seam a host (or a test) needs: drive a set / clear
// programmatically without re-implementing the wire conversion above.
defineExpose({ crud })
</script>

<style scoped>
.fv-tab {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-height: 0;
  flex: 1 1 auto;
}
.fv-scope {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.fv-scope__label {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.fv-scope__provider {
  width: 180px;
}
.fv-scope__key {
  width: 260px;
}
.fv-scope__inactive,
.fv-scope__hint {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 12px;
}
.fv-form-context {
  margin-bottom: 12px;
}
:deep(.fv-name) {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
}
:deep(.fv-name__code) {
  color: var(--tnzi-base-text-muted);
}
:deep(.fv-effective) {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}
:deep(.fv-effective__from) {
  color: var(--tnzi-base-text-muted);
}
</style>

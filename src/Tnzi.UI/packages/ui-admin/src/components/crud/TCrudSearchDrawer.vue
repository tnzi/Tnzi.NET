<template>
  <!-- TOverlayTheme: this drawer renders inside the content area (TListShell),
       so without the reset it would inherit the content's inner dark "Card /
       List" theme through the Teleport and open dark under global light mode
       (overlays are chrome - they follow the GLOBAL mode). -->
  <TOverlayTheme>
    <NDrawer
      :show="show"
      :width="width"
      placement="right"
      @update:show="(v: boolean) => emit('update:show', v)"
    >
      <NDrawerContent :title="t('admin.crud.advancedSearch')" closable>
        <TCrudSearchAdvanced
          ref="advRef"
          :state="state"
          :search-fields="searchFields"
          :translate="translate"
          hide-submit
          labeled
        />
        <template #footer>
          <div class="t-crud-search-drawer__footer">
            <NButton size="small" @click="onReset">{{ t('admin.crud.reset') }}</NButton>
            <NButton size="small" type="primary" @click="onSearch">{{ t('admin.crud.search') }}</NButton>
          </div>
        </template>
      </NDrawerContent>
    </NDrawer>
  </TOverlayTheme>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { NDrawer, NDrawerContent, NButton } from 'naive-ui'
import { TOverlayTheme } from '@tnzi/ui'
import TCrudSearchAdvanced from './TCrudSearchAdvanced.vue'
import type { SearchableState } from './TCrudSearch.vue'
import type { FormSchemaItem } from '@tnzi/ui'

interface Props {
  show: boolean
  state: SearchableState
  searchFields?: FormSchemaItem[]
  width?: number
  translate?: (key: string) => string
}
const props = withDefaults(defineProps<Props>(), {
  searchFields: undefined,
  width: 400,
  translate: undefined,
})
const emit = defineEmits<{ 'update:show': [value: boolean] }>()

function t(key: string): string {
  return props.translate ? props.translate(key) : key
}

const advRef = ref<{ apply: () => void; reset: () => void } | null>(null)
function onSearch(): void {
  advRef.value?.apply()
  emit('update:show', false)
}
function onReset(): void {
  advRef.value?.reset()
}
</script>

<style scoped>
.t-crud-search-drawer__footer { display: flex; justify-content: flex-end; gap: 8px; }
</style>

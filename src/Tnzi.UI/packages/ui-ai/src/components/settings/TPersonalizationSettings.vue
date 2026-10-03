<script setup lang="ts">
/**
 * @experimental
 * TPersonalizationSettings - what the assistant should know about the user
 * before the first message of every conversation.
 *
 * Built-in and wired: unlike the resource views (Critical Rule #12), the data
 * here is the framework's own (`GET/PUT /user-profile` in `Tnzi.AI`) and the
 * route is user-facing, so there is nothing for a consumer to supply but the
 * client. It renders only when one was given.
 *
 * The free-text box is the substance - the three short fields above it exist
 * because "call me X", "I do Y" and "answer in Z" are what everyone writes
 * first, and pinning them to their own fields keeps them out of the prose
 * where they would be re-stated in every deployment's own words.
 */
import { computed, onMounted } from 'vue'
import { NInput, NButton } from 'naive-ui'
import TSettingGroup from '../layout/TSettingGroup.vue'
import TSettingRow from '../layout/TSettingRow.vue'
import type { UseAiPersonalizationReturn } from '../../headless/useAiPersonalization'
import { useAiI18n } from '../../i18n'

/* No group title: the dialog already renders the section label as the pane
   heading, and a group called "Personalization" under a pane called
   "Personalization" is the same word twice. Pages with more than one group
   (Account, Security) title theirs because there the titles carry the split. */
const props = defineProps<{
  controller: UseAiPersonalizationReturn
}>()

const t = useAiI18n()

// The editable draft, lifted out of the controller so the template writes to a
// local binding. `vue/no-mutating-props` cannot tell "writing through a
// controller's ref" from "reassigning a prop" and flags the former; a computed
// keeps it reactive if the controller instance is ever swapped.
const draft = computed(() => props.controller.draft.value)

onMounted(() => {
  void props.controller.load()
})
</script>

<template>
  <TSettingGroup :separator="false">
    <TSettingRow
      :label="t.personalizationSettings.name"
      :description="t.personalizationSettings.nameHint"
    >
      <NInput
        v-model:value="draft.displayName"
        class="t-settings-field__control"
        size="small"
        :maxlength="64"
        :placeholder="t.personalizationSettings.namePlaceholder"
      />
    </TSettingRow>

    <TSettingRow :label="t.personalizationSettings.role" :description="t.personalizationSettings.roleHint">
      <NInput
        v-model:value="draft.role"
        class="t-settings-field__control"
        size="small"
        :maxlength="120"
        :placeholder="t.personalizationSettings.rolePlaceholder"
      />
    </TSettingRow>

    <TSettingRow
      :label="t.personalizationSettings.language"
      :description="t.personalizationSettings.languageHint"
    >
      <NInput
        v-model:value="draft.preferredLanguage"
        class="t-settings-field__control"
        size="small"
        :maxlength="40"
        :placeholder="t.personalizationSettings.languagePlaceholder"
      />
    </TSettingRow>

    <TSettingRow
      :label="t.personalizationSettings.content"
      :description="t.personalizationSettings.contentHint"
      stacked
    >
      <NInput
        v-model:value="draft.content"
        type="textarea"
        :autosize="{ minRows: 5, maxRows: 14 }"
        :placeholder="t.personalizationSettings.contentPlaceholder"
      />
    </TSettingRow>

    <!-- Errors sit with the action that produced them. Reads are fail-safe and
         say nothing; only a failed save has something to report. -->
    <p v-if="controller.error.value" class="t-settings-field__error" role="alert">
      {{ controller.error.value }}
    </p>

    <div class="t-settings-field__actions">
      <NButton
        size="small"
        :disabled="!controller.dirty.value || controller.saving.value"
        @click="controller.reset()"
      >
        {{ t.personalizationSettings.reset }}
      </NButton>
      <NButton
        size="small"
        type="primary"
        :loading="controller.saving.value"
        :disabled="!controller.dirty.value"
        @click="controller.save()"
      >
        {{ t.personalizationSettings.save }}
      </NButton>
    </div>
  </TSettingGroup>
</template>

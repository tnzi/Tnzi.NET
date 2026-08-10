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

/* No group title: the dialog already renders the section label as the pane
   heading, and a group called "Personalization" under a pane called
   "Personalization" is the same word twice. Pages with more than one group
   (Account, Security) title theirs because there the titles carry the split. */
const props = defineProps<{
  controller: UseAiPersonalizationReturn
}>()

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
      label="What should the assistant call you?"
      description="Used when it addresses you directly."
    >
      <NInput
        v-model:value="draft.displayName"
        class="t-settings-field__control"
        size="small"
        :maxlength="64"
        placeholder="Your preferred name"
      />
    </TSettingRow>

    <TSettingRow label="What do you do?" description="Helps it pitch answers at the right level.">
      <NInput
        v-model:value="draft.role"
        class="t-settings-field__control"
        size="small"
        :maxlength="120"
        placeholder="e.g. Backend engineer"
      />
    </TSettingRow>

    <TSettingRow
      label="Preferred language"
      description="What it should answer in unless you ask otherwise."
    >
      <NInput
        v-model:value="draft.preferredLanguage"
        class="t-settings-field__control"
        size="small"
        :maxlength="40"
        placeholder="e.g. English"
      />
    </TSettingRow>

    <TSettingRow
      label="Anything else it should know"
      description="Applied to every new conversation."
      stacked
    >
      <NInput
        v-model:value="draft.content"
        type="textarea"
        :autosize="{ minRows: 5, maxRows: 14 }"
        placeholder="Preferences, context about your work, how you like answers structured…"
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
        Reset
      </NButton>
      <NButton
        size="small"
        type="primary"
        :loading="controller.saving.value"
        :disabled="!controller.dirty.value"
        @click="controller.save()"
      >
        Save
      </NButton>
    </div>
  </TSettingGroup>
</template>

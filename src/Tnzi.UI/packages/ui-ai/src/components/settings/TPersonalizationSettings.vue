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
import { computed, onMounted, ref } from 'vue'
import TSettingGroup from '../layout/TSettingGroup.vue'
import TSettingRow from '../layout/TSettingRow.vue'
import TButton from '../controls/TButton.vue'
import { useAutoGrowTextarea } from '../../headless/useAutoGrowTextarea'
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

/* The free-text box grows with what is written, from five rows to about
   fourteen, then scrolls. */
const CONTENT_MAX_HEIGHT = 290
const contentBox = ref<HTMLTextAreaElement | null>(null)
useAutoGrowTextarea(contentBox, computed(() => draft.value.content), CONTENT_MAX_HEIGHT)
</script>

<template>
  <TSettingGroup :separator="false">
    <TSettingRow
      :label="t.personalizationSettings.name"
      :description="t.personalizationSettings.nameHint"
    >
      <input
        v-model="draft.displayName"
        class="t-input t-settings-field__control"
        type="text"
        maxlength="64"
        :placeholder="t.personalizationSettings.namePlaceholder"
        :aria-label="t.personalizationSettings.name"
      />
    </TSettingRow>

    <TSettingRow :label="t.personalizationSettings.role" :description="t.personalizationSettings.roleHint">
      <input
        v-model="draft.role"
        class="t-input t-settings-field__control"
        type="text"
        maxlength="120"
        :placeholder="t.personalizationSettings.rolePlaceholder"
        :aria-label="t.personalizationSettings.role"
      />
    </TSettingRow>

    <TSettingRow
      :label="t.personalizationSettings.language"
      :description="t.personalizationSettings.languageHint"
    >
      <input
        v-model="draft.preferredLanguage"
        class="t-input t-settings-field__control"
        type="text"
        maxlength="40"
        :placeholder="t.personalizationSettings.languagePlaceholder"
        :aria-label="t.personalizationSettings.language"
      />
    </TSettingRow>

    <TSettingRow
      :label="t.personalizationSettings.content"
      :description="t.personalizationSettings.contentHint"
      stacked
    >
      <textarea
        ref="contentBox"
        v-model="draft.content"
        class="t-input t-personalization__content"
        rows="5"
        :placeholder="t.personalizationSettings.contentPlaceholder"
        :aria-label="t.personalizationSettings.content"
      />
    </TSettingRow>

    <!-- Errors sit with the action that produced them. Reads are fail-safe and
         say nothing; only a failed save has something to report. -->
    <p v-if="controller.error.value" class="t-settings-field__error" role="alert">
      {{ controller.error.value }}
    </p>

    <div class="t-settings-field__actions">
      <TButton
        :disabled="!controller.dirty.value || controller.saving.value"
        @click="controller.reset()"
      >
        {{ t.personalizationSettings.reset }}
      </TButton>
      <TButton
        variant="primary"
        :loading="controller.saving.value"
        :disabled="!controller.dirty.value"
        @click="controller.save()"
      >
        {{ t.personalizationSettings.save }}
      </TButton>
    </div>
  </TSettingGroup>
</template>

<style scoped>
.t-personalization__content {
  display: block;
  resize: none;
}
</style>

<script setup lang="ts">
/**
 * The super-admin appearance pane, for the settings dialog.
 *
 * ## Why this sits in the settings dialog rather than an admin console
 *
 * The person who decides what this product looks like is looking AT the product
 * while they decide. Sending them to a separate console to pick a colour and
 * then back here to see it is the wrong loop - so the controls live where the
 * result is visible, and every edit previews live on the real surface.
 *
 * ## Preview vs publish
 *
 * Editing applies immediately to THIS browser only. `Publish` is what writes the
 * snapshot server-side and reaches everyone else. The distinction is stated in
 * the UI because "I changed a colour and it looked fine" must not be mistaken
 * for "everyone now sees this".
 *
 * `Reset` clears the server-side snapshot, so every client falls back to the
 * stylesheet defaults - it is not "undo my edits".
 */
import { computed, ref, watch } from 'vue';
import TSettingRow from './TSettingRow.vue';
import TSettingGroup from './TSettingGroup.vue';
import type { AiThemeSnapshot } from '../../theme/snapshot';
import { buildAiThemeSnapshot, applyAiThemeSnapshot } from '../../theme/snapshot';
import type { UseGlobalAiThemeReturn } from '../../headless/useGlobalAiTheme';

const props = withDefaults(
  defineProps<{
    /** The controller from `useGlobalAiTheme()`. */
    theme: UseGlobalAiThemeReturn;
    /** `(key, fallback?) => string`. Falls back to the English copy below. */
    translate?: (key: string, fallback?: string) => string;
  }>(),
  { translate: undefined },
);

const t = (key: string, fallback: string) =>
  props.translate ? props.translate(key, fallback) : fallback;

/**
 * Which tokens an operator gets. Deliberately NOT the full 40-key map: a colour
 * picker per token is a paint program, not a settings pane. These are the ones
 * that change the product's character; everything else follows from them or is
 * better left to the designed defaults. `applyThemeVars` remains the escape
 * hatch for a consumer that genuinely needs a specific token.
 */
const COLOR_FIELDS = [
  { key: 'bg', labelKey: 'appearance.token.bg', label: 'Canvas' },
  { key: 'sidebarBg', labelKey: 'appearance.token.sidebarBg', label: 'Sidebar' },
  { key: 'surface', labelKey: 'appearance.token.surface', label: 'Cards & composer' },
  { key: 'text', labelKey: 'appearance.token.text', label: 'Text' },
] as const;

const primary = ref('');
const colors = ref<Record<string, string>>({});
const modalRadius = ref('');
const composerRadius = ref('');

/** Seed the editor from the controller's draft (server value on first load). */
function seedFrom(snapshot: AiThemeSnapshot | null): void {
  primary.value = snapshot?.ui?.primary ?? '';
  colors.value = Object.fromEntries(
    COLOR_FIELDS.map((f) => [f.key, (snapshot?.ai as Record<string, string> | undefined)?.[f.key] ?? '']),
  );
  modalRadius.value = snapshot?.ai?.modalRadius ?? '';
  composerRadius.value = snapshot?.ai?.composerRadius ?? '';
}

seedFrom(props.theme.draft.value);
watch(() => props.theme.remote.value, (next) => seedFrom(next));

/**
 * Empty string means "no override" and must be OMITTED, not written as ''.
 * An empty CSS value silently wins over the stylesheet and blanks the token.
 */
function currentSnapshot(): AiThemeSnapshot {
  const ai: Record<string, string> = {};
  for (const f of COLOR_FIELDS) {
    const value = colors.value[f.key]?.trim();
    if (value) ai[f.key] = value;
  }
  if (modalRadius.value.trim()) ai.modalRadius = modalRadius.value.trim();
  if (composerRadius.value.trim()) ai.composerRadius = composerRadius.value.trim();

  return buildAiThemeSnapshot({
    ai,
    primary: primary.value.trim() || undefined,
    mode: props.theme.draft.value?.mode,
  });
}

/** Every edit previews locally so the operator judges the real surface. */
function onEdit(): void {
  const snapshot = currentSnapshot();
  props.theme.setDraft(snapshot);
  applyAiThemeSnapshot(snapshot);
}

async function onPublish(): Promise<void> {
  props.theme.setDraft(currentSnapshot());
  await props.theme.save();
}

async function onReset(): Promise<void> {
  const ok = await props.theme.reset();
  if (ok) seedFrom(null);
}

const busy = computed(() => props.theme.saving.value || props.theme.loading.value);
const canManage = computed(() => props.theme.canManage.value);
</script>

<template>
  <div class="t-appearance-admin">
    <p v-if="!canManage" class="t-appearance-admin__readonly">
      {{
        t(
          'appearance.readOnly',
          'The appearance of this product is managed by an administrator.',
        )
      }}
    </p>

    <template v-else>
      <TSettingGroup :title="t('appearance.group.brand', 'Brand')">
        <TSettingRow
          :label="t('appearance.primary', 'Brand colour')"
          :description="
            t(
              'appearance.primaryHint',
              'Shared with every product on this deployment. The accent used for send buttons and focus rings follows it.',
            )
          "
        >
          <input
            v-model="primary"
            class="t-appearance-admin__color"
            type="color"
            :disabled="busy"
            @change="onEdit"
          />
        </TSettingRow>
      </TSettingGroup>

      <TSettingGroup :title="t('appearance.group.surface', 'Surfaces')">
        <TSettingRow
          v-for="f in COLOR_FIELDS"
          :key="f.key"
          :label="t(f.labelKey, f.label)"
        >
          <input
            v-model="colors[f.key]"
            class="t-appearance-admin__color"
            type="color"
            :disabled="busy"
            @change="onEdit"
          />
        </TSettingRow>
      </TSettingGroup>

      <TSettingGroup :title="t('appearance.group.shape', 'Shape')">
        <TSettingRow :label="t('appearance.modalRadius', 'Dialog corner radius')">
          <input
            v-model="modalRadius"
            class="t-appearance-admin__text"
            type="text"
            placeholder="20px"
            :disabled="busy"
            @change="onEdit"
          />
        </TSettingRow>
        <TSettingRow :label="t('appearance.composerRadius', 'Composer corner radius')">
          <input
            v-model="composerRadius"
            class="t-appearance-admin__text"
            type="text"
            placeholder="22px"
            :disabled="busy"
            @change="onEdit"
          />
        </TSettingRow>
      </TSettingGroup>

      <p class="t-appearance-admin__scope">
        {{
          t(
            'appearance.publishHint',
            'Changes preview here only. Publish to apply them for everyone.',
          )
        }}
      </p>

      <p v-if="theme.error.value" class="t-appearance-admin__error" role="alert">
        {{ theme.error.value }}
      </p>

      <div class="t-appearance-admin__actions">
        <button type="button" class="t-appearance-admin__reset" :disabled="busy" @click="onReset">
          {{ t('appearance.reset', 'Reset for everyone') }}
        </button>
        <button
          type="button"
          class="t-appearance-admin__publish"
          :disabled="busy || !theme.isDirty.value"
          @click="onPublish"
        >
          {{
            theme.isDirty.value
              ? t('appearance.publish', 'Publish to everyone')
              : t('appearance.published', 'Published')
          }}
        </button>
      </div>
    </template>
  </div>
</template>

<style scoped>
.t-appearance-admin {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.t-appearance-admin__readonly,
.t-appearance-admin__scope {
  margin: 0;
  font-size: 13px;
  line-height: 18px;
  color: var(--tnzi-ai-text-secondary);
}

.t-appearance-admin__scope {
  margin-top: 8px;
}

.t-appearance-admin__error {
  margin: 0;
  font-size: 13px;
  color: var(--tnzi-ai-danger);
}

.t-appearance-admin__color {
  width: 44px;
  height: 28px;
  padding: 0;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 6px;
  background: var(--tnzi-ai-surface);
  cursor: pointer;
}

.t-appearance-admin__text {
  width: 96px;
  height: 28px;
  padding: 0 8px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 6px;
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  font-size: 13px;
  font-family: inherit;
  outline: none;
}

.t-appearance-admin__text:focus {
  border-color: var(--tnzi-ai-accent);
}

.t-appearance-admin__actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 8px;
}

.t-appearance-admin__reset,
.t-appearance-admin__publish {
  height: 32px;
  padding: 0 14px;
  border-radius: 8px;
  font-size: 13px;
  font-weight: 500;
  font-family: inherit;
  cursor: pointer;
  transition: opacity var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}

.t-appearance-admin__reset {
  border: 1px solid var(--tnzi-ai-border);
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text-secondary);
}

.t-appearance-admin__publish {
  border: none;
  background: var(--tnzi-ai-accent);
  color: var(--tnzi-ai-on-accent);
}

.t-appearance-admin__reset:disabled,
.t-appearance-admin__publish:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.t-appearance-admin__reset:hover:not(:disabled),
.t-appearance-admin__publish:hover:not(:disabled) {
  opacity: 0.85;
}
</style>

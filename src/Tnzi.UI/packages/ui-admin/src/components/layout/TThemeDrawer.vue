<template>
  <NDrawer
    :show="show"
    :width="320"
    placement="right"
    @update:show="(v: boolean) => emit('update:show', v)"
  >
    <NDrawerContent :title="translate('admin.theme.title')" closable>
      <!-- Section 1: Primary color -->
      <section class="t-theme-drawer__section">
        <h4 class="t-theme-drawer__section-title">{{ translate('admin.theme.primaryColor') }}</h4>
        <NColorPicker
          :value="ctx.settings.value.colors.primary"
          @update:value="onColorChange"
        />
      </section>

      <!-- Section 2: Layout mode -->
      <section class="t-theme-drawer__section">
        <h4 class="t-theme-drawer__section-title">{{ translate('admin.theme.layoutMode') }}</h4>
        <NRadioGroup
          :value="themeStore.layoutMode"
          @update:value="(m: AdminLayoutMode) => setLayoutMode(m)"
        >
          <NRadio value="vertical">{{ translate('admin.theme.layout.vertical') }}</NRadio>
          <NRadio value="vertical-mix">{{ translate('admin.theme.layout.verticalMix') }}</NRadio>
          <NRadio value="horizontal">{{ translate('admin.theme.layout.horizontal') }}</NRadio>
        </NRadioGroup>
      </section>

      <!-- Section 3: Dark mode -->
      <section class="t-theme-drawer__section">
        <h4 class="t-theme-drawer__section-title">{{ translate('admin.theme.darkMode') }}</h4>
        <NSwitch :value="ctx.isDark.value" @update:value="setDarkMode" />
      </section>

      <!-- Section 4: Presets -->
      <section class="t-theme-drawer__section">
        <h4 class="t-theme-drawer__section-title">{{ translate('admin.theme.presets') }}</h4>
        <div class="t-theme-drawer__presets">
          <NButton
            v-for="preset in resolvedPresets"
            :key="preset.name"
            class="t-theme-drawer__preset"
            @click="applyPreset(preset)"
          >
            <span
              class="t-theme-drawer__preset-swatch"
              :style="{ backgroundColor: preset.primary }"
            />
            {{ preset.name }}
          </NButton>
        </div>
      </section>

      <!-- Section 5: Reset -->
      <section class="t-theme-drawer__section">
        <button
          type="button"
          class="t-theme-drawer__reset"
          :aria-label="translate('admin.theme.reset')"
          @click="resetAll"
        >
          {{ translate('admin.theme.reset') }}
        </button>
      </section>
    </NDrawerContent>
  </NDrawer>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import {
  NDrawer,
  NDrawerContent,
  NColorPicker,
  NRadioGroup,
  NRadio,
  NSwitch,
  NButton,
} from 'naive-ui'
import { useTheme, type ThemeContext } from '@tnzi/ui'
import { useAdminThemeStore, type AdminLayoutMode } from '../../stores/useAdminThemeStore'

interface ThemePreset {
  name: string
  primary: string
  layoutMode?: AdminLayoutMode
  mode?: 'light' | 'dark'
}

interface Props {
  show: boolean
  themeContext?: ThemeContext
  presets?: ThemePreset[]
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  themeContext: undefined,
  presets: undefined,
  translate: undefined,
})

const emit = defineEmits<{
  'update:show': [value: boolean]
}>()

const DEFAULT_PRESETS: ThemePreset[] = [
  { name: 'Blue', primary: '#2080f0' },
  { name: 'Green', primary: '#18a058' },
  { name: 'Red', primary: '#d03050' },
  { name: 'Gold', primary: '#f0a020' },
]

const ctx = useTheme(props.themeContext)
const themeStore = useAdminThemeStore()

const resolvedPresets = computed<ThemePreset[]>(() => props.presets ?? DEFAULT_PRESETS)

function translate(key: string): string {
  return props.translate ? props.translate(key) : key
}

function onColorChange(value: string): void {
  ctx.setColor('primary', value)
}

function setLayoutMode(mode: AdminLayoutMode): void {
  themeStore.setLayoutMode(mode)
}

function setDarkMode(value: boolean): void {
  ctx.setMode(value ? 'dark' : 'light')
}

function applyPreset(preset: ThemePreset): void {
  ctx.setColor('primary', preset.primary)
  if (preset.layoutMode) {
    themeStore.setLayoutMode(preset.layoutMode)
  }
  if (preset.mode) {
    ctx.setMode(preset.mode)
  }
}

function resetAll(): void {
  ctx.reset()
  themeStore.reset()
}

function close(): void {
  emit('update:show', false)
}

defineExpose({ setLayoutMode, setDarkMode, applyPreset, close })
</script>

<style scoped>
.t-theme-drawer__section {
  padding: var(--tnzi-space-md, 16px) 0;
  border-bottom: 1px solid var(--tnzi-border-color, #e5e7eb);
}

.t-theme-drawer__section:last-child {
  border-bottom: none;
}

.t-theme-drawer__section-title {
  margin: 0 0 var(--tnzi-space-sm, 8px) 0;
  font-size: var(--tnzi-font-size-sm, 13px);
  font-weight: 600;
  color: var(--tnzi-text-color-2, #4b5563);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.t-theme-drawer__presets {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: var(--tnzi-space-sm, 8px);
}

.t-theme-drawer__preset {
  display: inline-flex;
  align-items: center;
  gap: var(--tnzi-space-xs, 6px);
}

.t-theme-drawer__preset-swatch {
  display: inline-block;
  width: 14px;
  height: 14px;
  border-radius: 50%;
  border: 1px solid var(--tnzi-border-color, #e5e7eb);
}

.t-theme-drawer__reset {
  width: 100%;
  padding: var(--tnzi-space-sm, 8px) var(--tnzi-space-md, 16px);
  background: transparent;
  color: var(--tnzi-error-color, #d03050);
  border: 1px solid var(--tnzi-error-color, #d03050);
  border-radius: var(--tnzi-radius-md, 6px);
  font-size: var(--tnzi-font-size-md, 14px);
  cursor: pointer;
  transition: background 0.15s ease;
}

.t-theme-drawer__reset:hover {
  background: var(--tnzi-error-color-hover, rgba(208, 48, 80, 0.1));
}
</style>

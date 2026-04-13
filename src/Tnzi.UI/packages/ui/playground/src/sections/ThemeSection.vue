<template>
  <div class="section-page">
    <div class="section-header">
      <h1 class="section-title">Theme Configuration</h1>
      <p class="section-desc">Customize colors, typography, spacing, and apply theme presets in real time.</p>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Mode</h2>
      <PreviewBox :no-badge>
        <n-button-group>
          <n-button
            v-for="m in modes"
            :key="m"
            :type="themeConfig.mode === m ? 'primary' : 'default'"
            @click="setMode(m)"
          >
            {{ m }}
          </n-button>
        </n-button-group>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Colors</h2>
      <PreviewBox :no-badge :full-width>
        <n-grid :cols="3" :x-gap="24" :y-gap="16">
          <n-gi v-for="(label, key) in colorLabels" :key="key">
            <div class="color-field">
              <n-text depth="3" class="color-label">{{ label }}</n-text>
              <n-color-picker
                :value="themeConfig.colors[key]"
                :show-alpha="false"
                :modes="['hex']"
                size="small"
                @update:value="(v: string) => (themeConfig.colors[key] = v)"
              />
            </div>
          </n-gi>
        </n-grid>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Layout</h2>
      <PreviewBox :no-badge :full-width>
        <n-grid :cols="2" :x-gap="24" :y-gap="16">
          <n-gi>
            <n-space vertical :size="4">
              <n-text>Border Radius: {{ themeConfig.borderRadius }}px</n-text>
              <n-slider v-model:value="themeConfig.borderRadius" :min="0" :max="24" :step="1" />
            </n-space>
          </n-gi>
          <n-gi>
            <n-space vertical :size="4">
              <n-text>Font Size: {{ themeConfig.fontSize }}px</n-text>
              <n-slider v-model:value="themeConfig.fontSize" :min="10" :max="24" :step="1" />
            </n-space>
          </n-gi>
          <n-gi>
            <n-space vertical :size="4">
              <n-text>Spacing: {{ themeConfig.spacing }}px</n-text>
              <n-slider v-model:value="themeConfig.spacing" :min="2" :max="16" :step="1" />
            </n-space>
          </n-gi>
          <n-gi>
            <n-space vertical :size="4">
              <n-text>Font Family</n-text>
              <n-select
                v-model:value="themeConfig.fontFamily"
                :options="fontFamilyOptions"
                size="small"
              />
            </n-space>
          </n-gi>
        </n-grid>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Presets</h2>
      <PreviewBox :no-badge>
        <n-space>
          <n-button
            v-for="preset in themePresets"
            :key="preset.key"
            size="small"
            @click="setThemeConfig(preset.config)"
          >
            {{ preset.name }}
          </n-button>
          <n-button size="small" type="warning" @click="reset">Reset</n-button>
        </n-space>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Preview</h2>
      <PreviewBox>
        <div>
          <n-space>
            <n-button type="primary">Primary</n-button>
            <n-button type="success">Success</n-button>
            <n-button type="warning">Warning</n-button>
            <n-button type="error">Danger</n-button>
            <n-button type="info">Info</n-button>
          </n-space>
          <div style="margin-top: 12px">
            <n-input placeholder="Input preview" style="max-width: 300px" />
          </div>
        </div>
      </PreviewBox>
    </div>
  </div>
</template>

<script setup lang="ts">
import type { ThemeMode, ThemeColors } from '@tnzi/core/types';
import { themePresets, fontFamilyOptions } from '../data/index';
import PreviewBox from '../components/PreviewBox.vue';
import { usePlaygroundTheme } from '../composables/usePlaygroundTheme';

const { themeConfig, setThemeConfig, setMode, reset } = usePlaygroundTheme();

const modes: ThemeMode[] = ['light', 'dark', 'system'];

const colorLabels: Record<keyof ThemeColors, string> = {
  primary: 'Primary',
  secondary: 'Secondary',
  success: 'Success',
  warning: 'Warning',
  danger: 'Danger',
  info: 'Info',
  background: 'Background',
  surface: 'Surface',
  text: 'Text',
  textSecondary: 'Text Secondary',
  border: 'Border',
  divider: 'Divider',
};
</script>

<style scoped>
.section-page {
  max-width: 960px;
}
.section-header {
  margin-bottom: 32px;
}
.section-title {
  font-size: 32px;
  font-weight: 800;
  color: var(--pg-text, #0f172a);
  margin: 0 0 8px 0;
}
.section-desc {
  font-size: 15px;
  color: var(--pg-text-muted, #64748b);
  margin: 0;
  line-height: 1.6;
}
.demo-block {
  margin-bottom: 32px;
}
.demo-label {
  font-size: 16px;
  font-weight: 600;
  color: var(--pg-text, #0f172a);
  margin: 0 0 12px 0;
}
.color-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.color-label {
  font-size: 12px;
  font-weight: 500;
}
</style>

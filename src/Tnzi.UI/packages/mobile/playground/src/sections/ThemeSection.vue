<script setup lang="ts">
import { ref, computed } from 'vue';
import { showToast } from 'vant';
import type { ThemeColors, ThemeMode } from '@tnzi/core/types/theme';
import { themePresets, fontFamilyOptions } from '../data';
import { usePlaygroundTheme } from '../composables/usePlaygroundTheme';

const { themeConfig, isDark, setColor, applyPreset, resetThemeConfig } = usePlaygroundTheme();

// 主题模式选项
const modeOptions = ['light', 'dark', 'system'] as const;

const setMode = (mode: ThemeMode) => {
  themeConfig.mode = mode;
};

// 颜色编辑列表
const colorEntries = computed(() => {
  const labels: Record<keyof ThemeColors, string> = {
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
  return (Object.keys(labels) as (keyof ThemeColors)[]).map((key) => ({
    key,
    label: labels[key],
    value: themeConfig.colors[key],
  }));
});

// 字体选择
const showFontPicker = ref(false);
const fontColumns = [fontFamilyOptions.map((o) => ({ text: o.label, value: o.value }))];
const onFontConfirm = ({ selectedValues }: { selectedValues: string[] }) => {
  themeConfig.fontFamily = selectedValues[0];
  showFontPicker.value = false;
};
const currentFontLabel = computed(() => {
  const opt = fontFamilyOptions.find((o) => o.value === themeConfig.fontFamily);
  return opt?.label ?? 'System Default';
});

// 预设选择
const onApplyPreset = (key: string) => {
  const preset = themePresets.find((p) => p.key === key);
  if (preset) {
    applyPreset(preset.config);
    showToast(`Applied: ${preset.name}`);
  }
};

const onReset = () => {
  resetThemeConfig();
  showToast('Theme reset');
};
</script>

<template>
  <div class="theme-section">
    <!-- 模式选择 -->
    <van-cell-group inset title="Theme Mode">
      <van-cell
        v-for="mode in modeOptions"
        :key="mode"
        :title="mode.charAt(0).toUpperCase() + mode.slice(1)"
        clickable
        :class="{ 'active-cell': themeConfig.mode === mode }"
        @click="setMode(mode)"
      >
        <template #right-icon>
          <van-tag v-if="themeConfig.mode === mode" type="primary" size="medium">Current</van-tag>
        </template>
      </van-cell>
    </van-cell-group>

    <!-- 预设 -->
    <van-cell-group inset title="Presets">
      <van-grid :column-num="3" :border="false">
        <van-grid-item
          v-for="preset in themePresets"
          :key="preset.key"
          :text="preset.name"
          clickable
          @click="onApplyPreset(preset.key)"
        >
          <template #icon>
            <div
              class="preset-dot"
              :style="{ backgroundColor: preset.config.colors.primary }"
            />
          </template>
        </van-grid-item>
      </van-grid>
    </van-cell-group>

    <!-- 颜色 -->
    <van-cell-group inset title="Colors">
      <van-cell v-for="entry in colorEntries" :key="entry.key" :title="entry.label">
        <template #right-icon>
          <label class="color-picker-wrapper">
            <input
              type="color"
              :value="entry.value"
              class="color-input"
              @input="setColor(entry.key, ($event.target as HTMLInputElement).value)"
            />
            <span class="color-swatch" :style="{ backgroundColor: entry.value }" />
          </label>
        </template>
      </van-cell>
    </van-cell-group>

    <!-- 数值调节 -->
    <van-cell-group inset title="Layout Values">
      <van-cell title="Border Radius" :label="`${themeConfig.borderRadius}px`">
        <template #right-icon>
          <van-stepper v-model="themeConfig.borderRadius" :min="0" :max="24" :step="2" />
        </template>
      </van-cell>
      <van-cell title="Font Size" :label="`${themeConfig.fontSize}px`">
        <template #right-icon>
          <van-stepper v-model="themeConfig.fontSize" :min="12" :max="20" :step="1" />
        </template>
      </van-cell>
      <van-cell title="Spacing" :label="`${themeConfig.spacing}px`">
        <template #right-icon>
          <van-stepper v-model="themeConfig.spacing" :min="4" :max="16" :step="2" />
        </template>
      </van-cell>
    </van-cell-group>

    <!-- 字体 -->
    <van-cell-group inset title="Font Family">
      <van-cell
        :title="currentFontLabel"
        is-link
        @click="showFontPicker = true"
      />
    </van-cell-group>
    <van-popup v-model:show="showFontPicker" round position="bottom">
      <van-picker :columns="fontColumns" @confirm="onFontConfirm" @cancel="showFontPicker = false" />
    </van-popup>

    <!-- 预览 -->
    <van-cell-group inset title="Preview">
      <div class="preview-area">
        <van-space>
          <van-button type="primary" size="small">Primary</van-button>
          <van-button type="success" size="small">Success</van-button>
          <van-button type="warning" size="small">Warning</van-button>
          <van-button type="danger" size="small">Danger</van-button>
        </van-space>
        <van-divider />
        <van-cell title="Preview Cell" value="OK" />
        <van-tag type="primary">Tag</van-tag>
      </div>
    </van-cell-group>

    <!-- 重置 -->
    <div class="action-bar">
      <van-button plain block @click="onReset">Reset to Default</van-button>
    </div>
  </div>
</template>

<style scoped>
.theme-section {
  padding-bottom: 8px;
}

.preset-dot {
  width: 24px;
  height: 24px;
  border-radius: 50%;
  border: 2px solid var(--van-border-color);
}

.color-picker-wrapper {
  position: relative;
  display: inline-flex;
  align-items: center;
  cursor: pointer;
}

.color-input {
  position: absolute;
  opacity: 0;
  width: 32px;
  height: 32px;
  cursor: pointer;
}

.color-swatch {
  display: inline-block;
  width: 32px;
  height: 24px;
  border-radius: 4px;
  border: 1px solid var(--van-border-color);
}

.preview-area {
  padding: 8px 12px;
}

.action-bar {
  padding: 8px 12px;
}

.active-cell {
  background: var(--van-primary-color, #1890ff);
  background: rgba(24, 144, 255, 0.06);
}
</style>

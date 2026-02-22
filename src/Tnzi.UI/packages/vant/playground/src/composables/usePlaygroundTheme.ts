/**
 * usePlaygroundTheme
 *
 * Vant playground 主题管理 composable。
 * 将 ThemeConfig 映射到 Vant van-config-provider 的 themeVars 格式，
 * 同时调用 applyThemeConfigToDOM 让 CSS 变量同步生效。
 */

import { reactive, computed, watch } from 'vue';
import type { ThemeConfig, ThemeColors } from '@tnzi/core/types/theme';
import {
  applyThemeConfigToDOM,
  getDefaultThemeConfig,
  getDefaultColorsForMode,
  resolveThemeMode,
} from '@tnzi/core/playground';

// ============================================
// State
// ============================================

const themeConfig = reactive<ThemeConfig>(getDefaultThemeConfig());

// ============================================
// Vant Theme Vars Mapping
// ============================================

/**
 * 将 ThemeConfig 映射为 Vant ConfigProvider themeVars 格式。
 */
const vantThemeVars = computed(() => ({
  // 语义色
  primaryColor: themeConfig.colors.primary,
  successColor: themeConfig.colors.success,
  warningColor: themeConfig.colors.warning,
  dangerColor: themeConfig.colors.danger,

  // 文本色
  textColor: themeConfig.colors.text,
  textColor2: themeConfig.colors.textSecondary,
  textColor3: themeConfig.colors.textSecondary,

  // 边框 & 背景
  borderColor: themeConfig.colors.border,
  background: themeConfig.colors.background,
  background2: themeConfig.colors.surface,

  // 按钮
  buttonPrimaryBorderColor: themeConfig.colors.primary,
  buttonPrimaryBackgroundColor: themeConfig.colors.primary,
  buttonBorderRadius: `${themeConfig.borderRadius}px`,

  // 导航栏
  navBarBackgroundColor: themeConfig.colors.surface,
  navBarTitleTextColor: themeConfig.colors.text,
  navBarIconColor: themeConfig.colors.text,

  // TabBar
  tabbarBackgroundColor: themeConfig.colors.surface,
  tabbarItemActiveColor: themeConfig.colors.primary,

  // Cell
  cellGroupBackgroundColor: themeConfig.colors.surface,
  cellBackgroundColor: themeConfig.colors.surface,
  cellTextColor: themeConfig.colors.text,

  // Field
  fieldInputTextColor: themeConfig.colors.text,

  // Tabs
  tabsNavBackgroundColor: themeConfig.colors.surface,
  tabsDefaultColor: themeConfig.colors.primary,

  // 圆角 / 字号 / 间距
  borderRadiusLg: `${themeConfig.borderRadius}px`,
  borderRadiusMd: `${Math.max(themeConfig.borderRadius - 2, 0)}px`,
  borderRadiusSm: `${Math.max(themeConfig.borderRadius - 4, 0)}px`,
  fontSizeLg: `${themeConfig.fontSize + 2}px`,
  fontSizeMd: `${themeConfig.fontSize}px`,
  fontSizeSm: `${themeConfig.fontSize - 2}px`,
  paddingXs: `${themeConfig.spacing}px`,
  paddingSm: `${themeConfig.spacing * 1.5}px`,
  paddingMd: `${themeConfig.spacing * 2}px`,
  paddingLg: `${themeConfig.spacing * 3}px`,
}));

/**
 * 解析后的暗色模式布尔值。
 */
const isDark = computed(() => resolveThemeMode(themeConfig.mode) === 'dark');

// ============================================
// Watchers - 自动同步到 DOM
// ============================================

watch(
  () => ({ ...themeConfig, colors: { ...themeConfig.colors } }),
  (cfg) => {
    applyThemeConfigToDOM(cfg);
  },
  { deep: true, immediate: true },
);

// ============================================
// Actions
// ============================================

function setThemeConfig(partial: Partial<ThemeConfig>) {
  if (partial.mode !== undefined) themeConfig.mode = partial.mode;
  if (partial.colors) Object.assign(themeConfig.colors, partial.colors);
  if (partial.borderRadius !== undefined) themeConfig.borderRadius = partial.borderRadius;
  if (partial.fontFamily !== undefined) themeConfig.fontFamily = partial.fontFamily;
  if (partial.fontSize !== undefined) themeConfig.fontSize = partial.fontSize;
  if (partial.spacing !== undefined) themeConfig.spacing = partial.spacing;
}

function resetThemeConfig() {
  const defaults = getDefaultThemeConfig();
  Object.assign(themeConfig, defaults);
  Object.assign(themeConfig.colors, defaults.colors);
}

function setColor(key: keyof ThemeColors, value: string) {
  themeConfig.colors[key] = value;
}

function applyPreset(preset: ThemeConfig) {
  Object.assign(themeConfig, { ...preset, colors: { ...preset.colors } });
}

function toggleDarkMode() {
  const next = isDark.value ? 'light' : 'dark';
  themeConfig.mode = next;
  themeConfig.colors = getDefaultColorsForMode(next);
}

// ============================================
// Composable
// ============================================

export function usePlaygroundTheme() {
  return {
    themeConfig,
    vantThemeVars,
    isDark,
    setThemeConfig,
    resetThemeConfig,
    setColor,
    applyPreset,
    toggleDarkMode,
  };
}

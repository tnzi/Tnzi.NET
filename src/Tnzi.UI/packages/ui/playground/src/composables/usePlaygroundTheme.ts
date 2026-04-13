/**
 * usePlaygroundTheme
 *
 * 管理 ThemeConfig 状态并生成 Naive UI 的 theme / themeOverrides。
 */

import { reactive, computed, watch } from 'vue';
import { darkTheme } from 'naive-ui';
import type { GlobalThemeOverrides } from 'naive-ui';
import type { ThemeConfig, ThemeMode } from '@tnzi/core/types';
import {
  applyThemeConfigToDOM,
  getDefaultThemeConfig,
  getDefaultColorsForMode,
  resolveThemeMode,
} from '../data/index';

const themeConfig = reactive<ThemeConfig>(getDefaultThemeConfig());

export function usePlaygroundTheme() {
  /** Naive UI theme 对象: darkTheme 或 null */
  const currentNaiveTheme = computed(() => {
    const resolved = resolveThemeMode(themeConfig.mode);
    return resolved === 'dark' ? darkTheme : null;
  });

  /** 将 ThemeConfig 映射为 Naive UI GlobalThemeOverrides */
  const themeOverrides = computed<GlobalThemeOverrides>(() => ({
    common: {
      primaryColor: themeConfig.colors.primary,
      successColor: themeConfig.colors.success,
      warningColor: themeConfig.colors.warning,
      errorColor: themeConfig.colors.danger,
      infoColor: themeConfig.colors.info,
      borderRadius: `${themeConfig.borderRadius}px`,
      fontSize: `${themeConfig.fontSize}px`,
      fontFamily: themeConfig.fontFamily,
      bodyColor: themeConfig.colors.background,
      cardColor: themeConfig.colors.surface,
      textColorBase: themeConfig.colors.text,
      borderColor: themeConfig.colors.border,
      dividerColor: themeConfig.colors.divider,
      // spacing 映射到 Naive UI 的间距相关属性
      paddingSmall: `0 ${themeConfig.spacing}px`,
      paddingMedium: `0 ${themeConfig.spacing * 1.5}px`,
      paddingLarge: `0 ${themeConfig.spacing * 2}px`,
    },
  }));

  /** 应用 config 到 DOM CSS 自定义属性 */
  function applyToDOM() {
    applyThemeConfigToDOM(themeConfig);
  }

  /** 设置完整 config */
  function setThemeConfig(config: ThemeConfig) {
    Object.assign(themeConfig, config);
    themeConfig.colors = { ...config.colors };
  }

  /** 切换 mode */
  function setMode(mode: ThemeMode) {
    themeConfig.mode = mode;
    themeConfig.colors = getDefaultColorsForMode(mode);
  }

  /** 重置为默认 */
  function reset() {
    setThemeConfig(getDefaultThemeConfig());
  }

  // 自动同步到 DOM
  watch(
    () => ({ ...themeConfig, colors: { ...themeConfig.colors } }),
    () => applyToDOM(),
    { deep: true, immediate: true },
  );

  return {
    themeConfig,
    currentNaiveTheme,
    themeOverrides,
    setThemeConfig,
    setMode,
    reset,
  };
}

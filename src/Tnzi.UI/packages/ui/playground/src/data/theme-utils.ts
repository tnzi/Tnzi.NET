/**
 * @tnzi/ui/playground/theme-utils
 *
 * 将 ThemeConfig 应用到 DOM CSS 自定义属性的工具函数。
 */

import type { ThemeConfig, ThemeColors, ThemeMode } from '@tnzi/core/types/index';
import { lightThemeColors, darkThemeColors, defaultThemeConfig } from '@tnzi/core/types/index';

/**
 * ThemeColors 中各属性对应的 CSS 变量名映射。
 */
const colorVarMap: Record<keyof ThemeColors, string> = {
  primary: '--tnzi-primary',
  secondary: '--tnzi-secondary',
  success: '--tnzi-success',
  warning: '--tnzi-warning',
  danger: '--tnzi-danger',
  info: '--tnzi-info',
  background: '--tnzi-background',
  surface: '--tnzi-surface',
  text: '--tnzi-text',
  textSecondary: '--tnzi-text-secondary',
  border: '--tnzi-border',
  divider: '--tnzi-divider',
};

/**
 * 将完整或部分 ThemeConfig 写入 document.documentElement 的 CSS 自定义属性。
 * 同时操作 <html> 的 class 来支持 Tailwind dark mode。
 */
export function applyThemeConfigToDOM(config: Partial<ThemeConfig>): void {
  if (typeof document === 'undefined') return;

  const root = document.documentElement;

  // 应用 mode
  if (config.mode) {
    const resolved = resolveThemeMode(config.mode);
    root.classList.toggle('dark', resolved === 'dark');
    root.setAttribute('data-theme', resolved);
  }

  // 应用颜色
  if (config.colors) {
    for (const [key, varName] of Object.entries(colorVarMap)) {
      const color = config.colors[key as keyof ThemeColors];
      if (color) {
        root.style.setProperty(varName, color);
      }
    }
  }

  // 应用数值属性
  if (config.borderRadius !== undefined) {
    root.style.setProperty('--tnzi-border-radius', `${config.borderRadius}px`);
  }
  if (config.fontSize !== undefined) {
    root.style.setProperty('--tnzi-font-size', `${config.fontSize}px`);
  }
  if (config.spacing !== undefined) {
    root.style.setProperty('--tnzi-spacing', `${config.spacing}px`);
  }
  if (config.fontFamily) {
    root.style.setProperty('--tnzi-font-family', config.fontFamily);
  }
}

/**
 * 从 DOM 清除所有 tnzi CSS 自定义属性。
 */
export function clearThemeConfigFromDOM(): void {
  if (typeof document === 'undefined') return;

  const root = document.documentElement;

  for (const varName of Object.values(colorVarMap)) {
    root.style.removeProperty(varName);
  }
  root.style.removeProperty('--tnzi-border-radius');
  root.style.removeProperty('--tnzi-font-size');
  root.style.removeProperty('--tnzi-spacing');
  root.style.removeProperty('--tnzi-font-family');
}

/**
 * 解析 ThemeMode: 'system' 会查询系统偏好。
 */
export function resolveThemeMode(mode: ThemeMode): 'light' | 'dark' {
  if (mode === 'system') {
    if (typeof window !== 'undefined') {
      return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    }
    return 'light';
  }
  return mode;
}

/**
 * 根据 mode 获取对应的默认颜色。
 */
export function getDefaultColorsForMode(mode: ThemeMode): ThemeColors {
  const resolved = resolveThemeMode(mode);
  return resolved === 'dark' ? { ...darkThemeColors } : { ...lightThemeColors };
}

/**
 * 获取默认的完整 ThemeConfig。
 */
export function getDefaultThemeConfig(): ThemeConfig {
  return { ...defaultThemeConfig, colors: { ...defaultThemeConfig.colors } };
}

/**
 * hex 转 RGB 组件 (用于某些框架需要拆分颜色值)。
 */
export function hexToRgb(hex: string): { r: number; g: number; b: number } | null {
  const result = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex);
  if (!result) return null;
  return {
    r: parseInt(result[1]!, 16),
    g: parseInt(result[2]!, 16),
    b: parseInt(result[3]!, 16),
  };
}

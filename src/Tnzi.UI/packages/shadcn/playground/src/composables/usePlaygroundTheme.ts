/**
 * usePlaygroundTheme
 *
 * shadcn playground 主题控制。
 * 核心: 将 ThemeConfig (hex 颜色) 转换为 shadcn 需要的 HSL CSS 变量。
 */

import { ref } from 'vue';
import type { ThemeConfig, ThemeColors, ThemeMode } from '@tnzi/core/types/theme';
import {
  applyThemeConfigToDOM,
  getDefaultThemeConfig,
  getDefaultColorsForMode,
  resolveThemeMode,
} from '@tnzi/core/playground';
import type { ThemePreset } from '@tnzi/core/playground';

// ────────────────────── hex → HSL 工具 ──────────────────────

/** 将 hex 颜色转为 "H S% L%" 格式 (shadcn/Tailwind 空格分隔 HSL) */
function hexToHsl(hex: string): string {
  const m = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex);
  if (!m) return '0 0% 0%';

  const r = parseInt(m[1]!, 16) / 255;
  const g = parseInt(m[2]!, 16) / 255;
  const b = parseInt(m[3]!, 16) / 255;

  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  let h = 0;
  let s = 0;
  const l = (max + min) / 2;

  if (max !== min) {
    const d = max - min;
    s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
    switch (max) {
      case r: h = ((g - b) / d + (g < b ? 6 : 0)) / 6; break;
      case g: h = ((b - r) / d + 2) / 6; break;
      case b: h = ((r - g) / d + 4) / 6; break;
    }
  }

  return `${(h * 360).toFixed(1)} ${(s * 100).toFixed(1)}% ${(l * 100).toFixed(1)}%`;
}

/** 根据颜色亮度返回对比前景色 HSL */
function contrastFg(hex: string): string {
  const m = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex);
  if (!m) return '210 40% 98%';
  const lum = (0.299 * parseInt(m[1]!, 16) + 0.587 * parseInt(m[2]!, 16) + 0.114 * parseInt(m[3]!, 16)) / 255;
  return lum > 0.5 ? '222.2 84% 4.9%' : '210 40% 98%';
}

// ────────────────────── 应用 shadcn CSS 变量 ──────────────────────

function applyShadcnVars(config: ThemeConfig): void {
  if (typeof document === 'undefined') return;

  const root = document.documentElement;
  const c = config.colors;

  // dark mode
  const resolved = resolveThemeMode(config.mode);
  root.classList.toggle('dark', resolved === 'dark');
  root.setAttribute('data-theme', resolved);

  // 主色
  root.style.setProperty('--primary', hexToHsl(c.primary));
  root.style.setProperty('--primary-foreground', contrastFg(c.primary));

  // 次色 / muted / accent
  root.style.setProperty('--secondary', hexToHsl(c.secondary));
  root.style.setProperty('--secondary-foreground', contrastFg(c.secondary));
  root.style.setProperty('--muted', hexToHsl(c.secondary));
  root.style.setProperty('--muted-foreground', hexToHsl(c.textSecondary));
  root.style.setProperty('--accent', hexToHsl(c.secondary));
  root.style.setProperty('--accent-foreground', contrastFg(c.secondary));

  // 背景 & 前景
  root.style.setProperty('--background', hexToHsl(c.background));
  root.style.setProperty('--foreground', hexToHsl(c.text));

  // Card / Popover
  root.style.setProperty('--card', hexToHsl(c.surface));
  root.style.setProperty('--card-foreground', hexToHsl(c.text));
  root.style.setProperty('--popover', hexToHsl(c.surface));
  root.style.setProperty('--popover-foreground', hexToHsl(c.text));

  // Destructive
  root.style.setProperty('--destructive', hexToHsl(c.danger));
  root.style.setProperty('--destructive-foreground', contrastFg(c.danger));

  // Border / Input / Ring
  root.style.setProperty('--border', hexToHsl(c.border));
  root.style.setProperty('--input', hexToHsl(c.border));
  root.style.setProperty('--ring', hexToHsl(c.primary));

  // 布局
  root.style.setProperty('--radius', `${config.borderRadius / 16}rem`);
  if (config.fontFamily) root.style.fontFamily = config.fontFamily;
  if (config.fontSize) root.style.fontSize = `${config.fontSize}px`;
}

// ────────────────────── 单例 ──────────────────────

const themeConfig = ref<ThemeConfig>(getDefaultThemeConfig());

export function usePlaygroundTheme() {
  const applyConfig = () => {
    applyShadcnVars(themeConfig.value);
    applyThemeConfigToDOM(themeConfig.value);
  };

  const updateMode = (mode: ThemeMode) => {
    themeConfig.value = {
      ...themeConfig.value,
      mode,
      colors: getDefaultColorsForMode(mode),
    };
    applyConfig();
  };

  const updateColors = (colors: Partial<ThemeColors>) => {
    themeConfig.value = {
      ...themeConfig.value,
      colors: { ...themeConfig.value.colors, ...colors },
    };
    applyConfig();
  };

  const updateProperty = <K extends keyof ThemeConfig>(key: K, value: ThemeConfig[K]) => {
    themeConfig.value = { ...themeConfig.value, [key]: value };
    applyConfig();
  };

  const applyPreset = (preset: ThemePreset) => {
    themeConfig.value = {
      ...preset.config,
      colors: { ...preset.config.colors },
    };
    applyConfig();
  };

  const resetTheme = () => {
    if (typeof document !== 'undefined') {
      document.documentElement.removeAttribute('style');
    }
    themeConfig.value = getDefaultThemeConfig();
    applyConfig();
  };

  const isDark = () => resolveThemeMode(themeConfig.value.mode) === 'dark';

  applyConfig();

  return {
    themeConfig,
    updateMode,
    updateColors,
    updateProperty,
    applyPreset,
    resetTheme,
    isDark,
  };
}

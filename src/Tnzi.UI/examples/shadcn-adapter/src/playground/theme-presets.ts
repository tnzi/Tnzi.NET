/**
 * @tnzi/ui/playground/theme-presets
 *
 * 主题预设，供 Theme Control Panel 使用。
 */

import type { ThemeConfig, ThemeColors } from '@tnzi/core/types/index';
import { darkThemeColors, defaultThemeConfig } from '@tnzi/core/types/index';

export interface ThemePreset {
  /** Preset name */
  name: string;
  /** Preset key */
  key: string;
  /** Theme configuration */
  config: ThemeConfig;
}

const oceanBlueColors: ThemeColors = {
  primary: '#0077b6',
  secondary: '#00b4d8',
  success: '#06d6a0',
  warning: '#ffd166',
  danger: '#ef476f',
  info: '#118ab2',
  background: '#ffffff',
  surface: '#f0f4f8',
  text: '#1b263b',
  textSecondary: '#415a77',
  border: '#a8dadc',
  divider: '#e0e8ef',
};

const forestGreenColors: ThemeColors = {
  primary: '#2d6a4f',
  secondary: '#52b788',
  success: '#40916c',
  warning: '#e9c46a',
  danger: '#e76f51',
  info: '#264653',
  background: '#ffffff',
  surface: '#f1faee',
  text: '#1d3557',
  textSecondary: '#457b9d',
  border: '#a8dadc',
  divider: '#e8f0e8',
};

const purpleHazeColors: ThemeColors = {
  primary: '#7209b7',
  secondary: '#b5179e',
  success: '#06d6a0',
  warning: '#f7b801',
  danger: '#f72585',
  info: '#4361ee',
  background: '#ffffff',
  surface: '#f8f0fc',
  text: '#2b2d42',
  textSecondary: '#6c757d',
  border: '#d8b4fe',
  divider: '#f0e6f6',
};

const warmSunsetColors: ThemeColors = {
  primary: '#e85d04',
  secondary: '#f48c06',
  success: '#52b788',
  warning: '#fca311',
  danger: '#d00000',
  info: '#3a86a7',
  background: '#ffffff',
  surface: '#fff8f0',
  text: '#3d405b',
  textSecondary: '#8d99ae',
  border: '#fec89a',
  divider: '#fde8d0',
};

export const themePresets: ThemePreset[] = [
  {
    name: 'Default Light',
    key: 'default-light',
    config: { ...defaultThemeConfig },
  },
  {
    name: 'Default Dark',
    key: 'default-dark',
    config: { ...defaultThemeConfig, mode: 'dark', colors: { ...darkThemeColors } },
  },
  {
    name: 'Ocean Blue',
    key: 'ocean-blue',
    config: { ...defaultThemeConfig, colors: { ...oceanBlueColors }, borderRadius: 8 },
  },
  {
    name: 'Forest Green',
    key: 'forest-green',
    config: { ...defaultThemeConfig, colors: { ...forestGreenColors }, borderRadius: 6 },
  },
  {
    name: 'Purple Haze',
    key: 'purple-haze',
    config: { ...defaultThemeConfig, colors: { ...purpleHazeColors }, borderRadius: 12 },
  },
  {
    name: 'Warm Sunset',
    key: 'warm-sunset',
    config: { ...defaultThemeConfig, colors: { ...warmSunsetColors }, borderRadius: 8 },
  },
];

/**
 * 获取指定 key 的预设主题。
 */
export function getPreset(key: string): ThemePreset | undefined {
  return themePresets.find(p => p.key === key);
}

/**
 * Font family 选项。
 */
export const fontFamilyOptions = [
  { label: 'System Default', value: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif' },
  { label: 'Inter', value: '"Inter", sans-serif' },
  { label: 'Roboto', value: '"Roboto", sans-serif' },
  { label: 'Noto Sans SC', value: '"Noto Sans SC", sans-serif' },
  { label: 'Monospace', value: '"JetBrains Mono", "Fira Code", "Consolas", monospace' },
];

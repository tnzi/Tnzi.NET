/**
 * Theme Types - Theme configuration and customization
 */

/**
 * Theme mode
 */
export type ThemeMode = 'light' | 'dark' | 'system';

/**
 * Color format
 */
export type ColorFormat = 'hex' | 'rgb' | 'hsl';

/**
 * RGB color components
 */
export interface RgbColor {
  r: number;
  g: number;
  b: number;
  a?: number;
}

/**
 * HSL color components
 */
export interface HslColor {
  h: number;
  s: number;
  l: number;
  a?: number;
}

/**
 * Theme color palette
 */
export interface ThemeColors {
  primary: string;
  secondary: string;
  success: string;
  warning: string;
  danger: string;
  info: string;
  background: string;
  surface: string;
  text: string;
  textSecondary: string;
  border: string;
  divider: string;
}

/**
 * Theme configuration
 */
export interface ThemeConfig {
  /** Current theme mode */
  mode: ThemeMode;
  /** Color palette */
  colors: ThemeColors;
  /** Border radius (px) */
  borderRadius: number;
  /** Font family */
  fontFamily: string;
  /** Base font size (px) */
  fontSize: number;
  /** Spacing unit (px) */
  spacing: number;
}

/**
 * Default light theme colors
 */
export const lightThemeColors: ThemeColors = {
  primary: '#1890ff',
  secondary: '#52c41a',
  success: '#52c41a',
  warning: '#faad14',
  danger: '#ff4d4f',
  info: '#1890ff',
  background: '#ffffff',
  surface: '#fafafa',
  text: '#000000',
  textSecondary: '#666666',
  border: '#d9d9d9',
  divider: '#f0f0f0',
};

/**
 * Default dark theme colors
 */
export const darkThemeColors: ThemeColors = {
  primary: '#177ddc',
  secondary: '#49aa19',
  success: '#49aa19',
  warning: '#d89614',
  danger: '#a61d24',
  info: '#177ddc',
  background: '#141414',
  surface: '#1f1f1f',
  text: '#ffffff',
  textSecondary: '#a0a0a0',
  border: '#434343',
  divider: '#303030',
};

/**
 * Default theme configuration
 */
export const defaultThemeConfig: ThemeConfig = {
  mode: 'light',
  colors: lightThemeColors,
  borderRadius: 4,
  fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif',
  fontSize: 14,
  spacing: 8,
};

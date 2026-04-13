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
 * naive-ui default light theme
 */
export const lightThemeColors: ThemeColors = {
  primary: '#18a058',
  secondary: '#f5f5f7',
  success: '#18a058',
  warning: '#f0a020',
  danger: '#d03050',
  info: '#2080f0',

  background: '#ffffff',
  surface: '#fafafc',      // actionColor
  text: '#333639',         // textColor2: rgb(51,54,57)
  textSecondary: '#767c82',// textColor3: rgb(118,124,130)
  border: '#e0e0e6',       // borderColor: rgb(224,224,230)
  divider: '#efefF5',      // dividerColor: rgb(239,239,245)
};

/**
 * naive-ui default dark theme
 */
export const darkThemeColors: ThemeColors = {
  primary: '#63e2b7',
  secondary: '#2a2a2e',
  success: '#63e2b7',
  warning: '#f2c97d',
  danger: '#e88080',
  info: '#70c0e8',

  background: '#101014',   // bodyColor: rgb(16,16,20)
  surface: '#18181c',      // cardColor: rgb(24,24,28)
  text: '#e8e8e8',         // ~overlay(0.9)
  textSecondary: '#8a8f98',// ~overlay(0.52)
  border: '#3a3a40',       // ~overlay(0.24)
  divider: '#232328',      // ~overlay(0.09)
};

/**
 * Default theme configuration
 */
export const defaultThemeConfig: ThemeConfig = {
  mode: 'light',
  colors: lightThemeColors,
  borderRadius: 3,         // naive-ui default: 3px
  fontFamily: 'v-sans, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif',
  fontSize: 14,
  spacing: 8,
};

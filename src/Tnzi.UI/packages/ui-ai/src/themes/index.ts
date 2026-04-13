export {
  type AiThemeTokens,
  lightTokens,
  darkTokens,
  applyAiTheme,
} from './tokens';

/**
 * Apply custom theme overrides at runtime via raw CSS variable names.
 *
 * @example applyTheme({ 'ai-user-bubble': '220 20% 18%' })
 */
export function applyTheme(
  overrides: Partial<Record<string, string>>,
  target: HTMLElement = document.documentElement,
): void {
  for (const [key, value] of Object.entries(overrides)) {
    if (value != null) {
      target.style.setProperty(`--${key}`, value);
    }
  }
}

/**
 * Remove all custom --ai-* theme overrides.
 */
export function resetTheme(target: HTMLElement = document.documentElement): void {
  const style = target.style;
  for (let i = style.length - 1; i >= 0; i--) {
    const prop = style[i];
    if (prop?.startsWith('--ai-')) {
      style.removeProperty(prop);
    }
  }
}

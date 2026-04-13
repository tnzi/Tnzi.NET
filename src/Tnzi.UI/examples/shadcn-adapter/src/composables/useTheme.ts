/**
 * @tnzi/ui/composables/useTheme
 *
 * Theme management composable.
 * Delegates to AppStore as the single source of truth — no standalone theme state.
 */

import { computed } from 'vue';
import { useApp } from '../stores/app';

export function useTheme() {
  const app = useApp();

  const isDark = computed(() => app.isDarkMode.value);
  const theme = computed(() => app.theme.value);

  const toggleTheme = () => {
    app.toggleTheme();
  };

  const setTheme = (dark: boolean) => {
    app.setTheme(dark ? 'dark' : 'light');
  };

  return {
    isDark,
    theme,
    toggleTheme,
    setTheme,
  };
}

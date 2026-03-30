/**
 * @tnzi/shadcn/composables/useShadcnTheme
 *
 * Theme management composable for shadcn-vue.
 * Delegates to AppStore as the single source of truth — no standalone theme state.
 */

import { computed } from 'vue';
import { useApp } from '../stores/app';

export function useShadcnTheme() {
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

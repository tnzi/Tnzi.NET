/**
 * @tnzi/ui/composables/useTheme
 *
 * Theme composable — delegates to useApp() store.
 */

import { computed } from 'vue';
import { useApp } from '../stores/app';

export function useTheme() {
  const app = useApp();
  return {
    isDark: computed(() => app.isDarkMode.value),
    theme: computed(() => app.theme.value),
    toggleTheme: () => app.toggleTheme(),
    setTheme: (dark: boolean) => app.setTheme(dark ? 'dark' : 'light'),
  };
}

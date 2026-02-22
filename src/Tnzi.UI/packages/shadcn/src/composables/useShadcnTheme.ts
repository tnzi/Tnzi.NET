/**
 * @tnzi/ui/hooks/useShadcnTheme
 *
 * Theme management hook for shadcn-vue.
 */

import { ref, computed } from 'vue';

export function useShadcnTheme() {
  const isDark = ref(false);
  const theme = computed(() => (isDark.value ? 'dark' : 'light'));

  const toggleTheme = () => {
    isDark.value = !isDark.value;
    document.documentElement.classList.toggle('dark', isDark.value);
    localStorage.setItem('tnzi:theme', isDark.value ? 'dark' : 'light');
  };

  const setTheme = (dark: boolean) => {
    isDark.value = dark;
    document.documentElement.classList.toggle('dark', dark);
    localStorage.setItem('tnzi:theme', dark ? 'dark' : 'light');
  };

  // Initialize from localStorage
  if (typeof window !== 'undefined') {
    const savedTheme = localStorage.getItem('tnzi:theme');
    if (savedTheme) {
      isDark.value = savedTheme === 'dark';
      document.documentElement.classList.toggle('dark', isDark.value);
    }
  }

  return {
    isDark,
    theme,
    toggleTheme,
    setTheme,
  };
}

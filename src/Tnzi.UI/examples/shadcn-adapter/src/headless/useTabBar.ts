import type { ITabItem } from '@tnzi/core/types/shared-ui'

export interface UseTabBarOptions {
  badge?: Record<string, number>
  onChange?: (key: string, tab: ITabItem) => void
}

export interface UseTabBarReturn {
  tabBadge: (key: string, fallback?: number) => number | undefined
  handleChange: (key: string, tab: ITabItem) => void
}

export function useTabBar(options: UseTabBarOptions = {}): UseTabBarReturn {
  function tabBadge(key: string, fallback?: number): number | undefined {
    return options.badge?.[key] ?? fallback
  }

  function handleChange(key: string, tab: ITabItem): void {
    options.onChange?.(key, tab)
  }

  return {
    tabBadge,
    handleChange,
  }
}

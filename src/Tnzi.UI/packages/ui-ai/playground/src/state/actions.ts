import { computed, type ComputedRef } from 'vue'
import type { CommandAction } from '@tnzi/ui-ai/shell'
import { usePlaygroundStore } from './playground-store'

/**
 * Builds the command palette action registry for the playground.
 *
 * Registered actions fall into four groups:
 *   - Scenario - jump to any of the 10 scenarios
 *   - Layout - toggle sidebar modes
 *   - Settings - open settings modal at a given section
 *   - Theme - toggle between light/dark
 */
export function useCommandActions(): ComputedRef<readonly CommandAction[]> {
  const store = usePlaygroundStore()

  return computed<readonly CommandAction[]>(() => {
    const actions: CommandAction[] = []

    for (const scenario of store.scenarioIndex.scenarios) {
      actions.push({
        id: `scenario.${scenario.meta.id}`,
        label: `Open: ${scenario.meta.title}`,
        description: scenario.meta.description,
        icon: scenario.meta.icon,
        category: 'scenario',
        keywords: [scenario.meta.category, ...scenario.meta.componentsShowcased],
        run: () => store.selectScenario(scenario.meta.id),
      })
    }

    actions.push(
      {
        id: 'layout.sidebar.expand',
        label: 'Expand sidebar',
        icon: 'lucide:panel-left-open',
        category: 'layout',
        run: () => {
          store.sidebarMode.value = 'expanded'
        },
      },
      {
        id: 'layout.sidebar.icon',
        label: 'Collapse sidebar to icon rail',
        icon: 'lucide:panel-left-close',
        category: 'layout',
        run: () => {
          store.sidebarMode.value = 'icon'
        },
      },
      {
        id: 'layout.sidebar.hide',
        label: 'Hide sidebar',
        icon: 'lucide:panel-left',
        category: 'layout',
        run: () => {
          store.sidebarMode.value = 'hidden'
        },
      },
    )

    for (const sectionId of ['appearance', 'model', 'language', 'about']) {
      actions.push({
        id: `settings.${sectionId}`,
        label: `Open settings: ${sectionId}`,
        icon: 'lucide:settings',
        category: 'settings',
        run: () => {
          store.settingsSection.value = sectionId
          store.settingsOpen.value = true
        },
      })
    }

    actions.push({
      id: 'theme.toggle',
      label: `Switch to ${store.theme.value === 'light' ? 'dark' : 'light'} theme`,
      icon: 'lucide:sun-moon',
      category: 'theme',
      keywords: ['dark', 'light', 'mode'],
      run: () => {
        store.theme.value = store.theme.value === 'light' ? 'dark' : 'light'
      },
    })

    return actions
  })
}

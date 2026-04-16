import { ref, computed, type Ref, type ComputedRef } from 'vue'

export interface SettingsSection {
  id: string
  label: string
  icon?: string
}

export interface UseSettingsDialogOptions {
  sections: Ref<readonly SettingsSection[]>
  initialSection?: string
  /**
   * Called when hide() is invoked while isDirty is true. Return false to cancel
   * the close (e.g., prompt the user). Return true or void to allow.
   */
  onBeforeClose?: () => boolean | void
}

export interface UseSettingsDialogReturn {
  open: Ref<boolean>
  activeSection: Ref<string>
  isDirty: Ref<boolean>
  currentSection: ComputedRef<SettingsSection | null>
  show: (sectionId?: string) => void
  hide: () => void
  setSection: (id: string) => void
  markDirty: () => void
  clearDirty: () => void
}

/**
 * @experimental
 * Manages settings dialog state: open/close, active section, dirty tracking.
 * Optional onBeforeClose guard lets consumers cancel close when unsaved.
 */
export function useSettingsDialog(options: UseSettingsDialogOptions): UseSettingsDialogReturn {
  const sections = options.sections
  const firstId = sections.value[0]?.id ?? ''
  const open = ref(false)
  const activeSection = ref(options.initialSection ?? firstId)
  const isDirty = ref(false)

  const currentSection = computed(() => {
    return sections.value.find((s) => s.id === activeSection.value) ?? null
  })

  function setSection(id: string): void {
    if (sections.value.some((s) => s.id === id)) {
      activeSection.value = id
    }
  }

  function show(sectionId?: string): void {
    if (sectionId) setSection(sectionId)
    open.value = true
  }

  function hide(): void {
    if (isDirty.value && options.onBeforeClose) {
      const allow = options.onBeforeClose()
      if (allow === false) return
    }
    open.value = false
  }

  function markDirty(): void {
    isDirty.value = true
  }

  function clearDirty(): void {
    isDirty.value = false
  }

  return { open, activeSection, isDirty, currentSection, show, hide, setSection, markDirty, clearDirty }
}

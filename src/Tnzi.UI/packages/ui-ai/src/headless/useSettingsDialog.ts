import { ref, computed, type Ref, type ComputedRef } from 'vue'

export interface SettingsSection {
  id: string
  label: string
  icon?: string
  /**
   * Optional heading this section files under. Sections declaring no group
   * render first, in one unlabelled block; grouped sections follow in the
   * order their group first appears. Grouping is derived from the flat list
   * rather than declared separately so that adding a section is a one-line
   * change and the two can never disagree about which sections exist.
   */
  group?: string
}

/** A run of sections sharing one heading. `label` is `''` for the leading
 *  unlabelled block. */
export interface SettingsSectionGroup {
  label: string
  sections: readonly SettingsSection[]
}

/**
 * On a narrow screen the section list and the selected section are two screens
 * of one sheet instead of two columns: which of them is showing.
 */
export type SettingsNarrowView = 'sections' | 'section'

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
  /** Free-text filter over section labels. */
  query: Ref<string>
  /** Sections grouped by heading, already filtered by `query`. */
  groupedSections: ComputedRef<readonly SettingsSectionGroup[]>
  /**
   * The screen a narrow layout shows. `show()` opens on the section list unless
   * it was asked for a section, and picking a section moves to it; the wide
   * two-column layout ignores this.
   */
  narrowView: Ref<SettingsNarrowView>
  show: (sectionId?: string) => void
  hide: () => void
  /** Select a section (and, on a narrow screen, move to it). Unknown ids are ignored. */
  setSection: (id: string) => void
  /** Back from a section to the section list (narrow layout). */
  showSections: () => void
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

  const query = ref('')
  const narrowView = ref<SettingsNarrowView>('sections')

  const currentSection = computed(() => {
    return sections.value.find((s) => s.id === activeSection.value) ?? null
  })

  const filteredSections = computed<readonly SettingsSection[]>(() => {
    const q = query.value.trim().toLowerCase()
    if (!q) return sections.value
    return sections.value.filter((s) => s.label.toLowerCase().includes(q))
  })

  /* A Map keyed by group name, so sections declaring the same group merge into
     one block even when they are not adjacent in the array, while insertion
     order still decides how the blocks are stacked. */
  const groupedSections = computed<readonly SettingsSectionGroup[]>(() => {
    const buckets = new Map<string, SettingsSection[]>()
    for (const section of filteredSections.value) {
      const key = section.group ?? ''
      const bucket = buckets.get(key)
      if (bucket) bucket.push(section)
      else buckets.set(key, [section])
    }
    return Array.from(buckets, ([label, groupSections]) => ({ label, sections: groupSections }))
  })

  function setSection(id: string): void {
    if (sections.value.some((s) => s.id === id)) {
      activeSection.value = id
      narrowView.value = 'section'
    }
  }

  /* Asked for a section ("Account" from the account menu), the dialog opens on
     it; opened plainly ("Settings"), a narrow sheet opens on the list, which is
     where a phone user expects to start. The selected section is kept either
     way, so the wide layout still reopens where the user left off. */
  function show(sectionId?: string): void {
    narrowView.value = 'sections'
    if (sectionId) setSection(sectionId)
    open.value = true
  }

  function showSections(): void {
    narrowView.value = 'sections'
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

  return {
    open,
    activeSection,
    isDirty,
    currentSection,
    query,
    groupedSections,
    narrowView,
    show,
    hide,
    setSection,
    showSections,
    markDirty,
    clearDirty,
  }
}

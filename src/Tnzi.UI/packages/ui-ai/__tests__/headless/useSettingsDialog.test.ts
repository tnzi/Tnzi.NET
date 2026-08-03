import { describe, it, expect, vi } from 'vitest'
import { ref } from 'vue'
import { useSettingsDialog, type SettingsSection } from '../../src/headless/useSettingsDialog'

const makeSections = (): SettingsSection[] => [
  { id: 'appearance', label: 'Appearance' },
  { id: 'model', label: 'Model' },
  { id: 'language', label: 'Language' },
  { id: 'about', label: 'About' },
]

describe('useSettingsDialog', () => {
  it('starts closed with the first section active', () => {
    const sections = ref(makeSections())
    const { open, activeSection } = useSettingsDialog({ sections })
    expect(open.value).toBe(false)
    expect(activeSection.value).toBe('appearance')
  })

  it('accepts initialSection', () => {
    const sections = ref(makeSections())
    const { activeSection } = useSettingsDialog({ sections, initialSection: 'model' })
    expect(activeSection.value).toBe('model')
  })

  it('show() opens the dialog', () => {
    const sections = ref(makeSections())
    const { open, show } = useSettingsDialog({ sections })
    show()
    expect(open.value).toBe(true)
  })

  it('show(sectionId) opens and jumps to section', () => {
    const sections = ref(makeSections())
    const { open, activeSection, show } = useSettingsDialog({ sections })
    show('language')
    expect(open.value).toBe(true)
    expect(activeSection.value).toBe('language')
  })

  it('hide() closes the dialog', () => {
    const sections = ref(makeSections())
    const { open, show, hide } = useSettingsDialog({ sections })
    show()
    hide()
    expect(open.value).toBe(false)
  })

  it('setSection changes the active section', () => {
    const sections = ref(makeSections())
    const { activeSection, setSection } = useSettingsDialog({ sections })
    setSection('about')
    expect(activeSection.value).toBe('about')
  })

  it('setSection rejects unknown ids (no-op)', () => {
    const sections = ref(makeSections())
    const { activeSection, setSection } = useSettingsDialog({ sections })
    setSection('bogus')
    expect(activeSection.value).toBe('appearance')
  })

  it('markDirty/isDirty/clearDirty track unsaved state', () => {
    const sections = ref(makeSections())
    const { isDirty, markDirty, clearDirty } = useSettingsDialog({ sections })
    expect(isDirty.value).toBe(false)
    markDirty()
    expect(isDirty.value).toBe(true)
    clearDirty()
    expect(isDirty.value).toBe(false)
  })

  it('onBeforeClose guard can cancel hide when dirty', () => {
    const sections = ref(makeSections())
    const guard = vi.fn(() => false)
    const { open, show, hide, markDirty } = useSettingsDialog({ sections, onBeforeClose: guard })
    show()
    markDirty()
    hide()
    expect(guard).toHaveBeenCalledOnce()
    expect(open.value).toBe(true)
  })

  it('onBeforeClose guard allowing true closes dialog', () => {
    const sections = ref(makeSections())
    const guard = vi.fn(() => true)
    const { open, show, hide, markDirty } = useSettingsDialog({ sections, onBeforeClose: guard })
    show()
    markDirty()
    hide()
    expect(open.value).toBe(false)
  })

  it('onBeforeClose is not called when not dirty', () => {
    const sections = ref(makeSections())
    const guard = vi.fn(() => false)
    const { show, hide } = useSettingsDialog({ sections, onBeforeClose: guard })
    show()
    hide()
    expect(guard).not.toHaveBeenCalled()
  })

  describe('grouping', () => {
    it('puts ungrouped sections in a single unlabelled group', () => {
      const sections = ref(makeSections())
      const { groupedSections } = useSettingsDialog({ sections })
      expect(groupedSections.value).toHaveLength(1)
      expect(groupedSections.value[0]?.label).toBe('')
      expect(groupedSections.value[0]?.sections).toHaveLength(4)
    })

    it('splits sections into groups in first-appearance order', () => {
      const sections = ref<SettingsSection[]>([
        { id: 'general', label: 'General', group: 'Settings' },
        { id: 'skills', label: 'Skills', group: 'Capabilities' },
        { id: 'account', label: 'Account', group: 'Settings' },
      ])
      const { groupedSections } = useSettingsDialog({ sections })

      expect(groupedSections.value.map((g) => g.label)).toEqual(['Settings', 'Capabilities'])
      // Non-adjacent members of the same group merge rather than opening a
      // second block with a duplicate heading.
      expect(groupedSections.value[0]?.sections.map((s) => s.id)).toEqual(['general', 'account'])
      expect(groupedSections.value[1]?.sections.map((s) => s.id)).toEqual(['skills'])
    })

    it('keeps the unlabelled block first when it is declared first', () => {
      const sections = ref<SettingsSection[]>([
        { id: 'general', label: 'General' },
        { id: 'skills', label: 'Skills', group: 'Capabilities' },
      ])
      const { groupedSections } = useSettingsDialog({ sections })
      expect(groupedSections.value.map((g) => g.label)).toEqual(['', 'Capabilities'])
    })
  })

  describe('query', () => {
    it('filters sections by label, case-insensitively', () => {
      const sections = ref(makeSections())
      const { query, groupedSections } = useSettingsDialog({ sections })

      query.value = 'LANG'
      expect(groupedSections.value[0]?.sections.map((s) => s.id)).toEqual(['language'])
    })

    it('ignores surrounding whitespace', () => {
      const sections = ref(makeSections())
      const { query, groupedSections } = useSettingsDialog({ sections })

      query.value = '   '
      expect(groupedSections.value[0]?.sections).toHaveLength(4)
    })

    it('drops a group entirely when none of its sections match', () => {
      const sections = ref<SettingsSection[]>([
        { id: 'general', label: 'General', group: 'Settings' },
        { id: 'skills', label: 'Skills', group: 'Capabilities' },
      ])
      const { query, groupedSections } = useSettingsDialog({ sections })

      query.value = 'skill'
      expect(groupedSections.value).toHaveLength(1)
      expect(groupedSections.value[0]?.label).toBe('Capabilities')
    })

    it('yields no groups when nothing matches', () => {
      const sections = ref(makeSections())
      const { query, groupedSections } = useSettingsDialog({ sections })

      query.value = 'nothing here'
      expect(groupedSections.value).toEqual([])
    })

    it('leaves the active section selected even when filtered out of view', () => {
      const sections = ref(makeSections())
      const { query, activeSection, currentSection } = useSettingsDialog({ sections })

      query.value = 'about'
      // Filtering is a view concern; it must not silently re-point the pane at
      // a different section behind the user.
      expect(activeSection.value).toBe('appearance')
      expect(currentSection.value?.id).toBe('appearance')
    })
  })
})

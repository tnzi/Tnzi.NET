import { describe, it, expect, vi } from 'vitest'
import { ref } from 'vue'
import { useSettingsDialog, type SettingsSection } from '../../src/composables/useSettingsDialog'

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
})

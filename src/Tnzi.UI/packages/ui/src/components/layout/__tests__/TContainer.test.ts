import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import type { CSSProperties } from 'vue'
import TContainer from '../TContainer.vue'

/**
 * NOTE: happy-dom's CSS parser drops values it cannot understand (notably
 * `clamp(...)`) and collapses 4-side padding into the `padding` shorthand.
 * We therefore assert on the raw computed style object exposed by the
 * component via defineExpose, not on the parsed DOM `style` property.
 */

function getStyle(wrapper: ReturnType<typeof mount>): CSSProperties {
  return (wrapper.vm as unknown as { containerStyle: CSSProperties }).containerStyle
}

describe('TContainer padding props', () => {
  it('emits the default padding shorthand when neither paddingX nor paddingY is provided', () => {
    const s = getStyle(mount(TContainer))
    expect(s.padding).toBe('0 clamp(16px, 4vw, 32px)')
    expect(s.paddingLeft).toBeUndefined()
    expect(s.paddingTop).toBeUndefined()
  })

  it('applies paddingX as both horizontal sides with default vertical 0 when only paddingX is provided', () => {
    const s = getStyle(mount(TContainer, { props: { paddingX: '24px' } }))
    expect(s.paddingLeft).toBe('24px')
    expect(s.paddingRight).toBe('24px')
    expect(s.paddingTop).toBe('0px')
    expect(s.paddingBottom).toBe('0px')
    expect(s.padding).toBeUndefined()
  })

  it('applies paddingY with default horizontal clamp when only paddingY is provided', () => {
    const s = getStyle(mount(TContainer, { props: { paddingY: '16px' } }))
    expect(s.paddingTop).toBe('16px')
    expect(s.paddingBottom).toBe('16px')
    expect(s.paddingLeft).toBe('clamp(16px, 4vw, 32px)')
    expect(s.paddingRight).toBe('clamp(16px, 4vw, 32px)')
    expect(s.padding).toBeUndefined()
  })

  it('applies both paddingX and paddingY together', () => {
    const s = getStyle(mount(TContainer, { props: { paddingX: '40px', paddingY: '12px' } }))
    expect(s.paddingLeft).toBe('40px')
    expect(s.paddingRight).toBe('40px')
    expect(s.paddingTop).toBe('12px')
    expect(s.paddingBottom).toBe('12px')
  })

  it('honors a legacy padding shorthand when paddingX/paddingY are absent', () => {
    const s = getStyle(mount(TContainer, { props: { padding: '8px 12px' } }))
    expect(s.padding).toBe('8px 12px')
    expect(s.paddingLeft).toBeUndefined()
  })
})

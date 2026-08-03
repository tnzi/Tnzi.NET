import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import type { CSSProperties } from 'vue'
import TContainer from '../../../src/components/layout/TContainer.vue'

/**
 * NOTE: happy-dom's CSS parser drops values it cannot understand (notably
 * `clamp(...)`) and collapses 4-side padding into the `padding` shorthand.
 * The padding-prop suite below therefore asserts on the raw computed style
 * object the component exposes via defineExpose, not on the parsed DOM
 * `style` property.
 */
function getStyle(wrapper: ReturnType<typeof mount>): CSSProperties {
  return (wrapper.vm as unknown as { containerStyle: CSSProperties }).containerStyle
}

describe('TContainer', () => {
  it('renders children via default slot', () => {
    const wrapper = mount(TContainer, {
      slots: { default: '<p class="inner">hello</p>' },
    })
    expect(wrapper.find('.inner').exists()).toBe(true)
    expect(wrapper.text()).toContain('hello')
  })

  it('applies default maxWidth xl (1280px)', () => {
    const wrapper = mount(TContainer)
    const style = wrapper.attributes('style') ?? ''
    expect(style).toContain('max-width: 1280px')
  })

  it('accepts custom maxWidth as prop', () => {
    const wrapper = mount(TContainer, { props: { maxWidth: '960px' } })
    expect(wrapper.attributes('style')).toContain('max-width: 960px')
  })

  it('accepts maxWidth size preset', () => {
    const wrapper = mount(TContainer, { props: { maxWidth: 'md' } })
    expect(wrapper.attributes('style')).toContain('max-width: 768px')
  })

  it('centers horizontally via margin auto', () => {
    const wrapper = mount(TContainer)
    expect(wrapper.attributes('style')).toContain('margin-left: auto')
    expect(wrapper.attributes('style')).toContain('margin-right: auto')
  })

  it('applies fluid prop to disable max-width', () => {
    const wrapper = mount(TContainer, { props: { fluid: true } })
    expect(wrapper.attributes('style')).not.toContain('max-width')
  })

  it('applies custom padding via padding prop', () => {
    const wrapper = mount(TContainer, { props: { padding: '32px' } })
    expect(wrapper.attributes('style')).toContain('padding: 32px')
  })

  it('default padding has zero vertical component', () => {
    const wrapper = mount(TContainer)
    // Default is '0 clamp(16px, 4vw, 32px)' - vertical 0, horizontal responsive.
    // jsdom may drop the shorthand containing clamp() from the serialized style
    // attribute, so assert on the resolved prop value instead.
    expect(wrapper.props('padding')).toMatch(/^0\s/)
  })
})

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

import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TPresenceDot from '../../../src/components/chat/TPresenceDot.vue'
import { UserPresenceStatus } from '@tnzi/core/services/chat'

describe('TPresenceDot', () => {
  it('Online → --online class', () => {
    const w = mount(TPresenceDot, { props: { status: UserPresenceStatus.Online } })
    expect(w.find('.t-presence-dot').classes()).toContain('t-presence-dot--online')
  })

  it('Away → --away class', () => {
    const w = mount(TPresenceDot, { props: { status: UserPresenceStatus.Away } })
    expect(w.find('.t-presence-dot').classes()).toContain('t-presence-dot--away')
  })

  it('Busy → --busy class', () => {
    const w = mount(TPresenceDot, { props: { status: UserPresenceStatus.Busy } })
    expect(w.find('.t-presence-dot').classes()).toContain('t-presence-dot--busy')
  })

  it('Offline → --offline class', () => {
    const w = mount(TPresenceDot, { props: { status: UserPresenceStatus.Offline } })
    expect(w.find('.t-presence-dot').classes()).toContain('t-presence-dot--offline')
  })

  it('Invisible → --invisible class (hollow dot distinct from offline)', () => {
    const w = mount(TPresenceDot, { props: { status: UserPresenceStatus.Invisible } })
    expect(w.find('.t-presence-dot').classes()).toContain('t-presence-dot--invisible')
    expect(w.find('.t-presence-dot').classes()).not.toContain('t-presence-dot--offline')
  })

  it('undefined status → --offline class', () => {
    const w = mount(TPresenceDot, { props: {} })
    expect(w.find('.t-presence-dot').classes()).toContain('t-presence-dot--offline')
  })

  it('applies the requested size as inline style', () => {
    const w = mount(TPresenceDot, { props: { status: UserPresenceStatus.Online, size: 14 } })
    const el = w.find('.t-presence-dot').element as HTMLElement
    expect(el.style.width).toBe('14px')
    expect(el.style.height).toBe('14px')
  })
})

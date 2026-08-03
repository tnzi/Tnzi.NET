import { describe, it, expect, vi } from 'vitest'
import { useSwipeCell } from '../src/headless/useSwipeCell'

describe('useSwipeCell', () => {
  it('initializes with none direction and closed state', () => {
    const { openDirection, isOpen, isOpenLeft, isOpenRight } = useSwipeCell()
    expect(openDirection.value).toBe('none')
    expect(isOpen.value).toBe(false)
    expect(isOpenLeft.value).toBe(false)
    expect(isOpenRight.value).toBe(false)
  })

  it('open sets direction to left', () => {
    const { openDirection, isOpen, isOpenLeft, isOpenRight, open } = useSwipeCell()
    open('left')
    expect(openDirection.value).toBe('left')
    expect(isOpen.value).toBe(true)
    expect(isOpenLeft.value).toBe(true)
    expect(isOpenRight.value).toBe(false)
  })

  it('open sets direction to right', () => {
    const { openDirection, isOpen, isOpenLeft, isOpenRight, open } = useSwipeCell()
    open('right')
    expect(openDirection.value).toBe('right')
    expect(isOpen.value).toBe(true)
    expect(isOpenLeft.value).toBe(false)
    expect(isOpenRight.value).toBe(true)
  })

  it('open calls onOpen callback', () => {
    const onOpen = vi.fn()
    const { open } = useSwipeCell({ onOpen })
    open('right')
    expect(onOpen).toHaveBeenCalledWith('right')
  })

  it('close resets to none and calls onClose', () => {
    const onClose = vi.fn()
    const { openDirection, isOpen, open, close } = useSwipeCell({ onClose })
    open('left')
    close()
    expect(openDirection.value).toBe('none')
    expect(isOpen.value).toBe(false)
    expect(onClose).toHaveBeenCalledOnce()
  })

  it('close does not call onClose when already closed', () => {
    const onClose = vi.fn()
    const { close } = useSwipeCell({ onClose })
    close()
    expect(onClose).not.toHaveBeenCalled()
  })

  it('toggle opens when closed', () => {
    const { openDirection, toggle } = useSwipeCell()
    toggle('right')
    expect(openDirection.value).toBe('right')
  })

  it('toggle closes when same direction is open', () => {
    const { openDirection, toggle } = useSwipeCell()
    toggle('right')
    toggle('right')
    expect(openDirection.value).toBe('none')
  })

  it('toggle switches direction when different direction is open', () => {
    const { openDirection, toggle } = useSwipeCell()
    toggle('left')
    toggle('right')
    expect(openDirection.value).toBe('right')
  })

  it('disabled prevents open', () => {
    const onOpen = vi.fn()
    const { isOpen, open } = useSwipeCell({ disabled: true, onOpen })
    open('left')
    expect(isOpen.value).toBe(false)
    expect(onOpen).not.toHaveBeenCalled()
  })

  it('disabled prevents toggle', () => {
    const { isOpen, toggle } = useSwipeCell({ disabled: true })
    toggle('left')
    expect(isOpen.value).toBe(false)
  })

  it('can switch from left to right open', () => {
    const { openDirection, open } = useSwipeCell()
    open('left')
    expect(openDirection.value).toBe('left')
    open('right')
    expect(openDirection.value).toBe('right')
  })
})

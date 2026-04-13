import { describe, it, expect } from 'vitest'
import { useConfirm } from '../../../src/composables/feedback/useConfirm'

describe('useConfirm', () => {
  it('resolves true when user confirms', async () => {
    const { confirm, state } = useConfirm()
    const promise = confirm({ title: 'Delete?' })
    expect(state.value.show).toBe(true)
    state.value.resolve?.(true)
    const result = await promise
    expect(result).toBe(true)
  })

  it('resolves false when user cancels', async () => {
    const { confirm, state } = useConfirm()
    const promise = confirm({ title: 'Delete?' })
    state.value.resolve?.(false)
    const result = await promise
    expect(result).toBe(false)
  })

  it('passes title and content to state', () => {
    const { confirm, state } = useConfirm()
    confirm({ title: 'T', content: 'C' })
    expect(state.value.title).toBe('T')
    expect(state.value.content).toBe('C')
  })
})

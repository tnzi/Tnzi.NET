import { afterEach, describe, expect, it } from 'vitest'
import { effectScope, type EffectScope } from 'vue'
import { useFormModal, useRetainedFormState } from '../../src/headless/useFormModal'

interface UserForm {
  id?: string
  name: string
}

// The composable's capture runs in a watcher, so the scope has to stay alive
// for the whole test - stopping it right after the call would leave the held
// pair frozen at whatever it was on the first run.
const scopes: EffectScope[] = []
afterEach(() => {
  while (scopes.length) scopes.pop()!.stop()
})

/** Create the retained view inside a scope, the way a chrome component does. */
function inScope<T>(fn: () => T): T {
  const scope = effectScope()
  scopes.push(scope)
  return scope.run(fn)!
}

describe('useRetainedFormState', () => {
  it('mirrors the live state while the overlay is open', () => {
    const state = useFormModal<UserForm>()
    const rendered = inScope(() => useRetainedFormState(state))

    expect(rendered.mode.value).toBe(null)
    expect(rendered.formData.value).toBe(null)

    state.open('edit', { id: '1', name: 'Alice' })
    expect(rendered.mode.value).toBe('edit')
    expect(rendered.formData.value).toEqual({ id: '1', name: 'Alice' })
    expect(rendered.formData.value).toBe(state.formData.value)
  })

  it('keeps the last open values after close, while the state itself clears', () => {
    const state = useFormModal<UserForm>()
    const rendered = inScope(() => useRetainedFormState(state))

    state.open('edit', { id: '1', name: 'Alice' })
    state.close()

    // The primitive still clears - consumers watching formData keep their signal.
    expect(state.mode.value).toBe(null)
    expect(state.formData.value).toBe(null)
    // The chrome keeps painting what it had until the body is gone.
    expect(rendered.mode.value).toBe('edit')
    expect(rendered.formData.value).toEqual({ id: '1', name: 'Alice' })
  })

  it('does not leak the previous record into the next open', () => {
    const state = useFormModal<UserForm>()
    const rendered = inScope(() => useRetainedFormState(state))

    state.open('edit', { id: '1', name: 'Alice' })
    state.close()
    state.open('edit', { id: '2', name: 'Bob' })

    expect(rendered.formData.value).toEqual({ id: '2', name: 'Bob' })
  })

  it('shows a null record that is genuinely null while open', () => {
    const state = useFormModal<UserForm>()
    const rendered = inScope(() => useRetainedFormState(state))

    state.open('edit', { id: '1', name: 'Alice' })
    // A page that clears the record while the overlay is still open must not
    // have the body keep painting the previous one: while open, the retained
    // view IS the live state, nulls included.
    state.formData.value = null
    expect(rendered.formData.value).toBe(null)

    // Same for `open(mode)` with no initial, which is a legitimate null record.
    state.close()
    state.open('create')
    expect(rendered.mode.value).toBe('create')
    expect(rendered.formData.value).toBe(null)
  })

  it('tracks edits to the record reference while open', () => {
    const state = useFormModal<UserForm>()
    const rendered = inScope(() => useRetainedFormState(state))

    state.open('create', { name: '' })
    state.formData.value = { name: 'typed' }
    expect(rendered.formData.value).toEqual({ name: 'typed' })

    state.close()
    expect(rendered.formData.value).toEqual({ name: 'typed' })
  })
})

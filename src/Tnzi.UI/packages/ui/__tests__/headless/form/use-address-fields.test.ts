import { describe, it, expect } from 'vitest'
import { reactive } from 'vue'
import {
  useAddressFields,
  resolveAddressKey,
} from '../../../src/headless/form/useAddressFields'

describe('useAddressFields', () => {
  it('reads a flat DTO through keyMap, which is the shape most real models have', () => {
    const form = reactive({ street: '100 King St W', city: 'Toronto', province: 'ON' })
    const fields = useAddressFields(() => form, { keyMap: { region: 'province' } })

    const region = fields.value.find((f) => f.key === 'region')!
    expect(region.modelKey).toBe('province')
    expect(region.value).toBe('ON')
  })

  it('reads the second address on the same model through prefix', () => {
    const form = reactive({ street: 'a', mailingStreet: 'b', mailingCity: 'c' })
    const fields = useAddressFields(() => form, { prefix: 'mailing' })

    expect(fields.value.find((f) => f.key === 'street')!.value).toBe('b')
    expect(fields.value.find((f) => f.key === 'city')!.value).toBe('c')
  })

  it('keyMap wins over prefix', () => {
    expect(resolveAddressKey('region', { prefix: 'mailing', keyMap: { region: 'province' } }))
      .toBe('province')
    expect(resolveAddressKey('city', { prefix: 'mailing' })).toBe('mailingCity')
  })

  // The host owns a reactive form; replacing the whole object would break its bindings.
  it('writes in place by default', () => {
    const form = reactive<Record<string, unknown>>({ city: 'Toronto' })
    const fields = useAddressFields(() => form)

    fields.value.find((f) => f.key === 'city')!.set('Ottawa')
    expect(form.city).toBe('Ottawa')
  })

  // v-model needs a new object instead - that is what TAddressFields does.
  it('hands out a new object when onUpdate is supplied, leaving the original alone', () => {
    const form = { city: 'Toronto', keepMe: 1 }
    let received: Record<string, unknown> | null = null
    const fields = useAddressFields(() => form, { onUpdate: (next) => { received = next } })

    fields.value.find((f) => f.key === 'city')!.set('Ottawa')

    expect(form.city).toBe('Toronto')
    expect(received).toEqual({ city: 'Ottawa', keepMe: 1 })
  })

  it('becomes a select only when options are supplied', () => {
    const plain = useAddressFields(() => ({}))
    expect(plain.value.find((f) => f.key === 'region')!.type).toBe('input')

    const withOptions = useAddressFields(() => ({}), {
      regionOptions: [{ label: 'Ontario', value: 'ON' }],
    })
    const region = withOptions.value.find((f) => f.key === 'region')!
    expect(region.type).toBe('select')
    expect(region.options).toHaveLength(1)
  })

  it('omits country unless asked, and keeps the component field order', () => {
    expect(useAddressFields(() => ({})).value.map((f) => f.key)).toEqual([
      'street', 'unit', 'city', 'region', 'postalCode',
    ])
    expect(useAddressFields(() => ({}), { showCountry: true }).value.map((f) => f.key)).toEqual([
      'street', 'unit', 'city', 'region', 'postalCode', 'country',
    ])
  })

  it('applies label overrides, then the region/postal defaults', () => {
    const fields = useAddressFields(() => ({}), {
      labels: { unit: 'Suite' },
      regionLabel: 'Province',
    })
    expect(fields.value.find((f) => f.key === 'unit')!.label).toBe('Suite')
    expect(fields.value.find((f) => f.key === 'region')!.label).toBe('Province')
    expect(fields.value.find((f) => f.key === 'city')!.label).toBe('City')
  })

  it('treats a missing model as empty rather than throwing', () => {
    const fields = useAddressFields(() => null)
    expect(fields.value.every((f) => f.value === null)).toBe(true)
  })

  it('tracks the model reactively', () => {
    const form = reactive<Record<string, unknown>>({ city: 'Toronto' })
    const fields = useAddressFields(() => form)

    expect(fields.value.find((f) => f.key === 'city')!.value).toBe('Toronto')
    form.city = 'Ottawa'
    expect(fields.value.find((f) => f.key === 'city')!.value).toBe('Ottawa')
  })
})

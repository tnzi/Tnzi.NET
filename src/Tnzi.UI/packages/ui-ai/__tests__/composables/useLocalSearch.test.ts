import { describe, it, expect } from 'vitest'
import { useLocalSearch } from '../../src/composables/useLocalSearch'

interface Item {
  id: string
  name: string
  description: string | null
  count: number
}

const dataset: Item[] = [
  { id: '1', name: 'Apple', description: 'Red fruit', count: 10 },
  { id: '2', name: 'Banana', description: 'Yellow fruit', count: 20 },
  { id: '3', name: 'Cherry', description: null, count: 5 },
]

describe('useLocalSearch', () => {
  it('returns all items when query is empty', () => {
    const { query, filtered } = useLocalSearch<Item>(() => dataset, ['name'])
    expect(filtered.value).toHaveLength(3)
    query.value = ''
    expect(filtered.value).toHaveLength(3)
  })

  it('filters by single field case-insensitively', () => {
    const { query, filtered } = useLocalSearch<Item>(() => dataset, ['name'])
    query.value = 'APPLE'
    expect(filtered.value).toHaveLength(1)
    expect(filtered.value[0]!.id).toBe('1')
  })

  it('filters by multiple fields with OR semantic', () => {
    const { query, filtered } = useLocalSearch<Item>(() => dataset, ['name', 'description'])
    query.value = 'fruit'
    expect(filtered.value).toHaveLength(2)
  })

  it('skips non-string field values safely', () => {
    const { query, filtered } = useLocalSearch<Item>(() => dataset, ['count'] as unknown as (keyof Item)[])
    query.value = '10'
    expect(filtered.value).toEqual([])
  })

  it('handles null field values safely', () => {
    const { query, filtered } = useLocalSearch<Item>(() => dataset, ['description'])
    query.value = 'red'
    expect(filtered.value).toHaveLength(1)
    expect(filtered.value[0]!.id).toBe('1')
  })

  it('returns empty array for no matches', () => {
    const { query, filtered } = useLocalSearch<Item>(() => dataset, ['name'])
    query.value = 'nonexistent'
    expect(filtered.value).toHaveLength(0)
  })

  it('honors query-driven filtering with updated items snapshot', () => {
    let items: Item[] = [{ id: 'x', name: 'X', description: '', count: 0 }]
    const { query, filtered } = useLocalSearch<Item>(() => items, ['name'])
    query.value = 'x'
    expect(filtered.value).toHaveLength(1)
    // Change items AND change query - computed will re-read both inputs
    items = [{ id: 'y', name: 'Y', description: '', count: 0 }]
    query.value = 'z'
    expect(filtered.value).toHaveLength(0)
    query.value = 'y'
    expect(filtered.value).toHaveLength(1)
    expect(filtered.value[0]!.id).toBe('y')
  })
})

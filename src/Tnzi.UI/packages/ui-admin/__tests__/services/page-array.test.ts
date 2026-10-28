import { describe, it, expect } from 'vitest'
import { pageArray } from '../../src/services/_mappers'

/**
 * `pageArray` filters a whole list by the page's keyword before paging it.
 * It used to search the row's JSON text, so a keyword that is a property name
 * ("name", "enabled") or a JSON literal ("true") matched every row, and a
 * stray trailing space matched none.
 */
const rows = [
  { id: '0f3c2a', name: 'Alpha', code: 'alpha', isEnabled: true, order: 1 },
  { id: '9b17d4', name: 'Beta', code: 'beta', isEnabled: false, order: 2 },
]
const query = (searchText: string) => ({ pageIndex: 1, pageSize: 20, searchText, filters: {} })

describe('pageArray keyword filter', () => {
  it('a property name is not a match', () => {
    expect(pageArray(rows, query('name')).totalCount).toBe(0)
    expect(pageArray(rows, query('enabled')).totalCount).toBe(0)
  })

  it('a JSON literal is not a match', () => {
    expect(pageArray(rows, query('true')).totalCount).toBe(0)
  })

  it('matches the row text, ignoring case and surrounding spaces', () => {
    expect(pageArray(rows, query('  ALPHA ')).items.map((r) => r.name)).toEqual(['Alpha'])
  })

  it('a field selector limits the match to the fields it returns', () => {
    const byName = (r: (typeof rows)[number]) => [r.name, r.code]
    expect(pageArray(rows, query('0f3c'), byName).totalCount).toBe(0)
    expect(pageArray(rows, query('bet'), byName).items.map((r) => r.name)).toEqual(['Beta'])
  })

  it('a blank keyword keeps every row and pages them', () => {
    const page = pageArray(rows, { ...query('   '), pageSize: 1, pageIndex: 2 })
    expect(page.totalCount).toBe(2)
    expect(page.items.map((r) => r.name)).toEqual(['Beta'])
  })
})

import { describe, it, expect } from 'vitest'
import { groupAvatarLayout } from '../../../src/components/chat/group-avatar'

describe('groupAvatarLayout (WeChat-style composite grid)', () => {
  it('returns one full-size cell for a single member', () => {
    expect(groupAvatarLayout(1)).toEqual([{ left: 0, top: 0, size: 1 }])
  })

  it.each([
    [2, 2],
    [3, 3],
    [4, 4],
    [5, 5],
    [6, 6],
    [7, 7],
    [8, 8],
    [9, 9],
  ])('returns %i cells for %i members', (count, expected) => {
    expect(groupAvatarLayout(count)).toHaveLength(expected)
  })

  it('clamps out-of-range counts to 1..9', () => {
    expect(groupAvatarLayout(0)).toHaveLength(1)
    expect(groupAvatarLayout(-3)).toHaveLength(1)
    expect(groupAvatarLayout(25)).toHaveLength(9)
  })

  it('uses 2-column cells for 2-4 members and 3-column cells for 5-9', () => {
    const four = groupAvatarLayout(4)[0].size
    const nine = groupAvatarLayout(9)[0].size
    expect(four).toBeGreaterThan(0.4)
    expect(nine).toBeLessThan(0.35)
  })

  it('keeps every cell inside the container', () => {
    for (let n = 1; n <= 9; n++) {
      for (const c of groupAvatarLayout(n)) {
        expect(c.left).toBeGreaterThanOrEqual(0)
        expect(c.top).toBeGreaterThanOrEqual(0)
        expect(c.left + c.size).toBeLessThanOrEqual(1.0001)
        expect(c.top + c.size).toBeLessThanOrEqual(1.0001)
      }
    }
  })

  it('horizontally centers the single top cell of a 3-member layout', () => {
    const cells = groupAvatarLayout(3)
    // Row layout [1, 2]: first cell centered, bottom two side by side.
    const top = cells[0]
    expect(top.left).toBeCloseTo((1 - top.size) / 2, 5)
    expect(cells[1].top).toBeCloseTo(cells[2].top, 5)
    expect(cells[1].top).toBeGreaterThan(top.top)
  })

  it('vertically centers the block (2 members sit mid-height)', () => {
    const [a, b] = groupAvatarLayout(2)
    expect(a.top).toBeCloseTo(b.top, 5)
    expect(a.top).toBeCloseTo((1 - a.size) / 2, 5)
  })

  it('lays out 9 members as a full 3x3 grid', () => {
    const cells = groupAvatarLayout(9)
    const rows = new Set(cells.map((c) => c.top.toFixed(4)))
    const cols = new Set(cells.map((c) => c.left.toFixed(4)))
    expect(rows.size).toBe(3)
    expect(cols.size).toBe(3)
  })
})

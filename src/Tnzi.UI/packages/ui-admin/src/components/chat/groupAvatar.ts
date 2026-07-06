/**
 * WeChat-style composite group-avatar layout.
 *
 * Given a member count (1-9) returns one cell per member as fractions of the
 * container edge (0-1), row-major. 1-4 members use a 2-column grid, 5-9 a
 * 3-column grid; partial rows are horizontally centered and the whole block is
 * vertically centered, matching the WeChat arrangement:
 *
 *   1 -> [1]     2 -> [2]      3 -> [1,2]    4 -> [2,2]   5 -> [2,3]
 *   6 -> [3,3]   7 -> [1,3,3]  8 -> [2,3,3]  9 -> [3,3,3]
 */
export interface GroupAvatarCell {
  /** Left offset as a fraction of the container edge (0-1). */
  left: number
  /** Top offset as a fraction of the container edge (0-1). */
  top: number
  /** Cell edge as a fraction of the container edge (0-1). */
  size: number
}

const ROWS_BY_COUNT: Record<number, number[]> = {
  1: [1],
  2: [2],
  3: [1, 2],
  4: [2, 2],
  5: [2, 3],
  6: [3, 3],
  7: [1, 3, 3],
  8: [2, 3, 3],
  9: [3, 3, 3],
}

/** Default gap between cells / container edge, as a fraction of the edge.
 *  Callers rendering to pixels pass `1 / sizePx` for an exact 1px gap. */
const DEFAULT_GAP = 0.02

export function groupAvatarLayout(count: number, gap: number = DEFAULT_GAP): GroupAvatarCell[] {
  const n = Math.max(1, Math.min(9, Math.floor(count)))
  if (n === 1) return [{ left: 0, top: 0, size: 1 }]

  const rows = ROWS_BY_COUNT[n] ?? [3, 3, 3]
  const cols = n <= 4 ? 2 : 3
  const cell = (1 - gap * (cols + 1)) / cols
  const totalHeight = rows.length * cell + (rows.length - 1) * gap
  const top0 = (1 - totalHeight) / 2

  const cells: GroupAvatarCell[] = []
  rows.forEach((cellsInRow, rowIndex) => {
    const rowWidth = cellsInRow * cell + (cellsInRow - 1) * gap
    const left0 = (1 - rowWidth) / 2
    for (let i = 0; i < cellsInRow; i++) {
      cells.push({
        left: left0 + i * (cell + gap),
        top: top0 + rowIndex * (cell + gap),
        size: cell,
      })
    }
  })
  return cells
}

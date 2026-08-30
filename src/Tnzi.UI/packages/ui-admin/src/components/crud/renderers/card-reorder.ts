/**
 * Drag-to-reorder contract for the card grid.
 *
 * Lives in its own module rather than inside the SFC because a type declared in
 * `<script setup>` is not importable from other modules - `TCardPage` has to
 * name this type to forward the event, and consumers have to name it to type
 * their handler.
 */

/** Payload of `TCardRenderer` / `TCardPage`'s `reorder` event. */
export interface CardReorderPayload<T, TId extends string | number = string | number> {
  /**
   * Ids of every currently visible card, in their new order - send this to a
   * `reorder` endpoint. It is the visible page, not necessarily the whole set;
   * the server merges it back by slot so records outside those positions keep
   * theirs.
   */
  orderedIds: TId[]
  /** The visible rows in their new order (already applied optimistically). */
  items: T[]
  /** Index the dragged card came from. */
  from: number
  /** Index it was dropped at. */
  to: number
  /** The dragged row. */
  moved: T
  /**
   * Put the list back the way it was before this drag. Call it when the persist
   * call fails - otherwise the screen keeps showing an order the server
   * rejected, and nothing tells the operator their change was lost.
   */
  revert: () => void
}

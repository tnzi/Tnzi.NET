/**
 * `@tnzi/ui-ai/utils` - stateless helpers shared across component domains.
 *
 * The rule for what lives here: **cross-domain pure functions only**. A helper
 * used by a single domain stays next to that domain's components
 * (`components/cli/timeline.ts`, `components/workflow/calcBezierPath.ts`,
 * `components/chat/composer-types.ts`), where it is maintained alongside the
 * thing it serves. Anything two or more domains reach for belongs here.
 *
 * This directory absorbed the former `src/lib/`, which held the same kind of
 * function under a second name with no distinguishing rule. Worse, `lib/` had
 * no barrel, so two of its three helpers (`scheduleFrame`, `fileIconForName`)
 * were unreachable from outside the package despite being generic.
 */

export { formatCompactNumber } from './format';
export { scheduleFrame } from './scheduleFrame';
export { fileIconForName } from './file-icon';
export {
  normalizeCjkSpacing,
  stripInvisibleControlChars,
  normalizeTimeFormat,
} from './markdown-normalizers';

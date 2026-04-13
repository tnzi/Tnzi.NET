/**
 * calcBezierPath -- Shared bezier path calculation for workflow edges
 */

import { getBezierPath, type Position } from '@vue-flow/core';

export function calcBezierPath(
  sourceX: number,
  sourceY: number,
  sourcePosition: Position,
  targetX: number,
  targetY: number,
  targetPosition: Position,
): string {
  const [edgePath] = getBezierPath({ sourceX, sourceY, sourcePosition, targetX, targetY, targetPosition });
  return edgePath;
}

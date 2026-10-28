/**
 * Step-id references outside the steps themselves.
 *
 * A workflow's graph configuration (conditional edges and loops) names steps by
 * id, alongside each step's `dependsOn`. Renaming or deleting a step in the
 * editor rewrote `dependsOn` but left the configuration pointing at an id that
 * no longer exists: the router's routes went nowhere and the backend's
 * validation only reported it after the save.
 *
 * Ids compare the way the backend compares them (ordinal, ignoring case), so a
 * reference spelled with different casing is still the same step.
 *
 * Both helpers return a new configuration and never mutate the one passed in.
 */
import type { WorkflowGraphConfigurationDto } from '@tnzi/core/services/ai'

export function sameStepId(a: string | null | undefined, b: string | null | undefined): boolean {
  if (a == null || b == null) return false
  return a.toLowerCase() === b.toLowerCase()
}

export interface GraphRewrite {
  configuration: WorkflowGraphConfigurationDto | null
  /** Whether any reference was rewritten (the caller then sends the configuration). */
  changed: boolean
}

/** Point every reference to `oldId` at `newId`. */
export function renameStepInGraph(
  configuration: WorkflowGraphConfigurationDto | null | undefined,
  oldId: string,
  newId: string,
): GraphRewrite {
  if (!configuration) return { configuration: configuration ?? null, changed: false }
  let changed = false
  const swap = (id: string): string => {
    if (!sameStepId(id, oldId)) return id
    changed = true
    return newId
  }

  const conditionalEdges = configuration.conditionalEdges?.map((edge) => ({
    ...edge,
    fromNodeId: swap(edge.fromNodeId),
    routes: Object.fromEntries(Object.entries(edge.routes ?? {}).map(([key, target]) => [key, swap(target)])),
    defaultTarget: edge.defaultTarget == null ? edge.defaultTarget : swap(edge.defaultTarget),
  }))
  const loops = configuration.loops
    ? Object.fromEntries(
        Object.entries(configuration.loops).map(([loopId, loop]) => [loopId, { ...loop, nodeIds: loop.nodeIds.map(swap) }]),
      )
    : configuration.loops

  return { configuration: { ...configuration, conditionalEdges, loops }, changed }
}

/**
 * Drop every reference to a deleted step, the way deleting it drops it from
 * the other steps' `dependsOn`: an edge leaving it goes, a route or default
 * target into it goes, and it leaves every loop (a loop left with no steps
 * goes too).
 */
export function removeStepFromGraph(
  configuration: WorkflowGraphConfigurationDto | null | undefined,
  stepId: string,
): GraphRewrite {
  if (!configuration) return { configuration: configuration ?? null, changed: false }
  let changed = false
  const keep = (id: string | null | undefined): boolean => {
    if (!sameStepId(id, stepId)) return true
    changed = true
    return false
  }

  const conditionalEdges = configuration.conditionalEdges
    ?.filter((edge) => keep(edge.fromNodeId))
    .map((edge) => ({
      ...edge,
      routes: Object.fromEntries(Object.entries(edge.routes ?? {}).filter(([, target]) => keep(target))),
      defaultTarget: keep(edge.defaultTarget) ? edge.defaultTarget : null,
    }))
  const loops = configuration.loops
    ? Object.fromEntries(
        Object.entries(configuration.loops)
          .map(([loopId, loop]) => [loopId, { ...loop, nodeIds: loop.nodeIds.filter((id) => keep(id)) }] as const)
          .filter(([, loop]) => loop.nodeIds.length > 0),
      )
    : configuration.loops

  return { configuration: { ...configuration, conditionalEdges, loops }, changed }
}

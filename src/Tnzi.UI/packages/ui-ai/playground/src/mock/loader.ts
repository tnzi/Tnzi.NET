import type { MockScenario } from './types'
import { allScenarios } from './scenarios/index'

export interface LoadedScenarioIndex {
  scenarios: readonly MockScenario[]
  byId: Map<string, MockScenario>
  byCategory: Map<string, readonly MockScenario[]>
}

function validate(scenario: MockScenario): void {
  if (!scenario.meta?.id) throw new Error('Scenario missing meta.id')
  if (!scenario.meta.title) throw new Error(`Scenario ${scenario.meta.id} missing title`)
  if (!Array.isArray(scenario.events)) throw new Error(`Scenario ${scenario.meta.id} missing events array`)
  let lastAt = -Infinity
  for (const event of scenario.events) {
    if (typeof event.at !== 'number') throw new Error(`Scenario ${scenario.meta.id} event missing at`)
    if (event.at < lastAt) {
      throw new Error(`Scenario ${scenario.meta.id} events not monotonically ordered by at`)
    }
    lastAt = event.at
  }
}

export function loadScenarios(): LoadedScenarioIndex {
  const byId = new Map<string, MockScenario>()
  const byCategoryMut = new Map<string, MockScenario[]>()
  for (const scenario of allScenarios) {
    validate(scenario)
    if (byId.has(scenario.meta.id)) {
      throw new Error(`Duplicate scenario id: ${scenario.meta.id}`)
    }
    byId.set(scenario.meta.id, scenario)
    const bucket = byCategoryMut.get(scenario.meta.category) ?? []
    bucket.push(scenario)
    byCategoryMut.set(scenario.meta.category, bucket)
  }
  const byCategory = new Map<string, readonly MockScenario[]>()
  for (const [k, v] of byCategoryMut) byCategory.set(k, v)
  return { scenarios: allScenarios, byId, byCategory }
}

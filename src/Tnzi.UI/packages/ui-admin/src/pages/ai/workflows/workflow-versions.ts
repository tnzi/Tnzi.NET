/**
 * Reading a workflow version snapshot for display.
 *
 * The backend stores each version as a JSON snapshot of the whole definition
 * (`name`, `description`, `steps`, `executionMode`, `isEnabled`,
 * `configuration`). Property casing follows the server's JSON options, and
 * snapshots written by older builds carry `steps` / `configuration` as a JSON
 * *string* instead of an embedded value, so both casings and both encodings
 * are accepted here. A snapshot that does not parse is reported as such
 * rather than rendered as an empty definition.
 */

export interface WorkflowVersionSnapshot {
  /** False when the snapshot text is not valid JSON (the raw text is kept in `pretty`). */
  parsed: boolean
  name?: string
  description?: string | null
  executionMode?: string
  isEnabled?: boolean
  /** Undefined when the steps could not be read as a list. */
  stepCount?: number
  /** Indented JSON with embedded-string columns expanded, for the raw view. */
  pretty: string
}

function readProp(root: Record<string, unknown>, pascal: string): unknown {
  if (pascal in root) return root[pascal]
  const camel = pascal.charAt(0).toLowerCase() + pascal.slice(1)
  return root[camel]
}

/** Older snapshots embed JSON columns as strings; expand them so they read as data. */
function expandEmbedded(value: unknown): unknown {
  if (typeof value !== 'string') return value
  try {
    return JSON.parse(value) as unknown
  } catch {
    return value
  }
}

export function summarizeVersionSnapshot(definition: string | null | undefined): WorkflowVersionSnapshot {
  if (!definition) return { parsed: false, pretty: '' }

  let root: unknown
  try {
    root = JSON.parse(definition)
  } catch {
    return { parsed: false, pretty: definition }
  }
  if (!root || typeof root !== 'object' || Array.isArray(root)) {
    return { parsed: false, pretty: definition }
  }

  const obj = root as Record<string, unknown>
  const steps = expandEmbedded(readProp(obj, 'Steps'))
  const configuration = expandEmbedded(readProp(obj, 'Configuration'))
  const name = readProp(obj, 'Name')
  const description = readProp(obj, 'Description')
  const executionMode = readProp(obj, 'ExecutionMode')
  const isEnabled = readProp(obj, 'IsEnabled')

  const expanded: Record<string, unknown> = { ...obj }
  for (const key of Object.keys(expanded)) {
    const lower = key.toLowerCase()
    if (lower === 'steps') expanded[key] = steps
    else if (lower === 'configuration') expanded[key] = configuration
  }

  return {
    parsed: true,
    name: typeof name === 'string' ? name : undefined,
    description: typeof description === 'string' ? description : null,
    executionMode: typeof executionMode === 'string' ? executionMode : undefined,
    isEnabled: typeof isEnabled === 'boolean' ? isEnabled : undefined,
    stepCount: Array.isArray(steps) ? steps.length : undefined,
    pretty: JSON.stringify(expanded, null, 2),
  }
}

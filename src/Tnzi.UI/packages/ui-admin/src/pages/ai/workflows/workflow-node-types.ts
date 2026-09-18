/**
 * Workflow node-type catalog used by the visual editor.
 *
 * The node kind is the backend's runtime contract: `WorkflowNodeExecutor`
 * resolves the `IWorkflowNode` implementation from
 * `WorkflowStepDto.configuration['nodeType']` (lowercase, matching the
 * `WorkflowNodeTypes` constants in Tnzi.AI) and falls back to the agent node
 * when the key is absent. The editor therefore writes that exact key: a step
 * whose kind lives under any other key looks fine in the editor and runs as a
 * plain agent call. (Until 2026-09-12 the editor wrote `__nodeType`, which no
 * backend commit ever read; `normalizeStepConfiguration` migrates it on load.)
 *
 * The editor also persists canvas positions on the same `configuration` bag
 * under `__x` / `__y` so the layout survives save/load. Those two really are
 * UI-only hints: the backend tolerates extra keys and never reads them.
 *
 * The catalog is static so the editor can show a typed property panel + icon
 * + color coding without round-tripping a schema endpoint.
 */
export type WorkflowNodeKind =
  | 'agent'
  | 'review'
  | 'approval'
  | 'router'
  | 'parallel'
  | 'synthesize'
  | 'debate'
  | 'conditional'
  | 'transform'

/**
 * Which property fields make sense for a given node kind. The editor renders
 * a property form that hides irrelevant fields rather than showing every
 * possible field for every node - the resulting form matches how the backend
 * executors actually consume the step.
 */
export interface WorkflowNodeFieldFlags {
  agent: boolean
  providerModel: boolean
  instructions: boolean
  condition: boolean
  requiresApproval: boolean
  retry: boolean
  timeout: boolean
}

export interface WorkflowNodeTypeMeta {
  kind: WorkflowNodeKind
  /** i18n key under `ai.workflows.nodeTypes.<kind>` for label display. */
  labelKey: string
  /** mdi icon name. */
  icon: string
  /** Pastel background color used on the canvas node card. */
  color: string
  /** Fields the property panel should render for this kind. */
  fields: WorkflowNodeFieldFlags
}

const allFields: WorkflowNodeFieldFlags = {
  agent: true,
  providerModel: true,
  instructions: true,
  condition: true,
  requiresApproval: true,
  retry: true,
  timeout: true,
}

export const workflowNodeTypes: readonly WorkflowNodeTypeMeta[] = [
  {
    kind: 'agent',
    labelKey: 'agent',
    icon: 'mdi:robot',
    color: '#4f8af7',
    fields: { ...allFields, condition: false },
  },
  {
    kind: 'review',
    labelKey: 'review',
    icon: 'mdi:account-eye-outline',
    color: '#9c27b0',
    fields: { ...allFields, condition: false },
  },
  {
    kind: 'approval',
    labelKey: 'approval',
    icon: 'mdi:gavel',
    color: '#ff9800',
    fields: {
      agent: false,
      providerModel: false,
      instructions: true,
      condition: false,
      requiresApproval: true,
      retry: false,
      timeout: true,
    },
  },
  {
    kind: 'router',
    labelKey: 'router',
    icon: 'mdi:source-branch',
    color: '#00bcd4',
    fields: {
      agent: false,
      providerModel: false,
      instructions: false,
      condition: true,
      requiresApproval: false,
      retry: false,
      timeout: true,
    },
  },
  {
    kind: 'parallel',
    labelKey: 'parallel',
    icon: 'mdi:call-split',
    color: '#3f51b5',
    fields: {
      agent: false,
      providerModel: false,
      instructions: true,
      condition: false,
      requiresApproval: false,
      retry: true,
      timeout: true,
    },
  },
  {
    kind: 'synthesize',
    labelKey: 'synthesize',
    icon: 'mdi:set-merge',
    color: '#009688',
    fields: {
      agent: true,
      providerModel: true,
      instructions: true,
      condition: false,
      requiresApproval: false,
      retry: true,
      timeout: true,
    },
  },
  {
    kind: 'debate',
    labelKey: 'debate',
    icon: 'mdi:forum-outline',
    color: '#e91e63',
    fields: {
      agent: false,
      providerModel: true,
      instructions: true,
      condition: false,
      requiresApproval: false,
      retry: true,
      timeout: true,
    },
  },
  {
    kind: 'conditional',
    labelKey: 'conditional',
    icon: 'mdi:directions-fork',
    color: '#795548',
    fields: {
      agent: false,
      providerModel: false,
      instructions: false,
      condition: true,
      requiresApproval: false,
      retry: false,
      timeout: false,
    },
  },
  {
    kind: 'transform',
    labelKey: 'transform',
    icon: 'mdi:swap-horizontal',
    color: '#607d8b',
    fields: {
      agent: false,
      providerModel: false,
      instructions: true,
      condition: false,
      requiresApproval: false,
      retry: false,
      timeout: true,
    },
  },
] as const

const byKind = new Map<WorkflowNodeKind, WorkflowNodeTypeMeta>(
  workflowNodeTypes.map((m) => [m.kind, m]),
)

export function getNodeTypeMeta(kind: string | undefined | null): WorkflowNodeTypeMeta {
  const key = (kind ?? 'agent').toLowerCase() as WorkflowNodeKind
  return byKind.get(key) ?? byKind.get('agent')!
}

/**
 * The configuration key the backend resolves the node implementation from.
 * Not `__`-prefixed on purpose: it is a runtime contract, not a UI hint.
 */
export const NODE_TYPE_KEY = 'nodeType'

/**
 * Key the editor wrote before 2026-09-12. Never read by the backend, so every
 * kind chosen in the editor executed as an agent node. Read-only here: loading
 * a definition migrates it to `NODE_TYPE_KEY` and drops it, so the next save
 * heals the stored definition.
 */
export const LEGACY_NODE_TYPE_KEY = '__nodeType'

/** Canvas position hints. UI-only; the backend tolerates and ignores them. */
export const POS_X_KEY = '__x'
export const POS_Y_KEY = '__y'

/**
 * Normalize a step's configuration bag on load: copy a legacy `__nodeType`
 * into `nodeType` when the latter is absent and drop the legacy key. Returns
 * a new object; the input is not mutated.
 */
export function normalizeStepConfiguration(
  configuration: Record<string, string> | null | undefined,
): Record<string, string> {
  const { [LEGACY_NODE_TYPE_KEY]: legacy, ...rest } = configuration ?? {}
  const current = rest[NODE_TYPE_KEY]
  if ((current === undefined || current === null || current === '') && legacy) {
    return { ...rest, [NODE_TYPE_KEY]: legacy }
  }
  return rest
}

/** Resolve the editor kind of a step from its `nodeType`; unknown / absent = agent. */
export function getStepKind(
  step: { configuration?: Record<string, string> | null } | null | undefined,
): WorkflowNodeKind {
  if (!step) return 'agent'
  const raw = step.configuration?.[NODE_TYPE_KEY]
  return getNodeTypeMeta(raw ?? 'agent').kind
}

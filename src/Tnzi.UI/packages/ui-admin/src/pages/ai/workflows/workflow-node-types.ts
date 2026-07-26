/**
 * Workflow node-type catalog used by the visual editor.
 *
 * The backend stores node type in `WorkflowStepDto.configuration['__nodeType']`
 * (lowercase, matching `WorkflowNodeTypes` constants in Tnzi.AI). We keep the
 * catalog static so the editor can show a typed property panel + icon + color
 * coding without round-tripping a schema endpoint.
 *
 * The editor also persists canvas positions on the same `configuration` bag
 * under `__x` / `__y` so the layout survives save/load. Both are stripped on
 * the wire by the backend graph builder (`CloneStep` removes `__originalIndex`
 * - we reuse that pattern for `__x` / `__y` / `__nodeType` so they remain
 * UI-only hints that don't leak into the runtime).
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

/** Stored under WorkflowStepDto.configuration. UI-only - stripped on save? No
 * - backend tolerates extra keys (CloneStep just removes `__originalIndex`).
 * We keep these prefixed with `__` so they're visually distinct from
 * user-defined config keys. */
export const NODE_TYPE_KEY = '__nodeType'
export const POS_X_KEY = '__x'
export const POS_Y_KEY = '__y'

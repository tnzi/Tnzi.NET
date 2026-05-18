import { h } from 'vue'
import type { ColumnDef } from '../../../headless/useColumnSettings'
import type { FormSchemaItem } from '../../_shared/form-schema'
import TStatusBadge from '../../../components/display/TStatusBadge.vue'

const EVAL_STATUS_MAP: Record<string, { type: 'info' | 'success' | 'warning' | 'error' | 'default'; label: string }> = {
  '0': { type: 'info',    label: 'Running' },
  '1': { type: 'success', label: 'Completed' },
  '2': { type: 'error',   label: 'Failed' },
}

/**
 * Phase 5 Task 5.14 — EvalViewer page config.
 *
 * Shape derives from @tnzi/core/services/ai EvaluationRunDto /
 * CreateEvaluationRunDto / EvaluationCaseDto. Stage 1 backend prereq added
 * useAdminEvaluationApi.create + runBatch. The bridge wires create() to the
 * backend's create-and-run-in-one-shot endpoint — there is no separate
 * "create then run later" flow, and bridge.evaluations.run(id) rejects.
 *
 * Plan deviations vs the task sketch:
 *   - The plan listed columns score / passCount / failCount / duration; the
 *     real DTO has averageScore, caseCount, passedCount (no failCount — it's
 *     caseCount - passedCount), and duration (timespan string).
 *   - "metrics array" / "model" / "datasetId" do not exist on the DTO.
 *     CreateEvaluationRunDto only has agentId, optional versionNumber, and
 *     `cases: EvaluationCaseDto[]` where each case is { input,
 *     expectedOutput? }. The MVP form takes agentId, versionNumber, and a
 *     single inline test case (input + expectedOutput) — multi-case bulk
 *     entry via JSON textarea is a follow-up task ("rich eval editor").
 *   - Update is intentionally not exposed (immutable runs); delete IS
 *     exposed via the bridge — useCrudPage requires a deleteData fn.
 */
export const evalRunColumns: ColumnDef[] = [
  { key: 'id', title: 'columns.id' },
  { key: 'agentId', title: 'columns.agentId' },
  { key: 'caseCount', title: 'columns.caseCount' },
  { key: 'passedCount', title: 'columns.passedCount' },
  { key: 'averageScore', title: 'columns.averageScore' },
  {
    key: 'status',
    title: 'columns.status',
    width: 120,
    render: (row) => {
      const raw = String(row.status ?? '')
      const m = EVAL_STATUS_MAP[raw] ?? { type: 'default' as const, label: row.status as string ?? '—' }
      return h(TStatusBadge, { value: raw, type: m.type, label: m.label })
    },
  },
  { key: 'duration', title: 'columns.duration' },
  { key: 'creationTime', title: 'columns.creationTime' },
]

export const evalRunStatusOptions: Array<{ label: string; value: number }> = [
  { label: 'Running', value: 0 },
  { label: 'Completed', value: 1 },
  { label: 'Failed', value: 2 },
]

/**
 * MVP create-and-run form. agentId + a single inline test case is the
 * minimum viable shape; a follow-up task can ship a rich multi-case editor.
 * On submit, the page assembles `{ agentId, versionNumber, cases: [{...}] }`
 * before handing to bridge.evaluations.create.
 */
export const evalRunFormSchema: FormSchemaItem[] = [
  { key: 'agentId', label: 'Agent ID', type: 'text', required: true },
  { key: 'versionNumber', label: 'Version (optional)', type: 'number', min: 1 },
  { key: 'caseInput', label: 'Test Input', type: 'textarea', required: true },
  { key: 'caseExpected', label: 'Expected Output (optional)', type: 'textarea' },
]

/**
 * i18n keys (Task 5.16 will sweep into tnzi.admin.modules.ai.evaluation.*):
 *
 * Page meta:
 *   modules.ai.evaluation.pageTitle
 *   modules.ai.evaluation.createAndRunNote
 *
 * Columns:
 *   modules.ai.evaluation.columns.id
 *   modules.ai.evaluation.columns.agentId
 *   modules.ai.evaluation.columns.caseCount
 *   modules.ai.evaluation.columns.passedCount
 *   modules.ai.evaluation.columns.averageScore
 *   modules.ai.evaluation.columns.status
 *   modules.ai.evaluation.columns.duration
 *   modules.ai.evaluation.columns.creationTime
 *
 * Form fields:
 *   modules.ai.evaluation.form.agentId
 *   modules.ai.evaluation.form.versionNumber
 *   modules.ai.evaluation.form.caseInput
 *   modules.ai.evaluation.form.caseExpected
 */

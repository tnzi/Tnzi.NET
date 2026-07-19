/**
 * Test-only stub for @tnzi/ui-ai.
 *
 * The real package depends on tailwind CSS, vue-flow, and other heavy assets
 * that aren't needed in unit tests. The vitest alias points imports of
 * `@tnzi/ui-ai` here, and individual tests can still vi.mock('@tnzi/ui-ai')
 * to override these exports with bespoke stubs.
 */
export const TWorkflowCanvas = {
  name: 'WorkflowCanvasStub',
  props: ['workflowId'],
  template: '<div data-test="canvas-stub-default" :data-workflow-id="workflowId" />',
}

/**
 * What a run of tool steps (`TToolRun`) tells the steps folded into it.
 *
 * A step inside a run is a row of the run's box rather than a line of its own
 * (`TToolStep` reads this to draw itself as one), and what it keeps in view (its
 * `after` slot: the files it handed over, the pictures it took) goes to the run's
 * outlet under the run's line, so folding the run never hides it.
 */
import { defineComponent, provide, type InjectionKey, type PropType, type Ref } from 'vue';

export interface ToolRunContext {
  /** Where a folded step's `after` content is shown: under the run's line. Null until the run is on the page. */
  readonly outlet: Ref<HTMLElement | null>;
  /**
   * Bumped when the run is opened while it holds a single step: that step opens too,
   * so one search is one click from its results, not two.
   */
  readonly openSole: Ref<number>;
}

export const TOOL_RUN_KEY: InjectionKey<ToolRunContext | null> = Symbol('tnzi-ui-ai:tool-run');

/**
 * Hands a run's context to the steps rendered in its slot, and only to those: the run's
 * own always-visible steps sit outside it and stay ordinary steps.
 */
export const ToolRunScope = defineComponent({
  name: 'ToolRunScope',
  props: {
    context: { type: Object as PropType<ToolRunContext | null>, default: null },
  },
  setup(props, { slots }) {
    provide(TOOL_RUN_KEY, props.context);
    return () => slots.default?.();
  },
});

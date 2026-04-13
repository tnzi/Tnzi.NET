import { defineComponent, h } from 'vue';

/**
 * Resizable primitives — structural pass-through wrappers.
 * Naive UI does not have a direct Resizable equivalent;
 * these render flex containers so layout doesn't break.
 */

export const ResizablePanelGroup = defineComponent({
  name: 'ResizablePanelGroup',
  inheritAttrs: true,
  props: { direction: { type: String, default: 'horizontal' } },
  setup(props, { slots, attrs }) {
    return () =>
      h(
        'div',
        {
          ...attrs,
          class: ['flex', props.direction === 'vertical' ? 'flex-col' : 'flex-row', attrs.class],
          style: [{ width: '100%', height: '100%' }, attrs.style],
        },
        slots.default?.(),
      );
  },
});

export const ResizablePanel = defineComponent({
  name: 'ResizablePanel',
  inheritAttrs: true,
  props: { defaultSize: { type: Number, default: undefined } },
  setup(props, { slots, attrs }) {
    return () =>
      h(
        'div',
        {
          ...attrs,
          class: ['flex-1 min-w-0 min-h-0 overflow-auto', attrs.class],
          style: [props.defaultSize ? { flex: `0 0 ${props.defaultSize}%` } : {}, attrs.style],
        },
        slots.default?.(),
      );
  },
});

export const ResizableHandle = defineComponent({
  name: 'ResizableHandle',
  inheritAttrs: true,
  setup(_, { attrs }) {
    return () =>
      h('div', {
        ...attrs,
        class: ['shrink-0 bg-border', attrs.class],
        style: [{ width: '1px' }, attrs.style],
      });
  },
});

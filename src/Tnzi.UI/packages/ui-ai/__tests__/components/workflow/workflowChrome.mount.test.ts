/**
 * The workflow toolbar, zoom controls and default node, drawn without naive:
 * every toolbar button is a named icon button that emits its event, the zoom
 * controls drive vue-flow's viewport, and the node shows its label,
 * description and status as a plain scoped-style card.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, h, nextTick, type App } from 'vue';

const viewport = vi.hoisted(() => ({ zoomIn: vi.fn(), zoomOut: vi.fn(), fitView: vi.fn() }));

vi.mock('@vue-flow/core', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@vue-flow/core')>()),
  useVueFlow: () => viewport,
}));

const { default: TWorkflowToolbar } = await import('../../../src/components/workflow/TWorkflowToolbar.vue');
const { default: TWorkflowControls } = await import('../../../src/components/workflow/TWorkflowControls.vue');
const { default: TWorkflowNode } = await import('../../../src/components/workflow/TWorkflowNode.vue');

let app: App | null = null;
let host: HTMLElement | null = null;

function mount(render: () => unknown): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({ render });
  app.mount(host);
  return host;
}

function button(root: HTMLElement, label: string): HTMLButtonElement {
  const found = root.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`);
  if (!found) throw new Error(`no button "${label}"`);
  return found;
}

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
  vi.clearAllMocks();
});

describe('TWorkflowToolbar', () => {
  it('emits add, delete, connect and run from named icon buttons', async () => {
    const handlers = { onAdd: vi.fn(), onDelete: vi.fn(), onConnect: vi.fn(), onRun: vi.fn() };
    const root = mount(() => h(TWorkflowToolbar, handlers));

    button(root, 'Add node').click();
    button(root, 'Delete node').click();
    button(root, 'Connect').click();
    button(root, 'Run workflow').click();
    await nextTick();

    for (const handler of Object.values(handlers)) expect(handler).toHaveBeenCalledOnce();
  });
});

describe('TWorkflowControls', () => {
  it('zooms in, zooms out and fits the view', async () => {
    const root = mount(() => h(TWorkflowControls));

    button(root, 'Zoom in').click();
    button(root, 'Zoom out').click();
    button(root, 'Fit to view').click();
    await nextTick();

    expect(viewport.zoomIn).toHaveBeenCalledOnce();
    expect(viewport.zoomOut).toHaveBeenCalledOnce();
    expect(viewport.fitView).toHaveBeenCalledOnce();
  });
});

describe('TWorkflowNode', () => {
  it('renders label, description and status as a plain card', () => {
    const root = mount(() => h(TWorkflowNode, { label: 'Fetch', description: 'Reads the feed', status: 'failed' }));
    const node = root.querySelector<HTMLElement>('.t-wf-node')!;

    expect(node.classList.contains('t-wf-node--failed')).toBe(true);
    expect(node.querySelector('.t-wf-node__label')!.textContent).toBe('Fetch');
    expect(node.querySelector('.t-wf-node__desc')!.textContent).toBe('Reads the feed');
    expect(root.querySelector('[class^="n-"], [class*=" n-"]')).toBeNull();
  });

  it('is pending without a status and renders its content and footer slots', () => {
    const root = mount(() =>
      h(TWorkflowNode, { label: 'Wait' }, { content: () => h('i', { class: 'c' }), footer: () => h('b', { class: 'f' }) }),
    );
    expect(root.querySelector('.t-wf-node--pending')).not.toBeNull();
    expect(root.querySelector('.t-wf-node__desc')).toBeNull();
    expect(root.querySelector('.c')).not.toBeNull();
    expect(root.querySelector('.f')).not.toBeNull();
  });
});

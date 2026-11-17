/**
 * The agent, context, knowledge and plan panels are drawn with the package's
 * own controls (`TPopover`, `TDropdownMenu`, `TProgress`, card buttons). These
 * pin the interactions each one offers: search and pick an agent, toggle a
 * knowledge base, open a platform from the menu, open the tool list, the quota
 * bar's tone, the checkpoint, the queue's sections and the plan's toggle.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, h, nextTick, type App } from 'vue';

const { default: TAgentSelector } = await import('../../src/components/agent/TAgentSelector.vue');
const { default: TCheckpoint } = await import('../../src/components/agent/TCheckpoint.vue');
const { default: TOpenIn } = await import('../../src/components/agent/TOpenIn.vue');
const { default: TMcpToolPanel } = await import('../../src/components/context/TMcpToolPanel.vue');
const { default: TQuotaStatus } = await import('../../src/components/context/TQuotaStatus.vue');
const { default: TKnowledgePanel } = await import('../../src/components/knowledge/TKnowledgePanel.vue');
const { default: TAgentQueue } = await import('../../src/components/reasoning/TAgentQueue.vue');
const { default: TAgentPlan } = await import('../../src/components/reasoning/TAgentPlan.vue');

let apps: App[] = [];

function mount(render: () => unknown): HTMLElement {
  const host = document.createElement('div');
  document.body.appendChild(host);
  const app = createApp({ render });
  app.mount(host);
  apps.push(app);
  return host;
}

const flush = async () => {
  await nextTick();
  await nextTick();
};

afterEach(() => {
  for (const app of apps) app.unmount();
  apps = [];
  vi.restoreAllMocks();
  document.body.innerHTML = '';
});

function type(input: HTMLInputElement, value: string): void {
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

describe('TAgentSelector', () => {
  const agents = [
    { id: 'a', name: 'Researcher', description: 'Finds sources', tags: ['web'] },
    { id: 'b', name: 'Coder', description: 'Writes code' },
  ];

  it('filters by the search and emits the picked agent', async () => {
    const onSelect = vi.fn();
    const host = mount(() => h(TAgentSelector, { agents, selectedId: 'b', onSelect }));
    await flush();
    expect(host.querySelectorAll('[data-agent-id]')).toHaveLength(2);
    expect(host.querySelector('[data-agent-id="b"]')?.getAttribute('aria-pressed')).toBe('true');
    expect(host.querySelector('[data-agent-id="a"] .t-tag')?.textContent).toBe('web');

    type(host.querySelector('input.t-input') as HTMLInputElement, 'sources');
    await flush();
    expect(host.querySelectorAll('[data-agent-id]')).toHaveLength(1);
    (host.querySelector('[data-agent-id="a"]') as HTMLButtonElement).click();
    expect(onSelect).toHaveBeenCalledWith('a');

    type(host.querySelector('input.t-input') as HTMLInputElement, 'zzz');
    await flush();
    expect(host.querySelectorAll('[data-agent-id]')).toHaveLength(0);
    expect(host.textContent).toContain('No data');
  });
});

describe('TKnowledgePanel', () => {
  it('toggles a base and shows the document count in words', async () => {
    const onToggle = vi.fn();
    const host = mount(() =>
      h(TKnowledgePanel, {
        bases: [{ id: 'kb1', name: 'Docs', documentCount: 12 }],
        selectedIds: ['kb1'],
        onToggle,
      }),
    );
    await flush();
    const card = host.querySelector('[data-base-id="kb1"]') as HTMLButtonElement;
    expect(card.getAttribute('aria-pressed')).toBe('true');
    expect(card.textContent).toContain('12 docs');
    card.click();
    expect(onToggle).toHaveBeenCalledWith('kb1');
  });

  it('says when there are no bases', async () => {
    const host = mount(() => h(TKnowledgePanel, { bases: [] }));
    await flush();
    expect(host.textContent).toContain('No knowledge bases');
  });
});

describe('TOpenIn', () => {
  it('opens the menu and opens the picked platform with the query', async () => {
    const open = vi.spyOn(window, 'open').mockReturnValue(null);
    const onSelect = vi.fn();
    const host = mount(() => h(TOpenIn, { query: 'a b', onSelect }));
    await flush();
    const trigger = host.querySelector('button[aria-haspopup="menu"]') as HTMLButtonElement;
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
    trigger.click();
    await flush();
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    const item = document.querySelector('[role="menuitem"][data-platform="claude"]') as HTMLButtonElement;
    expect(item.textContent).toContain('Claude');
    item.click();
    await flush();
    expect(open).toHaveBeenCalledWith('https://claude.ai/new?q=a%20b', '_blank', 'noopener,noreferrer');
    expect(onSelect).toHaveBeenCalledWith('claude', 'https://claude.ai/new?q=a%20b');
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
  });
});

describe('TMcpToolPanel', () => {
  it('opens a popover listing the tools and closes on Escape', async () => {
    const host = mount(() =>
      h(TMcpToolPanel, {
        status: 'error',
        tools: [
          { name: 'search', description: 'Web search', status: 'available' },
          { name: 'fetch', status: 'error' },
        ],
      }),
    );
    await flush();
    const trigger = host.querySelector('button[aria-haspopup="dialog"]') as HTMLButtonElement;
    expect(trigger.textContent).toContain('2');
    expect(host.querySelector('[role="dialog"]')).toBeNull();
    trigger.click();
    await flush();
    const panel = host.querySelector('[role="dialog"]') as HTMLElement;
    expect(panel.getAttribute('aria-label')).toBe('MCP Tools');
    expect(panel.querySelectorAll('.t-mcp-tool')).toHaveLength(2);
    expect(panel.querySelector('.t-tag--danger')?.textContent).toContain('Error');
    panel.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await flush();
    expect(host.querySelector('[role="dialog"]')).toBeNull();
  });
});

describe('TQuotaStatus', () => {
  const tone = (host: HTMLElement) =>
    [...(host.querySelector('.t-progress')?.classList ?? [])].find((c) => c.startsWith('t-progress--'));

  it('draws an accent bar with the figures for a screen reader', async () => {
    const host = mount(() => h(TQuotaStatus, { used: 25, limit: 100, resetDate: 'May 1' }));
    await flush();
    expect(tone(host)).toBe('t-progress--accent');
    expect((host.querySelector('.t-progress__fill') as HTMLElement).style.width).toBe('25%');
    expect(host.querySelector('.sr-only')?.textContent).toBe('Usage: 25 / 100 · Remaining: 75 · Resets on May 1');
  });

  it('warns near the limit', async () => {
    const host = mount(() => h(TQuotaStatus, { used: 95, limit: 100 }));
    await flush();
    expect(tone(host)).toBe('t-progress--warning');
  });

  it('turns danger and says so once the limit is reached', async () => {
    const host = mount(() => h(TQuotaStatus, { used: 120, limit: 100 }));
    await flush();
    expect(tone(host)).toBe('t-progress--danger');
    expect(host.querySelector('.t-quota')?.classList.contains('is-exceeded')).toBe(true);
    expect(host.textContent).toContain('100%');
    expect(host.querySelector('.sr-only')?.textContent).toContain('Quota exceeded');
  });
});

describe('TCheckpoint', () => {
  it('is a named button that emits click', async () => {
    const onClick = vi.fn();
    const host = mount(() => h(TCheckpoint, { tooltip: 'Restore here', onClick }));
    await flush();
    const button = host.querySelector('button') as HTMLButtonElement;
    expect(button.getAttribute('aria-label')).toBe('Restore here');
    button.click();
    expect(onClick).toHaveBeenCalledTimes(1);
  });
});

describe('TAgentQueue', () => {
  it('collapses a section from its header and emits item clicks', async () => {
    const onItemClick = vi.fn();
    const item = { id: '1', title: 'Write tests' };
    const host = mount(() =>
      h(TAgentQueue, { sections: [{ label: 'Pending', items: [item] }], onItemClick }),
    );
    await flush();
    const header = host.querySelector('.t-agent-queue__section-btn') as HTMLButtonElement;
    const list = host.querySelector('.t-agent-queue__items') as HTMLElement;
    expect(host.querySelector('.t-agent-queue__body')?.classList.contains('t-ai-scroll')).toBe(true);
    expect(header.getAttribute('aria-expanded')).toBe('true');
    (host.querySelector('.t-agent-queue__item') as HTMLElement).click();
    expect(onItemClick).toHaveBeenCalledWith(item);
    header.click();
    await flush();
    expect(header.getAttribute('aria-expanded')).toBe('false');
    expect(list.style.display).toBe('none');
  });
});

describe('TAgentPlan', () => {
  it('hides and shows its body, footer actions included', async () => {
    const onApprove = vi.fn();
    const host = mount(() =>
      h(TAgentPlan, { title: 'Plan', description: 'Three steps' }, {
        default: () => h('p', 'Step one'),
        footer: () => h('button', { class: 'approve', onClick: onApprove }, 'Approve'),
      }),
    );
    await flush();
    const body = host.querySelector('.t-agent-plan__body') as HTMLElement;
    (host.querySelector('.approve') as HTMLButtonElement).click();
    expect(onApprove).toHaveBeenCalledTimes(1);
    const toggle = host.querySelector('button[aria-label="Toggle plan"]') as HTMLButtonElement;
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    toggle.click();
    await flush();
    expect(body.style.display).toBe('none');
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
  });
});

import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  useUiAdapter,
  setActiveUiAdapter,
  resetUiAdapter,
  type IUiAdapter,
} from '../../src/adapters/ui-adapter';
import { useMessage, resetMessageAdapter } from '../../src/adapters/message';
import { useDialog, resetDialogAdapter } from '../../src/adapters/dialog';

describe('UiAdapterRuntime', () => {
  beforeEach(() => {
    resetUiAdapter();
    resetMessageAdapter();
    resetDialogAdapter();
  });

  it('should provide default adapter with all sub-adapters', () => {
    const adapter = useUiAdapter();
    expect(adapter).toBeDefined();
    expect(adapter.message).toBeDefined();
    expect(adapter.dialog).toBeDefined();
    expect(adapter.theme).toBeDefined();
  });

  it('should delegate message calls through default adapter', () => {
    const adapter = useUiAdapter();
    expect(() => adapter.message.info('test')).not.toThrow();
    expect(() => adapter.message.success('test')).not.toThrow();
  });

  it('should allow setting custom adapter', () => {
    const custom: IUiAdapter = {
      message: {
        info: vi.fn(),
        success: vi.fn(),
        warning: vi.fn(),
        error: vi.fn(),
        loading: vi.fn(() => vi.fn()),
      },
      dialog: {
        alert: vi.fn(async () => {}),
        confirm: vi.fn(async () => true),
        prompt: vi.fn(async () => 'test'),
      },
      theme: {
        applyTheme: vi.fn(),
        getResolvedTheme: vi.fn(() => 'light' as const),
      },
    };
    setActiveUiAdapter(custom);
    const adapter = useUiAdapter();
    expect(adapter).toBe(custom);
    adapter.message.success('done');
    expect(custom.message.success).toHaveBeenCalledWith('done');
  });

  it('should reset to default adapter', () => {
    const custom: IUiAdapter = {
      message: { info: vi.fn(), success: vi.fn(), warning: vi.fn(), error: vi.fn(), loading: vi.fn(() => vi.fn()) },
      dialog: { alert: vi.fn(async () => {}), confirm: vi.fn(async () => true), prompt: vi.fn(async () => 'test') },
      theme: { applyTheme: vi.fn(), getResolvedTheme: vi.fn(() => 'light' as const) },
    };
    setActiveUiAdapter(custom);
    resetUiAdapter();
    const adapter = useUiAdapter();
    expect(adapter).not.toBe(custom);
    expect(adapter.message).toBeDefined();
  });

  it('should sync individual adapter runtimes for backward compat', () => {
    const custom: IUiAdapter = {
      message: { info: vi.fn(), success: vi.fn(), warning: vi.fn(), error: vi.fn(), loading: vi.fn(() => vi.fn()) },
      dialog: { alert: vi.fn(async () => {}), confirm: vi.fn(async () => true), prompt: vi.fn(async () => null) },
      theme: { applyTheme: vi.fn(), getResolvedTheme: vi.fn(() => 'light' as const) },
    };
    setActiveUiAdapter(custom);

    // Individual useMessage/useDialog should return the composite's adapters
    const msg = useMessage();
    const dlg = useDialog();
    msg.info('test');
    expect(custom.message.info).toHaveBeenCalledWith('test');
  });
});

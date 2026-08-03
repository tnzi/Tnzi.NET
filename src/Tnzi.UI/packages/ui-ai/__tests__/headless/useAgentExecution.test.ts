import { describe, it, expect } from 'vitest';
import { useAgentExecution } from '../../src/headless/useAgentExecution';
import type { AgentExecutionEvent } from '../../src/headless/useAgentExecution';

describe('useAgentExecution', () => {
  it('should initialize with empty state', () => {
    const exec = useAgentExecution();
    expect(exec.currentAgent.value).toBeNull();
    expect(exec.toolCalls.value).toEqual([]);
    expect(exec.handoffHistory.value).toEqual([]);
    expect(exec.isExecuting.value).toBe(false);
    expect(exec.reasoning.value).toBe('');
  });

  it('should set isExecuting on first non-terminal event', () => {
    const exec = useAgentExecution();
    exec.handleEvent({ type: 'delta' });
    expect(exec.isExecuting.value).toBe(true);
  });

  it('should handle agent_switch event', () => {
    const exec = useAgentExecution();
    exec.handleEvent({ type: 'agent_switch', agentName: 'ResearchAgent' });
    expect(exec.currentAgent.value).toBe('ResearchAgent');
    expect(exec.handoffHistory.value).toHaveLength(1);
    expect(exec.handoffHistory.value[0]?.agentName).toBe('ResearchAgent');
  });

  it('should track multiple agent switches', () => {
    const exec = useAgentExecution();
    exec.handleEvent({ type: 'agent_switch', agentName: 'Agent-A' });
    exec.handleEvent({ type: 'agent_switch', agentName: 'Agent-B' });
    exec.handleEvent({ type: 'agent_switch', agentName: 'Agent-C' });
    expect(exec.handoffHistory.value).toHaveLength(3);
    expect(exec.currentAgent.value).toBe('Agent-C');
  });

  it('should handle tool_call_start event', () => {
    const exec = useAgentExecution();
    exec.handleEvent({ type: 'tool_call_start', toolNames: ['web_search', 'file_read'] });
    expect(exec.toolCalls.value).toHaveLength(2);
    expect(exec.toolCalls.value[0]?.name).toBe('web_search');
    expect(exec.toolCalls.value[0]?.endedAt).toBeNull();
    expect(exec.toolCalls.value[1]?.name).toBe('file_read');
  });

  it('should handle tool_call_end event', () => {
    const exec = useAgentExecution();
    exec.handleEvent({ type: 'tool_call_start', toolNames: ['web_search'] });
    exec.handleEvent({
      type: 'tool_call_end',
      toolCalls: [{ name: 'web_search', durationMs: 150, output: '{"result": "ok"}' }],
    });
    expect(exec.toolCalls.value[0]?.endedAt).not.toBeNull();
    expect(exec.toolCalls.value[0]?.durationMs).toBe(150);
    expect(exec.toolCalls.value[0]?.output).toBe('{"result": "ok"}');
  });

  it('should accumulate reasoning deltas (flushed on done)', () => {
    const exec = useAgentExecution();
    exec.handleEvent({ type: 'reasoning_delta', reasoningDelta: 'Let me ' });
    exec.handleEvent({ type: 'reasoning_delta', reasoningDelta: 'think...' });
    // Reasoning deltas are rAF-batched; flush via done event
    exec.handleEvent({ type: 'done' });
    expect(exec.reasoning.value).toBe('Let me think...');
  });

  it('should set isExecuting to false on done event', () => {
    const exec = useAgentExecution();
    exec.handleEvent({ type: 'delta' });
    expect(exec.isExecuting.value).toBe(true);
    exec.handleEvent({ type: 'done' });
    expect(exec.isExecuting.value).toBe(false);
  });

  it('should set isExecuting to false on error event', () => {
    const exec = useAgentExecution();
    exec.handleEvent({ type: 'delta' });
    exec.handleEvent({ type: 'error', errorMessage: 'Something failed' });
    expect(exec.isExecuting.value).toBe(false);
  });

  it('should reset all state', () => {
    const exec = useAgentExecution();
    exec.handleEvent({ type: 'agent_switch', agentName: 'TestAgent' });
    exec.handleEvent({ type: 'tool_call_start', toolNames: ['tool1'] });
    exec.handleEvent({ type: 'reasoning_delta', reasoningDelta: 'thinking' });

    exec.reset();
    expect(exec.currentAgent.value).toBeNull();
    expect(exec.toolCalls.value).toEqual([]);
    expect(exec.handoffHistory.value).toEqual([]);
    expect(exec.isExecuting.value).toBe(false);
    expect(exec.reasoning.value).toBe('');
  });

  it('should handle full execution lifecycle', () => {
    const exec = useAgentExecution();

    // Agent starts
    exec.handleEvent({ type: 'agent_switch', agentName: 'MainAgent' });
    expect(exec.isExecuting.value).toBe(true);

    // Reasoning
    exec.handleEvent({ type: 'reasoning_delta', reasoningDelta: 'I need to search...' });

    // Tool call
    exec.handleEvent({ type: 'tool_call_start', toolNames: ['web_search'] });
    exec.handleEvent({
      type: 'tool_call_end',
      toolCalls: [{ name: 'web_search', durationMs: 200 }],
    });

    // Handoff
    exec.handleEvent({ type: 'agent_switch', agentName: 'WriterAgent' });

    // Done
    exec.handleEvent({ type: 'done' });

    expect(exec.isExecuting.value).toBe(false);
    expect(exec.currentAgent.value).toBe('WriterAgent');
    expect(exec.handoffHistory.value).toHaveLength(2);
    expect(exec.toolCalls.value).toHaveLength(1);
    expect(exec.toolCalls.value[0]?.durationMs).toBe(200);
    expect(exec.reasoning.value).toBe('I need to search...');
  });

  it('should use immutable updates for toolCalls', () => {
    const exec = useAgentExecution();
    exec.handleEvent({ type: 'tool_call_start', toolNames: ['tool1'] });
    const firstRef = exec.toolCalls.value;
    exec.handleEvent({ type: 'tool_call_start', toolNames: ['tool2'] });
    expect(exec.toolCalls.value).not.toBe(firstRef);
  });

  it('should use immutable updates for handoffHistory', () => {
    const exec = useAgentExecution();
    exec.handleEvent({ type: 'agent_switch', agentName: 'A' });
    const firstRef = exec.handoffHistory.value;
    exec.handleEvent({ type: 'agent_switch', agentName: 'B' });
    expect(exec.handoffHistory.value).not.toBe(firstRef);
  });
});

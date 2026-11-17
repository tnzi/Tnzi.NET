import { describe, expect, it } from 'vitest';
import { effectScope, nextTick, ref } from 'vue';
import { useReasoningActivity, type ReasoningActivitySource } from '../../src/headless/useReasoningActivity';

function track(initial: ReasoningActivitySource) {
  const message = ref<ReasoningActivitySource>(initial);
  const scope = effectScope();
  const { isReasoning } = scope.run(() => useReasoningActivity(() => message.value))!;
  const update = async (patch: Partial<ReasoningActivitySource>) => {
    message.value = { ...message.value, ...patch };
    await nextTick();
  };
  return { isReasoning, update, stop: () => scope.stop() };
}

const streaming: ReasoningActivitySource = { content: '', reasoning: '', toolCalls: null, isStreaming: true };

describe('useReasoningActivity', () => {
  it('runs while reasoning is what arrives', async () => {
    const { isReasoning, update } = track(streaming);
    expect(isReasoning.value).toBe(false);
    await update({ reasoning: 'Let me' });
    expect(isReasoning.value).toBe(true);
    await update({ reasoning: 'Let me look' });
    expect(isReasoning.value).toBe(true);
  });

  it('stops once text arrives after it, though the message still streams', async () => {
    const { isReasoning, update } = track(streaming);
    await update({ reasoning: 'Thinking' });
    await update({ content: 'Here' });
    expect(isReasoning.value).toBe(false);
    await update({ content: 'Here is more' });
    expect(isReasoning.value).toBe(false);
  });

  it('stops once a tool call arrives after it', async () => {
    const { isReasoning, update } = track(streaming);
    await update({ reasoning: 'Thinking' });
    await update({ toolCalls: [{ name: 'grep' }] });
    expect(isReasoning.value).toBe(false);
  });

  it('runs again when the turn goes back to thinking, and stops after the next output', async () => {
    const { isReasoning, update } = track(streaming);
    await update({ reasoning: 'First' });
    await update({ content: 'Looking.', toolCalls: [{ name: 'read_file' }] });
    expect(isReasoning.value).toBe(false);
    await update({ reasoning: 'First\n\nThen' });
    expect(isReasoning.value).toBe(true);
    await update({ toolCalls: [{ name: 'read_file' }, { name: 'edit' }] });
    expect(isReasoning.value).toBe(false);
  });

  it('when reasoning and output both grew in one update, the output wins', async () => {
    const { isReasoning, update } = track(streaming);
    await update({ reasoning: 'Thinking' });
    await update({ reasoning: 'Thinking done', content: 'Answer' });
    expect(isReasoning.value).toBe(false);
  });

  it('is done when the message stops streaming: finished, stopped or failed', async () => {
    const { isReasoning, update } = track(streaming);
    await update({ reasoning: 'Thinking' });
    expect(isReasoning.value).toBe(true);
    await update({ isStreaming: false });
    expect(isReasoning.value).toBe(false);
  });

  it('a message loaded from history is done', () => {
    const { isReasoning } = track({ content: 'Answer', reasoning: 'Thought', toolCalls: null, isStreaming: false });
    expect(isReasoning.value).toBe(false);
  });

  it('on first use while streaming, runs only when nothing but reasoning has arrived', () => {
    expect(track({ ...streaming, reasoning: 'Thinking' }).isReasoning.value).toBe(true);
    expect(track({ ...streaming, reasoning: 'Thinking', content: 'Answer' }).isReasoning.value).toBe(false);
    expect(track({ ...streaming, reasoning: 'Thinking', toolCalls: [{ name: 'grep' }] }).isReasoning.value).toBe(false);
  });

  it('a row cleared for another answer is not running', async () => {
    const { isReasoning, update } = track({ ...streaming, reasoning: 'Thinking' });
    await update({ reasoning: '' });
    expect(isReasoning.value).toBe(false);
  });
});

/**
 * The steps where an external agent turns to the person (the Claude Code CLI's
 * AskUserQuestion, EnterPlanMode, ExitPlanMode): they read as what happened, in both
 * languages, and a plan sent back or a question left unanswered reads as an outcome,
 * not a failure, even though the CLI reports that call as failed.
 */
import { describe, expect, it } from 'vitest';
import {
  ASK_USER_QUESTION_TOOL,
  describeAskUserQuestion,
  describeEnterPlanMode,
  describeExitPlanMode,
  ENTER_PLAN_MODE_TOOL,
  EXIT_PLAN_MODE_TOOL,
  resolveToolEntry,
} from '../../src/components/reasoning/built-in-tool-renderers';
import {
  describeToolStep,
  isDeclinedStep,
  shownStepState,
  stepPhrase,
  summarizeToolRun,
  type ToolRunStep,
} from '../../src/components/reasoning/tool-steps';
import { en } from '../../src/locales/en';
import { zhCn } from '../../src/locales/zh-cn';
import type { ToolCallInfo } from '../../src/headless/useChat';
import type { ToolCallState } from '../../src/components/reasoning/tool-call-status';

const question: ToolCallInfo = {
  name: ASK_USER_QUESTION_TOOL,
  input: { questions: [{ question: 'Which color do you prefer?', header: 'Color', options: [{ label: 'Red' }], multiSelect: false }] },
};
const plan: ToolCallInfo = { name: EXIT_PLAN_MODE_TOOL, input: { plan: '# Plan: create hello.py\n\n1. Write it.' } };
const enter: ToolCallInfo = { name: ENTER_PLAN_MODE_TOOL, input: {} };

const step = (toolCall: ToolCallInfo, state: ToolCallState, locale = en): ToolRunStep => ({
  description: describeToolStep(toolCall, resolveToolEntry(toolCall.name, {}).describe, locale),
  state,
});

describe('the interaction tools are registered and named', () => {
  it('have their own words instead of "Used Ask User Question"', () => {
    expect(stepPhrase(step(question, 'running'))).toBe('Asking you a question');
    expect(stepPhrase(step(question, 'completed'))).toBe('Got your answer');
    expect(stepPhrase(step(enter, 'completed'))).toBe('Started a plan');
    expect(stepPhrase(step(plan, 'running'))).toBe('Waiting for the plan to be approved');
    expect(stepPhrase(step(plan, 'completed'))).toBe('Plan approved');
  });

  it('in Chinese too', () => {
    expect(stepPhrase(step(question, 'completed', zhCn))).toBe('收到了你的回答');
    expect(stepPhrase(step(plan, 'failed', zhCn))).toBe('计划已退回');
    expect(stepPhrase(step(enter, 'running', zhCn))).toBe('正在开始制定计划');
  });

  it('say what they acted on: the first question, the plan title', () => {
    expect(describeAskUserQuestion(question, en).subject).toBe('Color');
    expect(describeAskUserQuestion({ name: ASK_USER_QUESTION_TOOL, input: { questions: [{ question: 'Why?' }] } }, en).subject).toBe('Why?');
    expect(describeExitPlanMode(plan, en).subject).toBe('Plan: create hello.py');
    // The CLI hands over `{}` when there was no plan file: nothing to name, no raw JSON either
    expect(describeExitPlanMode({ name: EXIT_PLAN_MODE_TOOL, input: {} }, en).subject).toBeNull();
    expect(describeEnterPlanMode(enter, en).subject).toBeNull();
  });
});

describe('a plan sent back or a question left unanswered', () => {
  it('reads as an outcome and is drawn as finished', () => {
    const sentBack = step(plan, 'failed');
    expect(isDeclinedStep(sentBack)).toBe(true);
    expect(stepPhrase(sentBack)).toBe('Plan sent back');
    expect(shownStepState(sentBack)).toBe('completed');
    expect(stepPhrase(step(question, 'failed'))).toBe('Question left unanswered');
  });

  it('is not a failure in the run summary, and is said apart from an approval', () => {
    const summary = summarizeToolRun([step(plan, 'failed'), step(plan, 'completed')], en);
    expect(summary.unrecoveredCount).toBe(0);
    expect(summary.failedCount).toBe(0);
    expect(summary.parts.every((part) => part.tone === 'done')).toBe(true);
    expect(summary.text).toBe('Plan sent back, plan approved');
  });

  it('leaves real failures of other tools alone, and a tool without `declined` still fails', () => {
    expect(isDeclinedStep(step(enter, 'failed'))).toBe(false);
    expect(shownStepState(step(enter, 'failed'))).toBe('failed');
    expect(summarizeToolRun([step(enter, 'failed')], en).unrecoveredCount).toBe(1);
  });

  it('a product can give the same meaning to its own tool, and a blank `declined` is ignored', () => {
    const own = (declined: string): ToolRunStep => ({
      description: describeToolStep({ name: 'confirm' }, { running: 'Asking', done: 'Confirmed', failed: 'Failed', declined }, en),
      state: 'failed',
    });
    expect(stepPhrase(own('Not confirmed'))).toBe('Not confirmed');
    expect(isDeclinedStep(own('  '))).toBe(false);
  });
});

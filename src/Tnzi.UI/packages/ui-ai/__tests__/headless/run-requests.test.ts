import { describe, expect, it, vi } from 'vitest';
import type { ChatPermissionRequestDto } from '@tnzi/core/services/ai';
import {
  createRunRequestSource,
  permissionCardModel,
  permissionDetail,
  planCardModel,
  questionCardModel,
  runRequestFromEvent,
  runRequestItem,
  runRequestKind,
  type RunRequestCardLabels,
} from '../../src/headless/run-requests';
import { runRequestCardLabels } from '../../src/components/approvals/labels';
import { en } from '../../src/locales/en';

const LABELS: RunRequestCardLabels = runRequestCardLabels(en.approvals);

function request(over: Partial<ChatPermissionRequestDto> = {}): ChatPermissionRequestDto {
  return {
    runId: 'run-1',
    requestId: 'req-1',
    kind: 'permission',
    status: 'Pending',
    tool: 'Bash',
    input: { command: 'rm -rf build' },
    description: 'Remove the build directory',
    options: [
      { id: 'allow_once', name: 'Allow', kind: 'allow_once' },
      { id: 'reject_once', name: 'Deny', kind: 'reject_once' },
    ],
    expiresAt: '2026-11-08T10:05:00Z',
    createdAt: '2026-11-08T10:00:00Z',
    ...over,
  };
}

describe('run requests as pending items', () => {
  it('tells the kinds apart; a question without questions, or an unknown kind, is a permission', () => {
    expect(runRequestKind({ kind: 'question', questions: [{ question: 'q', options: [], multiSelect: false }] })).toBe('question');
    expect(runRequestKind({ kind: 'question', questions: [] })).toBe('permission');
    expect(runRequestKind({ kind: 'plan' })).toBe('plan');
    expect(runRequestKind({ kind: 'new_thing' })).toBe('permission');
  });

  it('keys an item by its request, and a stream event carries the run it came on', () => {
    const item = runRequestItem(request());
    expect(item).toMatchObject({ key: 'run:req-1', kind: 'permission', expiresAt: '2026-11-08T10:05:00Z', createdAt: '2026-11-08T10:00:00Z' });

    const { runId: _r, createdAt: _c, ...eventData } = request({ requestId: 'req-2' });
    const pushed = runRequestFromEvent('run-9', eventData, '2026-11-08T10:01:00Z');
    expect(pushed.key).toBe('run:req-2');
    expect(pushed.data.runId).toBe('run-9');
    expect(pushed.createdAt).toBe('2026-11-08T10:01:00Z');
  });
});

describe('the framework source', () => {
  it('lists the thread pending requests; a thread the server does not know waits for nothing', async () => {
    const api = {
      getPendingPermissions: vi.fn(async (threadId: string) =>
        threadId === 't1' ? { succeeded: true, code: 200, data: [request()] } : { succeeded: false, code: 404, errorCode: 'AI_THREAD_NOT_FOUND' },
      ),
      answerRunPermission: vi.fn(),
    };
    const source = createRunRequestSource(api as never);
    expect((await source.load('t1')).map((i) => i.key)).toEqual(['run:req-1']);
    expect(await source.load('gone')).toEqual([]);

    api.getPendingPermissions.mockResolvedValueOnce({ succeeded: false, code: 500, message: 'boom' } as never);
    await expect(source.load('t1')).rejects.toThrow('boom');
  });

  it('answers through the run endpoint; a closed request is `closed`, any other refusal throws its message', async () => {
    const api = {
      getPendingPermissions: vi.fn(),
      answerRunPermission: vi.fn(async () => ({ succeeded: true, code: 200 })),
    };
    const source = createRunRequestSource(api as never);
    const item = runRequestItem(request());

    await expect(source.answer(item, { optionId: 'reject_once', message: 'Keep it' })).resolves.toBe('answered');
    expect(api.answerRunPermission).toHaveBeenCalledWith('run-1', 'req-1', { optionId: 'reject_once', message: 'Keep it', answers: null });

    api.answerRunPermission.mockResolvedValueOnce({ succeeded: false, code: 409, errorCode: 'AI_CLI_PERMISSION_REQUEST_CLOSED' } as never);
    await expect(source.answer(item, { optionId: 'allow_once' })).resolves.toBe('closed');

    api.answerRunPermission.mockResolvedValueOnce({ succeeded: false, code: 400, errorCode: 'AI_CLI_QUESTION_ANSWERS_INVALID', message: 'Answer every question.' } as never);
    await expect(source.answer(item, { optionId: 'submit' })).rejects.toMatchObject({ message: 'Answer every question.', code: 'AI_CLI_QUESTION_ANSWERS_INVALID' });
  });
});

describe('default cards', () => {
  it('a command: what the tool does, the description, the command in the box, allow first', () => {
    const card = permissionCardModel(runRequestItem(request()), LABELS);
    expect(card).toMatchObject({
      key: 'run:req-1',
      icon: 'lucide:terminal',
      title: 'The agent wants to run a command',
      summary: 'Remove the build directory',
      detail: { text: 'rm -rf build', mono: true },
      expiresAt: '2026-11-08T10:05:00Z',
    });
    expect(card.choices.map((c) => c.label)).toEqual(['Allow once', 'Deny']);
  });

  it('a file outside the folder: the file, the blocked path when it differs, the reason', () => {
    const card = permissionCardModel(
      runRequestItem(request({ tool: 'Write', input: { file_path: '/etc/hosts', content: 'x' }, blockedPath: '/etc', reason: 'Outside the project', description: null })),
      LABELS,
    );
    expect(card.title).toBe('The agent wants to write a file');
    expect(card.fields).toEqual([
      { label: 'File', value: '/etc/hosts', mono: true },
      { label: 'Path', value: '/etc', mono: true },
      { label: 'Why', value: 'Outside the project' },
    ]);
  });

  it('a search says its query once, and an unknown tool is named with a shield', () => {
    const search = permissionCardModel(runRequestItem(request({ tool: 'WebSearch', input: { query: 'vue 4' }, description: 'vue 4' })), LABELS);
    expect(search.summary).toBeNull();
    expect(search.detail).toEqual({ text: 'vue 4', mono: false });

    const other = permissionCardModel(runRequestItem(request({ tool: 'mcp__db__drop', input: { table: 'users' }, description: null })), LABELS);
    expect(other.title).toBe('The agent wants to use mcp__db__drop');
    expect(other.icon).toBe('lucide:shield-question');
    expect(other.detail?.text).toContain('"table": "users"');
  });

  it('the input boxes: content, nothing, or JSON', () => {
    expect(permissionDetail({ input: { new_string: 'b' } })).toEqual({ text: 'b', mono: true });
    expect(permissionDetail({ input: {} })).toBeNull();
    expect(permissionDetail({ input: null })).toBeNull();
  });

  it('questions say how many there are', () => {
    const one = questionCardModel(runRequestItem(request({ kind: 'question', questions: [{ question: 'a', options: [], multiSelect: false }] })), LABELS);
    expect(one.title).toBe('The agent has a question for you');
    const two = questionCardModel(
      runRequestItem(request({ kind: 'question', questions: [{ question: 'a', options: [], multiSelect: false }, { question: 'b', options: [], multiSelect: true }] })),
      LABELS,
    );
    expect(two.title).toBe('The agent has 2 questions for you');
  });

  it('a plan from the agent thinking is called a draft; a plan of its own is not', () => {
    const options = [
      { id: 'approve', name: 'a', kind: 'allow_once' },
      { id: 'reject', name: 'r', kind: 'reject_once' },
    ];
    const draft = planCardModel(runRequestItem(request({ kind: 'plan', plan: '1. Do it', planSource: 'assistant_thinking', options })), LABELS);
    expect(draft.note).toBe('This is the draft the agent wrote while thinking.');
    expect(draft.choices.map((c) => c.label)).toEqual(['Approve', 'Revise']);
    const own = planCardModel(runRequestItem(request({ kind: 'plan', plan: '1. Do it', planSource: 'input', options })), LABELS);
    expect(own.note).toBeNull();
    expect(planCardModel(runRequestItem(request({ kind: 'plan', plan: null, options })), LABELS).plan).toBe('');
  });
});

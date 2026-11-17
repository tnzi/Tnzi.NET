import { describe, expect, it } from 'vitest';
import { composerPrimaryAction, type ComposerPrimaryActionInput } from '../../src/components/chat/composer-primary-action';

const idle: ComposerPrimaryActionInput = {
  running: false,
  listening: false,
  hasContent: false,
  voiceAvailable: true,
  disabled: false,
};

const decide = (over: Partial<ComposerPrimaryActionInput>) => composerPrimaryAction({ ...idle, ...over });

describe('composerPrimaryAction', () => {
  it('is the microphone when the box is empty and speech input works', () => {
    expect(decide({})).toEqual({ kind: 'voice', enabled: true });
  });

  it('is a disabled send when the box is empty and there is no speech input', () => {
    expect(decide({ voiceAvailable: false })).toEqual({ kind: 'send', enabled: false });
  });

  it('is send once there is content (text or an attachment alone)', () => {
    expect(decide({ hasContent: true })).toEqual({ kind: 'send', enabled: true });
    expect(decide({ hasContent: true, voiceAvailable: false })).toEqual({ kind: 'send', enabled: true });
  });

  it('keeps send in place but disabled when sending is refused', () => {
    expect(decide({ hasContent: true, disabled: true })).toEqual({ kind: 'send', enabled: false });
  });

  it('offers the microphone while sending is refused, since dictating only fills the box', () => {
    expect(decide({ disabled: true })).toEqual({ kind: 'voice', enabled: true });
  });

  it('ends the dictation while listening, even once the dictation has produced text', () => {
    expect(decide({ listening: true })).toEqual({ kind: 'end-dictation', enabled: true });
    expect(decide({ listening: true, hasContent: true })).toEqual({ kind: 'end-dictation', enabled: true });
  });

  it('is stop while a turn runs, whatever is in the box', () => {
    for (const over of [{}, { hasContent: true }, { listening: true }, { disabled: true }, { voiceAvailable: false }]) {
      expect(decide({ running: true, ...over })).toEqual({ kind: 'stop', enabled: true });
    }
  });

  it('stays stop while a turn runs and queueing is on but the box is empty', () => {
    expect(decide({ running: true, queueWhileRunning: true })).toEqual({ kind: 'stop', enabled: true });
  });

  it('is queue while a turn runs, queueing is on and there is content', () => {
    expect(decide({ running: true, queueWhileRunning: true, hasContent: true })).toEqual({ kind: 'queue', enabled: true });
    expect(decide({ running: true, queueWhileRunning: true, hasContent: true, voiceAvailable: false })).toEqual({ kind: 'queue', enabled: true });
  });

  it('keeps queue in place but disabled when sending is refused', () => {
    expect(decide({ running: true, queueWhileRunning: true, hasContent: true, disabled: true })).toEqual({ kind: 'queue', enabled: false });
  });

  it('never queues when the turn is not running', () => {
    expect(decide({ queueWhileRunning: true, hasContent: true })).toEqual({ kind: 'send', enabled: true });
  });
});

describe('composerPrimaryAction with a queue that takes words only', () => {
  const base = { running: true, listening: false, hasContent: true, voiceAvailable: false, disabled: false, queueWhileRunning: true };

  it('queues words, but attachments keep it on stop', () => {
    expect(composerPrimaryAction({ ...base, queueAttachments: false }).kind).toBe('queue');
    expect(composerPrimaryAction({ ...base, queueAttachments: false, hasFiles: true }).kind).toBe('stop');
    expect(composerPrimaryAction({ ...base, hasFiles: true }).kind).toBe('queue');
  });
});

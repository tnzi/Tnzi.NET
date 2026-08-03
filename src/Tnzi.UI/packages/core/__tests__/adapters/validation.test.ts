import { describe, it, expect } from 'vitest';
import { createZodSchema } from '../../src/adapters/validation';

describe('createZodSchema', () => {
  // ------------------------------------------
  // String fields
  // ------------------------------------------

  describe('string fields', () => {
    it('should create a required string field', () => {
      const schema = createZodSchema<{ name: string }>({
        name: { required: true },
      });
      expect(schema.safeParse({ name: 'hello' }).success).toBe(true);
      expect(schema.safeParse({}).success).toBe(false);
    });

    it('should enforce min length', () => {
      const schema = createZodSchema<{ name: string }>({
        name: { required: true, min: 3 },
      });
      expect(schema.safeParse({ name: 'ab' }).success).toBe(false);
      expect(schema.safeParse({ name: 'abc' }).success).toBe(true);
    });

    it('should enforce max length', () => {
      const schema = createZodSchema<{ name: string }>({
        name: { required: true, max: 5 },
      });
      expect(schema.safeParse({ name: 'abcdef' }).success).toBe(false);
      expect(schema.safeParse({ name: 'abcde' }).success).toBe(true);
    });

    it('should enforce pattern', () => {
      const schema = createZodSchema<{ code: string }>({
        code: { required: true, pattern: /^[A-Z]{3}$/ },
      });
      expect(schema.safeParse({ code: 'ABC' }).success).toBe(true);
      expect(schema.safeParse({ code: 'abc' }).success).toBe(false);
      expect(schema.safeParse({ code: 'ABCD' }).success).toBe(false);
    });

    it('should validate email', () => {
      const schema = createZodSchema<{ email: string }>({
        email: { required: true, email: true },
      });
      expect(schema.safeParse({ email: 'user@example.com' }).success).toBe(true);
      expect(schema.safeParse({ email: 'not-an-email' }).success).toBe(false);
    });

    it('should validate url', () => {
      const schema = createZodSchema<{ website: string }>({
        website: { required: true, url: true },
      });
      expect(schema.safeParse({ website: 'https://example.com' }).success).toBe(true);
      expect(schema.safeParse({ website: 'not-a-url' }).success).toBe(false);
    });

    it('should allow optional string fields', () => {
      const schema = createZodSchema<{ bio: string }>({
        bio: { required: false },
      });
      expect(schema.safeParse({}).success).toBe(true);
      expect(schema.safeParse({ bio: undefined }).success).toBe(true);
      expect(schema.safeParse({ bio: 'hello' }).success).toBe(true);
    });
  });

  // ------------------------------------------
  // Number fields
  // ------------------------------------------

  describe('number fields', () => {
    it('should create a required number field', () => {
      const schema = createZodSchema<{ age: number }>({
        age: { type: 'number', required: true },
      });
      expect(schema.safeParse({ age: 25 }).success).toBe(true);
      expect(schema.safeParse({}).success).toBe(false);
    });

    it('should enforce min value', () => {
      const schema = createZodSchema<{ age: number }>({
        age: { type: 'number', required: true, min: 0 },
      });
      expect(schema.safeParse({ age: -1 }).success).toBe(false);
      expect(schema.safeParse({ age: 0 }).success).toBe(true);
    });

    it('should enforce max value', () => {
      const schema = createZodSchema<{ age: number }>({
        age: { type: 'number', required: true, max: 150 },
      });
      expect(schema.safeParse({ age: 151 }).success).toBe(false);
      expect(schema.safeParse({ age: 150 }).success).toBe(true);
    });

    it('should reject non-number types', () => {
      const schema = createZodSchema<{ count: number }>({
        count: { type: 'number', required: true },
      });
      expect(schema.safeParse({ count: 'five' }).success).toBe(false);
    });

    it('should allow optional number fields', () => {
      const schema = createZodSchema<{ score: number }>({
        score: { type: 'number', required: false },
      });
      expect(schema.safeParse({}).success).toBe(true);
      expect(schema.safeParse({ score: undefined }).success).toBe(true);
      expect(schema.safeParse({ score: 100 }).success).toBe(true);
    });
  });

  // ------------------------------------------
  // Boolean fields
  // ------------------------------------------

  describe('boolean fields', () => {
    it('should create a required boolean field', () => {
      const schema = createZodSchema<{ active: boolean }>({
        active: { type: 'boolean', required: true },
      });
      expect(schema.safeParse({ active: true }).success).toBe(true);
      expect(schema.safeParse({ active: false }).success).toBe(true);
    });

    it('should allow optional boolean fields', () => {
      const schema = createZodSchema<{ agree: boolean }>({
        agree: { type: 'boolean', required: false },
      });
      expect(schema.safeParse({}).success).toBe(true);
      expect(schema.safeParse({ agree: true }).success).toBe(true);
    });
  });

  // ------------------------------------------
  // Date fields
  // ------------------------------------------

  describe('date fields', () => {
    it('should create a required date field', () => {
      const schema = createZodSchema<{ createdAt: Date }>({
        createdAt: { type: 'date', required: true },
      });
      expect(schema.safeParse({ createdAt: new Date() }).success).toBe(true);
    });

    it('should reject non-date values', () => {
      const schema = createZodSchema<{ createdAt: Date }>({
        createdAt: { type: 'date', required: true },
      });
      expect(schema.safeParse({ createdAt: 'not-a-date' }).success).toBe(false);
    });

    it('should allow optional date fields', () => {
      const schema = createZodSchema<{ updatedAt: Date }>({
        updatedAt: { type: 'date', required: false },
      });
      expect(schema.safeParse({}).success).toBe(true);
      expect(schema.safeParse({ updatedAt: new Date() }).success).toBe(true);
    });
  });

  // ------------------------------------------
  // Rule chaining
  // ------------------------------------------

  describe('rule chaining', () => {
    it('should enforce min + max + pattern on the same field', () => {
      const schema = createZodSchema<{ code: string }>({
        code: { required: true, min: 3, max: 6, pattern: /^[A-Z]+$/ },
      });
      // Too short
      expect(schema.safeParse({ code: 'AB' }).success).toBe(false);
      // Too long
      expect(schema.safeParse({ code: 'ABCDEFG' }).success).toBe(false);
      // Wrong pattern
      expect(schema.safeParse({ code: 'abc' }).success).toBe(false);
      // Valid
      expect(schema.safeParse({ code: 'ABCDE' }).success).toBe(true);
    });

    it('should enforce min + max on a number field', () => {
      const schema = createZodSchema<{ score: number }>({
        score: { type: 'number', required: true, min: 0, max: 100 },
      });
      expect(schema.safeParse({ score: -1 }).success).toBe(false);
      expect(schema.safeParse({ score: 101 }).success).toBe(false);
      expect(schema.safeParse({ score: 50 }).success).toBe(true);
    });
  });

  // ------------------------------------------
  // Custom error messages
  // ------------------------------------------

  describe('custom error messages', () => {
    it('should use custom requiredMessage', () => {
      const schema = createZodSchema<{ name: string }>({
        name: { required: true, requiredMessage: 'Please enter your name' },
      });
      const result = schema.safeParse({});
      expect(result.success).toBe(false);
      if (!result.success) {
        const messages = result.error.issues.map(i => i.message);
        expect(messages).toContain('Please enter your name');
      }
    });

    it('should use generated error messages for min/max', () => {
      const schema = createZodSchema<{ name: string }>({
        name: { required: true, min: 5 },
      });
      const result = schema.safeParse({ name: 'ab' });
      expect(result.success).toBe(false);
      if (!result.success) {
        expect(result.error.issues[0]!.message).toContain('at least 5');
      }
    });
  });

  // ------------------------------------------
  // Multiple fields
  // ------------------------------------------

  describe('multiple fields', () => {
    it('should validate a schema with multiple fields', () => {
      const schema = createZodSchema<{ name: string; age: number; email: string }>({
        name: { required: true, min: 2, max: 50 },
        age: { type: 'number', required: true, min: 0, max: 150 },
        email: { required: true, email: true },
      });

      // All valid
      expect(schema.safeParse({ name: 'Alice', age: 30, email: 'alice@test.com' }).success).toBe(true);

      // Multiple failures
      const result = schema.safeParse({ name: 'A', age: -1, email: 'bad' });
      expect(result.success).toBe(false);
      if (!result.success) {
        const fields = result.error.issues.map(i => i.path[0]);
        expect(fields).toContain('name');
        expect(fields).toContain('age');
        expect(fields).toContain('email');
      }
    });
  });
});

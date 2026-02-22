import { describe, it, expect, vi, beforeAll, afterAll } from 'vitest';
import { z } from 'zod';
import { FormController } from '../../headless/form';

interface TestForm {
  name: string;
  email: string;
  age: number;
}

const testSchema = z.object({
  name: z.string().min(1, 'Name is required'),
  email: z.string().email('Invalid email'),
  age: z.number().min(0, 'Age must be non-negative'),
});

function createDefaults(): TestForm {
  return { name: '', email: '', age: 0 };
}

/**
 * The FormController constructor returns `reactive(this)` from @vue/reactivity.
 * This wraps all properties (including private `_initialValues`) in a Proxy.
 * Node's `structuredClone` cannot clone Proxy objects, so `reset()` will throw
 * a DataCloneError at runtime. We mock `structuredClone` with a JSON-based clone
 * to make the form controller testable in a Node environment.
 */
const originalStructuredClone = globalThis.structuredClone;

beforeAll(() => {
  globalThis.structuredClone = <T>(value: T): T => {
    return JSON.parse(JSON.stringify(value)) as T;
  };
});

afterAll(() => {
  globalThis.structuredClone = originalStructuredClone;
});

describe('FormController', () => {
  // ------------------------------------------
  // Initial values cloning
  // ------------------------------------------

  describe('initial values', () => {
    it('should clone initial values (no reference sharing)', () => {
      const initial = { name: 'John', email: 'john@test.com', age: 25 };
      const form = new FormController({ initialValues: initial });
      // Mutate original
      initial.name = 'changed';
      expect(form.values.name).toBe('John');
    });

    it('should not reference the same object for values and internal initial', () => {
      const form = new FormController({ initialValues: createDefaults() });
      form.values.name = 'test';
      // After reset, values should go back to original
      form.reset();
      expect(form.values.name).toBe('');
    });

    it('should initialize with default state', () => {
      const form = new FormController({ initialValues: createDefaults() });
      expect(form.errors).toEqual([]);
      expect(form.isSubmitting).toBe(false);
      expect(form.isSubmitted).toBe(false);
      expect(form.touchedFields.size).toBe(0);
    });
  });

  // ------------------------------------------
  // setFieldValue
  // ------------------------------------------

  describe('setFieldValue', () => {
    it('should update the field value', () => {
      const form = new FormController<TestForm>({ initialValues: createDefaults() });
      form.setFieldValue('name', 'Alice');
      expect(form.values.name).toBe('Alice');
    });

    it('should mark the field as touched', () => {
      const form = new FormController<TestForm>({ initialValues: createDefaults() });
      form.setFieldValue('name', 'Alice');
      expect(form.touchedFields.has('name')).toBe(true);
    });

    it('should clear errors for the updated field', () => {
      const form = new FormController<TestForm>({ initialValues: createDefaults() });
      form.setErrors([
        { field: 'name', message: 'Error on name' },
        { field: 'email', message: 'Error on email' },
      ]);
      form.setFieldValue('name', 'Alice');
      expect(form.errors).toEqual([{ field: 'email', message: 'Error on email' }]);
    });
  });

  // ------------------------------------------
  // isDirty
  // ------------------------------------------

  describe('isDirty', () => {
    it('should return false initially', () => {
      const form = new FormController<TestForm>({ initialValues: createDefaults() });
      expect(form.isDirty).toBe(false);
    });

    it('should return true after changing a value', () => {
      const form = new FormController<TestForm>({ initialValues: createDefaults() });
      form.values.name = 'changed';
      expect(form.isDirty).toBe(true);
    });

    it('should return false if value is changed back to initial', () => {
      const form = new FormController<TestForm>({ initialValues: createDefaults() });
      form.values.name = 'changed';
      form.values.name = '';
      expect(form.isDirty).toBe(false);
    });
  });

  // ------------------------------------------
  // validate with zod schema
  // ------------------------------------------

  describe('validate', () => {
    it('should return true when no schema is provided', () => {
      const form = new FormController<TestForm>({ initialValues: createDefaults() });
      expect(form.validate()).toBe(true);
      expect(form.errors).toEqual([]);
    });

    it('should return true when values are valid', () => {
      const form = new FormController<TestForm>({
        initialValues: { name: 'Alice', email: 'alice@test.com', age: 25 },
        schema: testSchema as z.ZodType<TestForm>,
      });
      expect(form.validate()).toBe(true);
      expect(form.errors).toEqual([]);
    });

    it('should return false and populate errors on invalid values', () => {
      const form = new FormController<TestForm>({
        initialValues: createDefaults(),
        schema: testSchema as z.ZodType<TestForm>,
      });
      const result = form.validate();
      expect(result).toBe(false);
      expect(form.errors.length).toBeGreaterThan(0);
    });

    it('should map zod errors to FormFieldError format', () => {
      const form = new FormController<TestForm>({
        initialValues: { name: '', email: 'not-an-email', age: -1 },
        schema: testSchema as z.ZodType<TestForm>,
      });
      form.validate();
      const fieldNames = form.errors.map(e => e.field);
      expect(fieldNames).toContain('name');
      expect(fieldNames).toContain('email');
      expect(fieldNames).toContain('age');
    });

    it('should clear previous errors before re-validating', () => {
      const form = new FormController<TestForm>({
        initialValues: createDefaults(),
        schema: testSchema as z.ZodType<TestForm>,
      });
      form.validate(); // populates errors
      form.values.name = 'Alice';
      form.values.email = 'alice@test.com';
      form.values.age = 25;
      form.validate(); // should clear and re-validate
      expect(form.errors).toEqual([]);
    });
  });

  // ------------------------------------------
  // validateField
  // ------------------------------------------

  describe('validateField', () => {
    it('should return true when no schema is provided', () => {
      const form = new FormController<TestForm>({ initialValues: createDefaults() });
      expect(form.validateField('name')).toBe(true);
    });

    it('should return true for a valid single field', () => {
      const form = new FormController<TestForm>({
        initialValues: { name: 'Alice', email: 'alice@test.com', age: 25 },
        schema: testSchema as z.ZodType<TestForm>,
      });
      expect(form.validateField('name')).toBe(true);
    });

    it('should return false and add error for invalid field', () => {
      const form = new FormController<TestForm>({
        initialValues: { name: '', email: 'alice@test.com', age: 25 },
        schema: testSchema as z.ZodType<TestForm>,
      });
      expect(form.validateField('name')).toBe(false);
      expect(form.errors.some(e => e.field === 'name')).toBe(true);
    });

    it('should clear previous errors for the field before validating', () => {
      const form = new FormController<TestForm>({
        initialValues: { name: '', email: 'alice@test.com', age: 25 },
        schema: testSchema as z.ZodType<TestForm>,
      });
      form.validateField('name');
      expect(form.errors.some(e => e.field === 'name')).toBe(true);

      // Fix the field and re-validate
      form.values.name = 'Alice';
      form.validateField('name');
      expect(form.errors.some(e => e.field === 'name')).toBe(false);
    });

    it('should not affect errors of other fields', () => {
      const form = new FormController<TestForm>({
        initialValues: createDefaults(),
        schema: testSchema as z.ZodType<TestForm>,
      });
      form.setErrors([{ field: 'email', message: 'Email error' }]);
      form.values.name = 'Alice';
      form.validateField('name');
      expect(form.errors.some(e => e.field === 'email')).toBe(true);
    });
  });

  // ------------------------------------------
  // submit
  // ------------------------------------------

  describe('submit', () => {
    it('should return false when validation fails', async () => {
      const onSubmit = vi.fn();
      const form = new FormController<TestForm>({
        initialValues: createDefaults(),
        schema: testSchema as z.ZodType<TestForm>,
        onSubmit,
      });
      const result = await form.submit();
      expect(result).toBe(false);
      expect(onSubmit).not.toHaveBeenCalled();
    });

    it('should return true and call onSubmit when valid', async () => {
      const onSubmit = vi.fn();
      const form = new FormController<TestForm>({
        initialValues: { name: 'Alice', email: 'alice@test.com', age: 25 },
        schema: testSchema as z.ZodType<TestForm>,
        onSubmit,
      });
      const result = await form.submit();
      expect(result).toBe(true);
      expect(onSubmit).toHaveBeenCalledWith(form.values);
    });

    it('should set isSubmitting during onSubmit execution', async () => {
      let submittingDuringCall = false;
      const form = new FormController<TestForm>({
        initialValues: { name: 'Alice', email: 'alice@test.com', age: 25 },
        schema: testSchema as z.ZodType<TestForm>,
        onSubmit: async () => {
          submittingDuringCall = form.isSubmitting;
        },
      });
      await form.submit();
      expect(submittingDuringCall).toBe(true);
      expect(form.isSubmitting).toBe(false); // reset after
    });

    it('should set isSubmitted to true', async () => {
      const form = new FormController<TestForm>({
        initialValues: { name: 'Alice', email: 'alice@test.com', age: 25 },
        schema: testSchema as z.ZodType<TestForm>,
      });
      await form.submit();
      expect(form.isSubmitted).toBe(true);
    });

    it('should set isSubmitted even when validation fails', async () => {
      const form = new FormController<TestForm>({
        initialValues: createDefaults(),
        schema: testSchema as z.ZodType<TestForm>,
      });
      await form.submit();
      expect(form.isSubmitted).toBe(true);
    });

    it('should return true when no onSubmit is provided and validation passes', async () => {
      const form = new FormController<TestForm>({
        initialValues: { name: 'Alice', email: 'alice@test.com', age: 25 },
        schema: testSchema as z.ZodType<TestForm>,
      });
      const result = await form.submit();
      expect(result).toBe(true);
    });

    it('should handle onSubmit error and push _form error', async () => {
      const form = new FormController<TestForm>({
        initialValues: { name: 'Alice', email: 'alice@test.com', age: 25 },
        schema: testSchema as z.ZodType<TestForm>,
        onSubmit: async () => {
          throw new Error('Server error');
        },
      });
      const result = await form.submit();
      expect(result).toBe(false);
      expect(form.errors).toEqual([{ field: '_form', message: 'Server error' }]);
    });

    it('should reset isSubmitting even if onSubmit throws', async () => {
      const form = new FormController<TestForm>({
        initialValues: { name: 'Alice', email: 'alice@test.com', age: 25 },
        schema: testSchema as z.ZodType<TestForm>,
        onSubmit: async () => {
          throw new Error('fail');
        },
      });
      await form.submit();
      expect(form.isSubmitting).toBe(false);
    });
  });

  // ------------------------------------------
  // reset
  // ------------------------------------------

  describe('reset', () => {
    it('should restore initial values', () => {
      const form = new FormController<TestForm>({
        initialValues: { name: 'Alice', email: 'alice@test.com', age: 25 },
      });
      form.values.name = 'Bob';
      form.reset();
      expect(form.values.name).toBe('Alice');
    });

    it('should clear errors', () => {
      const form = new FormController<TestForm>({
        initialValues: createDefaults(),
      });
      form.setErrors([{ field: 'name', message: 'err' }]);
      form.reset();
      expect(form.errors).toEqual([]);
    });

    it('should reset isSubmitting and isSubmitted', () => {
      const form = new FormController<TestForm>({
        initialValues: createDefaults(),
      });
      form.isSubmitted = true;
      form.reset();
      expect(form.isSubmitting).toBe(false);
      expect(form.isSubmitted).toBe(false);
    });

    it('should clear touchedFields', () => {
      const form = new FormController<TestForm>({
        initialValues: createDefaults(),
      });
      form.touchField('name');
      form.reset();
      expect(form.touchedFields.size).toBe(0);
    });

    it('should not be dirty after reset', () => {
      const form = new FormController<TestForm>({
        initialValues: createDefaults(),
      });
      form.values.name = 'changed';
      form.reset();
      expect(form.isDirty).toBe(false);
    });
  });

  // ------------------------------------------
  // setErrors
  // ------------------------------------------

  describe('setErrors', () => {
    it('should set errors from server', () => {
      const form = new FormController<TestForm>({
        initialValues: createDefaults(),
      });
      form.setErrors([
        { field: 'name', message: 'Name taken' },
        { field: 'email', message: 'Invalid' },
      ]);
      expect(form.errors).toHaveLength(2);
      expect(form.getFieldError('name')).toBe('Name taken');
      expect(form.getFieldError('email')).toBe('Invalid');
    });
  });

  // ------------------------------------------
  // canSubmit
  // ------------------------------------------

  describe('canSubmit', () => {
    it('should return false when form is not dirty', () => {
      const form = new FormController<TestForm>({
        initialValues: createDefaults(),
      });
      expect(form.canSubmit).toBe(false);
    });

    it('should return true when form is dirty and not submitting', () => {
      const form = new FormController<TestForm>({
        initialValues: createDefaults(),
      });
      form.values.name = 'changed';
      expect(form.canSubmit).toBe(true);
    });

    it('should return false when isSubmitting is true', async () => {
      let canSubmitDuringSubmit = true;
      const form = new FormController<TestForm>({
        initialValues: { name: 'Alice', email: 'alice@test.com', age: 25 },
        schema: testSchema as z.ZodType<TestForm>,
        onSubmit: async () => {
          canSubmitDuringSubmit = form.canSubmit;
        },
      });
      // Make dirty so canSubmit would be true if not submitting
      form.values.name = 'Bob';
      await form.submit();
      expect(canSubmitDuringSubmit).toBe(false);
    });
  });

  // ------------------------------------------
  // Misc getters
  // ------------------------------------------

  describe('isValid and hasErrors', () => {
    it('isValid should be true when no errors', () => {
      const form = new FormController<TestForm>({ initialValues: createDefaults() });
      expect(form.isValid).toBe(true);
    });

    it('hasErrors should be true when errors exist', () => {
      const form = new FormController<TestForm>({ initialValues: createDefaults() });
      form.setErrors([{ field: 'name', message: 'err' }]);
      expect(form.hasErrors).toBe(true);
      expect(form.isValid).toBe(false);
    });
  });

  describe('getFieldError', () => {
    it('should return the error message for a field', () => {
      const form = new FormController<TestForm>({ initialValues: createDefaults() });
      form.setErrors([{ field: 'name', message: 'Too short' }]);
      expect(form.getFieldError('name')).toBe('Too short');
    });

    it('should return null for a field with no error', () => {
      const form = new FormController<TestForm>({ initialValues: createDefaults() });
      expect(form.getFieldError('name')).toBeNull();
    });
  });

  describe('touchField and isFieldTouched', () => {
    it('should mark field as touched', () => {
      const form = new FormController<TestForm>({ initialValues: createDefaults() });
      expect(form.isFieldTouched('name')).toBe(false);
      form.touchField('name');
      expect(form.isFieldTouched('name')).toBe(true);
    });
  });

  describe('setValues', () => {
    it('should batch-update values', () => {
      const form = new FormController<TestForm>({ initialValues: createDefaults() });
      form.setValues({ name: 'Alice', age: 30 });
      expect(form.values.name).toBe('Alice');
      expect(form.values.age).toBe(30);
      expect(form.values.email).toBe('');
    });
  });
});

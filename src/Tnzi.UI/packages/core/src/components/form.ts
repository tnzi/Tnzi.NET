/**
 * @tnzi/core/components/form
 *
 * Form component interfaces.
 */

/**
 * Form rule
 */
export interface IFormRule {
  /** Whether required */
  required?: boolean;
  /** Error message */
  message?: string;
  /** Trigger timing */
  trigger?: 'blur' | 'change' | 'input';
  /** Minimum length */
  min?: number;
  /** Maximum length */
  max?: number;
  /** Regular expression */
  pattern?: RegExp;
  /** Custom validator */
  validator?: (value: unknown) => boolean | string | Promise<boolean | string>;
}

/**
 * Form Props
 */
export interface IFormProps<T = Record<string, unknown>> {
  /** Form data model */
  model: T;
  /** Form validation rules */
  rules?: Record<string, IFormRule[]>;
  /** Label width */
  labelWidth?: number | string;
  /** Label position */
  labelPlacement?: 'left' | 'top';
  /** Whether disabled */
  disabled?: boolean;
  /** Whether show required mark */
  showRequireMark?: boolean;
  /** Form size */
  size?: 'small' | 'medium' | 'large';
  /** Custom style class */
  class?: string | string[];
  /** Custom inline style */
  style?: string | Record<string, string | number>;
}

/**
 * Form Emits
 */
export interface IFormEmits<T = Record<string, unknown>> {
  /** Form submit */
  submit: [data: T];
  /** Form reset */
  reset: [];
  /** Validation error */
  validateError: [errors: Record<string, string[]>];
}

/**
 * Dynamic form field definition
 */
export interface IDynamicFormField {
  /** Field key */
  key: string;
  /** Field label */
  label?: string;
  /** Field type */
  type:
    | 'text'
    | 'password'
    | 'email'
    | 'number'
    | 'textarea'
    | 'select'
    | 'checkbox'
    | 'radio'
    | 'date'
    | 'datetime'
    | 'file'
    | 'switch';
  /** Field placeholder */
  placeholder?: string;
  /** Whether required */
  required?: boolean;
  /** Disabled */
  disabled?: boolean;
  /** Options for select/radio/checkbox */
  options?: Array<{ label: string; value: string | number }>;
  /** Minimum value */
  min?: number;
  /** Maximum value */
  max?: number;
  /** Validation rules */
  rules?: IFormRule[];
  /** Additional props */
  props?: Record<string, unknown>;
}

/**
 * Dynamic form Props
 */
export interface IDynamicFormProps {
  /** Form data */
  model: Record<string, unknown>;
  /** Field definitions */
  fields: IDynamicFormField[];
  /** Whether inline mode */
  inline?: boolean;
  /** Whether disabled */
  disabled?: boolean;
  /** Form size */
  size?: 'small' | 'medium' | 'large';
  /** Custom style class */
  class?: string | string[];
  /** Custom inline style */
  style?: string | Record<string, string | number>;
}

/**
 * Dynamic form Emits
 */
export interface IDynamicFormEmits {
  /** Form submit */
  submit: [data: Record<string, unknown>];
  /** Form reset */
  reset: [];
  /** Field change */
  fieldChange: [key: string, value: unknown];
}

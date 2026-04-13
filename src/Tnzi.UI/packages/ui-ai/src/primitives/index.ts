/**
 * Temporary local adapters — shadcn → Naive UI mapping.
 * Will be removed when @tnzi/ui-ai is fully refactored to use Naive UI directly.
 */

// Simple re-exports
export { NInput as Input } from 'naive-ui';
export { NProgress as Progress } from 'naive-ui';
export { NScrollbar as ScrollArea, NScrollbar as ScrollBar } from 'naive-ui';
export { NDivider as Separator } from 'naive-ui';
export { NBadge as Badge } from 'naive-ui';
export { NCard as Card, NCard as CardContent } from 'naive-ui';
export { NTooltip as Tooltip } from 'naive-ui';
export { NDropdown as DropdownMenu } from 'naive-ui';
export { NPopover as HoverCard } from 'naive-ui';

// Types
export type BadgeVariants = {
  variant?: 'default' | 'secondary' | 'destructive' | 'outline';
};

// Complex adapters — re-exported from dedicated files
export { Button, type ButtonSize } from './button';
export { Textarea } from './textarea';
export {
  ResizablePanelGroup,
  ResizablePanel,
  ResizableHandle,
} from './resizable';
export {
  CardHeader,
  CardTitle,
  CardDescription,
  CardFooter,
} from './card';

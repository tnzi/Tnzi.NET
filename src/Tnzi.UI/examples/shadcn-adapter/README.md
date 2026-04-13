# @tnzi/core Adapter Example: shadcn/Reka-UI + Tailwind CSS

This is a standalone demo app showing how to implement `@tnzi/core` adapter interfaces using shadcn/Reka-UI and Tailwind CSS.

## Quick Start

```bash
cd src/Tnzi.UI
pnpm install
cd examples/shadcn-adapter
pnpm dev
```

Open http://localhost:3010 to see the demo.

## What This Demonstrates

- Implementing `IUiAdapter` (message, dialog, theme) — see `src/adapters/`
- Building Vue components on Reka-UI primitives — see `src/components/`
- Pinia store adapter with persistence — see `src/stores/`
- Headless composables consuming `@tnzi/core` controllers — see `src/headless/`

## Using in Your Project

Copy the `src/` directory into your project and install the required dependencies. Adjust adapter implementations to match your UI library of choice.

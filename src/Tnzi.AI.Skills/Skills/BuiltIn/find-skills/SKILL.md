---
name: Find Skills
slug: find-skills
description: Helps users discover and install third-party agent skills from the open ecosystem. Use when users ask "how do I do X", "find a skill for X", "is there a skill that can...", or want to extend agent capabilities with community skills.
agents: "*"
---

# Find Skills

Discover and install third-party skills from the open agent skills ecosystem.

## When to Use

- User asks "how do I do X" where X might have an existing community skill
- User says "find a skill for X" or "is there a skill for X"
- User wants to extend agent capabilities beyond built-in skills

## How Skills Are Stored

Installed skills are stored on the file system under `{DataPath}/skills/` (defaults to `App_Data/AI`, configurable via `AI:ContextProviders:Skills:DataPath`), with tenant/user isolation:

```
{DataPath}/skills/
├── react-skill/SKILL.md                            ← System scope (admin-deployed, shared globally)
├── tenants/{tenantId}/
│   ├── company-workflow/SKILL.md                    ← Tenant scope (shared within org)
│   └── users/{userId}/
│       └── tenant-user-skill/SKILL.md               ← User scope (multi-tenant isolated)
└── users/{userId}/
    └── my-custom-skill/                             ← User scope (single-tenant / no tenant)
        ├── SKILL.md
        └── scripts/generate.py
```

Skills with scripts/resources are fully supported — the entire directory is loaded.

## Workflow

### Step 1: Check Built-in Skills First

Use `skill_search` to check if a built-in skill already covers the need. If found, no installation needed.

### Step 2: Search for Community Skills

**Option A — Skills CLI** (if `npx` is available):
```bash
npx skills find [query]
```

**Option B — Web search**:
Search for `site:skills.sh [query]` or browse https://skills.sh/

**Option C — GitHub**:
```bash
gh search repos "agent skill [query]" --sort stars
```

### Step 3: Present Options

When you find relevant skills, present:
1. Skill name and what it does
2. The install command
3. A link to learn more

### Step 4: Install

Use the install script with the target directory for the current scope:

```bash
bash scripts/install-skill.sh <owner/repo@skill-name> <target-dir>
```

Target directory by scope:
- **System** (admin-deployed): `{DataPath}/skills/`
- **Tenant** (shared in org): `{DataPath}/skills/tenants/{tenantId}/`
- **User** (single-tenant): `{DataPath}/skills/users/{userId}/`
- **User** (multi-tenant): `{DataPath}/skills/tenants/{tenantId}/users/{userId}/`

The script downloads the skill via `npx skills add` or `git clone` (fallback) and places it in the target directory.

**Manual install** (if script fails):
```bash
mkdir -p {target-dir}/skill-name
# Download SKILL.md (and any scripts/) into the directory
```

### Step 5: Verify

Use `skill_search` to confirm the skill appears. `FileSystemSkillStore` cache refreshes every 15 minutes by default.

## Common Categories

| Category | Example Queries |
|----------|----------------|
| Web Development | react, nextjs, typescript, tailwind |
| Testing | testing, jest, playwright, e2e |
| DevOps | deploy, docker, kubernetes, ci-cd |
| Documentation | docs, readme, changelog, api-docs |
| Code Quality | review, lint, refactor, best-practices |
| Design | ui, ux, design-system, accessibility |

## When No Skills Are Found

1. Offer to help directly with general capabilities
2. Suggest creating a custom skill with `skill_creator` if the task is recurring

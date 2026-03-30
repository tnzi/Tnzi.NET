---
name: Skill Creator
slug: skill-creator
description: Create new skills, modify and improve existing skills, and measure skill performance. Use when users want to create a skill from scratch, edit, or optimize an existing skill, run evals to test a skill, benchmark skill performance with variance analysis, or optimize a skill's description for better triggering accuracy.
agents: "*"
---

# Skill Creator

A skill for creating new skills and iteratively improving them.

At a high level, the process of creating a skill goes like this:

- Decide what you want the skill to do and roughly how it should do it
- Write a draft of the skill
- Create a few test prompts and run the agent with access to the skill on them
- Help the user evaluate the results both qualitatively and quantitatively
- Rewrite the skill based on feedback from the user's evaluation
- Repeat until satisfied
- Expand the test set and try again at larger scale

Your job when using this skill is to figure out where the user is in this process and then jump in and help them progress through these stages. For instance, maybe they want to make a skill for X — you can help narrow down what they mean, write a draft, write the test cases, figure out how they want to evaluate, run all the prompts, and repeat.

On the other hand, maybe they already have a draft of the skill. In this case you can go straight to the eval/iterate part of the loop.

Of course, you should always be flexible and if the user says "I don't need to run evaluations, just help me write it", you can do that instead.

After the skill is done, you can also optimize the skill description to improve triggering accuracy.

## Communicating with the User

The skill creator may be used by people across a wide range of familiarity with coding jargon. Pay attention to context cues to understand how to phrase your communication. It's OK to briefly explain terms if in doubt.

---

## Creating a Skill

### Capture Intent

Start by understanding the user's intent. The current conversation might already contain a workflow the user wants to capture (e.g., they say "turn this into a skill"). If so, extract answers from the conversation history first — the tools used, the sequence of steps, corrections the user made, input/output formats observed. The user may need to fill gaps, and should confirm before proceeding.

1. What should this skill enable the agent to do?
2. When should this skill trigger? (what user phrases/contexts)
3. What's the expected output format?
4. Should we set up test cases to verify the skill works? Skills with objectively verifiable outputs (file transforms, data extraction, code generation, fixed workflow steps) benefit from test cases. Skills with subjective outputs (writing style, art) often don't need them. Suggest the appropriate default based on the skill type, but let the user decide.

### Interview and Research

Proactively ask questions about edge cases, input/output formats, example files, success criteria, and dependencies. Wait to write test prompts until this part is ironed out.

### Write the SKILL.md

Based on the user interview, fill in these components:

- **name**: Skill display name
- **slug**: Kebab-case identifier matching the directory name
- **description**: When to trigger, what it does. This is the primary triggering mechanism — include both what the skill does AND specific contexts for when to use it. All "when to use" info goes here, not in the body. Make the description somewhat "pushy" to combat under-triggering — include related contexts where the skill would be useful even if not explicitly named.
- **agents**: Which agents can use this skill (`"*"` for all, or comma-separated names with wildcard support)
- **the rest of the skill body**

### Skill Writing Guide

#### Anatomy of a Skill

```
skill-name/
├── SKILL.md (required)
│   ├── YAML frontmatter (name, slug, description required)
│   └── Markdown instructions
└── Bundled Resources (optional)
    ├── scripts/    - Executable code for deterministic/repetitive tasks
    ├── references/ - Docs loaded into context as needed
    └── assets/     - Files used in output (templates, icons, fonts)
```

#### Progressive Disclosure

Skills use a three-level loading system:
1. **Metadata** (name + description) - Always in context (~100 words)
2. **SKILL.md body** - In context whenever skill triggers (<500 lines ideal)
3. **Bundled resources** - As needed (unlimited, scripts can execute without loading)

**Key patterns:**
- Keep SKILL.md under 500 lines; if approaching this limit, add hierarchy with clear pointers about where to follow up
- Reference files clearly from SKILL.md with guidance on when to read them
- For large reference files (>300 lines), include a table of contents

**Domain organization**: When a skill supports multiple domains/frameworks, organize by variant:
```
cloud-deploy/
├── SKILL.md (workflow + selection)
└── references/
    ├── aws.md
    ├── gcp.md
    └── azure.md
```
The agent reads only the relevant reference file.

#### Writing Patterns

Prefer using the imperative form in instructions.

**Defining output formats:**
```markdown
## Report structure
ALWAYS use this exact template:
# [Title]
## Executive summary
## Key findings
## Recommendations
```

**Examples pattern:**
```markdown
## Commit message format
**Example 1:**
Input: Added user authentication with JWT tokens
Output: feat(auth): implement JWT-based authentication
```

### Writing Style

Explain to the model why things are important rather than relying on heavy-handed MUSTs. Use theory of mind and try to make the skill general rather than super-narrow to specific examples. Write a draft, then look at it with fresh eyes and improve it.

### Test Cases

After writing the skill draft, come up with 2-3 realistic test prompts — the kind of thing a real user would actually say. Share them with the user for review, then run them.

## Improving the Skill

This is the heart of the loop. You've run the test cases, the user has reviewed the results, and now you need to make the skill better based on their feedback.

### How to Think About Improvements

1. **Generalize from the feedback.** Skills are meant to be reused across many different prompts. You and the user are iterating on only a few examples to move faster. Rather than adding fiddly overfitting changes or oppressively constrictive MUSTs, if there's a stubborn issue, try branching out with different metaphors or recommending different patterns.

2. **Keep the prompt lean.** Remove things that aren't pulling their weight. Read the transcripts, not just the final outputs — if the skill is making the model waste time on unproductive things, remove those parts.

3. **Explain the why.** Try hard to explain the reasoning behind everything you're asking the model to do. Today's LLMs are smart and have good theory of mind. Even if user feedback is terse, understand the task deeply and transmit that understanding into the instructions. If you find yourself writing ALWAYS or NEVER in all caps, that's a yellow flag — reframe and explain the reasoning.

4. **Look for repeated work across test cases.** Read the transcripts and notice if all test cases independently wrote similar helper scripts. If so, bundle that script into `scripts/` to save future invocations from reinventing the wheel.

### The Iteration Loop

After improving the skill:

1. Apply improvements to the skill
2. Rerun all test cases
3. Present results to the user for review
4. Read feedback, improve again, repeat

Keep going until:
- The user says they're happy
- The feedback shows everything looks good
- You're not making meaningful progress

## Description Optimization

The description field in SKILL.md frontmatter is the primary mechanism that determines whether the agent invokes a skill. After creating or improving a skill, offer to optimize the description for better triggering accuracy.

### Step 1: Generate Trigger Eval Queries

Create 20 eval queries — a mix of should-trigger and should-not-trigger. The queries must be realistic with concrete details (file paths, personal context, column names, URLs). Include a mix of lengths, focus on edge cases rather than clear-cut examples.

For **should-trigger** queries (8-10): different phrasings of the same intent — some formal, some casual. Include cases where the user doesn't explicitly name the skill but clearly needs it.

For **should-not-trigger** queries (8-10): near-misses — queries that share keywords or concepts but need something different. Avoid obviously irrelevant queries.

### Step 2: Review with User

Present the eval set to the user for review and adjustment.

### Step 3: Optimize

Iteratively improve the description based on triggering accuracy. Evaluate each new description variant against the eval set, selecting by test score rather than training score to avoid overfitting.

### Step 4: Apply the Result

Update the skill's SKILL.md frontmatter with the optimized description. Show the user before/after and report the scores.

---

## Core Loop Summary

- Figure out what the skill is about
- Draft or edit the skill
- Run the agent with the skill on test prompts
- Evaluate the outputs with the user (qualitative + quantitative)
- Repeat until satisfied
- Package the final skill

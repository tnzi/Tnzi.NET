---
name: Document Co-Authoring Workflow
slug: doc-coauthoring
description: Guide users through a structured workflow for co-authoring documentation. Use when user wants to write documentation, proposals, technical specs, decision docs, or similar structured content. This workflow helps users efficiently transfer context, refine content through iteration, and verify the doc works for readers. Trigger when user mentions writing docs, creating proposals, drafting specs, or similar documentation tasks — including PRD, design doc, decision doc, RFC, architecture doc, or any substantial writing task.
agents: "*"
---

# Doc Co-Authoring Workflow

This skill provides a structured workflow for guiding users through collaborative document creation. Act as an active guide, walking users through three stages: Context Gathering, Refinement & Structure, and Reader Testing.

## When to Offer This Workflow

**Trigger conditions:**
- User mentions writing documentation: "write a doc", "draft a proposal", "create a spec", "write up"
- User mentions specific doc types: "PRD", "design doc", "decision doc", "RFC", "architecture doc"
- User seems to be starting a substantial writing task

**Initial offer:**
Offer the user a structured workflow for co-authoring the document. Explain the three stages:

1. **Context Gathering**: User provides all relevant context while the assistant asks clarifying questions
2. **Refinement & Structure**: Iteratively build each section through brainstorming and editing
3. **Reader Testing**: Test the doc with a fresh perspective (sub-agent or manual) to catch blind spots

Explain that this approach helps ensure the doc works well when others read it. Ask if they want to try this workflow or prefer to work freeform.

If user declines, work freeform. If user accepts, proceed to Stage 1.

## Stage 1: Context Gathering

**Goal:** Close the gap between what the user knows and what the assistant knows, enabling smart guidance later.

### Initial Questions

Start by asking the user for meta-context about the document:

1. What type of document is this? (e.g., technical spec, decision doc, proposal)
2. Who is the primary audience?
3. What is the desired impact when someone reads this?
4. Is there a template or specific format to follow?
5. Any other constraints or context to know?

Inform them they can answer in shorthand or dump information however works best for them.

**If user provides a template or mentions a doc type:**
- Ask if they have a template document to share
- If they provide a file, read it and use it as the structure reference

**If user mentions editing an existing document:**
- Read the current state of the document
- If it contains images without descriptions, note that AI readers cannot see them and suggest adding alt-text

### Info Dumping

Once initial questions are answered, encourage the user to dump all the context they have. Request information such as:
- Background on the project or problem
- Related team discussions or documents
- Why alternative solutions are not being used
- Organizational context (team dynamics, past incidents, constraints)
- Timeline pressures or constraints
- Technical architecture or dependencies
- Stakeholder concerns

Advise them not to worry about organizing it — just get it all out. Offer multiple ways to provide context:
- Info dump stream-of-consciousness
- Point to existing files or documents to read
- Paste relevant content directly

Inform them clarifying questions will be asked once they have done their initial dump.

**During context gathering:**

- If user mentions entities, projects, or concepts that are unknown, ask for clarification
- As user provides context, track what has been learned and what remains unclear
- Do not let gaps accumulate — address them as they come up

**Asking clarifying questions:**

When user signals they have done their initial dump (or after substantial context is provided), ask clarifying questions to ensure understanding:

Generate 5-10 numbered questions based on gaps in the context.

Inform them they can use shorthand to answer (e.g., "1: yes, 2: no because backwards compat, 3: see the RFC"), or just keep info-dumping. Whatever is most efficient for them.

**Exit condition:**
Sufficient context has been gathered when questions show understanding — when edge cases and trade-offs can be asked about without needing basics explained.

**Transition:**
Ask if there is any more context they want to provide at this stage, or if it is time to move on to drafting the document.

If user wants to add more, let them. When ready, proceed to Stage 2.

## Stage 2: Refinement & Structure

**Goal:** Build the document section by section through brainstorming, curation, and iterative refinement.

**Instructions to user:**
Explain that the document will be built section by section. For each section:
1. Clarifying questions will be asked about what to include
2. 5-20 options will be brainstormed
3. User will indicate what to keep, remove, or combine
4. The section will be drafted
5. It will be refined through surgical edits

Start with whichever section has the most unknowns (usually the core decision or proposal), then work through the rest.

**Section ordering:**

If the document structure is clear:
Ask which section they would like to start with.

Suggest starting with whichever section has the most unknowns. For decision docs, that is usually the core proposal. For specs, it is typically the technical approach. Summary sections are best left for last.

If user does not know what sections they need:
Based on the type of document and template, suggest 3-5 sections appropriate for the doc type.

Ask if this structure works, or if they want to adjust it.

**Once structure is agreed:**

Create the initial document structure with placeholder text for all sections.

Create a file (e.g., `decision-doc.md`, `technical-spec.md`) with all section headers and brief placeholder text like "[To be written]".

Confirm the file has been created and indicate it is time to fill in each section.

**For each section:**

### Step 1: Clarifying Questions

Announce work will begin on the [SECTION NAME] section. Ask 5-10 clarifying questions about what should be included:

Generate 5-10 specific questions based on context and section purpose.

Inform them they can answer in shorthand or just indicate what is important to cover.

### Step 2: Brainstorming

For the [SECTION NAME] section, brainstorm 5-20 things that might be included, depending on the section's complexity. Look for:
- Context shared that might have been forgotten
- Angles or considerations not yet mentioned

Generate 5-20 numbered options based on section complexity. At the end, offer to brainstorm more if they want additional options.

### Step 3: Curation

Ask which points should be kept, removed, or combined. Request brief justifications to help learn priorities for the next sections.

Provide examples:
- "Keep 1,4,7,9"
- "Remove 3 (duplicates 1)"
- "Remove 6 (audience already knows this)"
- "Combine 11 and 12"

**If user gives freeform feedback** (e.g., "looks good" or "I like most of it but...") instead of numbered selections, extract their preferences and proceed. Parse what they want kept, removed, or changed and apply it.

### Step 4: Gap Check

Based on what they have selected, ask if there is anything important missing for the [SECTION NAME] section.

### Step 5: Drafting

Use the Edit tool to replace the placeholder text for this section with the actual drafted content.

Announce the [SECTION NAME] section will be drafted now based on what they have selected.

After drafting, confirm completion.

Inform them the [SECTION NAME] section has been drafted. Ask them to read through it and indicate what to change. Note that being specific helps learning for the next sections.

**Key instruction for user (include when drafting the first section):**
Note: Instead of editing the doc directly, ask them to indicate what to change. This helps learning of their style for future sections. For example: "Remove the X bullet — already covered by Y" or "Make the third paragraph more concise".

### Step 6: Iterative Refinement

As user provides feedback:
- Use the Edit tool to make surgical edits (never reprint the whole doc)
- Confirm edits are complete after each change
- If user edits the doc directly, mentally note the changes they made and keep them in mind for future sections (this shows their preferences)

**Continue iterating** until user is satisfied with the section.

### Quality Checking

After 3 consecutive iterations with no substantial changes, ask if anything can be removed without losing important information.

When section is done, confirm [SECTION NAME] is complete. Ask if ready to move to the next section.

**Repeat for all sections.**

### Near Completion

As approaching completion (80%+ of sections done), announce intention to re-read the entire document and check for:
- Flow and consistency across sections
- Redundancy or contradictions
- Anything that feels like "slop" or generic filler
- Whether every sentence carries weight

Read entire document and provide feedback.

**When all sections are drafted and refined:**
Announce all sections are drafted. Indicate intention to review the complete document one more time.

Review for overall coherence, flow, completeness.

Provide any final suggestions.

Ask if ready to move to Reader Testing, or if they want to refine anything else.

## Stage 3: Reader Testing

**Goal:** Test the document with a fresh perspective (no context bleed) to verify it works for readers.

**Instructions to user:**
Explain that testing will now occur to see if the document actually works for readers. This catches blind spots — things that make sense to the authors but might confuse others.

### Testing Approach

**Sub-agent testing (preferred, available in agent environments):**

### Step 1: Predict Reader Questions

Announce intention to predict what questions readers might ask when discovering this document.

Generate 5-10 questions that readers would realistically ask.

### Step 2: Test with Sub-Agent

Announce that these questions will be tested with a fresh agent instance (no context from this conversation).

For each question, invoke a sub-agent with just the document content and the question.

Summarize what the reader agent got right and wrong for each question.

### Step 3: Run Additional Checks

Announce additional checks will be performed.

Invoke sub-agent to check for ambiguity, false assumptions, contradictions.

Summarize any issues found.

### Step 4: Report and Fix

If issues found:
Report specific issues the reader agent struggled with.

List the specific issues.

Indicate intention to fix these gaps.

Loop back to refinement for problematic sections.

---

**Manual testing (when sub-agents are not available):**

### Step 1: Predict Reader Questions

Ask what questions people might ask when discovering this document.

Generate 5-10 questions that readers would realistically ask.

### Step 2: Setup Testing

Provide testing instructions:
1. Open a fresh AI conversation (new session, no prior context)
2. Paste or share the document content
3. Ask the reader AI the generated questions

For each question, instruct the reader AI to provide:
- The answer
- Whether anything was ambiguous or unclear
- What knowledge or context the doc assumes is already known

Check if the reader AI gives correct answers or misinterprets anything.

### Step 3: Additional Checks

Also ask the reader AI:
- "What in this doc might be ambiguous or unclear to readers?"
- "What knowledge or context does this doc assume readers already have?"
- "Are there any internal contradictions or inconsistencies?"

### Step 4: Iterate Based on Results

Ask what the reader AI got wrong or struggled with. Indicate intention to fix those gaps.

Loop back to refinement for any problematic sections.

---

### Exit Condition (Both Approaches)

When the reader consistently answers questions correctly and does not surface new gaps or ambiguities, the doc is ready.

## Final Review

When Reader Testing passes:
Announce the doc has passed reader testing. Before completion:

1. Recommend they do a final read-through themselves — they own this document and are responsible for its quality
2. Suggest double-checking any facts, links, or technical details
3. Ask them to verify it achieves the impact they wanted

Ask if they want one more review, or if the work is done.

**If user wants final review, provide it. Otherwise:**
Announce document completion. Provide a few final tips:
- Consider linking this conversation in an appendix so readers can see how the doc was developed
- Use appendices to provide depth without bloating the main doc
- Update the doc as feedback is received from real readers

## Tips for Effective Guidance

**Tone:**
- Be direct and procedural
- Explain rationale briefly when it affects user behavior
- Do not try to "sell" the approach — just execute it

**Handling Deviations:**
- If user wants to skip a stage: Ask if they want to skip this and write freeform
- If user seems frustrated: Acknowledge this is taking longer than expected. Suggest ways to move faster
- Always give user agency to adjust the process

**Context Management:**
- Throughout, if context is missing on something mentioned, proactively ask
- Do not let gaps accumulate — address them as they come up

**File Management:**
- Use file creation for drafting the initial structure
- Use the Edit tool for all subsequent edits (never rewrite the whole doc)
- Never use files for brainstorming lists — that is just conversation

**Quality over Speed:**
- Do not rush through stages
- Each iteration should make meaningful improvements
- The goal is a document that actually works for readers

---
name: Video Generation
slug: video-generation
description: Use this skill when the user requests to generate, create, or imagine videos. Supports structured prompts and reference images for guided generation. Trigger on any request involving video creation, animation, or motion content generation.
agents: "*"
---

# Video Generation Skill

## Overview

This skill generates high-quality videos using structured prompts. The workflow includes creating JSON-formatted prompts and executing video generation with optional reference images.

## Core Capabilities

- Create structured JSON prompts for AIGC video generation
- Support reference images as guidance or as the first/last frame of the video
- Generate videos through automated script execution

## Workflow

### Step 1: Understand Requirements

When a user requests video generation, identify:

- Subject/content: What should be in the video
- Style preferences: Art style, mood, color palette
- Technical specs: Aspect ratio, composition, lighting
- Reference image: Any image to guide generation

### Step 2: Create Structured Prompt

Generate a structured JSON file with the following schema:

```json
{
  "title": "Scene title",
  "background": {
    "description": "Detailed scene description",
    "era": "Time period if relevant",
    "location": "Setting/location"
  },
  "characters": ["Character 1", "Character 2"],
  "camera": {
    "type": "Shot type (e.g., Close-up, Wide shot)",
    "movement": "Camera movement description",
    "angle": "Camera angle",
    "focus": "Focus description"
  },
  "dialogue": [
    {
      "character": "Character name",
      "text": "Dialogue line"
    }
  ],
  "audio": [
    {
      "type": "Sound description",
      "volume": 1.0
    }
  ]
}
```

### Step 3: Create Reference Image (Optional)

If an image-generation skill is available, generate a reference image for the video generation. If only 1 image is provided, use it as the guided frame of the video.

### Step 4: Execute Generation

Call the generation script with the following parameters:

- `--prompt-file`: Absolute path to JSON prompt file (required)
- `--reference-images`: Absolute paths to reference images (optional)
- `--output-file`: Absolute path to output video file (required)
- `--aspect-ratio`: Aspect ratio of the generated video (optional, default: 16:9)

## Example

User request: "Generate a short video clip depicting a train station farewell scene"

1. Research the visual style and details for the scene
2. Create a structured JSON prompt with background, characters, camera, dialogue, and audio
3. Optionally generate a reference image using the image-generation skill
4. Execute the generation script with the prompt and reference image

## Output Handling

After generation:

- Share generated videos with the user, as well as generated images if applicable
- Provide a brief description of the generation result
- Offer to iterate if adjustments are needed

## Notes

- Always use English for prompts regardless of user's language
- JSON format ensures structured, parsable prompts
- Reference images enhance generation quality significantly
- Iterative refinement is normal for optimal results

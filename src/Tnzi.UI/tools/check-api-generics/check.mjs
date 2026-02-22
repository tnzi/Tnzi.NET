#!/usr/bin/env node

import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';

const root = process.cwd();
const servicesDir = join(root, 'packages', 'core', 'src', 'services');
const targetPattern = /client\.(get|post|put|patch|del|upload)<\s*ApiResult\b/;

function collectApiFiles(dir, files = []) {
  const entries = readdirSync(dir);
  for (const entry of entries) {
    const fullPath = join(dir, entry);
    const stats = statSync(fullPath);
    if (stats.isDirectory()) {
      collectApiFiles(fullPath, files);
      continue;
    }
    if (entry === 'api.ts') {
      files.push(fullPath);
    }
  }
  return files;
}

function findViolations(content) {
  const lines = content.split(/\r?\n/);
  const violations = [];
  for (let i = 0; i < lines.length; i += 1) {
    if (targetPattern.test(lines[i])) {
      violations.push({ line: i + 1, code: lines[i].trim() });
    }
  }
  return violations;
}

const apiFiles = collectApiFiles(servicesDir);
const violations = [];

for (const file of apiFiles) {
  const content = readFileSync(file, 'utf8');
  const matches = findViolations(content);
  for (const match of matches) {
    violations.push({
      file: relative(root, file).replace(/\\/g, '/'),
      line: match.line,
      code: match.code,
    });
  }
}

if (violations.length === 0) {
  console.log('api generics check passed');
  process.exit(0);
}

console.error('api generics check failed: avoid client.xxx<ApiResult<...>>');
for (const violation of violations) {
  console.error(`- ${violation.file}:${violation.line} ${violation.code}`);
}
process.exit(1);

#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const uiRoot = path.resolve(__dirname, '..', '..');
const repoRoot = path.resolve(uiRoot, '..', '..');
const configPath = path.join(__dirname, 'config.json');
const reportsDir = path.join(__dirname, 'reports');
const baselinePath = path.join(__dirname, 'baseline.json');

const command = process.argv[2] ?? 'inventory';

function readJson(filePath) {
  return JSON.parse(fs.readFileSync(filePath, 'utf8'));
}

function ensureDir(dir) {
  fs.mkdirSync(dir, { recursive: true });
}

function getAllFiles(rootDir, extensions) {
  if (!fs.existsSync(rootDir)) {
    return [];
  }

  const result = [];
  const stack = [rootDir];

  while (stack.length > 0) {
    const current = stack.pop();
    const entries = fs.readdirSync(current, { withFileTypes: true });
    for (const entry of entries) {
      const fullPath = path.join(current, entry.name);
      if (entry.isDirectory()) {
        stack.push(fullPath);
      } else if (extensions.some((ext) => entry.name.endsWith(ext))) {
        result.push(fullPath);
      }
    }
  }

  return result;
}

function collectBackendSymbols(backendDirs) {
  const symbols = new Set();
  const regex = /\bpublic\s+(?:sealed\s+|abstract\s+|partial\s+)?(?:class|record|enum)\s+([A-Za-z_][A-Za-z0-9_]*)\b/g;

  for (const relDir of backendDirs) {
    const absDir = path.resolve(repoRoot, relDir);
    const files = getAllFiles(absDir, ['.cs']);
    for (const filePath of files) {
      const content = fs.readFileSync(filePath, 'utf8');
      let match = regex.exec(content);
      while (match) {
        symbols.add(match[1]);
        match = regex.exec(content);
      }
      regex.lastIndex = 0;
    }
  }

  return symbols;
}

function collectFrontendSymbols(frontendFiles) {
  const symbols = new Set();
  const regex = /\bexport\s+(?:interface|type|enum)\s+([A-Za-z_][A-Za-z0-9_]*)\b/g;

  for (const relFile of frontendFiles) {
    const absFile = path.resolve(uiRoot, relFile);
    if (!fs.existsSync(absFile)) {
      continue;
    }
    const content = fs.readFileSync(absFile, 'utf8');
    let match = regex.exec(content);
    while (match) {
      symbols.add(match[1]);
      match = regex.exec(content);
    }
    regex.lastIndex = 0;
  }

  return symbols;
}

function canonicalizeBackendName(name) {
  if (/^Query[A-Z].+Request$/.test(name)) {
    return name.replace(/^Query([A-Z].+)Request$/, '$1QueryDto');
  }
  if (/^Create[A-Z].+Request$/.test(name)) {
    return name.replace(/Request$/, 'Dto');
  }
  if (/^Update[A-Z].+Request$/.test(name)) {
    return name.replace(/Request$/, 'Dto');
  }
  if (name.endsWith('Info')) {
    return `${name.slice(0, -4)}Dto`;
  }
  if (name.endsWith('Statistics')) {
    return `${name}Dto`;
  }
  return name;
}

function normalizeModule(moduleConfig) {
  const backendSymbols = collectBackendSymbols(moduleConfig.backend);
  const frontendSymbols = collectFrontendSymbols(moduleConfig.frontend);
  const aliases = moduleConfig.aliases ?? {};
  const ignoreBackend = new Set(moduleConfig.ignoreBackend ?? []);
  const ignoreFrontend = new Set(moduleConfig.ignoreFrontend ?? []);

  const normalizedBackend = new Set();
  for (const symbol of backendSymbols) {
    if (ignoreBackend.has(symbol)) {
      continue;
    }
    const canonical = aliases[symbol] ?? canonicalizeBackendName(symbol);
    normalizedBackend.add(canonical);
  }

  const normalizedFrontend = new Set();
  for (const symbol of frontendSymbols) {
    if (!ignoreFrontend.has(symbol)) {
      normalizedFrontend.add(symbol);
    }
  }

  const missingInFrontend = [...normalizedBackend].filter((name) => !normalizedFrontend.has(name)).sort();
  const extraInFrontend = [...normalizedFrontend].filter((name) => !normalizedBackend.has(name)).sort();

  return {
    module: moduleConfig.name,
    backendCount: backendSymbols.size,
    frontendCount: frontendSymbols.size,
    missingInFrontend,
    extraInFrontend,
  };
}

function buildReport() {
  const config = readJson(configPath);
  const modules = config.modules.map((moduleConfig) => normalizeModule(moduleConfig));
  const summary = {
    generatedAt: new Date().toISOString(),
    moduleCount: modules.length,
    totalMissing: modules.reduce((sum, module) => sum + module.missingInFrontend.length, 0),
    totalExtra: modules.reduce((sum, module) => sum + module.extraInFrontend.length, 0),
  };

  return { summary, modules };
}

function writeReport(report) {
  ensureDir(reportsDir);
  const reportPath = path.join(reportsDir, 'contract-drift.json');
  fs.writeFileSync(reportPath, JSON.stringify(report, null, 2), 'utf8');
  return reportPath;
}

function buildSnapshot(report) {
  const snapshot = {};
  for (const module of report.modules) {
    snapshot[module.module] = {
      missingInFrontend: module.missingInFrontend,
      extraInFrontend: module.extraInFrontend,
    };
  }
  return snapshot;
}

function compareWithBaseline(report) {
  const current = buildSnapshot(report);
  const baseline = fs.existsSync(baselinePath) ? readJson(baselinePath) : {};
  const violations = [];

  for (const [moduleName, currentData] of Object.entries(current)) {
    const baselineData = baseline[moduleName] ?? { missingInFrontend: [], extraInFrontend: [] };
    const baselineMissing = new Set(baselineData.missingInFrontend ?? []);
    const baselineExtra = new Set(baselineData.extraInFrontend ?? []);

    const newMissing = currentData.missingInFrontend.filter((item) => !baselineMissing.has(item));
    const newExtra = currentData.extraInFrontend.filter((item) => !baselineExtra.has(item));

    if (newMissing.length > 0 || newExtra.length > 0) {
      violations.push({
        module: moduleName,
        newMissing,
        newExtra,
      });
    }
  }

  return violations;
}

function main() {
  const report = buildReport();
  const reportPath = writeReport(report);

  if (command === 'inventory') {
    console.log(`Contract report written: ${path.relative(uiRoot, reportPath)}`);
    console.log(`Missing in frontend: ${report.summary.totalMissing}`);
    console.log(`Extra in frontend: ${report.summary.totalExtra}`);
    return;
  }

  if (command === 'approve') {
    const snapshot = buildSnapshot(report);
    fs.writeFileSync(baselinePath, JSON.stringify(snapshot, null, 2), 'utf8');
    console.log(`Baseline updated: ${path.relative(uiRoot, baselinePath)}`);
    return;
  }

  if (command === 'check') {
    const violations = compareWithBaseline(report);
    if (violations.length > 0) {
      console.error('Contract drift check failed. New drift detected:');
      for (const violation of violations) {
        console.error(`- ${violation.module}`);
        if (violation.newMissing.length > 0) {
          console.error(`  newMissing: ${violation.newMissing.join(', ')}`);
        }
        if (violation.newExtra.length > 0) {
          console.error(`  newExtra: ${violation.newExtra.join(', ')}`);
        }
      }
      process.exit(1);
    }
    console.log('Contract drift check passed.');
    return;
  }

  console.error(`Unknown command: ${command}`);
  process.exit(1);
}

main();

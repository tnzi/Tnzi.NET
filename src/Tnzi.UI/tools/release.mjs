#!/usr/bin/env node

/**
 * Tnzi UI 快速发布工具
 *
 * 用法:
 *   node tools/release.mjs <包名> [版本类型]
 *
 * 参数:
 *   包名:       core | shadcn | vant | naive-ui | all
 *   版本类型:   patch (默认) | minor | major | <具体版本号如 0.2.1>
 *
 * 示例:
 *   pnpm release naive-ui              # naive-ui patch 发布 (0.1.0 → 0.1.1)
 *   pnpm release naive-ui minor        # naive-ui minor 发布 (0.1.0 → 0.2.0)
 *   pnpm release naive-ui 0.3.0        # naive-ui 指定版本号
 *   pnpm release all patch             # 所有包 patch 发布
 *   pnpm release --dry naive-ui        # 试运行，不实际发布
 *   pnpm release --status              # 查看各包当前版本
 */

import { execSync } from 'child_process';
import { readFileSync, writeFileSync } from 'fs';
import { resolve, dirname } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(__dirname, '..');

const PACKAGES = ['core', 'shadcn', 'vant', 'naive-ui'];
const SCOPE = '@tnzi';

// ── 工具函数 ──

function run(cmd, opts = {}) {
  console.log(`\x1b[36m$ ${cmd}\x1b[0m`);
  try {
    return execSync(cmd, { cwd: ROOT, stdio: 'inherit', ...opts });
  } catch (e) {
    if (!opts.ignoreError) process.exit(1);
  }
}

function runCapture(cmd) {
  try {
    return execSync(cmd, { cwd: ROOT, encoding: 'utf-8' }).trim();
  } catch {
    return '';
  }
}

function readPkgJson(name) {
  const path = resolve(ROOT, 'packages', name, 'package.json');
  return JSON.parse(readFileSync(path, 'utf-8'));
}

function writePkgJson(name, pkg) {
  const path = resolve(ROOT, 'packages', name, 'package.json');
  writeFileSync(path, JSON.stringify(pkg, null, 2) + '\n');
}

function bumpVersion(current, type) {
  // 如果是具体版本号（包含.），直接用
  if (type.includes('.')) return type;

  const [major, minor, patch] = current.split('.').map(Number);
  switch (type) {
    case 'major': return `${major + 1}.0.0`;
    case 'minor': return `${major}.${minor + 1}.0`;
    case 'patch':
    default:      return `${major}.${minor}.${patch + 1}`;
  }
}

function checkNpmAuth() {
  const whoami = runCapture('npm whoami 2>&1');
  if (!whoami || whoami.includes('ERR') || whoami.includes('error')) {
    console.error('\x1b[31m✗ 未登录 npm，请先执行: npm login\x1b[0m');
    process.exit(1);
  }
  console.log(`\x1b[32m✓ npm 已登录: ${whoami}\x1b[0m`);
}

function showStatus() {
  console.log('\n\x1b[1m当前版本:\x1b[0m\n');
  for (const name of PACKAGES) {
    const pkg = readPkgJson(name);
    const npmVer = runCapture(`npm view ${SCOPE}/${name} version`) || '(未发布)';
    console.log(`  ${SCOPE}/${name.padEnd(10)} 本地: \x1b[33m${pkg.version}\x1b[0m  npm: \x1b[36m${npmVer}\x1b[0m`);
  }
  console.log();
}

// ── 发布流程 ──

function publishPackage(name, versionType, dryRun) {
  const pkg = readPkgJson(name);
  const oldVersion = pkg.version;
  const newVersion = bumpVersion(oldVersion, versionType);
  const fullName = `${SCOPE}/${name}`;

  console.log(`\n\x1b[1m📦 ${fullName}\x1b[0m`);
  console.log(`   版本: ${oldVersion} → \x1b[33m${newVersion}\x1b[0m`);

  if (dryRun) {
    console.log(`   \x1b[36m[试运行] 跳过版本写入\x1b[0m`);
  } else {
    // 写入新版本号
    pkg.version = newVersion;
    writePkgJson(name, pkg);
  }

  // 构建（core 必须先构建，因为其他包依赖它）
  console.log(`\n   构建中...`);
  run(`pnpm --filter ${fullName} build`);

  // 发布
  const dryFlag = dryRun ? '--dry-run' : '';
  console.log(`\n   ${dryRun ? '试运行发布...' : '发布中...'}`);
  run(`pnpm --filter ${fullName} publish --no-git-checks ${dryFlag}`);

  if (dryRun) {
    // 试运行不改版本号，恢复
    console.log(`\x1b[32m   ✓ 试运行完成\x1b[0m\n`);
  } else {
    console.log(`\x1b[32m   ✓ ${fullName}@${newVersion} 发布成功\x1b[0m\n`);
  }

  return newVersion;
}

// ── 主流程 ──

const args = process.argv.slice(2);
let dryRun = false;

// 提取 flags
const flags = args.filter(a => a.startsWith('--'));
const positional = args.filter(a => !a.startsWith('--'));

if (flags.includes('--dry')) dryRun = true;
if (flags.includes('--status')) {
  showStatus();
  process.exit(0);
}
if (flags.includes('--help') || flags.includes('-h')) {
  // 输出文件顶部的注释作为帮助
  const src = readFileSync(fileURLToPath(import.meta.url), 'utf-8');
  const help = src.match(/\/\*\*([\s\S]*?)\*\//)?.[1]?.replace(/^ \* ?/gm, '') ?? '';
  console.log(help);
  process.exit(0);
}

const [target, versionType = 'patch'] = positional;

if (!target) {
  console.error('用法: pnpm release <core|shadcn|vant|naive-ui|all> [patch|minor|major|x.y.z]');
  console.error('      pnpm release --status    查看版本');
  console.error('      pnpm release --dry <包>  试运行');
  process.exit(1);
}

// 验证包名
const targets = target === 'all' ? [...PACKAGES] : [target];
for (const t of targets) {
  if (!PACKAGES.includes(t)) {
    console.error(`\x1b[31m✗ 未知的包: ${t}\x1b[0m`);
    console.error(`  可选: ${PACKAGES.join(', ')}, all`);
    process.exit(1);
  }
}

// 检查 npm 登录（非试运行时）
if (!dryRun) {
  checkNpmAuth();
}

// 发布顺序：core 优先（其他包依赖它）
if (targets.includes('core') && targets.length > 1) {
  targets.splice(targets.indexOf('core'), 1);
  targets.unshift('core');
}

console.log(`\n\x1b[1m🚀 Tnzi UI 发布\x1b[0m`);
console.log(`   目标: ${targets.map(t => `${SCOPE}/${t}`).join(', ')}`);
console.log(`   类型: ${versionType}`);
console.log(`   模式: ${dryRun ? '试运行' : '正式发布'}`);

for (const name of targets) {
  publishPackage(name, versionType, dryRun);
}

console.log('\x1b[32m✅ 完成\x1b[0m');

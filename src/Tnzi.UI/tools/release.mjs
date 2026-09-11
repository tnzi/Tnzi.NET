#!/usr/bin/env node

/**
 * Tnzi UI 发布工具（本地这一半）
 *
 * 发布本身在 CI 里完成（.github/workflows/ui-npm-publish.yml，npm trusted publishing，
 * 零 token 零登录）。本机只做三件事：升版本号 → 提交 package.json → 打 tag 推上去。
 * tag `v<stamp>-ui` 只是触发器，不参与版本计算；发出去的版本永远取自各包 package.json。
 *
 * 用法:
 *   node tools/release.mjs --status                              各包本地 / npm 版本
 *   node tools/release.mjs --plan                                按当前 package.json 算这次会发什么
 *   node tools/release.mjs <包名|all> [版本类型] [--tag [stamp]] [--push]
 *   node tools/release.mjs --tag [stamp] [--push]                 不动版本号，只给当前提交打 tag
 *
 * 参数:
 *   包名:       core | ui | ui-ai | ui-admin | mobile | all
 *   版本类型:   auto (默认) | patch | minor | major | <具体版本号如 0.2.1>
 *               auto = 版本号只需比线上多一个：本地版本还没发出去（不在 npm 上且高于线上最新）
 *                      就原样发、不升；已经在 npm 上才以线上最新为基 patch +1。
 *               patch / minor / major 总是升，基数取本地与线上较高者。
 *   --tag       提交版本号（若有改动）并打 tag v<stamp>-ui；stamp 缺省为今天 YYYY.MM.DD，同日第二次自动加 .2
 *   --push      推送当前分支与 tag，触发 CI 发布；隐含 --tag
 *
 * 示例:
 *   pnpm release all --push                    # 最常用：该发的原样发，已发过的 patch +1
 *   pnpm release ui-admin --push               # 只看 ui-admin（别的包本地领先线上时也会一起发，见计划）
 *   pnpm release ui minor --push               # 明确要升 minor
 *   pnpm release core 0.3.0 --tag 2026.09.11   # 指定版本号与 stamp，先不推
 *   pnpm release --tag --push                  # 版本号已经改好并提交，只打 tag 触发 CI
 *
 * 发出去的是哪些包由 tools/release-plan.mjs 决定（本地版本不在 npm 上的都发），
 * 与 CI Preflight 同一份逻辑；计划为空或依赖不可满足时这里就停下，不会打出一个必失败的 tag。
 */

import { spawnSync } from 'child_process';
import { readFileSync, writeFileSync } from 'fs';
import { resolve } from 'path';
import { fileURLToPath } from 'url';
import {
  PACKAGES, SCOPE, ROOT,
  readManifest, writeManifest, bumpVersion, compareVersions, isAheadOfNpm,
  buildPlan, formatPlanText, fetchAllPublished,
} from './release-plan.mjs';

const REPO_ROOT = resolve(ROOT, '..', '..');
const WORKFLOW_FILE = 'ui-npm-publish.yml';

// ── 工具函数 ──

/** 参数数组、不经 shell：提交信息带换行，cmd.exe 的引号规则会把它切碎。 */
function git(args) {
  const result = spawnSync('git', args, { cwd: REPO_ROOT, encoding: 'utf-8', stdio: ['ignore', 'pipe', 'pipe'] });
  if (result.status !== 0) throw new Error(`git ${args[0]} failed: ${result.stderr.trim()}`);
  return result.stdout.trimEnd(); // 只去尾：--porcelain 首行以状态位开头，可能是空格
}

function gitRun(args) {
  console.log(`\x1b[36m$ git ${args.join(' ')}\x1b[0m`);
  const result = spawnSync('git', args, { cwd: REPO_ROOT, stdio: 'inherit' });
  if (result.status !== 0) fail(`git ${args[0]} 失败（退出码 ${result.status}）`);
}

function fail(message) {
  console.error(`\x1b[31m✗ ${message}\x1b[0m`);
  process.exit(1);
}

function manifestRepoPath(name) {
  return `src/Tnzi.UI/packages/${name}/package.json`;
}

function todayStamp() {
  const d = new Date();
  const pad = (n) => String(n).padStart(2, '0');
  return `${d.getFullYear()}.${pad(d.getMonth() + 1)}.${pad(d.getDate())}`;
}

function repoWebUrl() {
  const remote = git(['remote', 'get-url', 'origin']);
  const match = remote.match(/github\.com[:/](.+?)(?:\.git)?$/);
  return match ? `https://github.com/${match[1]}` : null;
}

// ── --status ──

async function showStatus() {
  const published = await fetchAllPublished();
  console.log('\n\x1b[1m当前版本:\x1b[0m\n');
  for (const name of PACKAGES) {
    const { version } = readManifest(name);
    const onNpm = published[name];
    const npmVersion = onNpm?.latest ?? '(未发布)';
    const note = isAheadOfNpm(version, onNpm) ? '  待发布' : '';
    console.log(`  ${SCOPE}/${name.padEnd(10)} 本地: \x1b[33m${version}\x1b[0m  npm: \x1b[36m${npmVersion}\x1b[0m${note}`);
  }
  console.log();
}

// ── 版本升级 ──

/**
 * 一个包该变成什么版本。版本号只需比线上多一个：
 *   auto              本地已领先线上 → 原样；否则以本地与线上较高者为基 patch +1
 *   patch/minor/major 总是升，基数同上（本地落后于线上时从线上起跳，不然会撞回已发布的号）
 *   x.y.z             照给
 */
function decideVersion(local, onNpm, versionType) {
  const latest = onNpm?.latest ?? null;
  const base = latest && compareVersions(latest, local) > 0 ? latest : local;
  const baseNote = base !== local ? `（以线上 ${latest} 为基）` : '';

  if (versionType === 'auto') {
    if (onNpm === null) return { newVersion: local, reason: '不在 npm 上，按现值' };
    if (isAheadOfNpm(local, onNpm)) return { newVersion: local, reason: `已领先线上 ${latest}，不升` };
    const newVersion = bumpVersion(base, 'patch');
    const why = onNpm.versions.has(local) ? `${local} 已在 npm 上` : `${local} 低于线上 ${latest}`;
    return { newVersion, reason: `${why}，patch +1${baseNote}` };
  }
  if (versionType.includes('.')) return { newVersion: bumpVersion(local, versionType), reason: '指定版本' };
  return { newVersion: bumpVersion(base, versionType), reason: `${versionType} +1${baseNote}` };
}

function bumpPackages(targets, versionType, published) {
  const originals = Object.fromEntries(targets.map((name) => [name, readFileSync(resolve(ROOT, 'packages', name, 'package.json'), 'utf-8')]));
  const decisions = [];

  // 先把每个包的新版本都算出来再落盘：第二个包算不出来时，第一个不能已经写进去了。
  for (const name of targets) {
    const manifest = readManifest(name);
    const oldVersion = manifest.version;
    const { newVersion, reason } = decideVersion(oldVersion, published[name], versionType);
    if (newVersion === oldVersion && versionType !== 'auto') fail(`${SCOPE}/${name} 已经是 ${newVersion}`);
    decisions.push({ name, manifest, oldVersion, newVersion, reason });
  }

  const changes = decisions.filter((d) => d.newVersion !== d.oldVersion);
  if (targets.length > 0) console.log('\n\x1b[1m📦 版本\x1b[0m');
  for (const { name, manifest, oldVersion, newVersion, reason } of decisions) {
    if (newVersion !== oldVersion) {
      writeManifest(name, { ...manifest, version: newVersion });
      console.log(`   ${SCOPE}/${name.padEnd(10)} ${oldVersion} → \x1b[33m${newVersion}\x1b[0m  ${reason}`);
    } else {
      console.log(`   ${SCOPE}/${name.padEnd(10)} ${oldVersion} 不变  ${reason}`);
    }
  }

  const revert = () => {
    for (const [name, text] of Object.entries(originals)) {
      writeFileSync(resolve(ROOT, 'packages', name, 'package.json'), text);
    }
  };
  return { changes, revert };
}

// ── 提交 + tag ──

function warnAboutUncommittedWork(manifestPaths) {
  const status = git(['status', '--porcelain', '--', 'src/Tnzi.UI/packages']);
  const others = status.split('\n').filter(Boolean)
    .map((line) => line.slice(3).replace(/\\/g, '/'))
    .filter((path) => !manifestPaths.includes(path));
  if (others.length === 0) return;
  console.log('\n\x1b[33m⚠ 以下未提交改动不会进入这次发布（CI 从 tag 指向的提交构建）:\x1b[0m');
  for (const path of others) console.log(`   ${path}`);
  console.log();
}

function resolveStamp(requested) {
  const remoteTags = new Set(
    git(['ls-remote', '--tags', '--refs', 'origin', 'v*-ui']).split('\n').filter(Boolean).map((line) => line.split('refs/tags/')[1]),
  );
  const localTags = new Set(git(['tag', '-l', 'v*-ui']).split('\n').filter(Boolean));
  const taken = (stamp) => remoteTags.has(`v${stamp}-ui`) || localTags.has(`v${stamp}-ui`);

  if (requested) {
    if (taken(requested)) fail(`tag v${requested}-ui 已存在`);
    return requested;
  }

  const base = todayStamp();
  if (!taken(base)) return base;
  for (let n = 2; n < 100; n++) {
    if (!taken(`${base}.${n}`)) return `${base}.${n}`;
  }
  return fail(`今天的 stamp 都用完了: ${base}.2 ~ ${base}.99`);
}

function commitMessage(changes, stamp) {
  if (changes.length === 1) {
    const [c] = changes;
    return `chore(release): ${SCOPE}/${c.name} v${c.newVersion}`;
  }
  const body = changes.map((c) => `- ${SCOPE}/${c.name} ${c.oldVersion} -> ${c.newVersion}`).join('\n');
  return `chore(release): ${SCOPE}/* ${stamp}\n\n${body}`;
}

function commitAndTag(changes, stamp) {
  const branch = git(['rev-parse', '--abbrev-ref', 'HEAD']);
  if (branch !== 'main') {
    console.log(`\x1b[33m⚠ 当前分支是 ${branch}，不是 main。tag 会指向这个分支上的提交。\x1b[0m`);
  }

  const manifestPaths = changes.map((c) => manifestRepoPath(c.name));
  warnAboutUncommittedWork(manifestPaths);

  const tag = `v${stamp}-ui`;

  if (changes.length > 0) {
    const message = commitMessage(changes, stamp);
    // 显式 pathspec：本仓常有并发会话在改前端包，git add 目录会把别人写了一半的东西卷进来。
    gitRun(['add', '--', ...manifestPaths]);
    gitRun(['commit', '-m', message, '--', ...manifestPaths]);
    gitRun(['tag', '-a', tag, '-m', message.split('\n')[0]]);
  } else {
    // 只打 tag：版本号已经在之前的提交里（上次发到一半、或手动改的），CI 的 Preflight 会跳过已上线的包。
    gitRun(['tag', '-a', tag, '-m', `chore(release): ${SCOPE}/* ${stamp}`]);
  }

  return { branch, tag };
}

function push(branch, tag) {
  gitRun(['push', 'origin', branch]);
  gitRun(['push', 'origin', tag]);
  const web = repoWebUrl();
  console.log(`\n\x1b[32m✓ 已推送 ${tag}，CI 接手发布。\x1b[0m`);
  if (web) console.log(`   ${web}/actions/workflows/${WORKFLOW_FILE}`);
}

// ── 主流程 ──

function parseArgs(argv) {
  const args = { positional: [], status: false, plan: false, help: false, tag: false, stamp: null, push: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--status') args.status = true;
    else if (a === '--plan') args.plan = true;
    else if (a === '--help' || a === '-h') args.help = true;
    else if (a === '--push') { args.push = true; args.tag = true; }
    else if (a === '--tag') {
      args.tag = true;
      if (argv[i + 1] && !argv[i + 1].startsWith('--') && /^\d{4}\.\d{2}\.\d{2}/.test(argv[i + 1])) args.stamp = argv[++i];
    } else if (a.startsWith('--')) fail(`未知参数: ${a}`);
    else args.positional.push(a);
  }
  return args;
}

function printHelp() {
  const src = readFileSync(fileURLToPath(import.meta.url), 'utf-8');
  console.log(src.match(/\/\*\*([\s\S]*?)\*\//)?.[1]?.replace(/^ \* ?/gm, '') ?? '');
}

async function main() {
  const args = parseArgs(process.argv.slice(2));

  if (args.help) return printHelp();
  if (args.status) return showStatus();
  if (args.plan) {
    const plan = await buildPlan();
    console.log(formatPlanText(plan));
    process.exit(plan.errors.length ? 1 : 0);
  }

  const [target, versionType = 'auto'] = args.positional;
  if (!target && !args.tag) {
    console.error('用法: pnpm release <core|ui|ui-ai|ui-admin|mobile|all> [auto|patch|minor|major|x.y.z] [--tag [stamp]] [--push]');
    console.error('      pnpm release --tag [stamp] [--push]   不动版本号，只打 tag');
    console.error('      pnpm release --status    查看版本');
    console.error('      pnpm release --plan      查看这次会发哪些包');
    process.exit(1);
  }

  // 没给包名 + --tag = 只打 tag：版本号已经在之前的提交里（手动改过、或上次发到一半）。
  const targets = !target ? [] : target === 'all' ? [...PACKAGES] : [target];
  for (const t of targets) {
    if (!PACKAGES.includes(t)) fail(`未知的包: ${t}\n  可选: ${PACKAGES.join(', ')}, all`);
  }

  const published = await fetchAllPublished();
  const { changes, revert } = bumpPackages(targets, versionType, published);

  // 与 CI Preflight 同一份逻辑：这里过不了，推上去也一定过不了。
  console.log('\n\x1b[1m📋 发布计划\x1b[0m（本地版本不在 npm 上的包都会发）');
  const plan = await buildPlan({ published });
  console.log(formatPlanText(plan));
  if (plan.errors.length) {
    revert();
    fail(changes.length ? '发布计划不可执行，版本号已恢复。' : '发布计划不可执行。');
  }

  if (!args.tag) {
    if (changes.length) {
      console.log('\x1b[32m✓ 版本号已写入 package.json。\x1b[0m');
      console.log('  下一步: 加 --push 提交并打 tag\n');
    } else {
      console.log('\x1b[32m✓ 版本号无需改动。\x1b[0m');
      console.log('  下一步: pnpm release --tag --push 打 tag 触发 CI\n');
    }
    return;
  }

  if (changes.length === 0) {
    // 这次不提交任何东西，tag 指向 HEAD。计划是按磁盘上的 package.json 算的，而 CI 按 tag
    // 指向的提交算；两者不一致的唯一来源就是没提交的版本号改动：本机说「发」，CI 说
    // 「没东西可发」，tag 推上去只会红。
    const dirtyManifests = git(['status', '--porcelain', '--', ...PACKAGES.map(manifestRepoPath)]);
    if (dirtyManifests) fail(`package.json 有未提交的改动，tag 会指向旧版本。先提交：\n${dirtyManifests}`);
  }

  const stamp = resolveStamp(args.stamp);
  const { branch, tag } = commitAndTag(changes, stamp);

  if (args.push) {
    push(branch, tag);
  } else {
    console.log(`\n\x1b[32m✓ 已${changes.length ? '提交并' : ''}打 tag ${tag}（未推送）。\x1b[0m`);
    console.log(`  推送: git push origin ${branch} && git push origin ${tag}\n`);
  }
}

main().catch((error) => fail(error.message));

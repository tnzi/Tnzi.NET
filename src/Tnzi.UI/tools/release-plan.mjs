#!/usr/bin/env node

/**
 * @tnzi/* 发布计划：本地 package.json 版本 vs npm 线上版本 → 这一次会发出去哪些包。
 *
 * 同一份逻辑有两个调用方，必须保持一致，所以只写在这里：
 *   - tools/release.mjs            本地：升版本、打 tag 之前先算出会发出去什么
 *   - ui-npm-publish.yml Preflight CI：build 之前就拒掉「空发布」与「依赖不可满足」
 *
 * 用法:
 *   node tools/release-plan.mjs                      # 五个包全部纳入
 *   node tools/release-plan.mjs --packages ui,mobile # 只看列出的包
 *   node tools/release-plan.mjs --github             # 额外写 $GITHUB_OUTPUT / $GITHUB_STEP_SUMMARY
 *
 * 退出码: 0 = 计划可执行；1 = 计划为空 / 依赖不可满足 / 版本倒退 / registry 查询失败。
 *
 * 为什么「计划为空」是错误而不是「无事可做」：tag 触发的发布如果什么都没发却全绿，
 * 和「发布成功」在 Actions 列表里长得一模一样（NuGet 那条 2026-08-15 就是这样漏的）。
 */

import { readFileSync, writeFileSync, appendFileSync } from 'fs';
import { resolve, dirname } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
export const ROOT = resolve(__dirname, '..');

/** 发布顺序 = 依赖顺序（与根 package.json 的 build 脚本一致）：ui-admin 的 peer 里有 ui-ai。 */
export const PACKAGES = ['core', 'ui', 'ui-ai', 'ui-admin', 'mobile'];
export const SCOPE = '@tnzi';
export const REGISTRY = 'https://registry.npmjs.org';

// ── package.json ──

export function readManifest(name) {
  return JSON.parse(readFileSync(manifestPath(name), 'utf-8'));
}

/** 保留文件原有的换行风格：本仓 autocrlf 下工作区是 CRLF，写成 LF 会让 git 报一个空 diff 的「已修改」。 */
export function writeManifest(name, manifest) {
  const path = manifestPath(name);
  const eol = readFileSync(path, 'utf-8').includes('\r\n') ? '\r\n' : '\n';
  writeFileSync(path, JSON.stringify(manifest, null, 2).replace(/\n/g, eol) + eol);
}

function manifestPath(name) {
  return resolve(ROOT, 'packages', name, 'package.json');
}

export function bumpVersion(current, type) {
  if (type.includes('.')) {
    if (!/^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$/.test(type)) throw new Error(`Not a version: ${type}`);
    return type; // 具体版本号直接用
  }
  if (!/^\d+\.\d+\.\d+$/.test(current)) {
    throw new Error(`Cannot ${type}-bump "${current}": only plain x.y.z is supported, pass the exact version instead`);
  }

  const [major, minor, patch] = current.split('.').map(Number);
  switch (type) {
    case 'major': return `${major + 1}.0.0`;
    case 'minor': return `${major}.${minor + 1}.0`;
    case 'patch': return `${major}.${minor}.${patch + 1}`;
    default: throw new Error(`Unknown version bump type: ${type}`);
  }
}

/** 只比 x.y.z 三段；预发布后缀视为低于同号正式版。包一直用纯 x.y.z，够用。 */
export function compareVersions(a, b) {
  const parse = (v) => {
    const [core, pre] = v.split('-');
    return { nums: core.split('.').map(Number), pre };
  };
  const pa = parse(a);
  const pb = parse(b);
  for (let i = 0; i < 3; i++) {
    const diff = (pa.nums[i] ?? 0) - (pb.nums[i] ?? 0);
    if (diff !== 0) return diff;
  }
  if (pa.pre && !pb.pre) return -1;
  if (!pa.pre && pb.pre) return 1;
  return 0;
}

// ── npm registry ──

/**
 * 线上已有的版本集合；包不存在（404）返回 null。
 * 其它任何非 200 都抛出：查不到就拒发、不猜，误挡一次远好过发一次静默空包。
 */
export async function fetchPublishedVersions(name) {
  const url = `${REGISTRY}/${SCOPE}%2F${name}`;
  const response = await fetch(url, {
    headers: { Accept: 'application/vnd.npm.install-v1+json' },
  });
  if (response.status === 404) return null;
  if (!response.ok) throw new Error(`npm registry query for ${SCOPE}/${name} failed with HTTP ${response.status}`);
  const body = await response.json();
  return {
    versions: new Set(Object.keys(body.versions ?? {})),
    latest: body['dist-tags']?.latest ?? null,
  };
}

/** 五个包各查一次 registry；调用方拿着结果既做版本决策又算计划，不用查两遍。 */
export async function fetchAllPublished() {
  const published = {};
  for (const name of PACKAGES) {
    published[name] = await fetchPublishedVersions(name);
  }
  return published;
}

/**
 * 本地版本是否已经领先线上：不在 npm 上、且高于线上最新。
 * 这是「要不要升版本」的唯一判据：领先就原样发，版本号只需比线上多一个。
 */
export function isAheadOfNpm(localVersion, onNpm) {
  if (onNpm === null) return false;
  if (onNpm.versions.has(localVersion)) return false;
  return !onNpm.latest || compareVersions(localVersion, onNpm.latest) > 0;
}

/** manifest 里经 workspace 协议指向的同仓包（短名），peer 与普通依赖都算。 */
export function workspaceDeps(manifest) {
  const result = [];
  for (const field of ['dependencies', 'peerDependencies', 'optionalDependencies']) {
    for (const [dep, spec] of Object.entries(manifest[field] ?? {})) {
      if (!dep.startsWith(`${SCOPE}/`) || !String(spec).startsWith('workspace:')) continue;
      result.push({ name: dep.slice(SCOPE.length + 1), spec, field });
    }
  }
  return result;
}

// ── 计划 ──

/**
 * @param {{ targets?: string[] }} options targets 缺省为全部五个包
 * @returns {Promise<{ entries: PlanEntry[], publish: string[], errors: string[] }>}
 *
 * PlanEntry.status:
 *   publish    本地版本不在 npm 上，这次发
 *   skip       本地版本已在 npm 上，跳过（这是常态：没动的包版本号不变）
 *   first      包根本不在 npm 上（trusted publishing 发不了首个版本，报错）
 *   untargeted 不在 --packages 列表里，只列出来供对照
 */
export async function buildPlan({ targets = PACKAGES, published } = {}) {
  for (const t of targets) {
    if (!PACKAGES.includes(t)) throw new Error(`Unknown package: ${t} (expected one of ${PACKAGES.join(', ')})`);
  }

  const manifests = Object.fromEntries(PACKAGES.map((name) => [name, readManifest(name)]));
  published ??= await fetchAllPublished();

  const entries = PACKAGES.map((name) => {
    const { version } = manifests[name];
    const onNpm = published[name];
    const latest = onNpm?.latest ?? null;
    if (!targets.includes(name)) return { name, version, latest, status: 'untargeted' };
    if (onNpm === null) return { name, version, latest, status: 'first' };
    if (onNpm.versions.has(version)) return { name, version, latest, status: 'skip' };
    return { name, version, latest, status: 'publish' };
  });

  const publish = entries.filter((e) => e.status === 'publish').map((e) => e.name);
  const errors = [];

  for (const e of entries.filter((e) => e.status === 'first')) {
    errors.push(
      `${SCOPE}/${e.name} is not on npm yet. Trusted publishing cannot create a package: ` +
      `publish its first version by hand once, then add the trusted publisher on npmjs.com.`,
    );
  }

  for (const e of entries.filter((e) => e.status === 'publish')) {
    if (e.latest && compareVersions(e.version, e.latest) < 0) {
      errors.push(
        `${SCOPE}/${e.name}@${e.version} is lower than the latest on npm (${e.latest}); ` +
        `npm refuses to move the "latest" tag backwards.`,
      );
    }
  }

  // 依赖必须可安装：要么同批发布，要么那个版本已经在线上。
  // 否则 ui-admin 会带着 "@tnzi/core": "^0.2.3" 上线，而 0.2.3 根本不存在，
  // 消费方 install 直接失败，且这一条 publish 本身不会报错。
  for (const name of publish) {
    for (const dep of workspaceDeps(manifests[name])) {
      const depVersion = manifests[dep.name].version;
      const satisfied = publish.includes(dep.name) || published[dep.name]?.versions.has(depVersion) === true;
      if (!satisfied) {
        errors.push(
          `${SCOPE}/${name} ${dep.field} on ${SCOPE}/${dep.name}@${depVersion} (${dep.spec}), ` +
          `but that version is neither on npm nor in this release. Publish ${dep.name} too.`,
        );
      }
    }
  }

  if (publish.length === 0 && errors.length === 0) {
    errors.push(
      'Nothing to publish: every targeted package already has its package.json version on npm. ' +
      'Bump a version first (node tools/release.mjs <package> patch).',
    );
  }

  return { entries, publish, errors };
}

// ── 输出 ──

const STATUS_LABEL = {
  publish: '发布',
  skip: '跳过（已在 npm）',
  first: '不在 npm 上',
  untargeted: '未选中',
};

export function formatPlanText(plan) {
  const lines = [''];
  for (const e of plan.entries) {
    const name = `${SCOPE}/${e.name}`.padEnd(16);
    const local = e.version.padEnd(8);
    const npm = (e.latest ?? '(none)').padEnd(8);
    const mark = e.status === 'publish' ? '\x1b[32m→\x1b[0m' : ' ';
    lines.push(`  ${mark} ${name} 本地 \x1b[33m${local}\x1b[0m npm \x1b[36m${npm}\x1b[0m ${STATUS_LABEL[e.status]}`);
  }
  lines.push('');
  for (const err of plan.errors) lines.push(`  \x1b[31m✗ ${err}\x1b[0m`);
  if (plan.errors.length) lines.push('');
  return lines.join('\n');
}

export function formatPlanMarkdown(plan) {
  const lines = ['| Package | Local | npm latest | Action |', '|---|---|---|---|'];
  for (const e of plan.entries) {
    const action = e.status === 'publish' ? '**publish**' : STATUS_LABEL[e.status];
    lines.push(`| \`${SCOPE}/${e.name}\` | ${e.version} | ${e.latest ?? '(none)'} | ${action} |`);
  }
  return lines.join('\n');
}

// ── CLI ──

function parseArgs(argv) {
  const args = { github: false, packages: PACKAGES };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--github') args.github = true;
    else if (a === '--packages') args.packages = splitList(argv[++i]);
    else if (a.startsWith('--packages=')) args.packages = splitList(a.slice('--packages='.length));
    else throw new Error(`Unknown argument: ${a}`);
  }
  return args;
}

function splitList(value) {
  const list = (value ?? '').split(',').map((s) => s.trim()).filter(Boolean);
  return list.length ? list : PACKAGES;
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const plan = await buildPlan({ targets: args.packages });

  console.log(formatPlanText(plan));

  if (args.github) {
    if (process.env.GITHUB_OUTPUT) {
      appendFileSync(process.env.GITHUB_OUTPUT, `publish=${plan.publish.join(' ')}\n`);
      const versions = plan.entries.filter((e) => e.status === 'publish').map((e) => `${SCOPE}/${e.name}@${e.version}`);
      appendFileSync(process.env.GITHUB_OUTPUT, `versions=${versions.join(' ')}\n`);
    }
    if (process.env.GITHUB_STEP_SUMMARY) {
      appendFileSync(process.env.GITHUB_STEP_SUMMARY, `## Release plan\n\n${formatPlanMarkdown(plan)}\n\n`);
    }
    for (const err of plan.errors) console.log(`::error::${err}`);
  }

  process.exit(plan.errors.length ? 1 : 0);
}

const isDirectRun = process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url);
if (isDirectRun) {
  main().catch((error) => {
    console.error(`\x1b[31m✗ ${error.message}\x1b[0m`);
    if (process.env.GITHUB_ACTIONS) console.log(`::error::${error.message}`);
    process.exit(1);
  });
}

// Adapts `docfx metadata` markdown output (dest: src/content/docs/api) for Starlight:
//   1. injects frontmatter (title, description, explicit slug, no edit link / pagination)
//   2. rewrites flat `Type.Name.md` links to the explicit route slugs
//   3. converts unresolved <xref> tags (invisible in HTML) to inline code
//   4. builds src/generated/api-sidebar.json from DocFX's toc.yml, then removes toc.yml
//   5. writes the /api/ overview page
// Idempotent: files that already start with frontmatter are left untouched.
import { readFileSync, writeFileSync, readdirSync, existsSync, mkdirSync, rmSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const SITE_ROOT = path.dirname(fileURLToPath(new URL('.', import.meta.url)));
const API_DIR = path.join(SITE_ROOT, 'src', 'content', 'docs', 'api');
const GENERATED_DIR = path.join(SITE_ROOT, 'src', 'generated');
const SIDEBAR_FILE = path.join(GENERATED_DIR, 'api-sidebar.json');
const TOC_FILE = path.join(API_DIR, 'toc.yml');

if (!existsSync(API_DIR)) {
  console.error(
    `API markdown not found at ${API_DIR}.\n` +
      'Run `dotnet docfx metadata docfx.json` from the repo root first.',
  );
  process.exit(1);
}

function routeFor(mdFileName) {
  return `/api/${mdFileName.replace(/\.md$/, '').toLowerCase()}/`;
}

function slugFor(mdFileName) {
  return `api/${mdFileName.replace(/\.md$/, '').toLowerCase()}`;
}

function yamlQuote(s) {
  return `'${s.replace(/'/g, "''")}'`;
}

// --- 1-3. Per-page transform -------------------------------------------------

const mdFiles = readdirSync(API_DIR).filter((f) => f.endsWith('.md') && f !== 'index.md');
let processed = 0;
let skipped = 0;

for (const file of mdFiles) {
  const abs = path.join(API_DIR, file);
  const raw = readFileSync(abs, 'utf8');
  if (raw.startsWith('---')) {
    skipped++;
    continue;
  }
  const lines = raw.split('\n');
  const h1 = lines[0].match(/^#\s+(?:(<a id="[^"]*"><\/a>)\s*)?(.*)$/);
  if (!h1) throw new Error(`Unexpected first line in ${file}: ${lines[0]}`);
  const anchor = h1[1] ?? '';
  // Undo CommonMark escapes (e.g. `IEventKernel<TEvent\>`) for the plain-text title.
  const title = h1[2].replace(/\\(.)/g, '$1').trim();

  let body = lines.slice(1).join('\n');
  // Flat intra-API links -> explicit route slugs (match the URL, not the label,
  // so labels containing brackets cannot break the rewrite). DocFX backslash-escapes
  // `-`, `_`, and `#` inside destinations (e.g. `IEventKernel\-1.md`), so unescape
  // before deciding whether a destination is an intra-API .md link.
  body = body.replace(/\]\(([^()\s]+)\)/g, (m, rawTarget) => {
    const target = rawTarget.replace(/\\(.)/g, '$1');
    const link = target.match(/^([A-Za-z0-9_.\-]+\.md)(#[^)\s]*)?$/);
    if (!link) return m;
    return `](${routeFor(link[1])}${link[2] ?? ''})`;
  });
  // Unresolved cross-references render as empty inline HTML; show them as code.
  body = body.replace(/<xref href="([^"]+)"[^>]*><\/xref>/g, (_m, href) => {
    const name = decodeURIComponent(href).replace(/`+\d+/g, '');
    return `\`${name}\``;
  });

  const fm = [
    '---',
    `title: ${yamlQuote(title)}`,
    `description: ${yamlQuote(`${title} — DotBoxD API reference.`)}`,
    `slug: ${yamlQuote(slugFor(file))}`,
    'editUrl: false',
    'prev: false',
    'next: false',
    '---',
    '',
  ].join('\n');
  writeFileSync(abs, fm + (anchor ? anchor + '\n\n' : '') + body, 'utf8');
  processed++;
}

// --- 4. Sidebar from toc.yml -------------------------------------------------

// DocFX's toc.yml only uses `- name:`, `href:`, and `items:` with 2-space indents,
// so a tiny indentation parser is enough — no YAML dependency.
function parseToc(text) {
  const root = { items: [] };
  const stack = [{ indent: -1, node: root }];
  for (const rawLine of text.split('\n')) {
    if (!rawLine.trim() || rawLine.startsWith('###')) continue;
    const indent = rawLine.length - rawLine.trimStart().length;
    const line = rawLine.trim();
    if (line.startsWith('- name:')) {
      const node = { name: line.slice('- name:'.length).trim(), items: [] };
      while (stack[stack.length - 1].indent >= indent) stack.pop();
      stack[stack.length - 1].node.items.push(node);
      stack.push({ indent, node });
    } else if (line.startsWith('href:')) {
      stack[stack.length - 1].node.href = line.slice('href:'.length).trim();
    }
    // `items:` lines carry no data; nesting is tracked via indentation.
  }
  return root.items;
}

// A toc node with children is a namespace; leaves with an href are types;
// bare `Classes`/`Interfaces`/... separators have neither and are dropped.
function collectNamespaces(nodes, out) {
  for (const node of nodes) {
    if (!node.items.length) continue;
    const fullName = node.href ? node.href.replace(/\.md$/, '') : node.name;
    const types = node.items.filter((i) => i.href && !i.items.length);
    const group = {
      label: fullName,
      collapsed: true,
      items: [
        ...(node.href ? [{ label: 'Namespace overview', link: routeFor(node.href) }] : []),
        ...types.map((t) => ({ label: t.name, link: routeFor(t.href) })),
      ],
    };
    if (group.items.length) out.push(group);
    collectNamespaces(node.items.filter((i) => i.items.length), out);
  }
  return out;
}

let namespaceGroups = [];
if (existsSync(TOC_FILE)) {
  namespaceGroups = collectNamespaces(parseToc(readFileSync(TOC_FILE, 'utf8')), []);
  rmSync(TOC_FILE); // keep the content directory markdown-only
}

mkdirSync(GENERATED_DIR, { recursive: true });
writeFileSync(
  SIDEBAR_FILE,
  JSON.stringify(
    [
      {
        label: 'API reference',
        collapsed: true,
        items: [{ label: 'Overview', link: '/api/' }, ...namespaceGroups],
      },
    ],
    null,
    2,
  ),
  'utf8',
);

// --- 5. /api/ overview page --------------------------------------------------

const nsList = namespaceGroups
  .map((g) => `- [\`${g.label}\`](${g.items[0]?.link ?? '/api/'})`)
  .join('\n');
writeFileSync(
  path.join(API_DIR, 'index.md'),
  `---
title: 'API reference'
description: 'Generated .NET API reference for every published DotBoxD package.'
slug: 'api'
editUrl: false
prev: false
next: false
---

Generated from the XML documentation comments of every published DotBoxD package on each deploy
(via \`docfx metadata\`). Use the search box to jump straight to a type or member.

## Namespaces

${nsList}
`,
  'utf8',
);

console.log(
  `postprocess-api: ${processed} pages processed, ${skipped} already processed, ` +
    `${namespaceGroups.length} namespace groups in sidebar`,
);

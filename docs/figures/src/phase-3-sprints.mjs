#!/usr/bin/env node
// Builds the Phase 3 sprint figures (docs/PHASE_3_SPRINTS.md). Never edit the SVGs by hand:
//   node docs/figures/src/phase-3-sprints.mjs
// Figure kit adapted from the blueprint-figure skill template. Hue meanings for this set:
//   rose failure · grey outside Stria · amber lifecycle · teal durable state
//   blue Stria host behaviour · violet configuration and artifacts · green verification
//   indigo observability · olive effects outside Stria
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const OUT = join(dirname(fileURLToPath(import.meta.url)), '..', 'phase-3');

const WIDTH = 640;
const MAX_HEIGHT = 760;
const MIN_FONT = 11;
const MONO = 0.6;
const SERIF = 0.54;
// Quoted: an unquoted family name containing "4" is invalid CSS, which drops the whole list.
const SERIF_FONT = "'Source Serif 4', Georgia, serif";
const ASCENT = 0.78;
const DESCENT = 0.22;

const TOKENS = {
  paper: '#fafaf5', panel: '#f3f1e8', 'panel-edge': '#e4e1d4',
  ink: '#1a1a1a', 'ink-soft': '#4a4a4a', 'ink-mute': '#6f6f6a',
  'grey-ink': '#5e5e59', 'grey-fill': '#ecebe3',
  'blue-ink': '#2f5d9a', 'blue-fill': '#e6edf6',
  'teal-ink': '#18696a', 'teal-fill': '#e1efee',
  'violet-ink': '#5b4fa3', 'violet-fill': '#ebe8f5',
  'green-ink': '#336b40', 'green-fill': '#e5f0e4',
  'amber-ink': '#85590c', 'amber-fill': '#f5ecd9',
  'plum-ink': '#8a3d72', 'plum-fill': '#f4e6ef',
  'olive-ink': '#515f26', 'olive-fill': '#eef0dc',
  'indigo-ink': '#4b4f9e', 'indigo-fill': '#e9e9f6',
  'rose-ink': '#b0344d', 'rose-fill': '#f7e4e7',
};
const NEUTRALS = ['ink', 'ink-soft', 'ink-mute', 'paper', 'panel', 'panel-edge'];
const DASH = { solid: null, dashed: '5 4', dotted: '1.5 3.5' };
const STYLES = {
  call: ['ink', 'solid'], reply: ['ink', 'dashed'], write: ['teal', 'solid'],
  state: ['amber', 'dashed'], effect: ['olive', 'dashed'], fail: ['rose', 'dashed'],
};

const color = name => {
  if (!TOKENS[name]) throw new Error(`unknown token ${name}`);
  return `var(--m-${name}, ${TOKENS[name]})`;
};
const ink = hue => color(NEUTRALS.includes(hue) ? hue : `${hue}-ink`);
const fill = hue => color(NEUTRALS.includes(hue) ? hue : `${hue}-fill`);
const esc = s => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
const attrs = map => Object.entries(map).filter(([, v]) => v !== undefined && v !== null).map(([k, v]) => ` ${k}="${esc(v)}"`).join('');
const measure = (text, size, serif, spacing = 0) => {
  const n = Array.from(String(text)).length;
  return n * size * (serif ? SERIF : MONO) + Math.max(0, n - 1) * spacing;
};

function figure(id, { height, title, desc }, draw) {
  if (!/^[a-z0-9]+(-[a-z0-9]+)*$/.test(id)) throw new Error(`bad id ${id}`);
  if (height > MAX_HEIGHT) throw new Error(`${id}: height ${height} is above ${MAX_HEIGHT}`);
  if (!title || !desc || desc.length < 40) throw new Error(`${id}: title and a desc of 40+ characters required`);
  const out = [];
  const heads = new Set();

  const f = {
    text(x, y, content, o = {}) {
      const size = o.size || 12;
      const anchor = o.anchor || 'start';
      const w = measure(content, size, o.serif, o.spacing);
      const left = anchor === 'middle' ? x - w / 2 : anchor === 'end' ? x - w : x;
      if (size < MIN_FONT) throw new Error(`${id}: "${content}" is ${size} units, minimum ${MIN_FONT}`);
      if (/…|\.\.\./.test(content)) throw new Error(`${id}: no ellipsis in "${content}"`);
      if (o.max && w > o.max) throw new Error(`${id}: "${content}" is ${Math.ceil(w)} wide, max ${o.max}`);
      if (left < 1 || left + w > WIDTH - 1 || y - ASCENT * size < 1 || y + DESCENT * size > height - 1)
        throw new Error(`${id}: "${content}" runs outside the figure`);
      if (o.knock) out.push(`<rect${attrs({ class: 'm-knock', x: left - 3, y: y - ASCENT * size - 2, width: w + 6, height: size + 4, fill: color('panel') })}/>`);
      out.push(`<text${attrs({
        x, y, 'font-size': size,
        'text-anchor': anchor === 'start' ? undefined : anchor,
        'font-weight': o.weight,
        'letter-spacing': o.spacing,
        class: o.serif ? 'm-serif' : undefined,
        'font-family': o.serif ? SERIF_FONT : undefined,
        fill: o.hue ? ink(o.hue) : undefined,
      })}>${esc(content)}</text>`);
    },
    kicker(x, y, content, anchor) {
      f.text(x, y, String(content).toUpperCase(), { size: 11, spacing: 1.2, hue: 'ink-mute', anchor });
    },
    note(x, y, content, anchor) {
      f.text(x, y, content, { size: 11, serif: true, hue: 'ink-soft', anchor });
    },
    box({ x, y, w, h, hue = 'grey', title: t, sub, dash, double, center }) {
      const left = center ? x - w / 2 : x;
      out.push(`<rect${attrs({ x: left, y, width: w, height: h, fill: fill(hue), stroke: ink(hue), 'stroke-dasharray': dash ? DASH[dash] : undefined })}/>`);
      if (double) out.push(`<rect${attrs({ x: left + 3, y: y + 3, width: w - 6, height: h - 6, fill: 'none', stroke: ink(hue) })}/>`);
      const cx = left + w / 2;
      const inner = w - 24;
      if (sub) {
        f.text(cx, y + h / 2 - 2, t, { size: 12.5, weight: 700, anchor: 'middle', max: inner });
        f.text(cx, y + h / 2 + 12, sub, { size: 11, serif: true, hue: 'ink-soft', anchor: 'middle', max: inner });
      } else {
        f.text(cx, y + h / 2 + 4.5, t, { size: 12.5, weight: 700, anchor: 'middle', max: inner });
      }
      return { x: left, y, w, h, cx, cy: y + h / 2, r: left + w, b: y + h };
    },
    line(x1, y1, x2, y2) {
      out.push(`<line${attrs({ x1, y1, x2, y2, stroke: color('ink-mute'), 'stroke-width': 1 })}/>`);
    },
    lifeline(x, y1, y2) {
      out.push(`<line${attrs({ x1: x, y1, x2: x, y2, stroke: color('ink-mute'), 'stroke-width': 1, 'stroke-dasharray': '2 3' })}/>`);
    },
    dot(x, y) {
      out.push(`<circle${attrs({ cx: x, cy: y, r: 5, fill: color('ink') })}/>`);
    },
    step(x, y, n) {
      out.push(`<circle${attrs({ cx: x, cy: y, r: 9, fill: color('paper'), stroke: color('ink'), 'stroke-width': 1 })}/>`);
      f.text(x, y + 4, String(n), { size: 11, anchor: 'middle' });
    },
    arrow([x1, y1], [x2, y2], { style = 'call', label, note } = {}) {
      const [hue, dash] = STYLES[style];
      heads.add(hue);
      out.push(`<line${attrs({
        x1, y1, x2, y2, stroke: ink(hue), 'stroke-width': 1.2,
        'stroke-dasharray': DASH[dash] || undefined,
        'stroke-linecap': dash === 'dotted' ? 'round' : undefined,
        'marker-end': `url(#${id}-head-${hue})`,
      })}/>`);
      const mx = (x1 + x2) / 2;
      const my = (y1 + y2) / 2;
      if (label) f.text(mx, my - 6, label, { size: 11.5, anchor: 'middle', hue: hue === 'ink' ? undefined : hue, knock: true });
      if (note) f.text(mx, my + 15, note, { size: 11, serif: true, hue: 'ink-soft', anchor: 'middle', knock: true });
    },
  };

  draw(f);
  const defs = [...heads].map(hue => `<marker id="${id}-head-${hue}" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="8" markerHeight="8" markerUnits="userSpaceOnUse" orient="auto-start-reverse"><path d="M0 1 L10 5 L0 9 z" fill="${ink(hue)}"/></marker>`);
  const svg = [
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${WIDTH} ${height}" role="img" aria-labelledby="${id}-title ${id}-desc" font-family="JetBrains Mono, ui-monospace, monospace" fill="${color('ink')}">`,
    `  <title id="${id}-title">${esc(title)}</title>`,
    `  <desc id="${id}-desc">${esc(desc)}</desc>`,
    `  <rect x="0" y="0" width="${WIDTH}" height="${height}" fill="${color('panel')}"/>`,
    defs.length ? `  <defs>${defs.join('')}</defs>` : null,
    ...out.map(line => `  ${line}`),
    '</svg>',
    '',
  ].filter(line => line !== null).join('\n');
  if (Buffer.byteLength(svg) > 40 * 1024) throw new Error(`${id}: SVG is larger than 40 KB`);
  return svg;
}

// Sequence helper: four lanes, numbered horizontal exchanges.
function sequence(f, lanes, rows, { top = 28, firstRow = 104, rowGap = 44, bottom }) {
  for (const lane of lanes) {
    f.box({ x: lane.x, y: top, w: 144, h: 40, hue: lane.hue, title: lane.title, sub: lane.sub, center: true });
    f.lifeline(lane.x, top + 40, bottom);
  }
  rows.forEach(([from, to, style, label, note], index) => {
    const y = firstRow + index * rowGap;
    f.step(16, y, index + 1);
    const x1 = lanes[from].x;
    const x2 = lanes[to].x;
    f.arrow([x1, y], [x2 + (x2 > x1 ? -1 : 1), y], { style, label, note });
  });
}

const figures = {};

// Figure 0.1: the legend for the set.
figures['fig-p3-legend'] = figure('fig-p3-legend', {
  height: 300,
  title: 'Legend for the Phase 3 sprint figures',
  desc: 'Arrow styles: solid ink is a call, dashed ink a reply, solid teal a durable write, dashed amber a state change, dashed rose a failure. Borders: solid is a part that exists, dashed is planned or optional, double is a terminal state.',
}, f => {
  f.kicker(20, 22, 'Figure 0.1 · how to read the sprint figures');
  const arrows = [
    ['call', 'call or request'],
    ['reply', 'reply'],
    ['write', 'durable write (database, Blob, Key Vault, registry)'],
    ['state', 'state change'],
    ['fail', 'failure: the run stops here'],
  ];
  arrows.forEach(([style, meaning], i) => {
    const y = 56 + i * 30;
    f.arrow([40, y], [179, y], { style });
    f.note(200, y + 4, meaning);
  });
  f.kicker(20, 218, 'Borders');
  f.box({ x: 20, y: 232, w: 180, h: 44, hue: 'blue', title: 'solid', sub: 'exists or runs now' });
  f.box({ x: 230, y: 232, w: 180, h: 44, hue: 'blue', title: 'dashed', sub: 'planned or optional', dash: 'dashed' });
  f.box({ x: 440, y: 232, w: 180, h: 44, hue: 'amber', title: 'double', sub: 'terminal state', double: true });
});

// Sprint 0: the smoke script no longer races the container start.
figures['fig-p3-s0'] = figure('fig-p3-s0', {
  height: 490,
  title: 'Sprint 0: compose-smoke.sh waits for /readyz before checking',
  desc: 'Lifelines: docker compose, compose-smoke.sh, stria-server and the PostgreSQL db. 1 compose starts stria-server and returns before Kestrel listens. 2 a GET /readyz gets 000. 3 the script polls /readyz every second up to STRIA_READY_TIMEOUT. 4 stria-server calls CanConnectAsync on db. 5 db answers. 6 stria-server replies 200. 7 the script runs 16 checks. 8 all checks pass.',
}, f => {
  f.kicker(20, 18, 'Sprint 0 · close out prep · ticket 0.3');
  const lanes = [
    { x: 92, title: 'docker compose', sub: 'up --build -d', hue: 'grey' },
    { x: 240, title: 'compose-smoke.sh', sub: 'scripts/', hue: 'green' },
    { x: 388, title: 'stria-server', sub: 'Kestrel :8080', hue: 'blue' },
    { x: 548, title: 'db', sub: 'PostgreSQL', hue: 'teal' },
  ];
  sequence(f, lanes, [
    [0, 2, 'call', 'start stria-server', 'returns before Kestrel listens'],
    [1, 2, 'fail', 'GET /readyz: 000', 'no listener; old script failed 16 checks'],
    [1, 2, 'call', 'GET /readyz every 1 s', 'up to STRIA_READY_TIMEOUT=60'],
    [2, 3, 'call', 'CanConnectAsync()', null],
    [3, 2, 'reply', 'connected', null],
    [2, 1, 'reply', '200 ready', null],
    [1, 2, 'call', '16 checks: 401, 403, 204, CSP', null],
    [2, 1, 'reply', 'All checks passed.', null],
  ], { bottom: 432 });
  f.note(20, 456, 'Also in Sprint 0: PR #22 merged after 8 of 8 checks; baseline 119 tests, 0 warnings.');
  f.note(20, 474, 'Verified twice: a forced restart passes after about 1.5 s; a dead port exits 1 after the timeout.');
});

// Sprint 1: one image, two behaviours, chosen by configuration.
figures['fig-p3-s1'] = figure('fig-p3-s1', {
  height: 520,
  title: 'Sprint 1: each Azure behaviour is a setting; Compose leaves them unset',
  desc: 'Five rows compare Compose, with the setting unset, and Azure, with it set: ForwardedHeaders:Enabled, the DataProtection Blob and Key Vault URIs, Database:UseEntraAuth, APPLICATIONINSIGHTS_CONNECTION_STRING, and the AzureRuntimeSettings startup check, which refuses a partial set.',
}, f => {
  f.kicker(20, 18, 'Sprint 1 · cloud-ready code · tickets 1.1 to 1.5');
  f.kicker(20, 48, 'Compose · setting unset');
  f.kicker(330, 48, 'Azure · setting set');
  const rows = [
    ['ForwardedHeaders:Enabled',
      ['blue', 'X-Forwarded-* ignored', 'nothing proxies; redirect_uri http://'],
      ['blue', 'trust ingress X-Forwarded-*', 'UseForwardedHeaders first; https://']],
    ['DataProtection:BlobUri + DataProtection:KeyVaultKeyUri',
      ['teal', 'stria-dpkeys volume', '/var/lib/stria/dpkeys'],
      ['teal', 'Blob keys.xml + Key Vault key', 'one key ring for every replica']],
    ['Database:UseEntraAuth',
      ['teal', 'password in connection string', 'STRIA_DB_PASSWORD from .env'],
      ['teal', 'Entra token as the password', 'UsePeriodicPasswordProvider, 55 min']],
    ['APPLICATIONINSIGHTS_CONNECTION_STRING',
      ['indigo', 'no exporter registered', 'StartActivity returns null'],
      ['indigo', 'UseAzureMonitor()', 'Stria and Npgsql spans to App Insights']],
    ['startup check: AzureRuntimeSettings.Missing()',
      ['green', 'none set: starts', 'Compose and tests unchanged'],
      ['rose', 'partial set: refuses to start', 'error names keys, never values']],
  ];
  rows.forEach(([key, left, right], i) => {
    const y = 68 + i * 78;
    f.text(20, y + 12, key, { size: 11.5, hue: 'violet' });
    const [lh, lt, ls] = left;
    const [rh, rt, rs] = right;
    f.box({ x: 20, y: y + 22, w: 290, h: 44, hue: lh, title: lt, sub: ls });
    f.box({ x: 330, y: y + 22, w: 290, h: 44, hue: rh, title: rt, sub: rs, dash: rh === 'rose' ? 'dashed' : undefined });
  });
  f.note(20, 478, 'Forwarded headers alone are allowed, since any reverse proxy may need them. App Insights is optional.');
  f.note(20, 496, 'Result: 139 tests pass (20 new); the Compose stack passes compose-smoke.sh unchanged.');
});

// Sprint 2: CI turns a commit into scanned, SHA-tagged release artifacts.
figures['fig-p3-s2'] = figure('fig-p3-s2', {
  height: 470,
  title: 'Sprint 2: CI builds scanned, SHA-tagged images and a migration bundle',
  desc: 'A push or pull request starts the container-build job. docker build targets linux/amd64, the image is tagged sha-commit, Trivy scans it and a HIGH or CRITICAL finding fails the job, then the efbundle migration image is built and run against CI PostgreSQL. Pull requests push nothing; main hands the images to the Sprint 5 deploy.',
}, f => {
  f.kicker(20, 18, 'Sprint 2 · release artifacts in CI · no Azure login');
  const stages = [
    ['grey', 'GitHub: push or pull_request', 'ci.yml, container-build job'],
    ['blue', 'docker build', '--platform linux/amd64 (Macs build arm64)'],
    ['violet', 'tag sha-<commit>', 'never latest; the same tag through deploy'],
    ['green', 'Trivy image scan', 'HIGH or CRITICAL fails the job; SARIF uploaded'],
    ['violet', 'efbundle migration image', 'bundle run against the CI PostgreSQL service'],
  ];
  const boxes = stages.map(([hue, t, s], i) => f.box({ x: 40, y: 34 + i * 64, w: 340, h: 42, hue, title: t, sub: s }));
  for (let i = 0; i < boxes.length - 1; i++)
    f.arrow([boxes[i].cx, boxes[i].b], [boxes[i].cx, boxes[i + 1].y - 1]);
  const scan = boxes[3];
  const failed = f.box({ x: 430, y: scan.y - 10, w: 190, h: 62, hue: 'rose', title: 'container-build fails', sub: 'required check blocks merge', dash: 'dashed' });
  f.arrow([scan.r, scan.cy], [failed.x - 1, scan.cy], { style: 'fail' });
  const last = boxes[4];
  const pr = f.box({ x: 40, y: 386, w: 260, h: 46, hue: 'grey', title: 'pull_request', sub: 'nothing is pushed', dash: 'dashed' });
  const main = f.box({ x: 340, y: 386, w: 280, h: 46, hue: 'violet', title: 'push to main', sub: 'images ready for the Sprint 5 deploy', dash: 'dashed' });
  f.arrow([last.cx, last.b], [pr.cx, pr.y - 1]);
  f.arrow([last.cx, last.b], [main.cx, main.y - 1]);
  f.note(20, 456, 'Publishing to Azure Container Registry is part of the deploy job in Sprint 5, never a PR.');
});

// Sprint 3: Bicep describes the whole resource set before anything exists.
figures['fig-p3-s3'] = figure('fig-p3-s3', {
  height: 410,
  title: 'Sprint 3: infra/main.bicep describes every resource in rg-stria-demo',
  desc: 'The resource group rg-stria-demo, from infra/main.bicep and azure.yaml, owns three groups. Shared: managed identity, ACR Basic, Key Vault and the storage account. Data and operations: PostgreSQL Flexible Server, Log Analytics, App Insights and a budget alert. Container Apps environment: stria-server, sample-payment-gateway and the migration job. All are dashed because none exists yet.',
}, f => {
  f.kicker(20, 18, 'Sprint 3 · infrastructure as code · bicep build in CI');
  const root = f.box({ x: 320, y: 30, w: 320, h: 44, hue: 'violet', title: 'rg-stria-demo', sub: 'infra/main.bicep + azure.yaml', center: true });
  const groups = [
    { x: 20, w: 180, name: 'Shared', items: [
      ['violet', 'Managed identity', 'roles on ACR, KV, Blob'],
      ['teal', 'ACR Basic', 'sha- tagged images'],
      ['teal', 'Key Vault (RBAC)', 'secrets + DP key'],
      ['teal', 'Storage account', 'dpkeys container'],
    ] },
    { x: 210, w: 180, name: 'Data and operations', items: [
      ['teal', 'PostgreSQL Flex', 'B1ms, Entra auth'],
      ['indigo', 'Log Analytics', 'container logs'],
      ['indigo', 'App Insights', 'traces from Stria'],
      ['olive', 'Budget alert', 'emails at 50%, 90%'],
    ] },
    { x: 400, w: 220, name: 'Container Apps env', items: [
      ['blue', 'stria-server', 'external HTTPS, max 1'],
      ['blue', 'sample-payment-gateway', 'internal ingress only'],
      ['amber', 'migration job', 'manual, before each release'],
    ] },
  ];
  for (const g of groups) {
    f.line(root.cx, root.b, g.x + 8, 106);
    f.kicker(g.x, 120, g.name);
    const ys = g.items.map((_, i) => 132 + i * 58);
    f.line(g.x + 8, 126, g.x + 8, ys[ys.length - 1] + 22);
    g.items.forEach(([hue, t, s], i) => {
      f.line(g.x + 8, ys[i] + 22, g.x + 19, ys[i] + 22);
      f.box({ x: g.x + 20, y: ys[i], w: g.w - 20, h: 44, hue, title: t, sub: s, dash: 'dashed' });
    });
  }
  f.note(20, 386, 'Dashed: described in Bicep and linted in CI, but not created until the Sprint 4 authorization.');
});

// Sprint 4: provisioning, only after written approval.
figures['fig-p3-s4'] = figure('fig-p3-s4', {
  height: 450,
  title: 'Sprint 4: provisioning and the one-time setup, after authorization',
  desc: 'Lifelines: Alvin, azd with Bicep, the Azure resource group and Entra ID. 1 Alvin runs azd provision after written approval. 2 azd creates the resources and budget. 3 Azure returns outputs. 4 Alvin sets the PostgreSQL Entra admin. 5 creates the identity principal with minimal grants. 6 stores secrets in Key Vault. 7 adds the hosted redirect URIs in Entra ID.',
}, f => {
  f.kicker(20, 18, 'Sprint 4 · provision · requires authorization');
  const lanes = [
    { x: 92, title: 'Alvin', sub: 'operator', hue: 'grey' },
    { x: 240, title: 'azd + Bicep', sub: 'infra/main.bicep', hue: 'violet' },
    { x: 388, title: 'Azure', sub: 'rg-stria-demo', hue: 'teal' },
    { x: 548, title: 'Entra ID', sub: 'app registration', hue: 'grey' },
  ];
  sequence(f, lanes, [
    [0, 1, 'call', 'azd provision', 'after written approval'],
    [1, 2, 'write', 'create resources + budget', null],
    [2, 1, 'reply', 'outputs: app URL, identity', null],
    [0, 2, 'call', 'set PostgreSQL Entra admin', null],
    [0, 2, 'write', 'pgaadauth_create_principal', 'identity gets minimal grants'],
    [0, 2, 'write', 'Key Vault secrets', 'client secret, data-plane key'],
    [0, 3, 'call', 'add hosted redirect URIs', '/signin-oidc, /signout-callback-oidc'],
  ], { rowGap: 46, bottom: 408 });
  f.note(20, 432, 'Before step 1: region, monthly budget and a teardown date agreed in writing (PHASE_3_PLAN.md section 5).');
});

// Sprint 5: every deploy stage stops the run on failure.
figures['fig-p3-s5'] = figure('fig-p3-s5', {
  height: 430,
  title: 'Sprint 5: the OIDC deploy job stops at the first failing stage',
  desc: 'Six stages run in order: CI checks pass on main, Alvin approves the demo environment, an OIDC federated token is issued, sha- images are pushed to ACR, the migration job runs, then both container apps move to the new revision. A failure at any stage stops the deploy and the old revision keeps serving.',
}, f => {
  f.kicker(20, 18, 'Sprint 5 · CI deploy with OIDC · requires authorization');
  const stages = [
    ['green', 'CI checks pass on main', 'build-and-test, audit, review, container-build'],
    ['grey', 'environment: demo', 'Alvin approves the run'],
    ['violet', 'OIDC federated token', 'azure/login; no secret stored in GitHub'],
    ['teal', 'push sha- images to ACR', 'stria-server, gateway, efbundle'],
    ['amber', 'run migration job', 'efbundle; wait for success'],
    ['blue', 'update both container apps', 'new revision on the same sha- tag'],
  ];
  const boxes = stages.map(([hue, t, s], i) => f.box({ x: 30, y: 34 + i * 62, w: 340, h: 42, hue, title: t, sub: s }));
  for (let i = 0; i < boxes.length - 1; i++)
    f.arrow([boxes[i].cx, boxes[i].b], [boxes[i].cx, boxes[i + 1].y - 1]);
  const stop = f.box({ x: 440, y: 34, w: 180, h: boxes[5].b - 34, hue: 'rose', title: 'deploy stops', sub: 'old revision keeps serving', dash: 'dashed' });
  for (const b of boxes)
    f.arrow([b.r, b.cy], [stop.x - 1, b.cy], { style: 'fail' });
  f.note(20, 412, 'Each stage starts only if the one above succeeded. Rollback is the previous sha- tag (Sprint 6).');
});

// Sprint 6: the revision lifecycle that verification walks through.
figures['fig-p3-s6'] = figure('fig-p3-s6', {
  height: 380,
  title: 'Sprint 6: a hosted revision is verified, rolled back once, then torn down',
  desc: 'States: deployed, verified, rolled-back and torn-down, which is terminal. Deployed becomes verified when the smoke test and role walkthroughs pass, or rolled-back when the smoke test fails. The rollback rehearsal moves verified to rolled-back, and the previous tag serving moves it back. azd down on the agreed date ends in torn-down.',
}, f => {
  f.kicker(20, 18, 'Sprint 6 · verify, rehearse, hand off');
  f.dot(26, 124);
  const deployed = f.box({ x: 40, y: 100, w: 140, h: 48, hue: 'amber', title: 'deployed', sub: 'sha-N from Sprint 5' });
  const verified = f.box({ x: 250, y: 100, w: 150, h: 48, hue: 'amber', title: 'verified', sub: 'smoke + walkthroughs' });
  const torn = f.box({ x: 480, y: 100, w: 140, h: 48, hue: 'amber', title: 'torn-down', sub: 'agreed date', double: true });
  const rolled = f.box({ x: 250, y: 270, w: 150, h: 48, hue: 'amber', title: 'rolled-back', sub: 'redeploy sha-N-1' });
  f.arrow([31, 124], [deployed.x - 1, 124], { style: 'state' });
  f.arrow([deployed.r, 124], [verified.x - 1, 124], { style: 'state', label: 'pass' });
  f.arrow([verified.r, 124], [torn.x - 1, 124], { style: 'state', label: 'azd down' });
  f.arrow([110, deployed.b], [rolled.x - 1, 290], { style: 'fail', label: 'smoke fails' });
  f.arrow([300, verified.b], [300, rolled.y - 1], { style: 'state' });
  f.text(292, 214, 'rehearsal', { size: 11.5, anchor: 'end', hue: 'amber' });
  f.arrow([385, rolled.y], [385, verified.b + 1], { style: 'state' });
  f.text(393, 214, 'sha-N-1 serves', { size: 11.5, hue: 'amber' });
  f.note(20, 350, 'Evidence for each transition goes into docs/PHASE_3_HANDOFF.md, as Phase 2 did.');
  f.note(20, 368, 'hosted smoke: compose-smoke.sh with STRIA_URL; the sample service and database must be unreachable.');
});

mkdirSync(OUT, { recursive: true });
for (const [id, svg] of Object.entries(figures)) {
  writeFileSync(join(OUT, `${id}.svg`), svg, 'utf8');
  console.log(`wrote ${id}.svg (${Buffer.byteLength(svg)} bytes)`);
}

import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const projectRoot = path.resolve(import.meta.dirname, '..');
const sourceWorkspace = path.resolve(process.argv[2] || 'D:\\Godot\\war3_assets');
const rawRoot = path.join(sourceWorkspace, 'extracted', 'raw', 'classic');
const parserUrl = pathToFileURL(path.join(
  sourceWorkspace,
  'web',
  'node_modules',
  'war3-model',
  'dist',
  'es',
  'war3-model.mjs'
)).href;
const { parseMDX } = await import(parserUrl);
const catalogRoot = path.join(projectRoot, 'assets', 'warcraft3', 'classic', 'catalog');
const manifest = JSON.parse(fs.readFileSync(path.join(catalogRoot, 'manifest.json'), 'utf8'));
const portraitPattern = /(?:^|\\)(?:portrait[^\\]*|[^\\]*_portrait)\.mdx$/i;
const items = [];

for (const entry of manifest.models || []) {
  if (!portraitPattern.test(entry.source || '')) continue;
  const filename = path.join(rawRoot, entry.layer, ...entry.source.split('\\'));
  if (!fs.existsSync(filename)) continue;
  const source = fs.readFileSync(filename);
  const model = parseMDX(source.buffer.slice(source.byteOffset, source.byteOffset + source.byteLength));
  if (!model.Cameras?.length) continue;
  items.push({ source: entry.source, cameras: model.Cameras });
}

const output = path.join(catalogRoot, 'portrait_cameras.json');
fs.writeFileSync(output, JSON.stringify(
  { schema: 1, items },
  (_key, value) => ArrayBuffer.isView(value) ? Array.from(value) : value
));
console.log(`Wrote ${items.length} portrait camera records to ${output}`);

import fs from 'node:fs';
import path from 'node:path';

const projectRoot = path.resolve(import.meta.dirname, '..');
const output = path.join(
  projectRoot,
  'assets',
  'warcraft3',
  'classic',
  'catalog',
  'display_catalog.json'
);
const endpoint = process.argv[2] || 'http://127.0.0.1:4173/api/catalog';

const response = await fetch(endpoint);
if (!response.ok) throw new Error(`Catalog request failed: ${response.status}`);
const catalog = await response.json();
const bySource = new Map();
for (const item of catalog.items || []) {
  const source = String(item.modelPath || '').replaceAll('/', '\\');
  if (!source) continue;
  const key = source.toLowerCase();
  const previous = bySource.get(key);
  const candidate = {
    source,
    id: String(item.id || ''),
    name: String(item.name || ''),
    category: String(item.category || ''),
    race: String(item.race || ''),
    iconPath: String(item.iconPath || '')
  };
  if (!previous || (!previous.id || previous.id.startsWith('model:')) &&
      candidate.id && !candidate.id.startsWith('model:')) {
    bySource.set(key, candidate);
  }
}

fs.mkdirSync(path.dirname(output), { recursive: true });
fs.writeFileSync(output, JSON.stringify({ schema: 1, items: [...bySource.values()] }));
console.log(`Wrote ${bySource.size} display names to ${output}`);

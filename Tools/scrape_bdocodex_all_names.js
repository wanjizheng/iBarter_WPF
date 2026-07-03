#!/usr/bin/env node
// SPDX-License-Identifier: MIT
//
// One-shot scraper: pulls the Traditional Chinese (zh-TW) names of every
// row in every query.php endpoint that bdocodex exposes, deduplicates by
// the rendered name string, sorts, and writes one name per line.
//
// Usage:  node Tools/scrape_bdocodex_all_names.js [outFile]
//   default outFile: ./bdocodex_all_names.zh-TW.txt
//
// Endpoint contract:
//   All endpoints respond with { aaData: [[id, iconHtml, nameHtml, ...], ...] }
//   The name cell (index 2) is the same <a class="qtooltip"><b>(<span></span>)?NAME</b></a>
//   shape used on every list page; we extract the text between the <b> tags.
//   Some endpoints (barter, specialbarter, etc.) put the name in a different
//   cell - the regex below is liberal enough to find <b>NAME</b> anywhere.
//
// Politeness:
//   200ms pause between requests; one shared HttpClient (kept alive by node).
//
// Re-runnable. Overwrites the output file.

'use strict';
const fs = require('fs');
const path = require('path');
const https = require('https');

const BASE = 'bdocodex.com';
const LANG = 'tw';
const UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36';

// All (a, optional type) pairs we want to scrape.  The top-level `items`
// endpoint already returns every item kind (weapons/armor/consumables/...)
// so we don't enumerate subcategories - that just produces dupes we dedupe
// later anyway.  weapon / subweapon / armor / accessory / awakening are
// separate because their top-level pages carry their own query endpoints
// (and the items endpoint also covers them, so dedup collapses the overlap).
const QUERIES = [
  { a: 'items' },
  { a: 'weapon' },
  { a: 'subweapon' },
  { a: 'armor' },
  { a: 'accessory' },
  { a: 'awakening' },
  { a: 'npcs' },
  { a: 'quests' },
  { a: 'skills' },
  { a: 'achievements' },
  { a: 'titles' },
  { a: 'pets' },
  { a: 'mounts' },
  { a: 'cashshop' },
  { a: 'itemsets' },
  { a: 'itemsforenergy' },
  { a: 'lease' },
  { a: 'bartermastery' },
  { a: 'alchemymastery' },
  { a: 'barter' },
  { a: 'specialbarter' },
];

// Match the <b>(<span></span>)?NAME</b> link cell the lists emit.
// Falls back to <b>NAME</b> for NPCs/quests where the <span> wrapper
// is sometimes absent, and to <b>[TAG]NAME</b> for items that lead
// with a [活動] / [故事圖鑑] bracket (those brackets are kept - the
// user asked for what's on bdocodex).
const RE_NAME = /<b[^>]*>(?:<span><\/span>)?([^<]+)<\/b>/;

// Barter items on the bdocodex list view come out as
// "[5階段]102年黃金草" - the [N階段] is the item's tier/level
// metadata bolted onto the H1 by the list renderer.  The detail page
// strips it, and Phase 4's existing importer strips it too, so we
// strip it here for consistency.  Pure-CJK, non-tier brackets like
// [活動] / [故事圖鑑] are preserved.
const RE_TIER_PREFIX = /^\[\d+\s*階段\]\s*/;

function fetchUrl(p) {
  return new Promise((resolve, reject) => {
    const req = https.request(
      {
        hostname: BASE,
        port: 443,
        path: p,
        method: 'GET',
        headers: {
          'User-Agent': UA,
          'Accept': 'application/json, text/plain, */*',
          'Accept-Language': 'zh-TW,zh;q=0.9,en;q=0.8',
          'Referer': `https://${BASE}/${LANG}/`,
          'X-Requested-With': 'XMLHttpRequest',
        },
      },
      (res) => {
        if (res.statusCode !== 200) {
          res.resume();
          reject(new Error(`HTTP ${res.statusCode} for ${p}`));
          return;
        }
        const chunks = [];
        res.setEncoding('utf8');
        res.on('data', (c) => chunks.push(c));
        res.on('end', () => resolve(chunks.join('')));
        res.on('error', reject);
      }
    );
    req.on('error', reject);
    req.setTimeout(60000, () => req.destroy(new Error('timeout')));
    req.end();
  });
}

function buildQueryPath(q) {
  const params = new URLSearchParams();
  params.set('a', q.a);
  if (q.type) params.set('type', q.type);
  params.set('l', LANG);
  return `/query.php?${params.toString()}`;
}

function extractNames(jsonText, label) {
  let json;
  try {
    json = JSON.parse(jsonText.replace(/^﻿/, ''));
  } catch (e) {
    console.error(`[${label}] JSON parse failed: ${e.message}`);
    return [];
  }
  const rows = json.aaData;
  if (!Array.isArray(rows)) {
    console.error(`[${label}] no aaData`);
    return [];
  }
  const names = [];
  let empty = 0;
  for (const row of rows) {
    // The name cell is column 2 in most endpoints.  Some endpoints (barter)
    // put the name in column 1.  Scan all string cells for the first <b>NAME</b>
    // - that's a little looser but bullet-proof.
    let found = null;
    for (let i = 0; i < row.length; i++) {
      const cell = row[i];
      if (typeof cell !== 'string') continue;
      const m = cell.match(RE_NAME);
      if (m && m[1]) {
        found = m[1];
        break;
      }
    }
    if (found) {
      // Drop the [N階段] tier prefix on barter items so the bare
      // name ("102年黃金草") survives in the dump and matches the
      // form on the detail page / Phase 4's existing CSV.
      const stripped = found.replace(RE_TIER_PREFIX, '');
      names.push(stripped);
    } else {
      empty++;
    }
  }
  return { names, rows: rows.length, empty };
}

function sleep(ms) {
  return new Promise((r) => setTimeout(r, ms));
}

async function main() {
  const outFile = process.argv[2]
    || path.join(__dirname, '..', 'bdocodex_all_names.zh-TW.txt');
  console.log(`[scrape] target: ${outFile}`);

  const allNames = new Set();
  const stats = [];
  const t0 = Date.now();

  for (const q of QUERIES) {
    const p = buildQueryPath(q);
    const label = q.type ? `${q.a}/${q.type}` : q.a;
    process.stdout.write(`[scrape] ${label.padEnd(20)} ... `);
    try {
      const text = await fetchUrl(p);
      const result = extractNames(text, label);
      if (Array.isArray(result)) {
        console.log(`names=${result.length}`);
        stats.push({ label, rows: result.length });
      } else {
        let added = 0;
        for (const n of result.names) {
          if (!allNames.has(n)) {
            allNames.add(n);
            added++;
          }
        }
        console.log(
          `rows=${result.rows} extracted=${result.names.length} ` +
          `new_unique=${added} empty=${result.empty}`
        );
        stats.push({
          label, rows: result.rows, extracted: result.names.length,
          new_unique: added, empty: result.empty,
        });
      }
    } catch (e) {
      console.log(`ERROR: ${e.message}`);
      stats.push({ label, error: e.message });
    }
    await sleep(200);
  }

  const sorted = Array.from(allNames).sort((a, b) => a.localeCompare(b, 'zh-TW'));
  const content = sorted.join('\n') + '\n';
  fs.writeFileSync(outFile, content, { encoding: 'utf8' });

  const dt = ((Date.now() - t0) / 1000).toFixed(1);
  console.log('');
  console.log('=== summary ===');
  for (const s of stats) console.log(' ', JSON.stringify(s));
  console.log(`unique_names=${sorted.length} bytes=${Buffer.byteLength(content, 'utf8')} time=${dt}s`);
  console.log(`written: ${outFile}`);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
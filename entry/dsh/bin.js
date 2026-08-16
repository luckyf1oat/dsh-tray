// Dev-checkout adapter entry for dsh-tray.
//
// The tray launches `node <entry> web` and then identity-checks the spawned process
// (command line must look like dsh: contains `\dsh\`, `bin.js` or `@deepseek-ai`).
// A dev checkout runs `node --import tsx/esm apps/cli/src/bin.ts "web"` from the repo
// root, which fails that check, so this wrapper bridges the two:
//   * lives under `entry\dsh\bin.js` -> the tray's identity check passes
//   * forwards the "web" command to the real dev entry via tsx
//   * forwards termination so Stop/Restart (taskkill /T tree kill) reaches the real harness
//
// Configure in dshtray.ini:
//   dshentry  = <this dir>\entry\dsh\bin.js
//   dshworkdir= <this dir>\entry
//   node      = (auto-detected; must be a Node that can load tsx from the repo)
//
// For a standard npm-global install, delete this adapter and leave dshentry empty:
// auto-detection finds @deepseek-ai/dsh and the identity check passes natively.
'use strict';
const { spawn } = require('child_process');
const path = require('path');

// the dev repo root; change here if the harness lives elsewhere
const HARNESS_DIR = process.env.DSH_DEV_HARNESS_DIR || 'D:\\deepseek-harness';

// command line of the real dev entry (relative to HARNESS_DIR), exactly as the
// running harness uses: node --import tsx/esm apps/cli/src/bin.ts "web"
const args = ['--import', 'tsx/esm', 'apps/cli/src/bin.ts', 'web'];

// eslint-disable-next-line no-console
console.log('[dsh-dev-entry] spawning dev harness in ' + HARNESS_DIR + ' pid=' + process.pid);
const child = spawn(process.execPath, args, {
  cwd: HARNESS_DIR,
  stdio: 'inherit',
  env: Object.assign({}, process.env),
});

child.on('error', (err) => {
  // eslint-disable-next-line no-console
  console.error('[dsh-dev-entry] spawn error: ' + err.message);
  process.exit(1);
});

// mirror exit codes so the tray sees a real failure instead of a silent wrapper
child.on('exit', (code, signal) => {
  // eslint-disable-next-line no-console
  console.log('[dsh-dev-entry] child exited code=' + code + ' signal=' + signal);
  if (signal) process.kill(process.pid, signal);
  else process.exit(code == null ? 0 : code);
});

// forward termination signals to the child so taskkill /T from the tray reaches it
['SIGTERM', 'SIGINT'].forEach((sig) => {
  process.on(sig, () => {
    // eslint-disable-next-line no-console
    console.log('[dsh-dev-entry] received ' + sig + ', forwarding to child');
    child.kill(sig);
  });
});
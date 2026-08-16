// Mock DSH harness entry for integration tests.
// Mimics the real dsh entry: invoked as `node <this> web`, listens on the port
// derived from the MOCK_DSH_PORT env (default 3099), and exits on SIGTERM.
const http = require('http');
const fs = require('fs');
const path = require('path');

const port = parseInt(process.env.MOCK_DSH_PORT || '3099', 10);
const readyFile = process.env.MOCK_DSH_READY || '';

const server = http.createServer((req, res) => {
  res.writeHead(200, { 'Content-Type': 'text/plain' });
  res.end('mock-dsh-ok');
});

server.listen(port, '127.0.0.1', () => {
  if (readyFile) {
    try { fs.writeFileSync(readyFile, String(process.pid), 'utf8'); } catch (e) {}
  }
  // eslint-disable-next-line no-console
  console.log('mock-dsh listening on ' + port + ' pid=' + process.pid);
});

process.on('SIGTERM', () => {
  // eslint-disable-next-line no-console
  console.log('mock-dsh SIGTERM, exiting');
  server.close(() => process.exit(0));
  setTimeout(() => process.exit(0), 500).unref();
});
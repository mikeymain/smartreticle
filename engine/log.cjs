// Append-only debug log. Never throws: logging must not break the overlay.
const fs = require('node:fs');

const MAX_BYTES = 2 * 1024 * 1024;

function createLog(file) {
  return function log(message) {
    try {
      try { if (fs.statSync(file).size > MAX_BYTES) fs.unlinkSync(file); } catch { /* no file yet */ }
      const t = new Date();
      const stamp = [t.getHours(), t.getMinutes(), t.getSeconds()].map(n => String(n).padStart(2, '0')).join(':') +
        '.' + String(t.getMilliseconds()).padStart(3, '0');
      fs.appendFileSync(file, `${stamp}  ${message}\n`);
    } catch { /* disk full or locked: drop the line */ }
  };
}

module.exports = { createLog };

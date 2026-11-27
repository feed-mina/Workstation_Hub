const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const notice = JSON.parse(fs.readFileSync(path.join(root, 'android/app/src/main/assets/privacy-android.json'), 'utf8'));
const markdown = `# Mini Workstation Hub — ${notice.title}\n\n시행일: ${notice.effective_date}\n\n` +
  notice.sections.map(s => `## ${s.heading}\n\n${s.body}\n`).join('\n');
const destination = path.join(root, 'docs/privacy-android.md');
if (process.argv.includes('--check')) {
  if (fs.readFileSync(destination, 'utf8').replace(/\r\n/g, '\n') !== markdown) throw new Error('Public and in-app privacy notices differ. Run node tools/android-policy.cjs.');
  console.log('Public and offline privacy notices match.');
} else { fs.writeFileSync(destination, markdown); }

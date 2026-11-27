"""Verify the release payload without exposing signing credentials."""
import json
import sys
import zipfile
from pathlib import Path

bundle = Path(sys.argv[1])
with zipfile.ZipFile(bundle) as archive:
    files = archive.namelist()
    assert 'base/manifest/AndroidManifest.xml' in files, 'Missing bundle manifest'
    assert any(name.startswith('base/dex/') and name.endswith('.dex') for name in files), 'Missing app code'
    assert 'base/assets/privacy-android.json' in files, 'Missing offline privacy notice'
    assert not any(name.endswith(('.so', '.jks', '.keystore', '.p12', '.pfx', '.password.dpapi')) for name in files), 'Unexpected native library or signing material'
    notice = json.loads(archive.read('base/assets/privacy-android.json'))
    assert len(notice['sections']) >= 6
    # Ignore tests and generated desktop material: this must be the installed app payload.
    assert not any('/src/androidTest/' in name or '/test-results/' in name for name in files)
print(json.dumps({'bundle': bundle.name, 'entries': len(files), 'offline_privacy': True, 'native_libraries': 0, 'private_keys': 0}))

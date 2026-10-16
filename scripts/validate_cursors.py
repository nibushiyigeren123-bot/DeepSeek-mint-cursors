from pathlib import Path
import json,struct
P=Path(__file__).resolve().parents[1]
entries=json.loads((P/'source/cursor_manifest.json').read_text(encoding='utf-8'))
for e in entries:
 raw=(P/e['file']).read_bytes()
 assert raw[:4]==b'RIFF' and raw[8:12]==b'ACON'
 assert struct.unpack_from('<I',raw,4)[0]==len(raw)-8
 assert e['render_size']>=e['size']
print(json.dumps({'manifestFilesVerified':len(entries),'note':'File integrity only; installed display requires Windows testing.'}))

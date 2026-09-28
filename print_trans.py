import json
d=json.load(open('transcript.json',encoding='utf-8'))
print(d['language'],len(d['segments']))
for s in d['segments']:
 if s['start']>1130: print(f"{s['start']:.2f}-{s['end']:.2f} {s['text']}")

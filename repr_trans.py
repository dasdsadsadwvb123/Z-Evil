import json
D=json.load(open('transcript.json',encoding='utf-8'))
for s in D['segments']:
 if 450 < s['start'] < 800:
  print(f"{s['start']:.2f}-{s['end']:.2f} {s['text'].encode('unicode_escape').decode()}")

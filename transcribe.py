from faster_whisper import WhisperModel
import json, sys, time
wav=r"C:\Users\31748\OneDrive\桌面\Z-Evil\work_narration.wav"
for device,ctype in [('cuda','float16'),('cpu','int8')]:
  try:
    print('loading',device,ctype,flush=True)
    model=WhisperModel('small',device=device,compute_type=ctype)
    print('loaded',device,flush=True)
    segs,info=model.transcribe(wav,language='zh',vad_filter=True,beam_size=5,word_timestamps=True,condition_on_previous_text=True)
    out=[]
    for s in segs:
      out.append({'start':s.start,'end':s.end,'text':s.text,'words':[{'start':w.start,'end':w.end,'word':w.word} for w in (s.words or [])]})
      print(f"{s.start:8.2f} {s.end:8.2f} {s.text}",flush=True)
    json.dump({'language':info.language,'duration':info.duration,'segments':out},open(r"C:\Users\31748\OneDrive\桌面\Z-Evil\transcript.json",'w',encoding='utf-8'),ensure_ascii=False,indent=2)
    print('done',len(out),flush=True)
    sys.exit(0)
  except Exception as e:
    print('failed',device,repr(e),flush=True)

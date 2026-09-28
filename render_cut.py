from pathlib import Path
segments = [
    (1225.00,1237.00,'BOSS COLD OPEN'),
    (0.00,15.00,'Z-Evil | 2D SURVIVAL HORROR'),
    (116.30,136.30,'CORE LOOP / EXPLORE'),
    (147.17,173.17,'RESOURCE MANAGEMENT'),
    (186.00,196.00,'NO HEALTH BAR'),
    (206.00,236.00,'CLUES / LIGHT / STEALTH'),
    (296.44,314.44,'CHAINSAW CHASE 01'),
    (369.69,387.69,'60-SECOND ESCAPE'),
    (501.51,517.51,'CHAINSAW CHASE 02'),
    (649.08,676.08,'WEAPONS / AMMO'),
    (706.03,714.03,'WEAPON SWITCHING'),
    (992.45,1007.45,'CHAINSAW CHASE 03'),
    (1146.53,1173.53,'THREE GEMS -> BOSS'),
    (1219.03,1294.03,'BOSS FIGHT'),
    (1304.58,1329.58,'Z-Evil | CHEN HAOZE'),
]
# Build filter graph.
lines=[]
for i,(s,e,label) in enumerate(segments):
    lines.append(f"[0:v]trim=start={s}:end={e},setpts=PTS-STARTPTS[v{i}]")
    lines.append(f"[0:a]atrim=start={s}:end={e},asetpts=PTS-STARTPTS[a{i}]")
concat=''.join(f'[v{i}][a{i}]' for i in range(len(segments)))
lines.append(f"{concat}concat=n={len(segments)}:v=1:a=1[vc][ac]")
# Source is 2014x1080. Normalize to 1920x1080 with black pillar/letterbox padding.
video="[vc]scale=w=1920:h=-2:flags=lanczos,pad=1920:1080:(ow-iw)/2:(oh-ih)/2:color=black"
# Timeline labels (seconds in the assembled output).
starts=[]; t=0
for s,e,label in segments:
    starts.append((t,t+e-s,label)); t += e-s
for j,(st,en,label) in enumerate(starts):
    # Put labels in the top-left black margin. Keep the game image unobscured.
    esc=label.replace('\\','\\\\').replace(':','\\:').replace("'","\\'")
    video += f",drawtext=fontfile='C\\:/Windows/Fonts/arial.ttf':text='{esc}':x=52:y=32:fontsize=30:fontcolor=white:box=1:boxcolor=black@0.62:boxborderw=10:enable='between(t\\,{st:.3f}\\,{en:.3f})'"
video += "[vout]"
lines.append(video)
lines.append("[ac]aresample=async=1:first_pts=0[aout]")
Path('cut_filter.txt').write_text(';\n'.join(lines),encoding='utf-8')
with open('cut_list.txt','w',encoding='utf-8') as f:
    f.write('Z-Evil 比赛展示粗剪（目标 5-6 分钟）\\n\\n')
    for i,(s,e,label) in enumerate(segments,1):
        f.write(f'{i:02d}. {s:07.2f} - {e:07.2f}  ({e-s:05.2f}s)  {label}\\n')
    f.write(f'\nTotal: {t:.2f}s ({t/60:.2f} min)\\n')
print(f'generated {len(segments)} segments, {t:.2f}s')

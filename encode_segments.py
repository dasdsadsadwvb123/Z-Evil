from pathlib import Path
import subprocess, shutil, sys
src = r"E:\剪辑成品和素材\成品\25yuan\Z-Evil 演示.mp4"
outdir = Path('segments')
outdir.mkdir(exist_ok=True)
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
for old in outdir.glob('seg_*.mp4'):
    old.unlink()
for i,(start,end,label) in enumerate(segments,1):
    dur=end-start
    out=outdir/f'seg_{i:02d}.mp4'
    filt=("scale=1920:-2:flags=lanczos,pad=1920:1080:(ow-iw)/2:(oh-ih)/2:color=black,"
          f"drawtext=fontfile='C\\:/Windows/Fonts/arial.ttf':text='{label}':x=52:y=32:fontsize=30:"
          "fontcolor=white:box=1:boxcolor=black@0.62:boxborderw=10")
    cmd=['ffmpeg','-y','-hide_banner','-loglevel','warning','-ss',f'{start:.3f}','-i',src,'-t',f'{dur:.3f}',
         '-vf',filt,'-map','0:v:0','-map','0:a:0','-r','60','-fps_mode','cfr',
         '-c:v','h264_nvenc','-preset','p4','-cq','23','-b:v','0','-pix_fmt','yuv420p',
         '-c:a','aac','-b:a','192k','-ar','44100','-ac','2','-map_metadata','-1','-movflags','+faststart',str(out)]
    print(f'[{i}/{len(segments)}] {start:.2f}-{end:.2f} {label}',flush=True)
    r=subprocess.run(cmd)
    if r.returncode:
        print('FAILED',r.returncode,flush=True);sys.exit(r.returncode)
with open('concat_list.txt','w',encoding='utf-8') as f:
    for i in range(1,len(segments)+1):
        f.write(f"file 'segments/seg_{i:02d}.mp4'\n")
print('all segments encoded')

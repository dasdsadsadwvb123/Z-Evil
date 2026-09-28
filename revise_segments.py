from pathlib import Path
import subprocess,sys
src = r"E:\剪辑成品和素材\成品\25yuan\Z-Evil 演示.mp4"
outdir=Path('segments'); outdir.mkdir(exist_ok=True)
# Revised slots: actual chainsaw appearances are source 545-565, 730-760, 992-1007.
changed=[
 (369.69,387.69,'60-SECOND ESCAPE',7),
 (545.00,565.00,'CHAINSAW CHASE 01',8),
 (649.08,676.08,'WEAPONS / AMMO',9),
 (706.03,714.03,'WEAPON SWITCHING',10),
 (730.00,760.00,'CHAINSAW CHASE 02',11),
 (992.45,1007.45,'CHAINSAW CHASE 03',12),
]
for start,end,label,i in changed:
 out=outdir/f'seg_{i:02d}.mp4'; filt=("scale=1920:-2:flags=lanczos,pad=1920:1080:(ow-iw)/2:(oh-ih)/2:color=black,"
 f"drawtext=fontfile='C\\:/Windows/Fonts/arial.ttf':text='{label}':x=52:y=32:fontsize=30:fontcolor=white:box=1:boxcolor=black@0.62:boxborderw=10")
 cmd=['ffmpeg','-y','-hide_banner','-loglevel','warning','-ss',f'{start:.3f}','-i',src,'-t',f'{end-start:.3f}','-vf',filt,'-map','0:v:0','-map','0:a:0','-r','60','-fps_mode','cfr','-c:v','h264_nvenc','-preset','p4','-cq','23','-b:v','0','-pix_fmt','yuv420p','-c:a','aac','-b:a','192k','-ar','44100','-ac','2','-map_metadata','-1','-movflags','+faststart',str(out)]
 print(f'encoding {i}: {start}-{end} {label}',flush=True); r=subprocess.run(cmd)
 if r.returncode: sys.exit(r.returncode)
# Final ordered timeline
order=list(range(1,16))
with open('concat_list.txt','w',encoding='utf-8') as f:
 for i in order:f.write(f"file 'segments/seg_{i:02d}.mp4'\n")
# document exact source ranges
with open('cut_list_revised.txt','w',encoding='utf-8') as f:
 f.write('Z-Evil 比赛展示版（修订）\n\n')
 for i in order:
  # read durations from expected list, source labels documented manually
  pass
 f.write('真实锯子追逐：545-565s / 730-760s / 992.45-1007.45s\n')
 f.write('总时长：358 秒（5 分 58 秒）\n')

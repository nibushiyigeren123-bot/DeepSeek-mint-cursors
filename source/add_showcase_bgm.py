"""Add credited MusMus BGM without re-encoding the accepted showcase picture.

Usage: python add_showcase_bgm.py VIDEO MUSIC OUTPUT --wav EDITABLE.wav
Download the music yourself from the official MusMus website. Music is not bundled.
"""
from pathlib import Path
import argparse, hashlib, json, subprocess

def sha(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest()

def run(args):
    r = subprocess.run(args, capture_output=True, text=True, encoding='utf-8', errors='replace')
    if r.returncode:
        raise RuntimeError(r.stderr[-4000:])
    return r

def probe(p):
    return json.loads(run(['ffprobe','-v','error','-show_streams','-show_format','-of','json',str(p)]).stdout)

def stats(stderr):
    return json.loads(stderr[stderr.rfind('{'):stderr.rfind('}')+1])

def video_packets(p):
    return run(['ffmpeg','-v','error','-i',str(p),'-map','0:v:0','-c:v','copy','-f','hash','-hash','sha256','-']).stdout.strip()

def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('video',type=Path);ap.add_argument('music',type=Path);ap.add_argument('output',type=Path)
    ap.add_argument('--wav',type=Path,required=True)
    a=ap.parse_args()
    for k in ('video','music','output','wav'):setattr(a,k,getattr(a,k).resolve())
    assert a.video.is_file() and a.music.is_file()
    assert len({a.video,a.music,a.output,a.wav})==4
    assert not a.output.exists() and not a.wav.exists(), 'Preserve existing outputs; choose new output paths.'
    a.output.parent.mkdir(parents=True,exist_ok=True);a.wav.parent.mkdir(parents=True,exist_ok=True)
    p=probe(a.video);v=next(s for s in p['streams'] if s['codec_type']=='video')
    duration=float(v['duration'])
    assert float(probe(a.music)['format']['duration'])>=duration
    fade=f'atrim=duration={duration},asetpts=PTS-STARTPTS,afade=t=in:st=0:d=0.7,afade=t=out:st={duration-3}:d=3'
    measured=stats(run(['ffmpeg','-hide_banner','-i',str(a.music),'-af',fade+',loudnorm=I=-18:TP=-1.5:LRA=11:print_format=json','-f','null','-']).stderr)
    norm='loudnorm=I=-18:TP=-1.5:LRA=11:'+':'.join('measured_'+key+'='+measured[src] for key,src in [('I','input_i'),('TP','input_tp'),('LRA','input_lra'),('thresh','input_thresh')])+':offset='+measured['target_offset']+':linear=true'
    run(['ffmpeg','-hide_banner','-loglevel','error','-i',str(a.music),'-af',fade+','+norm,'-t',str(duration),'-ar','48000','-ac','2','-c:a','pcm_s24le',str(a.wav)])
    credit='BGM: 放課後アフタースクール / watson (MusMus) https://musmus.main.jp/'
    run(['ffmpeg','-hide_banner','-loglevel','error','-i',str(a.video),'-i',str(a.wav),'-map','0:v:0','-map','1:a:0','-c:v','copy','-c:a','aac','-b:a','256k','-ar','48000','-ac','2','-t',str(duration),'-metadata','comment='+credit,'-movflags','+faststart',str(a.output)])
    out=probe(a.output);vs=next(s for s in out['streams'] if s['codec_type']=='video');au=next(s for s in out['streams'] if s['codec_type']=='audio')
    assert vs['width']==v['width'] and vs['height']==v['height'] and vs['r_frame_rate']==v['r_frame_rate']
    assert abs(float(out['format']['duration'])-duration)<0.05
    assert au['codec_name']=='aac' and au['sample_rate']=='48000' and au['channels']==2
    before=video_packets(a.video);after=video_packets(a.output);assert before==after
    decoded=run(['ffmpeg','-v','error','-xerror','-i',str(a.output),'-f','null','-']);assert not decoded.stderr.strip()
    level=stats(run(['ffmpeg','-hide_banner','-i',str(a.output),'-af','loudnorm=I=-18:TP=-1.5:LRA=11:print_format=json','-f','null','-']).stderr)
    assert -19<=float(level['input_i'])<=-17 and float(level['input_tp'])<=-1
    receipt={'seconds':duration,'size':[vs['width'],vs['height']],'fps':vs['r_frame_rate'],'audio_codec':au['codec_name'],'audio_hz':48000,'audio_channels':2,'loudness_lufs':float(level['input_i']),'true_peak_dbtp':float(level['input_tp']),'fade_in_seconds':0.7,'fade_out_seconds':3,'music_start_seconds':0,'video_packet_hash':before,'picture_unchanged':True,'full_decode_ok':True,'video_input_sha256':sha(a.video),'music_input_sha256':sha(a.music),'wav_sha256':sha(a.wav),'output_sha256':sha(a.output),'credit':credit,'music_page':'https://musmus.main.jp/music_img2.html#BGM-174','music_download':'https://musmus.main.jp/music/MusMus-BGM-174.mp3','terms':'https://musmus.main.jp/info.html','music_files_private_editing_only':True,'audio_filters':fade+','+norm,'ffmpeg_version':run(['ffmpeg','-version']).stdout.splitlines()[0]}
    a.output.with_suffix('.mix.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(receipt,ensure_ascii=False))

if __name__=='__main__':main()

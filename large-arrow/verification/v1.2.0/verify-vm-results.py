"""Review actual native role readbacks and captured pixels, independently of pass flags."""
from pathlib import Path
import json,hashlib,sys,subprocess,argparse,os
import numpy as np
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[1]
ROWS=json.loads((ROOT/'targets.json').read_text(encoding='utf-8'))
parser=argparse.ArgumentParser();parser.add_argument('--repo');parser.add_argument('--available',action='store_true');args=parser.parse_args()
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def output_for(name):
 if name=='mimo-cursors':return ROOT/'vm-results/mimo-retest-and-largest-before-boot/out/mimo-retest'
 return ROOT/'vm-results'/('series-'+name+'-attempt20')/'out'
def worker(row):
 repo=Path(row['checkout']);name=row['repository'];out=output_for(name)
 accepted=json.loads((out/'acceptance.json').read_text(encoding='utf-8-sig'));assert accepted['success']
 observations=json.loads((out/'observations.json').read_text(encoding='utf-8-sig'));assert len(observations)==accepted['observations']
 expected_total=len(row['profile']['characters'])*26+6;assert len(observations)==expected_total
 default=sha(out/'00-default-before.png');default_count=0
 for ob in observations:
  label=ob['label'];expected=ob['expected'];assert ob['globalBase']==32 and ob['dpi']==96,label
  assert ob['current']['visible'] and ob['current']['bitmap'][:2]==[expected,expected],label
  assert Image.open(out/(label+'.png')).size==(expected,expected)
  if '-default' in label or label=='00-default-before':assert sha(out/(label+'.png'))==default;default_count+=1
  else:assert len(ob['roles'])==17 and all(v['bitmap'][:2]==[expected,expected] for v in ob['roles']),label
 sys.path.insert(0,str(repo/'source'));import art_renderer as art
 layout=json.loads((repo/'large-arrow/source/layout-same-character.json').read_text(encoding='utf-8'))
 comparisons=[]
 for c in row['profile']['characters']:
  cid=c['id'];phase=0;size=96;role='Arrow'
  if hasattr(art,'ROLE_POSE'):base=art.action_frame(cid,art.ROLE_POSE[role],phase)
  else:
   source=art.action_frame(cid,role,phase);base=Image.new('RGBA',(256,256));base.alpha_composite(source,((256-source.width)//2,248-source.height))
  char=np.asarray(base.resize((size,size),Image.Resampling.LANCZOS))
  native_out=ROOT/'vm-results/native-dib-final16-attempt2/out'
  normal=np.asarray(Image.open(native_out/f'{name}-original-{cid}-96.png').convert('RGB'))
  dx,dy=layout[cid][role]['96']['offsets'][phase]
  plus=np.asarray(Image.open(native_out/f'{name}-plus-{cid}-96.png').convert('RGB'))[dy:dy+size,dx:dx+size]
  for edition,base in [('original',repo),('plus',repo/'large-arrow')]:
   source=Image.open(base/f'source/frames/{cid}/96/Arrow-00.png').convert('RGBA')
   composed=Image.new('RGBA',source.size,(216,226,219,255));composed.alpha_composite(source)
   captured=np.asarray(Image.open(native_out/f'{name}-{edition}-{cid}-96.png').convert('RGB')).astype(int)
   assert np.abs(np.asarray(composed.convert('RGB')).astype(int)-captured).max()<=1,(name,cid,edition,'Native alpha rendering differs from source')
  opaque=char[:,:,3]>=250
  # The old ordinary renderer resizes character+badge together; its Lanczos fringe
  # can alter a boundary pixel. Compare only pixels unchanged by that old composition.
  original_source=np.asarray(Image.open(repo/f'source/frames/{cid}/96/Arrow-00.png').convert('RGBA'))
  mask=opaque & np.all(original_source==char,axis=2)
  excluded=int((opaque & ~mask).sum());assert excluded<=max(4,int(opaque.sum()*.01)),(cid,'Unexpected ordinary composition coverage',excluded)
  assert mask.sum()>100
  differences=np.abs(normal[mask].astype(int)-plus[mask].astype(int))
  assert differences.max()==0,(name,cid,'native character pixel mismatch',int(differences.max()))
  comparisons.append({'character':cid,'samePhysicalSize':True,'opaquePixelsCompared':int(mask.sum()),'maxRGBDifference':int(differences.max()),'ordinaryCompositionPixelsExcluded':excluded,'offset':[dx,dy]})
 return {'repository':name,'observations':len(observations),'sameDefaultCaptures':default_count,'nativeCharacterComparisons':comparisons,'success':True}
if args.repo:
 print(json.dumps(worker(next(r for r in ROWS if r['repository']==args.repo)),ensure_ascii=False));sys.exit()
results=[]
for row in ROWS:
 out=output_for(row['repository'])/'acceptance.json'
 if args.available and not out.is_file():continue
 run=subprocess.run([sys.executable,'-X','utf8',str(Path(__file__).resolve()),'--repo',row['repository']],capture_output=True,env=dict(os.environ,PYTHONDONTWRITEBYTECODE='1'))
 if run.returncode:raise RuntimeError(row['repository']+': '+run.stderr.decode('utf-8',errors='backslashreplace')[-2000:])
 result=json.loads(run.stdout);results.append(result);print(row['repository'],'reviewed',result['observations'],'native observations',flush=True)
if args.available:print(json.dumps({'reviewed':len(results),'results':results},ensure_ascii=False));sys.exit()
assert len(results)==11
native_out=ROOT/'vm-results/native-dib-final16-attempt2/out'
native_capture=json.loads((native_out/'acceptance.json').read_text(encoding='utf-8-sig'))
assert native_capture['success'] and native_capture['observations']==32 and native_capture['characters']==16
native_obs=json.loads((native_out/'observations.json').read_text(encoding='utf-8-sig'))
assert len(native_obs)==32
for ob in native_obs:
 assert ob['globalBase']==32 and ob['current']['visible'] and ob['current']['bitmap'][:2]==[ob['expected'],ob['expected']]
 assert all(x['bitmap'][:2]==[ob['expected'],ob['expected']] for x in ob['roles'])
native_alpha=json.loads((ROOT/'native-alpha-all-sizes-validation.json').read_text(encoding='utf-8'))
assert native_alpha['success'] and native_alpha['cases']==208 and native_alpha['maximumRGBError']<=1
for number in [*range(20,31),36,45,50,55,56,57,58]:
 response=json.loads((ROOT/f'vm-queue/{number:03}.response.json').read_text(encoding='utf-8-sig'))
 if number==28:
  assert not response['success'] and 'Missing cursor:' in response['error']
  assert json.loads((output_for('mimo-cursors')/'acceptance.json').read_text(encoding='utf-8-sig'))['success']
 else:assert response['success'],number
cycle=json.loads((ROOT/'vm-queue/056.response.json').read_text(encoding='utf-8-sig'))
before_boot=cycle['beforeBoot'].get('value') if isinstance(cycle['beforeBoot'],dict) else cycle['beforeBoot']
after_boot=cycle['afterBoot'].get('value') if isinstance(cycle['afterBoot'],dict) else cycle['afterBoot']
assert isinstance(before_boot,str) and isinstance(after_boot,str) and after_boot>before_boot
preboot=json.loads((ROOT/'vm-results/accepted-largest-before-boot/out/acceptance.json').read_text(encoding='utf-8-sig'))
assert preboot['success'] and preboot['beforeBootPlus256Native352']
preobs=json.loads((ROOT/'vm-results/accepted-largest-before-boot/out/observations.json').read_text(encoding='utf-8-sig'))
assert len(preobs)==1 and preobs[0]['expected']==352 and preobs[0]['globalBase']==32
assert preobs[0]['current']['visible'] and preobs[0]['current']['bitmap'][:2]==[352,352]
assert all(x['bitmap'][:2]==[352,352] for x in preobs[0]['roles'])
out=ROOT/'vm-results/accepted-edge-cases/out'
final=json.loads((out/'acceptance.json').read_text(encoding='utf-8-sig'))
for flag in ['success','reboot','cachedRepair','userBase48Preserved','inf96and256','ownNameForeignPaths','restore','legacyGlobalSizeIgnored']:assert final[flag],flag
observations=json.loads((out/'observations.json').read_text(encoding='utf-8-sig'))
for ob in observations:
 expected_base=48 if ob['label'].startswith(('104-','105-','106-','107-','108-')) else 32
 assert ob['globalBase']==expected_base,ob['label']
 assert ob['current']['visible'] and ob['current']['bitmap'][:2]==[ob['expected'],ob['expected']]
 if ob['expected'] in [96,128,352]:assert all(x['bitmap'][:2]==[ob['expected'],ob['expected']] for x in ob['roles'])
baseline=sha(out/'109-user-size-restored32.png')
for name in ['111-inf96-default','111-inf256-default','112-own-name-foreign-paths-untouched','113-restore-original-default','114-final-default-after-uninstall']:assert sha(out/(name+'.png'))==baseline,name
assert json.loads((ROOT/'vm-queue/058.response.json').read_text(encoding='utf-8-sig'))['vmState']=='Off'
stopped=json.loads((ROOT/'vm-queue/controller-stopped.json').read_text(encoding='utf-8-sig'));assert stopped['state']=='stopped'
build=json.loads((ROOT/'build-verification.json').read_text(encoding='utf-8'))
report={'success':True,'repositories':11,'characters':16,'nativeObservations':sum(r['observations'] for r in results)+len(observations)+len(native_obs)+len(preobs),'cmdSelections':16*13,'sameDefaultCaptures':sum(r['sameDefaultCaptures'] for r in results),'nativeCharacterPixelsCompared':sum(c['opaquePixelsCompared'] for r in results for c in r['nativeCharacterComparisons']),'environment':'Windows11-Lab; interactive LabUser; 96DPI; system preference32 and48','main':results,'reboot':cycle,'finalScenario':final,'build':{k:v for k,v in build.items() if k!='results'},'controllerStopped':stopped,'limits':'Actual VM acceptance covers this Windows11 build at96DPI; otherWindows versions and DPI combinations are not claimed tested.'}
(ROOT/'final-acceptance.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k not in ['main','reboot','finalScenario','controllerStopped']},ensure_ascii=False))

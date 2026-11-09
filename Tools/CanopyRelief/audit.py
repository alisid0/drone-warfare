"""Retain compact, reproducible evidence; managed scheduler timings are never reported as GPU/frame timings."""
from pathlib import Path
import json
import runpy

ROOT=Path(__file__).resolve().parents[2]
TEMP=ROOT/'Temp/canopy-relief'
OUT=ROOT/'Tools/CanopyRelief/Review'
OUT.mkdir(exist_ok=True)
# Reuse the established projection metric without running its older, unrelated report.
scope={}
source=(ROOT/'Tools/ForestPolish/audit.py').read_text().split('results = {}')[0]
scope['__file__']=str(ROOT/'Tools/ForestPolish/audit.py')
exec(compile(source,scope['__file__'],'exec'),scope)
results={}
for sector in ('deep','border','varrow-base','varrow-bridge','varrow-farmland'):
    for phase in ('before','after'):
        folder=TEMP/(phase+'-'+sector)
        item={name:json.loads((folder/(name+'.json')).read_text()) for name in ('metrics','flight')}
        for name in ('startup','recipe','floor-input'):
            if (folder/(name+'.json')).exists():item[name]=json.loads((folder/(name+'.json')).read_text())
        item['screenEarly']=scope['screen'](json.loads((TEMP/(phase+'-'+sector+'-early')/'scene.json').read_text()))
        results[phase+'-'+sector]=item
    before,after=results['before-'+sector],results['after-'+sector]
    assert before['metrics']['logical']==after['metrics']['logical'], 'No extra tree instances'
    assert after['flight']['peakCombinedTriangles']<=before['flight']['peakCombinedTriangles'], sector+' geometry regression'
    assert after['flight']['refinementStarts']==0, sector+' refinement churn in fast flight'
    if sector.startswith('varrow'):
        assert after['startup']['densePatches']==after['startup']['patchesWithCanopy']
        assert after['floor-input']['persistent']==after['metrics']['logical']
(OUT/'measurements.json').write_bytes((json.dumps(results,indent=2)+'\n').replace('\n','\r\n').encode())
for sector in ('deep','border','varrow-base','varrow-bridge','varrow-farmland'):
    a,b=results['before-'+sector],results['after-'+sector]
    print(sector, 'combined peak',a['flight']['peakCombinedTriangles'],'->',b['flight']['peakCombinedTriangles'],
          'managed CPU p95',a['metrics']['lowFlight']['managedP95Ms'],'->',b['metrics']['lowFlight']['managedP95Ms'])

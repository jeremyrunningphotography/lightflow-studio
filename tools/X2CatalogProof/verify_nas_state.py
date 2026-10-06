"""Independent all-table/field comparator for retained NAS control JSON."""
from pathlib import Path
import json,sys
p=Path(sys.argv[1]);a=json.loads((p/'NAS_ALTERNATIVE_RESULTS.json').read_text());b=json.loads((p/'delete-recovery.json').read_text());changes=[]
def compare(before,after,asset,fields,note=None):
 assert before.keys()==after.keys()
 for table,x in before.items():
  y=after[table];assert x['columns']==y['columns']
  if table!='MediaAssetDescriptions':assert x==y,table;continue
  old={json.loads(z)[0]:json.loads(z) for z in x['rows']};new={json.loads(z)[0]:json.loads(z) for z in y['rows']};assert old.keys()==new.keys()
  for id,row in old.items():
   for index,value in enumerate(row):
    if value!=new[id][index]:
     assert id==asset and index in fields,(id,index)
     if note is not None:assert new[id][index]==note
     changes.append({'table':table,'id':id,'column':x['columns'][index]})
compare(a['before'],a['after'],a['assetIdEdited'],(3,6,8));authored=list(changes);changes.clear()
compare(a['after'],b['snapshot'],b['id'],(3,),'X2 delete committed before kill')
result={'status':'passed','beforeToAfterAllowedDifferences':authored,'postKillDifferences':changes,'allOtherTablesAndRowsEqual':True,'tableCounts':{t:len(x['rows']) for t,x in a['after'].items()}}
(p/'AUTHORED_STATE_COMPARISON.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps({'status':'passed','tables':len(result['tableCounts']),'authoredDifferences':len(authored),'postKillDifferences':len(changes)}))

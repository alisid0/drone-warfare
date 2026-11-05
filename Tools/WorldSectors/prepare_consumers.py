"""Prepare managed engine substitutes; all three production consumers compile without rewriting them."""
import re
import sys
from pathlib import Path

repo = Path(__file__).resolve().parents[2]
out = Path(sys.argv[1])
scripts = repo / 'Assets/DroneSandbox/Scripts'


def write(name, text):
    """Keep generated scaffolding out of the production tree and preserve CRLF."""
    (out / name).write_bytes(text.replace('\r\n', '\n').replace('\n', '\r\n').encode())


math = (repo / 'Tools/Tests/SectorIdentity/UnityMath.cs').read_text().split('namespace DroneSandbox')[0]
math = math.replace('public const float PI', '''public static float Floor(float x)=>(float)Math.Floor(x);
        public static float Pow(float x,float y)=>(float)Math.Pow(x,y);
        public const float PI''')
math = math.replace('public static Vector2 one', '''public static Vector2 Min(Vector2 a,Vector2 b)=>new Vector2(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y));
        public static Vector2 Max(Vector2 a,Vector2 b)=>new Vector2(Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y));
        public static Vector2 one''')
math = math.replace('public float x, y, width, height;', '''public float x, y, width, height;
        public float xMin=>x; public float xMax=>x+width; public float yMin=>y; public float yMax=>y+height;
        public Vector2 center=>new Vector2(x+width/2,y+height/2); public Vector2 size=>new Vector2(width,height);
        public static Rect MinMaxRect(float a,float b,float c,float d)=>new Rect(a,b,c-a,d-b);''')
write('ConsumerMath.cs', math)
enums = '\n'.join(re.search(r'public enum ' + enum + r'\s*\{[^}]+\}', (scripts / (name + '.cs')).read_text()).group()
                  for name, enum in [('EnemyUnit', 'EnemyType'), ('Drone', 'DroneType')])
write('Enums.cs', 'namespace DroneSandbox { ' + enums + ' }')


def method(text, anchor):
    """Copy a complete production method verbatim into the flow probe."""
    start = text.index(anchor)
    brace = text.index('{', start)
    depth = 1
    end = brace + 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}')
        end += 1
    return text[start:end]


game = (scripts / 'SandboxGame.cs').read_text()
wrapper = (Path(__file__).parent / 'FlowProbe.txt').read_text()
wrapper = wrapper.replace('/* LOAD */', method(game, 'public void LoadSector(SectorPlan plan)'))
wrapper = wrapper.replace('/* PLAY */', method(game, 'IEnumerator PlayHotspotSoon(Hotspot hotspot)'))
write('SandboxFlowProbe.cs', wrapper)
